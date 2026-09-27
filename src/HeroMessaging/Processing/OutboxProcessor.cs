using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;
/// <summary>
/// Represents the outbox processor type.
/// </summary>

public class OutboxProcessor : PollingBackgroundServiceBase<OutboxWorkItem>, IOutboxProcessor
{
    private static readonly TimeSpan ExternalLeaseDuration = TimeSpan.FromMinutes(1);
    private readonly IOutboxStorage _outboxStorage;
    private readonly IExternalOutboxStorage? _externalStorage;
    private readonly ExternalOutboxDelivery? _externalDelivery;
    private int _externalClaimInFlight;
    /// <summary>
    /// Represents service provider.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;
    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxProcessor"/> class.
    /// </summary>

    public OutboxProcessor(
        IOutboxStorage outboxStorage,
        IServiceProvider serviceProvider,
        ILogger<OutboxProcessor> logger,
        TimeProvider timeProvider,
        IMessageTransport? transport = null,
        IJsonSerializer? jsonSerializer = null)
        : base(logger, timeProvider, maxDegreeOfParallelism: Environment.ProcessorCount, boundedCapacity: 100)
    {
        _outboxStorage = outboxStorage;
        _externalStorage = outboxStorage as IExternalOutboxStorage;
        if (_externalStorage?.SupportsExternalClaims == true && transport is IConfirmedQueueTransport confirmedTransport && jsonSerializer is not null)
            _externalDelivery = new ExternalOutboxDelivery(_externalStorage, confirmedTransport, jsonSerializer, timeProvider, logger);
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }
    /// <summary>
    /// Executes publish to outbox async.
    /// </summary>

    public async Task PublishToOutboxAsync(IMessage message, OutboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new OutboxOptions();

        if (options.Destination is not null)
        {
            if (string.IsNullOrWhiteSpace(options.Destination))
                throw new ArgumentException("External outbox destination must name a queue.", nameof(options));
            if (_externalDelivery is null)
                throw new NotSupportedException("External outbox delivery requires leased storage, a confirmed queue transport, and JSON serialization.");
        }

        var entry = await _outboxStorage.AddAsync(message, options, cancellationToken);

        // Trigger immediate processing for high priority messages
        if (options.Destination is null && options.Priority > 5)
        {
            await SubmitWorkItemAsync(new OutboxWorkItem(entry), cancellationToken);
        }
    }
    /// <summary>
    /// Gets is running.
    /// </summary>

    public new bool IsRunning => base.IsRunning;
    /// <summary>
    /// Executes get metrics.
    /// </summary>

    public IOutboxProcessorMetrics GetMetrics()
    {
        return new OutboxProcessorMetrics
        {
            PendingMessages = 0, // TODO: Track metrics
            ProcessedMessages = 0,
            FailedMessages = 0,
            LastProcessedTime = _timeProvider.GetUtcNow()
        };
    }
    /// <summary>
    /// Represents the outbox processor metrics type.
    /// </summary>

    private class OutboxProcessorMetrics : IOutboxProcessorMetrics
    {
        public long PendingMessages { get; init; }
        public long ProcessedMessages { get; init; }
        public long FailedMessages { get; init; }
        /// <summary>
        /// Gets last processed time.
        /// </summary>
        public DateTimeOffset? LastProcessedTime { get; init; }
    }
    /// <summary>
    /// Executes get service name.
    /// </summary>

    protected override string GetServiceName() => "Outbox processor";
    /// <summary>
    /// Executes poll for work items async.
    /// </summary>

    protected override async Task<IEnumerable<OutboxWorkItem>> PollForWorkItemsAsync(CancellationToken cancellationToken)
    {
        if (_externalStorage is null)
        {
            var pending = await _outboxStorage.GetPendingAsync(100, cancellationToken);
            return pending.Select(static entry => new OutboxWorkItem(entry));
        }

        var local = await _externalStorage.GetLocalPendingAsync(100, cancellationToken);
        var work = local.Select(static entry => new OutboxWorkItem(entry)).ToList();
        if (!_externalStorage.SupportsExternalClaims || _externalDelivery is null)
            return work;

        if (Interlocked.CompareExchange(ref _externalClaimInFlight, 1, 0) != 0)
            return work;

        try
        {
            var claims = await _externalStorage.ClaimExternalAsync(1, ExternalLeaseDuration, cancellationToken);
            if (claims.Count == 0)
                Volatile.Write(ref _externalClaimInFlight, 0);
            else
                work.Insert(0, new OutboxWorkItem(claims[0].Entry, claims[0].Token));
            return work;
        }
        catch
        {
            Volatile.Write(ref _externalClaimInFlight, 0);
            throw;
        }
    }
    /// <summary>
    /// Executes process work item async.
    /// </summary>

    protected override async Task ProcessWorkItemAsync(OutboxWorkItem work)
    {
        var entry = work.Entry;
        if (work.LeaseToken is Guid token)
        {
            try
            {
                await _externalDelivery!.DeliverAsync(entry, token);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "External outbox state could not be updated for {EntryId}; its lease will expire for retry", entry.Id);
            }
            finally
            {
                Volatile.Write(ref _externalClaimInFlight, 0);
            }
            return;
        }

        if (!string.IsNullOrEmpty(entry.Options.Destination))
        {
            await _outboxStorage.MarkFailedAsync(entry.Id, "External outbox destinations are not supported.");
            Logger.LogError("Outbox entry {EntryId} has an unsupported external destination", entry.Id);
            return;
        }

        try
        {
            // Mark as processing to prevent duplicate processing
            entry.Status = OutboxStatus.Processing;

            await ScopedMessagingExecutor.DispatchAsync(_serviceProvider, entry.Message, Logger, "outbox");

            await _outboxStorage.MarkProcessedAsync(entry.Id);

            Logger.LogInformation("Outbox entry {EntryId} processed successfully", entry.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing outbox entry {EntryId}", entry.Id);

            entry.RetryCount++;

            if (entry.RetryCount >= entry.Options.MaxRetries)
            {
                await _outboxStorage.MarkFailedAsync(entry.Id, ex.Message);
                Logger.LogError("Outbox entry {EntryId} failed after {RetryCount} retries",
                    entry.Id, entry.RetryCount);
            }
            else
            {
                var delay = entry.Options.RetryDelay ?? RetryDelayCalculator.CalculateWithoutJitter(entry.RetryCount);
                var nextRetry = _timeProvider.GetUtcNow().Add(delay);

                await _outboxStorage.UpdateRetryCountAsync(entry.Id, entry.RetryCount, nextRetry);

                Logger.LogWarning("Outbox entry {EntryId} will be retried at {NextRetry} (attempt {RetryCount}/{MaxRetries})",
                    entry.Id, nextRetry, entry.RetryCount, entry.Options.MaxRetries);
            }
        }
    }
}

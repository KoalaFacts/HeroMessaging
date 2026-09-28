using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;
/// <summary>
/// Represents the outbox processor type.
/// </summary>

public class OutboxProcessor : PollingBackgroundServiceBase<OutboxWorkItem>, IOutboxProcessor, IAsyncDisposable
{
    private static readonly TimeSpan ExternalLeaseDuration = TimeSpan.FromMinutes(1);
    private readonly IOutboxStorage _outboxStorage;
    private readonly IExternalOutboxStorage? _externalStorage;
    private readonly ExternalOutboxDelivery? _externalDelivery;
    private readonly CancellationTokenSource _shutdown = new();
    private bool _stopTimedOut;
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
        IMessageSerializer? messageSerializer = null)
        : base(logger, timeProvider, maxDegreeOfParallelism: Environment.ProcessorCount, boundedCapacity: 100)
    {
        _outboxStorage = outboxStorage;
        _externalStorage = outboxStorage as IExternalOutboxStorage;
        if (_externalStorage?.SupportsExternalClaims == true && transport is IConfirmedQueueTransport confirmedTransport && messageSerializer is not null)
            _externalDelivery = new ExternalOutboxDelivery(_externalStorage, confirmedTransport, messageSerializer, timeProvider, logger);
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
                throw new NotSupportedException("External outbox delivery requires leased storage, a confirmed queue transport, and a message serializer.");
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
    /// Stops polling and waits for in-flight deliveries until the host's shutdown deadline.
    /// </summary>
    public new async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await base.StopAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _shutdown.CancelAsync().ConfigureAwait(false);
            _stopTimedOut = true;
            Logger.LogWarning("Outbox processor did not drain before the host shutdown deadline");
        }
    }

    Task IOutboxProcessor.StopAsync(CancellationToken cancellationToken) => StopAsync(cancellationToken);

    /// <summary>
    /// Disposes the processor without waiting again after a host shutdown deadline has expired.
    /// </summary>
    public new async ValueTask DisposeAsync()
    {
        if (_stopTimedOut)
            return;

        await base.DisposeAsync().ConfigureAwait(false);
        _shutdown.Dispose();
    }

    ValueTask IAsyncDisposable.DisposeAsync() => DisposeAsync();
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
    /// Avoid a fixed delay between external deliveries while preserving the idle polling interval.
    /// </summary>
    protected override TimeSpan GetPollingDelay(bool hasWork)
    {
        if (_externalDelivery is not null && (hasWork || Volatile.Read(ref _externalClaimInFlight) != 0))
            return TimeSpan.FromMilliseconds(25);

        return base.GetPollingDelay(hasWork);
    }
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
                await _externalDelivery!.DeliverAsync(entry, token, _shutdown.Token);
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

            await ScopedMessagingExecutor.DispatchAsync(_serviceProvider, entry.Message, Logger, "outbox", _shutdown.Token);

            await _outboxStorage.MarkProcessedAsync(entry.Id);

            Logger.LogInformation("Outbox entry {EntryId} processed successfully", entry.Id);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            await _outboxStorage.UpdateRetryCountAsync(entry.Id, entry.RetryCount, _timeProvider.GetUtcNow());
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

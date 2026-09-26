using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;
/// <summary>
/// Represents the outbox processor type.
/// </summary>

public class OutboxProcessor : PollingBackgroundServiceBase<OutboxEntry>, IOutboxProcessor
{
    private readonly IOutboxStorage _outboxStorage;
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
        TimeProvider timeProvider)
        : base(logger, timeProvider, maxDegreeOfParallelism: Environment.ProcessorCount, boundedCapacity: 100)
    {
        _outboxStorage = outboxStorage;
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }
    /// <summary>
    /// Executes publish to outbox async.
    /// </summary>

    public async Task PublishToOutboxAsync(IMessage message, OutboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new OutboxOptions();

        if (!string.IsNullOrEmpty(options.Destination))
            throw new NotSupportedException("External outbox destinations are not supported. The message was not stored or delivered.");

        var entry = await _outboxStorage.AddAsync(message, options, cancellationToken);

        // Trigger immediate processing for high priority messages
        if (options.Priority > 5)
        {
            await SubmitWorkItemAsync(entry, cancellationToken);
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

    protected override async Task<IEnumerable<OutboxEntry>> PollForWorkItemsAsync(CancellationToken cancellationToken)
    {
        return await _outboxStorage.GetPendingAsync(100, cancellationToken);
    }
    /// <summary>
    /// Executes process work item async.
    /// </summary>

    protected override async Task ProcessWorkItemAsync(OutboxEntry entry)
    {
        try
        {
            // Mark as processing to prevent duplicate processing
            entry.Status = OutboxStatus.Processing;

            if (!string.IsNullOrEmpty(entry.Options.Destination))
            {
                throw new NotSupportedException("External outbox destinations are not supported. The message was not delivered.");
            }
            else
            {
                // Process internally
                await ScopedMessagingExecutor.DispatchAsync(_serviceProvider, entry.Message, Logger, "outbox");
            }

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

using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;
/// <summary>
/// Represents the inbox processor type.
/// </summary>

public class InboxProcessor : PollingBackgroundServiceBase<InboxEntry>, IInboxProcessor
{
    private readonly IInboxStorage _inboxStorage;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;
    /// <summary>
    /// Represents cleanup task.
    /// </summary>
    private Task? _cleanupTask;
    private CancellationTokenSource? _cleanupCancellationTokenSource;
    /// <summary>
    /// Initializes a new instance of the <see cref="InboxProcessor"/> class.
    /// </summary>

    public InboxProcessor(
        IInboxStorage inboxStorage,
        IServiceProvider serviceProvider,
        ILogger<InboxProcessor> logger,
        TimeProvider timeProvider)
        : base(logger, timeProvider, maxDegreeOfParallelism: 1, boundedCapacity: 100, ensureOrdered: true)
    {
        _inboxStorage = inboxStorage;
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }
    /// <summary>
    /// Executes process incoming async.
    /// </summary>

    public async Task<bool> ProcessIncomingAsync(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new InboxOptions();

        // Check for duplicates if idempotency is required
        if (options.RequireIdempotency)
        {
            var isDuplicate = await _inboxStorage.IsDuplicateAsync(
                message.MessageId.ToString(),
                options.DeduplicationWindow,
                cancellationToken);

            if (isDuplicate)
            {
                Logger.LogWarning("Duplicate message detected: {MessageId}. Skipping processing.", message.MessageId);
                return false;
            }
        }

        // Add to inbox
        var entry = await _inboxStorage.AddAsync(message, options, cancellationToken);

        if (entry == null)
        {
            Logger.LogWarning("Message {MessageId} was rejected as duplicate", message.MessageId);
            return false;
        }

        // Process immediately
        await SubmitWorkItemAsync(entry, cancellationToken);

        return true;
    }
    /// <summary>
    /// Executes start async.
    /// </summary>

    public new Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Start cleanup task in addition to base polling
        _cleanupCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cleanupTask = RunCleanup(_cleanupCancellationTokenSource.Token);

        return base.StartAsync(cancellationToken);
    }
    /// <summary>
    /// Executes stop async.
    /// </summary>

    public new async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _cleanupCancellationTokenSource?.Cancel();

        if (_cleanupTask != null)
            await _cleanupTask.ConfigureAwait(false);

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
    /// <summary>
    /// Gets is running.
    /// </summary>

    public new bool IsRunning => base.IsRunning;
    /// <summary>
    /// Executes get metrics.
    /// </summary>

    public IInboxProcessorMetrics GetMetrics()
    {
        return new InboxProcessorMetrics
        {
            ProcessedMessages = 0, // TODO: Track metrics
            DuplicateMessages = 0,
            FailedMessages = 0,
            DeduplicationRate = 0.0
        };
    }
    /// <summary>
    /// Represents the inbox processor metrics type.
    /// </summary>

    private class InboxProcessorMetrics : IInboxProcessorMetrics
    {
        public long ProcessedMessages { get; init; }
        public long DuplicateMessages { get; init; }
        public long FailedMessages { get; init; }
        /// <summary>
        /// Gets deduplication rate.
        /// </summary>
        public double DeduplicationRate { get; init; }
    }
    /// <summary>
    /// Executes get service name.
    /// </summary>

    protected override string GetServiceName() => "Inbox processor";
    /// <summary>
    /// Executes poll for work items async.
    /// </summary>

    protected override async Task<IEnumerable<InboxEntry>> PollForWorkItemsAsync(CancellationToken cancellationToken)
    {
        return await _inboxStorage.GetUnprocessedAsync(100, cancellationToken);
    }
    /// <summary>
    /// Executes get polling delay.
    /// </summary>

    protected override TimeSpan GetPollingDelay(bool hasWork)
    {
        return hasWork ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(5);
    }
    /// <summary>
    /// Executes run cleanup.
    /// </summary>

    private async Task RunCleanup(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(1), _timeProvider, cancellationToken);

                await _inboxStorage.CleanupOldEntriesAsync(TimeSpan.FromDays(7), cancellationToken);

                Logger.LogDebug("Inbox cleanup completed");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error during inbox cleanup");
            }
        }
    }
    /// <summary>
    /// Executes process work item async.
    /// </summary>

    protected override async Task ProcessWorkItemAsync(InboxEntry entry)
    {
        try
        {
            // Mark as processing
            entry.Status = InboxStatus.Processing;

            // Process based on message type
            await ScopedMessagingExecutor.DispatchAsync(_serviceProvider, entry.Message, Logger, "inbox");

            await _inboxStorage.MarkProcessedAsync(entry.Id);

            Logger.LogInformation("Inbox entry {EntryId} (Message: {MessageId}) processed successfully from source {Source}",
                entry.Id, entry.Message.MessageId, entry.Options.Source ?? "Unknown");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing inbox entry {EntryId} (Message: {MessageId})",
                entry.Id, entry.Message.MessageId);

            await _inboxStorage.MarkFailedAsync(entry.Id, ex.Message);
        }
    }
    /// <summary>
    /// Executes get unprocessed count async.
    /// </summary>

    public async Task<long> GetUnprocessedCountAsync(CancellationToken cancellationToken = default)
    {
        return await _inboxStorage.GetUnprocessedCountAsync(cancellationToken);
    }
}

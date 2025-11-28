using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;

public class InboxProcessor : PollingBackgroundServiceBase<InboxEntry>, IInboxProcessor
{
    private readonly IInboxStorage _inboxStorage;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;
    private Task? _cleanupTask;
    private CancellationTokenSource? _cleanupCancellationTokenSource;

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

    public async Task<bool> ProcessIncoming(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default)
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
        await SubmitWorkItem(entry, cancellationToken);

        return true;
    }

    public new Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Start cleanup task in addition to base polling
        _cleanupCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cleanupTask = RunCleanup(_cleanupCancellationTokenSource.Token);

        return base.StartAsync(cancellationToken);
    }

    public new async Task StopAsync()
    {
        _cleanupCancellationTokenSource?.Cancel();

        if (_cleanupTask != null)
            await _cleanupTask;

        await base.StopAsync();
    }

    public new bool IsRunning => base.IsRunning;

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

    private class InboxProcessorMetrics : IInboxProcessorMetrics
    {
        public long ProcessedMessages { get; init; }
        public long DuplicateMessages { get; init; }
        public long FailedMessages { get; init; }
        public double DeduplicationRate { get; init; }
    }

    protected override string GetServiceName() => "Inbox processor";

    protected override async Task<IEnumerable<InboxEntry>> PollForWorkItems(CancellationToken cancellationToken)
    {
        return await _inboxStorage.GetUnprocessedAsync(100, cancellationToken);
    }

    protected override TimeSpan GetPollingDelay(bool hasWork)
    {
        return hasWork ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(5);
    }

    private async Task RunCleanup(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Clean up old processed entries every hour
#if NET8_0_OR_GREATER
                await Task.Delay(TimeSpan.FromHours(1), _timeProvider, cancellationToken);
#else
                await Task.Delay(TimeSpan.FromHours(1), cancellationToken);
#endif

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

    protected override async Task ProcessWorkItem(InboxEntry entry)
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

    public async Task<long> GetUnprocessedCount(CancellationToken cancellationToken = default)
    {
        return await _inboxStorage.GetUnprocessedCountAsync(cancellationToken);
    }
}
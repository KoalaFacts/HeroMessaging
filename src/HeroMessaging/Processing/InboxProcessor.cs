using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;

public class InboxProcessor : PollingBackgroundServiceBase<InboxEntry>, IInboxProcessor
{
    private readonly IInboxStorage _inboxStorage;
    private readonly IServiceProvider _serviceProvider;
    private Task? _cleanupTask;
    private CancellationTokenSource? _cleanupCancellationTokenSource;

    public InboxProcessor(
        IInboxStorage inboxStorage,
        IServiceProvider serviceProvider,
        ILogger<InboxProcessor> logger)
        : base(logger, maxDegreeOfParallelism: 1, boundedCapacity: 100, ensureOrdered: true)
    {
        _inboxStorage = inboxStorage;
        _serviceProvider = serviceProvider;
    }

    public async Task<bool> ProcessIncoming(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new InboxOptions();

        // Check for duplicates if idempotency is required
        if (options.RequireIdempotency)
        {
            var isDuplicate = await _inboxStorage.IsDuplicate(
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
        var entry = await _inboxStorage.Add(message, options, cancellationToken);

        if (entry == null)
        {
            Logger.LogWarning("Message {MessageId} was rejected as duplicate", message.MessageId);
            return false;
        }

        // Process immediately
        await SubmitWorkItem(entry, cancellationToken);

        return true;
    }

    public new Task Start(CancellationToken cancellationToken = default)
    {
        // Start cleanup task in addition to base polling
        _cleanupCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cleanupTask = RunCleanup(_cleanupCancellationTokenSource.Token);

        return base.Start(cancellationToken);
    }

    public new async Task Stop()
    {
        _cleanupCancellationTokenSource?.Cancel();

        if (_cleanupTask != null)
            await _cleanupTask;

        await base.Stop();
    }

    protected override string GetServiceName() => "Inbox processor";

    protected override async Task<IEnumerable<InboxEntry>> PollForWorkItems(CancellationToken cancellationToken)
    {
        return await _inboxStorage.GetUnprocessed(100, cancellationToken);
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
                await Task.Delay(TimeSpan.FromHours(1), cancellationToken);

                await _inboxStorage.CleanupOldEntries(TimeSpan.FromDays(7), cancellationToken);

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

            using var scope = _serviceProvider.CreateScope();
            var messaging = scope.ServiceProvider.GetRequiredService<IHeroMessaging>();

            // Process based on message type
            switch (entry.Message)
            {
                case ICommand command:
                    await messaging.SendAsync(command);
                    break;

                case IEvent @event:
                    await messaging.PublishAsync(@event);
                    break;

                default:
                    Logger.LogWarning("Unknown message type in inbox: {MessageType}",
                        entry.Message.GetType().Name);
                    break;
            }

            await _inboxStorage.MarkProcessed(entry.Id);

            Logger.LogInformation("Inbox entry {EntryId} (Message: {MessageId}) processed successfully from source {Source}",
                entry.Id, entry.Message.MessageId, entry.Options.Source ?? "Unknown");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing inbox entry {EntryId} (Message: {MessageId})",
                entry.Id, entry.Message.MessageId);

            await _inboxStorage.MarkFailed(entry.Id, ex.Message);
        }
    }

    public async Task<long> GetUnprocessedCount(CancellationToken cancellationToken = default)
    {
        return await _inboxStorage.GetUnprocessedCount(cancellationToken);
    }
}

public interface IInboxProcessor
{
    Task<bool> ProcessIncoming(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default);
    Task Start(CancellationToken cancellationToken = default);
    Task Stop();
    Task<long> GetUnprocessedCount(CancellationToken cancellationToken = default);
}
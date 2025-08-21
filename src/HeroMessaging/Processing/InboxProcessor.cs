using System.Threading.Tasks.Dataflow;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;

public class InboxProcessor : IInboxProcessor
{
    private readonly IInboxStorage _inboxStorage;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InboxProcessor> _logger;
    private readonly ActionBlock<InboxEntry> _processingBlock;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _pollingTask;
    private Task? _cleanupTask;

    public InboxProcessor(
        IInboxStorage inboxStorage,
        IServiceProvider serviceProvider,
        ILogger<InboxProcessor> logger)
    {
        _inboxStorage = inboxStorage;
        _serviceProvider = serviceProvider;
        _logger = logger;

        _processingBlock = new ActionBlock<InboxEntry>(
            ProcessInboxEntry,
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 1, // Sequential processing for ordering
                BoundedCapacity = 100,
                EnsureOrdered = true
            });
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
                _logger.LogWarning("Duplicate message detected: {MessageId}. Skipping processing.", message.MessageId);
                return false;
            }
        }
        
        // Add to inbox
        var entry = await _inboxStorage.Add(message, options, cancellationToken);
        
        if (entry == null)
        {
            _logger.LogWarning("Message {MessageId} was rejected as duplicate", message.MessageId);
            return false;
        }
        
        _logger.LogDebug("Message {MessageId} added to inbox from source {Source}", 
            message.MessageId, options.Source ?? "Unknown");
        
        // Process immediately
        await _processingBlock.SendAsync(entry, cancellationToken);
        
        return true;
    }

    public Task Start(CancellationToken cancellationToken = default)
    {
        if (_cancellationTokenSource != null)
            return Task.CompletedTask;

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pollingTask = PollInbox(_cancellationTokenSource.Token);
        _cleanupTask = RunCleanup(_cancellationTokenSource.Token);
        
        _logger.LogInformation("Inbox processor started");
        return Task.CompletedTask;
    }

    public async Task Stop()
    {
        _cancellationTokenSource?.Cancel();
        _processingBlock.Complete();
        
        if (_pollingTask != null)
            await _pollingTask;
        
        if (_cleanupTask != null)
            await _cleanupTask;
        
        await _processingBlock.Completion;
        
        _logger.LogInformation("Inbox processor stopped");
    }

    private async Task PollInbox(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var entries = await _inboxStorage.GetUnprocessed(100, cancellationToken);
                
                foreach (var entry in entries)
                {
                    await _processingBlock.SendAsync(entry, cancellationToken);
                }
                
                await Task.Delay(entries.Any() ? 100 : 5000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling inbox");
                await Task.Delay(5000, cancellationToken);
            }
        }
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
                
                _logger.LogDebug("Inbox cleanup completed");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during inbox cleanup");
            }
        }
    }

    private async Task ProcessInboxEntry(InboxEntry entry)
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
                    await messaging.Send(command);
                    break;
                    
                case IEvent @event:
                    await messaging.Publish(@event);
                    break;
                    
                default:
                    _logger.LogWarning("Unknown message type in inbox: {MessageType}", 
                        entry.Message.GetType().Name);
                    break;
            }
            
            await _inboxStorage.MarkProcessed(entry.Id);
            
            _logger.LogInformation("Inbox entry {EntryId} (Message: {MessageId}) processed successfully from source {Source}",
                entry.Id, entry.Message.MessageId, entry.Options.Source ?? "Unknown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing inbox entry {EntryId} (Message: {MessageId})",
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
using System.Threading.Tasks.Dataflow;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Core.Processing;

public class OutboxProcessor : IOutboxProcessor
{
    private readonly IOutboxStorage _outboxStorage;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly ActionBlock<OutboxEntry> _processingBlock;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _pollingTask;

    public OutboxProcessor(
        IOutboxStorage outboxStorage,
        IServiceProvider serviceProvider,
        ILogger<OutboxProcessor> logger)
    {
        _outboxStorage = outboxStorage;
        _serviceProvider = serviceProvider;
        _logger = logger;

        _processingBlock = new ActionBlock<OutboxEntry>(
            ProcessOutboxEntry,
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                BoundedCapacity = 100
            });
    }

    public async Task PublishToOutbox(IMessage message, OutboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new OutboxOptions();
        
        var entry = await _outboxStorage.Add(message, options, cancellationToken);
        
        _logger.LogDebug("Message {MessageId} added to outbox with priority {Priority}", 
            message.MessageId, options.Priority);
        
        // Trigger immediate processing for high priority messages
        if (options.Priority > 5)
        {
            await _processingBlock.SendAsync(entry, cancellationToken);
        }
    }

    public Task Start(CancellationToken cancellationToken = default)
    {
        if (_cancellationTokenSource != null)
            return Task.CompletedTask;

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pollingTask = PollOutbox(_cancellationTokenSource.Token);
        
        _logger.LogInformation("Outbox processor started");
        return Task.CompletedTask;
    }

    public async Task Stop()
    {
        _cancellationTokenSource?.Cancel();
        _processingBlock.Complete();
        
        if (_pollingTask != null)
            await _pollingTask;
        
        await _processingBlock.Completion;
        
        _logger.LogInformation("Outbox processor stopped");
    }

    private async Task PollOutbox(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var entries = await _outboxStorage.GetPending(100, cancellationToken);
                
                foreach (var entry in entries)
                {
                    await _processingBlock.SendAsync(entry, cancellationToken);
                }
                
                await Task.Delay(entries.Any() ? 100 : 1000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling outbox");
                await Task.Delay(5000, cancellationToken);
            }
        }
    }

    private async Task ProcessOutboxEntry(OutboxEntry entry)
    {
        try
        {
            // Mark as processing to prevent duplicate processing
            entry.Status = OutboxStatus.Processing;
            
            // Simulate sending to external system based on destination
            if (!string.IsNullOrEmpty(entry.Options.Destination))
            {
                await SendToExternalSystem(entry);
            }
            else
            {
                // Process internally
                using var scope = _serviceProvider.CreateScope();
                var messaging = scope.ServiceProvider.GetRequiredService<IHeroMessaging>();
                
                switch (entry.Message)
                {
                    case ICommand command:
                        await messaging.Send(command);
                        break;
                    case IEvent @event:
                        await messaging.Publish(@event);
                        break;
                }
            }
            
            await _outboxStorage.MarkProcessed(entry.Id);
            
            _logger.LogInformation("Outbox entry {EntryId} processed successfully", entry.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing outbox entry {EntryId}", entry.Id);
            
            entry.RetryCount++;
            
            if (entry.RetryCount >= entry.Options.MaxRetries)
            {
                await _outboxStorage.MarkFailed(entry.Id, ex.Message);
                _logger.LogError("Outbox entry {EntryId} failed after {RetryCount} retries", 
                    entry.Id, entry.RetryCount);
            }
            else
            {
                var delay = entry.Options.RetryDelay ?? TimeSpan.FromSeconds(Math.Pow(2, entry.RetryCount));
                var nextRetry = DateTime.UtcNow.Add(delay);
                
                await _outboxStorage.UpdateRetryCount(entry.Id, entry.RetryCount, nextRetry);
                
                _logger.LogWarning("Outbox entry {EntryId} will be retried at {NextRetry} (attempt {RetryCount}/{MaxRetries})",
                    entry.Id, nextRetry, entry.RetryCount, entry.Options.MaxRetries);
            }
        }
    }

    private async Task SendToExternalSystem(OutboxEntry entry)
    {
        // This is where you would implement actual external system integration
        // For now, we'll simulate it
        _logger.LogInformation("Sending message {MessageId} to external system: {Destination}",
            entry.Message.MessageId, entry.Options.Destination);
        
        // Simulate network call
        await Task.Delay(100);
        
        // Simulate occasional failures for testing
        if (Random.Shared.Next(10) == 0)
        {
            throw new InvalidOperationException($"Failed to send to {entry.Options.Destination}");
        }
    }
}

public interface IOutboxProcessor
{
    Task PublishToOutbox(IMessage message, OutboxOptions? options = null, CancellationToken cancellationToken = default);
    Task Start(CancellationToken cancellationToken = default);
    Task Stop();
}
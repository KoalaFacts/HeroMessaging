using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Core.Processing;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Core;

public class HeroMessagingService : IHeroMessaging
{
    private readonly ICommandProcessor _commandProcessor;
    private readonly IQueryProcessor _queryProcessor;
    private readonly IEventBus _eventBus;
    private readonly IQueueProcessor? _queueProcessor;
    private readonly IOutboxProcessor? _outboxProcessor;
    private readonly IInboxProcessor? _inboxProcessor;
    private readonly ILogger<HeroMessagingService> _logger;
    
    private readonly MessagingMetrics _metrics = new();
    
    public HeroMessagingService(
        ICommandProcessor commandProcessor,
        IQueryProcessor queryProcessor,
        IEventBus eventBus,
        ILogger<HeroMessagingService> logger,
        IQueueProcessor? queueProcessor = null,
        IOutboxProcessor? outboxProcessor = null,
        IInboxProcessor? inboxProcessor = null)
    {
        _commandProcessor = commandProcessor;
        _queryProcessor = queryProcessor;
        _eventBus = eventBus;
        _queueProcessor = queueProcessor;
        _outboxProcessor = outboxProcessor;
        _inboxProcessor = inboxProcessor;
        _logger = logger;
    }

    public async Task Send(ICommand command, CancellationToken cancellationToken = default)
    {
        _metrics.CommandsSent++;
        await _commandProcessor.Send(command, cancellationToken);
    }

    public async Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        _metrics.CommandsSent++;
        return await _commandProcessor.Send(command, cancellationToken);
    }

    public async Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        _metrics.QueriesSent++;
        return await _queryProcessor.Send(query, cancellationToken);
    }

    public async Task Publish(IEvent @event, CancellationToken cancellationToken = default)
    {
        _metrics.EventsPublished++;
        await _eventBus.Publish(@event, cancellationToken);
    }

    public async Task Enqueue(IMessage message, string queueName, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (_queueProcessor == null)
            throw new InvalidOperationException("Queue functionality is not enabled. Call WithQueues() during configuration.");
        
        _metrics.MessagesQueued++;
        await _queueProcessor.Enqueue(message, queueName, options, cancellationToken);
    }

    public async Task StartQueue(string queueName, CancellationToken cancellationToken = default)
    {
        if (_queueProcessor == null)
            throw new InvalidOperationException("Queue functionality is not enabled. Call WithQueues() during configuration.");
            
        await _queueProcessor.StartQueue(queueName, cancellationToken);
    }

    public async Task StopQueue(string queueName, CancellationToken cancellationToken = default)
    {
        if (_queueProcessor == null)
            throw new InvalidOperationException("Queue functionality is not enabled. Call WithQueues() during configuration.");
            
        await _queueProcessor.StopQueue(queueName, cancellationToken);
    }

    public async Task PublishToOutbox(IMessage message, OutboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (_outboxProcessor == null)
            throw new InvalidOperationException("Outbox functionality is not enabled. Call WithOutbox() during configuration.");
        
        _metrics.OutboxMessages++;
        await _outboxProcessor.PublishToOutbox(message, options, cancellationToken);
    }

    public async Task ProcessIncoming(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (_inboxProcessor == null)
            throw new InvalidOperationException("Inbox functionality is not enabled. Call WithInbox() during configuration.");
        
        _metrics.InboxMessages++;
        await _inboxProcessor.ProcessIncoming(message, options, cancellationToken);
    }

    public MessagingMetrics GetMetrics()
    {
        return _metrics;
    }

    public MessagingHealth GetHealth()
    {
        return new MessagingHealth
        {
            IsHealthy = true,
            Components = new Dictionary<string, ComponentHealth>
            {
                ["CommandProcessor"] = new ComponentHealth { IsHealthy = true, Status = "Operational", LastChecked = DateTime.UtcNow },
                ["QueryProcessor"] = new ComponentHealth { IsHealthy = true, Status = "Operational", LastChecked = DateTime.UtcNow },
                ["EventBus"] = new ComponentHealth { IsHealthy = true, Status = "Operational", LastChecked = DateTime.UtcNow },
                ["QueueProcessor"] = new ComponentHealth { IsHealthy = _queueProcessor != null, Status = _queueProcessor != null ? "Operational" : "Not Configured", LastChecked = DateTime.UtcNow },
                ["OutboxProcessor"] = new ComponentHealth { IsHealthy = _outboxProcessor != null, Status = _outboxProcessor != null ? "Operational" : "Not Configured", LastChecked = DateTime.UtcNow },
                ["InboxProcessor"] = new ComponentHealth { IsHealthy = _inboxProcessor != null, Status = _inboxProcessor != null ? "Operational" : "Not Configured", LastChecked = DateTime.UtcNow }
            }
        };
    }
}
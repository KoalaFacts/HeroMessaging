using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Processing;
using Microsoft.Extensions.Logging;

namespace HeroMessaging;

/// <summary>
/// Default implementation of <see cref="IHeroMessaging"/> providing unified access to all messaging patterns
/// </summary>
/// <remarks>
/// This service acts as the main entry point for HeroMessaging functionality, coordinating between
/// command processing, query handling, event publishing, queue management, and transactional messaging
/// patterns (outbox/inbox). It aggregates metrics and provides health status for all configured components.
///
/// Supported messaging patterns:
/// - Commands: Send and await responses from command handlers (mediator pattern)
/// - Queries: Send queries and receive responses (CQRS query side)
/// - Events: Publish events to multiple subscribers (pub/sub pattern)
/// - Queues: Enqueue messages for asynchronous processing with competing consumers
/// - Outbox: Publish messages transactionally with guaranteed delivery
/// - Inbox: Process incoming messages idempotently with deduplication
///
/// Component availability:
/// - CommandProcessor, QueryProcessor, EventBus: Always available
/// - QueueProcessor: Available when configured with WithQueues()
/// - OutboxProcessor: Available when configured with WithOutbox()
/// - InboxProcessor: Available when configured with WithInbox()
///
/// Metrics tracking:
/// - Commands sent
/// - Queries executed
/// - Events published
/// - Messages queued
/// - Outbox messages
/// - Inbox messages
///
/// Example usage:
/// <code>
/// // Inject the service
/// public class OrderService
/// {
///     private readonly IHeroMessaging _messaging;
///
///     public OrderService(IHeroMessaging messaging)
///     {
///         _messaging = messaging;
///     }
///
///     public async Task CreateOrder(CreateOrderCommand command)
///     {
///         // Send command and await response
///         var orderId = await _messaging.Send(command);
///
///         // Publish event about order creation
///         await _messaging.Publish(new OrderCreatedEvent(orderId));
///     }
/// }
/// </code>
/// </remarks>
public class HeroMessagingService(
    ICommandProcessor commandProcessor,
    IQueryProcessor queryProcessor,
    IEventBus eventBus,
    TimeProvider timeProvider,
    IQueueProcessor? queueProcessor = null,
    IOutboxProcessor? outboxProcessor = null,
    IInboxProcessor? inboxProcessor = null) : IHeroMessaging
{
    private readonly ICommandProcessor _commandProcessor = commandProcessor;
    private readonly IQueryProcessor _queryProcessor = queryProcessor;
    private readonly IEventBus _eventBus = eventBus;
    private readonly IQueueProcessor? _queueProcessor = queueProcessor;
    private readonly IOutboxProcessor? _outboxProcessor = outboxProcessor;
    private readonly IInboxProcessor? _inboxProcessor = inboxProcessor;
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    private readonly MessagingMetrics _metrics = new();

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
        var now = _timeProvider.GetUtcNow().DateTime;
        return new MessagingHealth
        {
            IsHealthy = true,
            Components = new Dictionary<string, ComponentHealth>
            {
                ["CommandProcessor"] = new ComponentHealth { IsHealthy = true, Status = "Operational", LastChecked = now },
                ["QueryProcessor"] = new ComponentHealth { IsHealthy = true, Status = "Operational", LastChecked = now },
                ["EventBus"] = new ComponentHealth { IsHealthy = true, Status = "Operational", LastChecked = now },
                ["QueueProcessor"] = new ComponentHealth { IsHealthy = _queueProcessor != null, Status = _queueProcessor != null ? "Operational" : "Not Configured", LastChecked = now },
                ["OutboxProcessor"] = new ComponentHealth { IsHealthy = _outboxProcessor != null, Status = _outboxProcessor != null ? "Operational" : "Not Configured", LastChecked = now },
                ["InboxProcessor"] = new ComponentHealth { IsHealthy = _inboxProcessor != null, Status = _inboxProcessor != null ? "Operational" : "Not Configured", LastChecked = now }
            }
        };
    }
}
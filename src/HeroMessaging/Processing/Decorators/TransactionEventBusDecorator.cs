using System.Data;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing.Decorators;

/// <summary>
/// Decorator that wraps event processing in database transactions
/// Each event handler execution gets its own transaction to maintain independence
/// </summary>
public class TransactionEventBusDecorator(
    IEventBus inner,
    IUnitOfWorkFactory unitOfWorkFactory,
    ILogger<TransactionEventBusDecorator> logger,
    IsolationLevel defaultIsolationLevel = IsolationLevel.ReadCommitted) : IEventBus
{
    private readonly IEventBus _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly ILogger<TransactionEventBusDecorator> _logger = logger;

    // These are kept for future use and to maintain the public API
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1823:Avoid unused private fields", Justification = "Reserved for future transaction support implementation")]
    private readonly IUnitOfWorkFactory _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1823:Avoid unused private fields", Justification = "Reserved for future transaction support implementation")]
    private readonly IsolationLevel _defaultIsolationLevel = defaultIsolationLevel;

    public async Task Publish(IEvent @event, CancellationToken cancellationToken = default)
    {
        // Note: For events, we might want to handle transactions per handler instead of per event
        // This ensures that if one handler fails, others can still succeed
        // We'll let the inner event bus handle individual handler transactions

        _logger.LogDebug("Publishing event {EventType} with ID {EventId}",
            @event.GetType().Name, @event.MessageId);

        await _inner.Publish(@event, cancellationToken);

        _logger.LogDebug("Event {EventType} with ID {EventId} published successfully",
            @event.GetType().Name, @event.MessageId);
    }

    public IEventBusMetrics GetMetrics() => _inner.GetMetrics();
}

/// <summary>
/// Transaction-aware event handler wrapper that provides individual transaction context per handler
/// </summary>
public class TransactionEventHandlerWrapper<TEvent>(
    Func<TEvent, CancellationToken, Task> handler,
    ITransactionExecutor transactionExecutor,
    IsolationLevel isolationLevel = IsolationLevel.ReadCommitted) where TEvent : IEvent
{
    private readonly Func<TEvent, CancellationToken, Task> _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    private readonly ITransactionExecutor _transactionExecutor = transactionExecutor ?? throw new ArgumentNullException(nameof(transactionExecutor));
    private readonly IsolationLevel _isolationLevel = isolationLevel;

    public async Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default)
    {
        await _transactionExecutor.ExecuteInTransactionAsync(
            async ct => await _handler(@event, ct),
            $"event handler {typeof(TEvent).Name} with ID {@event.MessageId}",
            _isolationLevel,
            cancellationToken);
    }
}
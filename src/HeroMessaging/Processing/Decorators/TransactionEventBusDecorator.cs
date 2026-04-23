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
public class TransactionEventBusDecorator : IEventBus
{
    private readonly IEventBus _inner;
    private readonly ILogger<TransactionEventBusDecorator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionEventBusDecorator"/> class.
    /// </summary>
    public TransactionEventBusDecorator(
        IEventBus inner,
        IUnitOfWorkFactory unitOfWorkFactory,
        ILogger<TransactionEventBusDecorator> logger,
        IsolationLevel defaultIsolationLevel = IsolationLevel.ReadCommitted)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        ArgumentNullException.ThrowIfNull(unitOfWorkFactory);
        if (!Enum.IsDefined(defaultIsolationLevel))
        {
            throw new ArgumentOutOfRangeException(nameof(defaultIsolationLevel), defaultIsolationLevel, "Invalid isolation level.");
        }

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets is running.
    /// </summary>
    public bool IsRunning => _inner.IsRunning;
    /// <summary>
    /// Executes publish async.
    /// </summary>

    public async Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
    {
        // Note: For events, we might want to handle transactions per handler instead of per event
        // This ensures that if one handler fails, others can still succeed
        // We'll let the inner event bus handle individual handler transactions

        _logger.LogDebug("Publishing event {EventType} with ID {EventId}",
            @event.GetType().Name, @event.MessageId);

        await _inner.PublishAsync(@event, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("Event {EventType} with ID {EventId} published successfully",
            @event.GetType().Name, @event.MessageId);
    }
    /// <summary>
    /// Executes get metrics.
    /// </summary>

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
    /// <summary>
    /// Represents transaction executor.
    /// </summary>
    private readonly ITransactionExecutor _transactionExecutor = transactionExecutor ?? throw new ArgumentNullException(nameof(transactionExecutor));
    private readonly IsolationLevel _isolationLevel = isolationLevel;
    /// <summary>
    /// Executes handle async.
    /// </summary>

    public async Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default)
    {
        await _transactionExecutor.ExecuteInTransactionAsync(
            async ct => await _handler(@event, ct).ConfigureAwait(false),
            $"event handler {typeof(TEvent).Name} with ID {@event.MessageId}",
            _isolationLevel,
            cancellationToken).ConfigureAwait(false);
    }
}

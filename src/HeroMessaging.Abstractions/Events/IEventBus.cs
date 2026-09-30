using HeroMessaging.Abstractions.Processing;

namespace HeroMessaging.Abstractions.Events;

/// <summary>
/// Defines the contract for an event bus that publishes events to registered handlers
/// </summary>
public interface IEventBus : IProcessor
{
    /// <summary>
    /// Publishes an event to all registered handlers
    /// </summary>
    /// <param name="event">The event to publish</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default);

    /// <summary>Publishes an in-process event and waits for all handler pipeline outcomes.</summary>
    /// <param name="event">The event to publish.</param>
    /// <param name="cancellationToken">Cancels waiting, not handler execution or ongoing admission.</param>
    /// <returns>A receipt in handler registration order; no handlers produces an empty successful receipt.</returns>
    /// <remarks>
    /// A token already cancelled at entry prevents publication. After publication starts, cancellation
    /// stops only the caller's wait; accepted and pending deliveries continue. This is not a durable acknowledgement.
    /// Awaiting a receipt inside a handler on the same bus can deadlock when capacity or concurrency is exhausted.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The event is null.</exception>
    /// <exception cref="OperationCanceledException">The caller's wait was cancelled.</exception>
    Task<EventPublishReceipt> PublishAndWaitAsync(IEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets metrics about event bus performance
    /// </summary>
    IEventBusMetrics GetMetrics();
}

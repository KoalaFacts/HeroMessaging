using HeroMessaging.Abstractions.Events;

namespace HeroMessaging.Abstractions;

/// <summary>
/// Provides event publishing capabilities.
/// </summary>
/// <remarks>
/// This interface is a subset of <see cref="IHeroMessaging"/> for consumers
/// that only need to publish events. Use this for dependency injection when
/// your component only needs event publishing capabilities.
/// </remarks>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes an event to all registered event handlers.
    /// </summary>
    /// <param name="event">The event to publish.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="event"/> is null.</exception>
    Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default);

    /// <summary>Publishes an in-process event and waits for its handler pipeline outcomes.</summary>
    /// <param name="event">The event to publish.</param>
    /// <param name="cancellationToken">Cancels waiting, not handler execution or ongoing admission.</param>
    /// <returns>Final outcomes in handler registration order; no handlers is an empty successful no-op.</returns>
    /// <remarks>
    /// Cancellation at entry prevents publication; cancellation afterwards only stops waiting.
    /// Delivery continues, including pending admission. This is not a durable or broker acknowledgement.
    /// Do not await a receipt inside handlers on the same bus when capacity or concurrency is exhausted.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The event is null.</exception>
    /// <exception cref="OperationCanceledException">The caller's wait was cancelled.</exception>
    Task<EventPublishReceipt> PublishAndWaitAsync(IEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes multiple events in a batch operation.
    /// </summary>
    /// <param name="events">The events to publish.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of boolean values indicating success (true) or failure (false) for each event.</returns>
    /// <remarks>
    /// Failed events do not stop processing of remaining events. The operation continues
    /// until all events are processed or the cancellation token is triggered.
    /// </remarks>
    Task<IReadOnlyList<bool>> PublishBatchAsync(IReadOnlyList<IEvent> events, CancellationToken cancellationToken = default);
}

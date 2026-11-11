using HeroMessaging.Abstractions.Events;

namespace HeroMessaging.Processing;

/// <summary>
/// Publishes an event and waits for every handler while passing the caller's token to the handlers.
/// </summary>
/// <remarks>
/// <see cref="IEventBus.PublishAndWaitAsync(IEvent, CancellationToken)"/> deliberately lets its token cancel only the wait.
/// The Inbox, Outbox and queue processors need their shutdown token to reach the handlers as well, so a
/// handler still running at stop is cancelled instead of outliving the entry it was dispatched for.
/// </remarks>
internal interface IHandlerCancellableEventPublisher
{
    /// <summary>
    /// Publishes <paramref name="event"/> and waits for every handler; <paramref name="cancellationToken"/>
    /// is passed to each handler and also cancels the wait.
    /// </summary>
    Task<EventPublishReceipt> PublishAndWaitWithHandlerCancellationAsync(IEvent @event, CancellationToken cancellationToken);
}

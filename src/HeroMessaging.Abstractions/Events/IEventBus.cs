using HeroMessaging.Abstractions.Processing;

namespace HeroMessaging.Abstractions.Events;

/// <summary>
/// Defines the contract for an event bus that publishes events to registered handlers
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publishes an event to all registered handlers
    /// </summary>
    /// <param name="event">The event to publish</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task Publish(IEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets metrics about event bus performance
    /// </summary>
    IEventBusMetrics GetMetrics();
}

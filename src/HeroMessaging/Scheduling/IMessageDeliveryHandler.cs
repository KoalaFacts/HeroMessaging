using HeroMessaging.Abstractions.Scheduling;

namespace HeroMessaging.Scheduling;

/// <summary>
/// Handles the delivery of scheduled messages when they become due.
/// </summary>
public interface IMessageDeliveryHandler
{
    /// <summary>
    /// Delivers a scheduled message that has become due.
    /// </summary>
    /// <param name="scheduledMessage">The scheduled message to deliver</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DeliverAsync(ScheduledMessage scheduledMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles a failure during message delivery.
    /// </summary>
    /// <param name="scheduleId">The schedule ID of the failed message</param>
    /// <param name="exception">The exception that occurred</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task HandleDeliveryFailureAsync(Guid scheduleId, Exception exception, CancellationToken cancellationToken = default);
}

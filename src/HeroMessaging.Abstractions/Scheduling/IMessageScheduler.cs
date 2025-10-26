using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Scheduling;

/// <summary>
/// Provides message scheduling capabilities for delayed message delivery.
/// </summary>
/// <remarks>
/// The message scheduler allows messages to be scheduled for future delivery either by specifying
/// a delay duration or an absolute delivery time. Scheduled messages can be cancelled before delivery
/// and queried for status information.
/// </remarks>
public interface IMessageScheduler
{
    /// <summary>
    /// Schedules a message for delivery after the specified delay.
    /// </summary>
    /// <typeparam name="T">The message type</typeparam>
    /// <param name="message">The message to schedule</param>
    /// <param name="delay">The delay before message delivery</param>
    /// <param name="options">Optional scheduling options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A result containing the schedule ID and delivery time</returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when delay is negative</exception>
    Task<ScheduleResult> ScheduleAsync<T>(
        T message,
        TimeSpan delay,
        SchedulingOptions? options = null,
        CancellationToken cancellationToken = default) where T : IMessage;

    /// <summary>
    /// Schedules a message for delivery at the specified absolute time.
    /// </summary>
    /// <typeparam name="T">The message type</typeparam>
    /// <param name="message">The message to schedule</param>
    /// <param name="deliverAt">The absolute time for message delivery</param>
    /// <param name="options">Optional scheduling options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A result containing the schedule ID and delivery time</returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null</exception>
    /// <exception cref="ArgumentException">Thrown when deliverAt is in the past</exception>
    Task<ScheduleResult> ScheduleAsync<T>(
        T message,
        DateTimeOffset deliverAt,
        SchedulingOptions? options = null,
        CancellationToken cancellationToken = default) where T : IMessage;

    /// <summary>
    /// Cancels a previously scheduled message.
    /// </summary>
    /// <param name="scheduleId">The ID of the scheduled message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message was cancelled; false if it was already delivered or not found</returns>
    Task<bool> CancelScheduledAsync(Guid scheduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about a scheduled message.
    /// </summary>
    /// <param name="scheduleId">The ID of the scheduled message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Information about the scheduled message, or null if not found</returns>
    Task<ScheduledMessageInfo?> GetScheduledAsync(Guid scheduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of pending scheduled messages matching the query.
    /// </summary>
    /// <param name="query">Optional query parameters to filter results</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of pending scheduled messages</returns>
    Task<IReadOnlyList<ScheduledMessageInfo>> GetPendingAsync(
        ScheduledMessageQuery? query = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total count of pending scheduled messages.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The count of pending scheduled messages</returns>
    Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default);
}

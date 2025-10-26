namespace HeroMessaging.Abstractions.Scheduling;

/// <summary>
/// Provides persistent storage for scheduled messages.
/// </summary>
/// <remarks>
/// This interface is implemented by storage providers to enable persistent message scheduling.
/// The storage must support efficient queries for messages that are due for delivery.
/// </remarks>
public interface IScheduledMessageStorage
{
    /// <summary>
    /// Adds a scheduled message to storage.
    /// </summary>
    /// <param name="message">The scheduled message to store</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The stored message entry</returns>
    Task<ScheduledMessageEntry> AddAsync(ScheduledMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves scheduled messages that are due for delivery.
    /// </summary>
    /// <param name="asOf">The time to check against (typically UtcNow)</param>
    /// <param name="limit">Maximum number of messages to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of messages due for delivery</returns>
    /// <remarks>
    /// This method should use an efficient index on DeliverAt to support high-volume scheduling.
    /// Results should be ordered by DeliverAt ascending, then by Priority descending.
    /// </remarks>
    Task<IReadOnlyList<ScheduledMessageEntry>> GetDueAsync(
        DateTimeOffset asOf,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a specific scheduled message by ID.
    /// </summary>
    /// <param name="scheduleId">The schedule ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The scheduled message entry, or null if not found</returns>
    Task<ScheduledMessageEntry?> GetAsync(Guid scheduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a scheduled message by changing its status to Cancelled.
    /// </summary>
    /// <param name="scheduleId">The schedule ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message was cancelled; false if not found or already delivered</returns>
    Task<bool> CancelAsync(Guid scheduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a scheduled message as delivered.
    /// </summary>
    /// <param name="scheduleId">The schedule ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if marked as delivered; false if not found</returns>
    Task<bool> MarkDeliveredAsync(Guid scheduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a scheduled message as failed with an error message.
    /// </summary>
    /// <param name="scheduleId">The schedule ID</param>
    /// <param name="error">The error message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if marked as failed; false if not found</returns>
    Task<bool> MarkFailedAsync(Guid scheduleId, string error, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of pending scheduled messages.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The count of pending messages</returns>
    Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries scheduled messages with filtering and paging.
    /// </summary>
    /// <param name="query">The query parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of scheduled messages matching the query</returns>
    Task<IReadOnlyList<ScheduledMessageEntry>> QueryAsync(
        ScheduledMessageQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes old scheduled messages that have been delivered or cancelled.
    /// </summary>
    /// <param name="olderThan">Remove messages older than this time</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The number of messages removed</returns>
    Task<long> CleanupAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}

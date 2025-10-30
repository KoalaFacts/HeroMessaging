using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Storage;

/// <summary>
/// Provides storage for the Outbox pattern, ensuring transactional message publishing.
/// </summary>
/// <remarks>
/// The Outbox pattern solves the dual-write problem by storing messages in the same transaction
/// as business data changes. This guarantees that messages are only published if the transaction
/// commits, maintaining consistency between database state and published events.
///
/// Pattern flow:
/// 1. Business logic updates database and adds message to outbox (same transaction)
/// 2. Transaction commits (both updates and outbox entry are persisted)
/// 3. Background processor polls outbox for pending messages
/// 4. Messages are published to message broker
/// 5. Successfully published messages are marked as processed
///
/// This ensures exactly-once publishing semantics and prevents message loss or duplicate
/// publishing due to transaction rollbacks.
///
/// Example usage:
/// <code>
/// // Within a database transaction
/// using var transaction = await unitOfWork.BeginTransactionAsync();
/// try
/// {
///     // Update business data
///     await repository.SaveOrderAsync(order);
///
///     // Add message to outbox (same transaction)
///     await outboxStorage.Add(new OrderCreatedEvent(order.Id), new OutboxOptions
///     {
///         Destination = "order-events",
///         Priority = 1,
///         MaxRetries = 3
///     });
///
///     await transaction.CommitAsync(); // Both persist together
/// }
/// catch
/// {
///     await transaction.RollbackAsync(); // Both rolled back together
///     throw;
/// }
///
/// // Background processor polls and publishes
/// var pending = await outboxStorage.GetPending(100);
/// foreach (var entry in pending)
/// {
///     await publisher.PublishAsync(entry.Message);
///     await outboxStorage.MarkProcessed(entry.Id);
/// }
/// </code>
/// </remarks>
public interface IOutboxStorage
{
    /// <summary>
    /// Adds a message to the outbox for transactional publishing.
    /// </summary>
    /// <param name="message">The message to store in the outbox</param>
    /// <param name="options">Publishing options including destination, priority, and retry configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created outbox entry with assigned ID and metadata</returns>
    /// <remarks>
    /// This method should be called within the same database transaction as the business logic
    /// that triggers the message. This ensures atomicity - the message is only stored if the
    /// transaction commits.
    ///
    /// The entry is created with status <see cref="OutboxStatus.Pending"/> and will be picked up
    /// by the background processor for publishing.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when message or options is null</exception>
    Task<OutboxEntry> Add(IMessage message, Abstractions.OutboxOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves pending outbox entries matching the specified query criteria.
    /// </summary>
    /// <param name="query">Query criteria including status, time range, and limit</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of outbox entries matching the query</returns>
    /// <remarks>
    /// Use this method for advanced filtering of outbox entries, such as:
    /// - Retrieving entries older than a specific time
    /// - Filtering by status (Pending, Processing, Failed)
    /// - Limiting the batch size for processing
    ///
    /// Background processors typically use this to poll for messages to publish.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when query is null</exception>
    Task<IEnumerable<OutboxEntry>> GetPending(OutboxQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a limited number of pending outbox entries ready for publishing.
    /// </summary>
    /// <param name="limit">Maximum number of entries to retrieve. Default is 100</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of pending outbox entries, ordered by creation time</returns>
    /// <remarks>
    /// This is a convenience method for polling the outbox for pending messages.
    /// Entries are returned in FIFO order (oldest first) with status <see cref="OutboxStatus.Pending"/>.
    ///
    /// Entries with retry schedules (NextRetryAt) are only returned when the retry time has passed.
    /// </remarks>
    Task<IEnumerable<OutboxEntry>> GetPending(int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an outbox entry as successfully processed (published).
    /// </summary>
    /// <param name="entryId">The unique identifier of the outbox entry</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was marked as processed; false if not found</returns>
    /// <remarks>
    /// Call this method after successfully publishing a message to the message broker.
    /// The entry status is updated to <see cref="OutboxStatus.Processed"/> and ProcessedAt
    /// timestamp is set.
    ///
    /// Processed entries are typically archived or cleaned up by a maintenance process.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when entryId is null or empty</exception>
    Task<bool> MarkProcessed(string entryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an outbox entry as failed with an error message.
    /// </summary>
    /// <param name="entryId">The unique identifier of the outbox entry</param>
    /// <param name="error">The error message describing why publishing failed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was marked as failed; false if not found</returns>
    /// <remarks>
    /// Call this method when publishing fails after all retry attempts are exhausted.
    /// The entry status is updated to <see cref="OutboxStatus.Failed"/> and the error
    /// is recorded for debugging.
    ///
    /// Failed entries should be monitored and may require manual intervention or
    /// compensation logic.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when entryId or error is null or empty</exception>
    Task<bool> MarkFailed(string entryId, string error, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the retry count and schedules the next retry attempt for an outbox entry.
    /// </summary>
    /// <param name="entryId">The unique identifier of the outbox entry</param>
    /// <param name="retryCount">The updated retry count (typically incremented by 1)</param>
    /// <param name="nextRetry">When the next retry attempt should occur. If null, message is immediately available for retry</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was updated; false if not found</returns>
    /// <remarks>
    /// Use this method to implement retry logic with exponential backoff or scheduled retries.
    /// The entry remains in <see cref="OutboxStatus.Pending"/> status but won't be returned
    /// by <see cref="GetPending(int, CancellationToken)"/> until NextRetryAt time has passed.
    ///
    /// Typical retry logic:
    /// <code>
    /// if (entry.RetryCount &lt; entry.Options.MaxRetries)
    /// {
    ///     var nextRetry = DateTime.UtcNow.AddSeconds(Math.Pow(2, entry.RetryCount)); // Exponential backoff
    ///     await outboxStorage.UpdateRetryCount(entry.Id, entry.RetryCount + 1, nextRetry);
    /// }
    /// else
    /// {
    ///     await outboxStorage.MarkFailed(entry.Id, "Max retries exceeded");
    /// }
    /// </code>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when entryId is null or empty</exception>
    Task<bool> UpdateRetryCount(string entryId, int retryCount, DateTime? nextRetry = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total count of pending outbox entries ready for publishing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The number of entries with status <see cref="OutboxStatus.Pending"/></returns>
    /// <remarks>
    /// Use this for monitoring and observability. A growing pending count may indicate:
    /// - Publishing bottleneck
    /// - Message broker connectivity issues
    /// - Insufficient background processor capacity
    /// </remarks>
    Task<long> GetPendingCount(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves outbox entries that have failed after exhausting all retry attempts.
    /// </summary>
    /// <param name="limit">Maximum number of failed entries to retrieve. Default is 100</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of failed outbox entries with error details</returns>
    /// <remarks>
    /// Failed entries require attention as they represent messages that could not be published.
    /// Review failed entries to:
    /// - Identify systemic issues (e.g., message broker configuration)
    /// - Implement compensation logic
    /// - Manually republish after fixing issues
    /// - Archive for audit purposes
    /// </remarks>
    Task<IEnumerable<OutboxEntry>> GetFailed(int limit = 100, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a message stored in the outbox awaiting publication.
/// </summary>
/// <remarks>
/// Outbox entries track the lifecycle of messages from creation through publication.
/// Each entry contains the original message, publishing configuration, status, and
/// retry metadata for reliable message delivery.
/// </remarks>
public class OutboxEntry
{
    /// <summary>
    /// Gets or sets the unique identifier for this outbox entry.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the message to be published.
    /// </summary>
    public IMessage Message { get; set; } = null!;

    /// <summary>
    /// Gets or sets the publishing options including destination, priority, and retry configuration.
    /// See <see cref="OutboxOptions"/> for available configuration options.
    /// </summary>
    public Abstractions.OutboxOptions Options { get; set; } = new();

    /// <summary>
    /// Gets or sets the current status of this outbox entry.
    /// </summary>
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    /// <summary>
    /// Gets or sets the number of times publishing has been attempted for this entry.
    /// </summary>
    /// <remarks>
    /// Incremented each time publishing fails and is retried.
    /// When RetryCount reaches MaxRetries in Options, the entry is marked as Failed.
    /// </remarks>
    public int RetryCount { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this entry was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = TimeProvider.System.GetUtcNow().DateTime;

    /// <summary>
    /// Gets or sets the timestamp when this entry was successfully processed (published).
    /// Null if not yet processed.
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the next retry attempt should occur.
    /// </summary>
    /// <remarks>
    /// Used for implementing exponential backoff or scheduled retries.
    /// Pending entries with a future NextRetryAt are not returned by GetPending
    /// until this time has passed.
    /// </remarks>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>
    /// Gets or sets the error message from the most recent failed publishing attempt.
    /// Null if no errors have occurred.
    /// </summary>
    public string? LastError { get; set; }
}

/// <summary>
/// Defines the possible states of an outbox entry in its publishing lifecycle.
/// </summary>
public enum OutboxStatus
{
    /// <summary>
    /// Entry is waiting to be published. This is the initial state.
    /// </summary>
    Pending,

    /// <summary>
    /// Entry is currently being processed by a publisher.
    /// </summary>
    Processing,

    /// <summary>
    /// Entry has been successfully published and can be archived or deleted.
    /// </summary>
    Processed,

    /// <summary>
    /// Entry failed to publish after all retry attempts were exhausted.
    /// Requires manual intervention or compensation logic.
    /// </summary>
    Failed
}

/// <summary>
/// Defines the possible states of an outbox entry for querying purposes.
/// </summary>
/// <remarks>
/// This enum duplicates <see cref="OutboxStatus"/> to maintain API compatibility
/// while allowing for future divergence in query-specific status values.
/// </remarks>
public enum OutboxEntryStatus
{
    /// <summary>
    /// Entry is waiting to be published.
    /// </summary>
    Pending,

    /// <summary>
    /// Entry is currently being processed.
    /// </summary>
    Processing,

    /// <summary>
    /// Entry has been successfully published.
    /// </summary>
    Processed,

    /// <summary>
    /// Entry failed to publish after all retries.
    /// </summary>
    Failed
}

/// <summary>
/// Defines query criteria for filtering outbox entries.
/// </summary>
/// <remarks>
/// Use this class to build complex queries for retrieving specific outbox entries
/// based on status, time range, and batch size.
///
/// Example:
/// <code>
/// var query = new OutboxQuery
/// {
///     Status = OutboxEntryStatus.Failed,
///     OlderThan = DateTime.UtcNow.AddHours(-1),
///     Limit = 50
/// };
/// var failedEntries = await outboxStorage.GetPending(query);
/// </code>
/// </remarks>
public class OutboxQuery
{
    /// <summary>
    /// Gets or sets the status to filter by. If null, all statuses are included.
    /// </summary>
    public OutboxEntryStatus? Status { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of entries to return.
    /// Default is 100.
    /// </summary>
    public int Limit { get; set; } = 100;

    /// <summary>
    /// Gets or sets a timestamp filter to only include entries created before this time.
    /// Useful for finding stale entries that may need attention.
    /// </summary>
    public DateTime? OlderThan { get; set; }

    /// <summary>
    /// Gets or sets a timestamp filter to only include entries created after this time.
    /// Useful for focusing on recently added entries.
    /// </summary>
    public DateTime? NewerThan { get; set; }
}
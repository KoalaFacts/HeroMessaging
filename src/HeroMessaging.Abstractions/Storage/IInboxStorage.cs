using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Storage;

/// <summary>
/// Provides storage for the Inbox pattern, ensuring exactly-once message processing through deduplication.
/// </summary>
/// <remarks>
/// The Inbox pattern prevents duplicate message processing by tracking all received messages
/// and their processing status. This is essential in distributed systems where messages may be
/// delivered multiple times due to network issues, retries, or at-least-once delivery semantics.
///
/// Pattern flow:
/// 1. Message arrives from external source (message broker, API, etc.)
/// 2. Check if message ID already exists in inbox (deduplication check)
/// 3. If duplicate: Skip processing and return (idempotent)
/// 4. If new: Add to inbox with Pending status
/// 5. Process the message
/// 6. Mark as Processed in inbox (within same transaction as processing)
///
/// This ensures exactly-once processing semantics even when messages are delivered multiple times.
///
/// Example usage:
/// <code>
/// // Receiving a message from external broker
/// var message = await broker.ReceiveAsync();
///
/// // Check for duplicates
/// if (await inboxStorage.IsDuplicate(message.MessageId, TimeSpan.FromDays(7)))
/// {
///     // Already processed, acknowledge and skip
///     await broker.AcknowledgeAsync(message);
///     return;
/// }
///
/// // Add to inbox and process atomically
/// using var transaction = await unitOfWork.BeginTransactionAsync();
/// try
/// {
///     await inboxStorage.Add(message, new InboxOptions
///     {
///         Source = "rabbitmq",
///         RequireIdempotency = true
///     });
///
///     // Process the message (update business data)
///     await ProcessMessageAsync(message);
///
///     // Mark as processed
///     await inboxStorage.MarkProcessed(message.MessageId);
///
///     await transaction.CommitAsync(); // Both processing and inbox update succeed together
///     await broker.AcknowledgeAsync(message);
/// }
/// catch
/// {
///     await transaction.RollbackAsync(); // Both rolled back together
///     throw;
/// }
/// </code>
/// </remarks>
public interface IInboxStorage
{
    /// <summary>
    /// Adds a message to the inbox for tracking and deduplication.
    /// </summary>
    /// <param name="message">The incoming message to track</param>
    /// <param name="options">Inbox options including source, idempotency requirements, and deduplication window</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created inbox entry, or null if the message is a duplicate and RequireIdempotency is true</returns>
    /// <remarks>
    /// This method should be called within the same database transaction as message processing
    /// to ensure atomicity. If the message ID already exists and RequireIdempotency is true,
    /// returns null to indicate a duplicate.
    ///
    /// The entry is created with status <see cref="InboxStatus.Pending"/> and must be marked
    /// as processed after successful message handling.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when message or options is null</exception>
    Task<InboxEntry?> Add(IMessage message, InboxOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a message with the specified ID has already been received and processed.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message to check</param>
    /// <param name="window">Optional time window for deduplication. Only checks messages received within this timespan</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message ID exists in the inbox (indicating a duplicate); otherwise false</returns>
    /// <remarks>
    /// Use this method before processing a message to implement exactly-once semantics.
    /// The window parameter allows for efficient cleanup - older entries can be removed
    /// while still maintaining deduplication within a reasonable timeframe.
    ///
    /// Typical deduplication windows:
    /// - Real-time events: 1-7 days
    /// - Financial transactions: 30-90 days
    /// - Audit events: 1 year
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when messageId is null or empty</exception>
    Task<bool> IsDuplicate(string messageId, TimeSpan? window = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves an inbox entry by message ID.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The inbox entry if found; otherwise null</returns>
    /// <remarks>
    /// Use this to retrieve full details about a message's processing status,
    /// including when it was received, processed, or if it failed.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when messageId is null or empty</exception>
    Task<InboxEntry?> Get(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an inbox entry as successfully processed.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was marked as processed; false if not found</returns>
    /// <remarks>
    /// Call this method within the same transaction as message processing to ensure
    /// atomicity. The entry status is updated to <see cref="InboxStatus.Processed"/>
    /// and ProcessedAt timestamp is set.
    ///
    /// If the transaction rolls back, both the processing and status update are reverted,
    /// allowing the message to be retried.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when messageId is null or empty</exception>
    Task<bool> MarkProcessed(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an inbox entry as failed with an error message.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message</param>
    /// <param name="error">The error message describing why processing failed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was marked as failed; false if not found</returns>
    /// <remarks>
    /// Use this when message processing fails and should not be retried automatically.
    /// The entry status is updated to <see cref="InboxStatus.Failed"/> and the error
    /// is recorded for debugging.
    ///
    /// Failed entries should be monitored and may require:
    /// - Manual investigation and compensation
    /// - Message broker dead-letter queue handling
    /// - Alert notifications to operations teams
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when messageId or error is null or empty</exception>
    Task<bool> MarkFailed(string messageId, string error, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves inbox entries matching the specified query criteria.
    /// </summary>
    /// <param name="query">Query criteria including status, time range, and limit</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of inbox entries matching the query</returns>
    /// <remarks>
    /// Use this for monitoring, reporting, and cleanup operations:
    /// - Find failed messages for investigation
    /// - Identify old processed messages for archival
    /// - Monitor pending message backlog
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when query is null</exception>
    Task<IEnumerable<InboxEntry>> GetPending(InboxQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves unprocessed inbox entries ready for processing or retry.
    /// </summary>
    /// <param name="limit">Maximum number of entries to retrieve. Default is 100</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of unprocessed inbox entries, ordered by received time</returns>
    /// <remarks>
    /// This is a convenience method for polling the inbox for messages that need processing.
    /// Entries are returned in FIFO order (oldest first) with status <see cref="InboxStatus.Pending"/>.
    ///
    /// Use this for implementing:
    /// - Retry mechanisms for failed processing
    /// - Background workers that poll for new messages
    /// - Monitoring dashboards showing pending work
    /// </remarks>
    Task<IEnumerable<InboxEntry>> GetUnprocessed(int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total count of unprocessed inbox entries.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The number of entries with status <see cref="InboxStatus.Pending"/></returns>
    /// <remarks>
    /// Use this for monitoring and observability. A growing unprocessed count may indicate:
    /// - Processing bottleneck
    /// - Handler failures or bugs
    /// - Insufficient processing capacity
    /// - Upstream system sending messages too quickly
    /// </remarks>
    Task<long> GetUnprocessedCount(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes old inbox entries to prevent unbounded growth.
    /// </summary>
    /// <param name="olderThan">Remove entries older than this duration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous cleanup operation</returns>
    /// <remarks>
    /// Run this periodically (e.g., daily) to clean up old processed entries while maintaining
    /// the deduplication window. Typically:
    /// - Keep processed entries for 7-30 days (for deduplication)
    /// - Keep failed entries longer for investigation (30-90 days)
    /// - Archive critical entries before deletion for audit compliance
    ///
    /// Example cleanup policy:
    /// <code>
    /// // Daily cleanup job
    /// await inboxStorage.CleanupOldEntries(TimeSpan.FromDays(30));
    /// </code>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when olderThan is negative</exception>
    Task CleanupOldEntries(TimeSpan olderThan, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a message tracked in the inbox for deduplication and processing status.
/// </summary>
/// <remarks>
/// Inbox entries track the lifecycle of incoming messages from receipt through processing.
/// Each entry stores the original message, source information, processing status, and
/// error details for debugging failed messages.
/// </remarks>
public class InboxEntry
{
    /// <summary>
    /// Gets or sets the unique identifier for this inbox entry.
    /// Typically matches the MessageId of the incoming message for deduplication.
    /// </summary>
    public string Id { get; set; } = null!;

    /// <summary>
    /// Gets or sets the received message being tracked.
    /// </summary>
    public IMessage Message { get; set; } = null!;

    /// <summary>
    /// Gets or sets the inbox options including source and idempotency settings.
    /// See <see cref="InboxOptions"/> for available configuration options.
    /// </summary>
    public InboxOptions Options { get; set; } = new();

    /// <summary>
    /// Gets or sets the current processing status of this inbox entry.
    /// </summary>
    public InboxStatus Status { get; set; } = InboxStatus.Pending;

    /// <summary>
    /// Gets or sets the timestamp when this message was received.
    /// </summary>
    public DateTime ReceivedAt { get; set; } = TimeProvider.System.GetUtcNow().DateTime;

    /// <summary>
    /// Gets or sets the timestamp when this message was successfully processed.
    /// Null if not yet processed.
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Gets or sets the error message if processing failed.
    /// Null if no errors have occurred.
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Defines the possible states of an inbox entry in its processing lifecycle.
/// </summary>
public enum InboxStatus
{
    /// <summary>
    /// Entry is waiting to be processed. This is the initial state.
    /// </summary>
    Pending,

    /// <summary>
    /// Entry is currently being processed by a handler.
    /// </summary>
    Processing,

    /// <summary>
    /// Entry has been successfully processed and can be archived or cleaned up.
    /// </summary>
    Processed,

    /// <summary>
    /// Entry processing failed and requires investigation or compensation.
    /// </summary>
    Failed,

    /// <summary>
    /// Entry is a duplicate of a previously received message and was skipped.
    /// Used when deduplication is enabled.
    /// </summary>
    Duplicate
}

/// <summary>
/// Defines the possible states of an inbox entry for querying purposes.
/// </summary>
/// <remarks>
/// This enum duplicates <see cref="InboxStatus"/> to maintain API compatibility
/// while allowing for future divergence in query-specific status values.
/// </remarks>
public enum InboxEntryStatus
{
    /// <summary>
    /// Entry is waiting to be processed.
    /// </summary>
    Pending,

    /// <summary>
    /// Entry is currently being processed.
    /// </summary>
    Processing,

    /// <summary>
    /// Entry has been successfully processed.
    /// </summary>
    Processed,

    /// <summary>
    /// Entry processing failed.
    /// </summary>
    Failed,

    /// <summary>
    /// Entry is a duplicate and was skipped.
    /// </summary>
    Duplicate
}

/// <summary>
/// Defines query criteria for filtering inbox entries.
/// </summary>
/// <remarks>
/// Use this class to build queries for retrieving specific inbox entries
/// based on processing status, time range, and batch size.
///
/// Example:
/// <code>
/// var query = new InboxQuery
/// {
///     Status = InboxEntryStatus.Failed,
///     OlderThan = DateTime.UtcNow.AddHours(-1),
///     Limit = 50
/// };
/// var failedEntries = await inboxStorage.GetPending(query);
/// </code>
/// </remarks>
public class InboxQuery
{
    /// <summary>
    /// Gets or sets the status to filter by. If null, all statuses are included.
    /// </summary>
    public InboxEntryStatus? Status { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of entries to return.
    /// Default is 100.
    /// </summary>
    public int Limit { get; set; } = 100;

    /// <summary>
    /// Gets or sets a timestamp filter to only include entries received before this time.
    /// Useful for finding stale entries that may need cleanup or investigation.
    /// </summary>
    public DateTime? OlderThan { get; set; }

    /// <summary>
    /// Gets or sets a timestamp filter to only include entries received after this time.
    /// Useful for focusing on recently received entries.
    /// </summary>
    public DateTime? NewerThan { get; set; }
}
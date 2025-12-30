using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Storage;

/// <summary>
/// Storage for incoming messages providing idempotent processing and duplicate detection.
/// </summary>
public interface IInboxStorage
{
    /// <summary>
    /// Adds a message to the inbox for processing.
    /// </summary>
    /// <param name="message">The message to add</param>
    /// <param name="options">Inbox configuration options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The inbox entry, or null if the message is a duplicate</returns>
    Task<InboxEntry?> AddAsync(IMessage message, InboxOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a message with the specified ID has already been processed.
    /// </summary>
    /// <param name="messageId">The message ID to check</param>
    /// <param name="window">Optional time window for duplicate detection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message is a duplicate; otherwise, false</returns>
    Task<bool> IsDuplicateAsync(string messageId, TimeSpan? window = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an inbox entry by message ID.
    /// </summary>
    /// <param name="messageId">The message ID to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The inbox entry, or null if not found</returns>
    Task<InboxEntry?> GetAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a message as successfully processed.
    /// </summary>
    /// <param name="messageId">The message ID to mark as processed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was updated; otherwise, false</returns>
    Task<bool> MarkProcessedAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a message as failed with an error message.
    /// </summary>
    /// <param name="messageId">The message ID to mark as failed</param>
    /// <param name="error">The error message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was updated; otherwise, false</returns>
    Task<bool> MarkFailedAsync(string messageId, string error, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets pending inbox entries matching the specified query.
    /// </summary>
    /// <param name="query">The query criteria</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of matching inbox entries</returns>
    Task<IEnumerable<InboxEntry>> GetPendingAsync(InboxQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets unprocessed inbox entries.
    /// </summary>
    /// <param name="limit">Maximum number of entries to return. Default: 100.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of unprocessed inbox entries</returns>
    Task<IEnumerable<InboxEntry>> GetUnprocessedAsync(int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of unprocessed entries.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The count of unprocessed entries</returns>
    Task<long> GetUnprocessedCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes entries older than the specified time span.
    /// </summary>
    /// <param name="olderThan">Time threshold for cleanup</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task CleanupOldEntriesAsync(TimeSpan olderThan, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an entry in the inbox storage.
/// </summary>
public class InboxEntry
{
    /// <summary>
    /// Unique identifier of the inbox entry.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// The message stored in the inbox.
    /// </summary>
    public required IMessage Message { get; set; }

    /// <summary>
    /// Options that were applied when the entry was created.
    /// </summary>
    public InboxOptions Options { get; set; } = new();

    /// <summary>
    /// Current processing status of the entry.
    /// </summary>
    public InboxStatus Status { get; set; } = InboxStatus.Pending;

    /// <summary>
    /// When the message was received.
    /// </summary>
    public DateTimeOffset ReceivedAt { get; set; } = TimeProvider.System.GetUtcNow();

    /// <summary>
    /// When the message was processed, if applicable.
    /// </summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>
    /// Error message if processing failed.
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Status of an inbox entry.
/// </summary>
public enum InboxStatus
{
    /// <summary>
    /// Message is pending processing.
    /// </summary>
    Pending,

    /// <summary>
    /// Message is currently being processed.
    /// </summary>
    Processing,

    /// <summary>
    /// Message has been successfully processed.
    /// </summary>
    Processed,

    /// <summary>
    /// Message processing failed.
    /// </summary>
    Failed,

    /// <summary>
    /// Message was detected as a duplicate.
    /// </summary>
    Duplicate
}

/// <summary>
/// Query parameters for retrieving inbox entries.
/// </summary>
public class InboxQuery
{
    /// <summary>
    /// Filter by entry status. If null, all statuses are included.
    /// </summary>
    public InboxStatus? Status { get; set; }

    /// <summary>
    /// Maximum number of entries to return. Default: 100.
    /// </summary>
    public int Limit { get; set; } = 100;

    /// <summary>
    /// Filter for entries older than this timestamp.
    /// </summary>
    public DateTimeOffset? OlderThan { get; set; }

    /// <summary>
    /// Filter for entries newer than this timestamp.
    /// </summary>
    public DateTimeOffset? NewerThan { get; set; }
}

using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Storage;

/// <summary>
/// Storage for outgoing messages providing reliable delivery with retry support.
/// </summary>
public interface IOutboxStorage
{
    /// <summary>
    /// Adds a message to the outbox for delivery.
    /// </summary>
    /// <param name="message">The message to add</param>
    /// <param name="options">Outbox configuration options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created outbox entry</returns>
    Task<OutboxEntry> AddAsync(IMessage message, OutboxOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets pending outbox entries matching the specified query.
    /// </summary>
    /// <param name="query">The query criteria</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of matching outbox entries</returns>
    Task<IEnumerable<OutboxEntry>> GetPendingAsync(OutboxQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets pending outbox entries awaiting delivery.
    /// </summary>
    /// <param name="limit">Maximum number of entries to return. Default: 100.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of pending outbox entries</returns>
    Task<IEnumerable<OutboxEntry>> GetPendingAsync(int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an outbox entry as successfully processed.
    /// </summary>
    /// <param name="entryId">The entry ID to mark as processed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was updated; otherwise, false</returns>
    Task<bool> MarkProcessedAsync(string entryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an outbox entry as failed with an error message.
    /// </summary>
    /// <param name="entryId">The entry ID to mark as failed</param>
    /// <param name="error">The error message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was updated; otherwise, false</returns>
    Task<bool> MarkFailedAsync(string entryId, string error, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the retry count and next retry time for an entry.
    /// </summary>
    /// <param name="entryId">The entry ID to update</param>
    /// <param name="retryCount">The new retry count</param>
    /// <param name="nextRetry">When to attempt the next retry</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was updated; otherwise, false</returns>
    Task<bool> UpdateRetryCountAsync(string entryId, int retryCount, DateTimeOffset? nextRetry = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of pending entries awaiting delivery.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The count of pending entries</returns>
    Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets failed outbox entries.
    /// </summary>
    /// <param name="limit">Maximum number of entries to return. Default: 100.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of failed outbox entries</returns>
    Task<IEnumerable<OutboxEntry>> GetFailedAsync(int limit = 100, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an entry in the outbox storage.
/// </summary>
public class OutboxEntry
{
    /// <summary>
    /// Unique identifier of the outbox entry.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The message to be delivered.
    /// </summary>
    public required IMessage Message { get; set; }

    /// <summary>
    /// Options that were applied when the entry was created.
    /// </summary>
    public OutboxOptions Options { get; set; } = new();

    /// <summary>
    /// Current delivery status of the entry.
    /// </summary>
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    /// <summary>
    /// Number of times delivery has been attempted.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// When the entry was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = TimeProvider.System.GetUtcNow();

    /// <summary>
    /// When the entry was successfully delivered, if applicable.
    /// </summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>
    /// When the next delivery attempt will be made.
    /// </summary>
    public DateTimeOffset? NextRetryAt { get; set; }

    /// <summary>
    /// The most recent error message, if any.
    /// </summary>
    public string? LastError { get; set; }
}

/// <summary>
/// Status of an outbox entry.
/// </summary>
public enum OutboxStatus
{
    /// <summary>
    /// Message is pending delivery.
    /// </summary>
    Pending,

    /// <summary>
    /// Message is currently being delivered.
    /// </summary>
    Processing,

    /// <summary>
    /// Message has been successfully delivered.
    /// </summary>
    Processed,

    /// <summary>
    /// Message delivery failed after all retries.
    /// </summary>
    Failed
}

/// <summary>
/// Query parameters for retrieving outbox entries.
/// </summary>
public class OutboxQuery
{
    /// <summary>
    /// Filter by entry status. If null, all statuses are included.
    /// </summary>
    public OutboxStatus? Status { get; set; }

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

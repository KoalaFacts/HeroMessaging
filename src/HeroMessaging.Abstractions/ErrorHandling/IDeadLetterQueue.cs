using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.ErrorHandling;

/// <summary>
/// Manages dead letter messages that failed processing and cannot be retried automatically.
/// </summary>
public interface IDeadLetterQueue
{
    /// <summary>
    /// Sends a failed message to the dead letter queue for manual inspection or retry.
    /// </summary>
    /// <typeparam name="T">The type of message</typeparam>
    /// <param name="message">The message that failed processing</param>
    /// <param name="context">Context information about the failure</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The unique identifier of the dead letter entry</returns>
    Task<string> SendToDeadLetterAsync<T>(T message, DeadLetterContext context, CancellationToken cancellationToken = default) where T : IMessage;

    /// <summary>
    /// Retrieves dead letter entries of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of message to retrieve</typeparam>
    /// <param name="limit">Maximum number of entries to return. Default: 100.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of dead letter entries</returns>
    Task<IEnumerable<DeadLetterEntry<T>>> GetDeadLettersAsync<T>(int limit = 100, CancellationToken cancellationToken = default) where T : IMessage;

    /// <summary>
    /// Attempts to reprocess a dead letter message.
    /// </summary>
    /// <typeparam name="T">The type of message</typeparam>
    /// <param name="deadLetterId">The unique identifier of the dead letter entry</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message was successfully requeued for processing; otherwise, false</returns>
    Task<bool> RetryAsync<T>(string deadLetterId, CancellationToken cancellationToken = default) where T : IMessage;

    /// <summary>
    /// Permanently discards a dead letter message.
    /// </summary>
    /// <typeparam name="T">The type of message</typeparam>
    /// <param name="deadLetterId">The unique identifier of the dead letter entry</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message was successfully discarded; otherwise, false</returns>
    Task<bool> DiscardAsync<T>(string deadLetterId, CancellationToken cancellationToken = default) where T : IMessage;

    /// <summary>
    /// Gets the total count of active dead letter messages.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The total count of dead letter messages</returns>
    Task<long> GetDeadLetterCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistical information about the dead letter queue.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dead letter queue statistics</returns>
    Task<DeadLetterStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Context information about why a message was sent to the dead letter queue.
/// </summary>
public sealed record DeadLetterContext
{
    /// <summary>
    /// Human-readable reason why the message failed.
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// The exception that caused the failure, if any.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// The component or handler that failed to process the message.
    /// </summary>
    public string Component { get; init; } = string.Empty;

    /// <summary>
    /// Number of times the message was retried before being dead-lettered.
    /// </summary>
    public int RetryCount { get; init; }

    /// <summary>
    /// When the failure occurred.
    /// </summary>
    public DateTimeOffset FailureTime { get; init; } = TimeProvider.System.GetUtcNow();

    /// <summary>
    /// Additional metadata about the failure.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = [];
}

/// <summary>
/// Non-generic base interface for dead letter entries.
/// </summary>
public interface IDeadLetterEntry
{
    /// <summary>
    /// Unique identifier of this dead letter entry.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Context information about why the message failed.
    /// </summary>
    DeadLetterContext Context { get; }

    /// <summary>
    /// When this dead letter entry was created.
    /// </summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Current status of this dead letter entry.
    /// </summary>
    DeadLetterStatus Status { get; }

    /// <summary>
    /// When this entry was retried, if applicable.
    /// </summary>
    DateTimeOffset? RetriedAt { get; }

    /// <summary>
    /// When this entry was discarded, if applicable.
    /// </summary>
    DateTimeOffset? DiscardedAt { get; }
}

/// <summary>
/// Represents a dead letter entry containing a failed message of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The type of message in this entry</typeparam>
public sealed record DeadLetterEntry<T> : IDeadLetterEntry where T : IMessage
{
    /// <summary>
    /// Unique identifier of this dead letter entry.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The message that failed processing.
    /// </summary>
    public T Message { get; init; } = default!;

    /// <summary>
    /// Context information about why the message failed.
    /// </summary>
    public DeadLetterContext Context { get; init; } = new();

    /// <summary>
    /// When this dead letter entry was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = TimeProvider.System.GetUtcNow();

    /// <summary>
    /// Current status of this dead letter entry.
    /// </summary>
    public DeadLetterStatus Status { get; init; } = DeadLetterStatus.Active;

    /// <summary>
    /// When this entry was retried, if applicable.
    /// </summary>
    public DateTimeOffset? RetriedAt { get; init; }

    /// <summary>
    /// When this entry was discarded, if applicable.
    /// </summary>
    public DateTimeOffset? DiscardedAt { get; init; }
}

/// <summary>
/// Status of a dead letter entry.
/// </summary>
public enum DeadLetterStatus
{
    /// <summary>
    /// The entry is active and awaiting action.
    /// </summary>
    Active,

    /// <summary>
    /// The entry has been retried for reprocessing.
    /// </summary>
    Retried,

    /// <summary>
    /// The entry has been permanently discarded.
    /// </summary>
    Discarded,

    /// <summary>
    /// The entry has expired based on retention policy.
    /// </summary>
    Expired
}

/// <summary>
/// Statistical information about the dead letter queue.
/// </summary>
public sealed record DeadLetterStatistics
{
    /// <summary>
    /// Total number of dead letter entries across all statuses.
    /// </summary>
    public long TotalCount { get; init; }

    /// <summary>
    /// Number of active (pending action) dead letter entries.
    /// </summary>
    public long ActiveCount { get; init; }

    /// <summary>
    /// Number of entries that have been retried.
    /// </summary>
    public long RetriedCount { get; init; }

    /// <summary>
    /// Number of entries that have been discarded.
    /// </summary>
    public long DiscardedCount { get; init; }

    /// <summary>
    /// Dead letter counts grouped by component.
    /// </summary>
    public Dictionary<string, long> CountByComponent { get; init; } = [];

    /// <summary>
    /// Dead letter counts grouped by failure reason.
    /// </summary>
    public Dictionary<string, long> CountByReason { get; init; } = [];

    /// <summary>
    /// Timestamp of the oldest dead letter entry.
    /// </summary>
    public DateTimeOffset? OldestEntry { get; init; }

    /// <summary>
    /// Timestamp of the newest dead letter entry.
    /// </summary>
    public DateTimeOffset? NewestEntry { get; init; }
}

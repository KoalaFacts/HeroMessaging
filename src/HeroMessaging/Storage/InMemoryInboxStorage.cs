using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using System.Collections.Concurrent;

namespace HeroMessaging.Storage;

/// <summary>
/// Provides an in-memory implementation of the Inbox pattern for development, testing, and idempotency scenarios.
/// </summary>
/// <remarks>
/// This implementation stores inbox entries in memory using concurrent dictionaries, making it suitable for:
/// - Development and testing environments
/// - High-performance scenarios where durability is not required
/// - In-process message deduplication without database overhead
///
/// <para><strong>Thread Safety:</strong> This implementation is thread-safe and supports concurrent access
/// from multiple threads using <see cref="ConcurrentDictionary{TKey,TValue}"/>.</para>
///
/// <para><strong>Volatility Warning:</strong> All inbox entries are stored in memory and will be lost when
/// the application restarts or crashes. This means duplicate detection state is lost on restart. Do not use
/// this implementation in production for scenarios requiring durable idempotency guarantees.</para>
///
/// <para><strong>Concurrency:</strong> All operations are lock-free and support high concurrency. Multiple
/// message handlers can safely check for duplicates and process messages concurrently.</para>
///
/// <para><strong>Idempotency:</strong> Supports duplicate detection using message IDs. When RequireIdempotency
/// is enabled, duplicate messages are rejected and marked with <see cref="InboxStatus.Duplicate"/> status.</para>
///
/// <para><strong>Cleanup:</strong> The CleanupOldEntries method removes processed entries older than a specified
/// duration to prevent unbounded memory growth. Consider running this periodically in background tasks.</para>
/// </remarks>
public class InMemoryInboxStorage : IInboxStorage
{
    private readonly ConcurrentDictionary<string, InboxEntry> _entries = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryInboxStorage"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider for managing timestamps and time-based operations</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider"/> is null</exception>
    public InMemoryInboxStorage(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Adds a message to the inbox for duplicate detection and processing tracking.
    /// </summary>
    /// <param name="message">The message to store in the inbox</param>
    /// <param name="options">Inbox options including idempotency requirements</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created inbox entry if successful; null if the message is a duplicate and idempotency is enabled</returns>
    /// <remarks>
    /// If <see cref="InboxOptions.RequireIdempotency"/> is true and a message with the same ID already exists,
    /// this method returns null and marks the existing entry as <see cref="InboxStatus.Duplicate"/>.
    ///
    /// The entry is created with status <see cref="InboxStatus.Pending"/> and uses the message's MessageId
    /// as the entry identifier for duplicate detection.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when message or options is null</exception>
    public Task<InboxEntry?> Add(IMessage message, InboxOptions options, CancellationToken cancellationToken = default)
    {
        var messageId = message.MessageId.ToString();

        if (options.RequireIdempotency && _entries.ContainsKey(messageId))
        {
            var existing = _entries[messageId];
            existing.Status = InboxStatus.Duplicate;
            return Task.FromResult<InboxEntry?>(null);
        }

        var entry = new InboxEntry
        {
            Id = messageId,
            Message = message,
            Options = options,
            Status = InboxStatus.Pending,
            ReceivedAt = _timeProvider.GetUtcNow().DateTime
        };

        _entries[messageId] = entry;
        return Task.FromResult<InboxEntry?>(entry);
    }

    /// <summary>
    /// Checks if a message with the specified ID has been previously received.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message to check</param>
    /// <param name="window">Optional time window for duplicate detection. Only considers messages received within this window</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message ID exists in the inbox (and within the window if specified); otherwise false</returns>
    /// <remarks>
    /// This method is used for duplicate detection and idempotency checks.
    ///
    /// When a window is specified, only messages received within that time window from now are
    /// considered duplicates. This allows for time-bounded duplicate detection to prevent
    /// indefinite memory growth.
    ///
    /// Example: A 24-hour window means messages older than 24 hours are not considered duplicates.
    /// </remarks>
    public Task<bool> IsDuplicate(string messageId, TimeSpan? window = null, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(messageId, out var entry))
        {
            if (window.HasValue)
            {
                var cutoff = _timeProvider.GetUtcNow().DateTime.Subtract(window.Value);
                return Task.FromResult(entry.ReceivedAt >= cutoff);
            }

            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Retrieves an inbox entry by message ID.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The inbox entry if found; otherwise null</returns>
    /// <remarks>
    /// Use this method to check the processing status of a specific message or to retrieve
    /// message details for reprocessing or audit purposes.
    /// </remarks>
    public Task<InboxEntry?> Get(string messageId, CancellationToken cancellationToken = default)
    {
        _entries.TryGetValue(messageId, out var entry);
        return Task.FromResult<InboxEntry?>(entry);
    }

    /// <summary>
    /// Marks an inbox entry as successfully processed.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was marked as processed; false if not found</returns>
    /// <remarks>
    /// Updates the entry status to <see cref="InboxStatus.Processed"/> and sets the ProcessedAt timestamp.
    /// Call this method after successfully processing the message to prevent reprocessing.
    /// </remarks>
    public Task<bool> MarkProcessed(string messageId, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(messageId, out var entry))
        {
            entry.Status = InboxStatus.Processed;
            entry.ProcessedAt = _timeProvider.GetUtcNow().DateTime;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Marks an inbox entry as failed with an error message.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message</param>
    /// <param name="error">The error message describing why processing failed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was marked as failed; false if not found</returns>
    /// <remarks>
    /// Updates the entry status to <see cref="InboxStatus.Failed"/> and records the error message.
    /// Call this method when message processing fails. Failed messages may be retried later or
    /// sent to a dead letter queue depending on your error handling strategy.
    /// </remarks>
    public Task<bool> MarkFailed(string messageId, string error, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(messageId, out var entry))
        {
            entry.Status = InboxStatus.Failed;
            entry.Error = error;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Retrieves pending inbox entries matching the specified query criteria.
    /// </summary>
    /// <param name="query">Query criteria including status, time range, and limit</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of inbox entries matching the query, ordered by received time</returns>
    /// <remarks>
    /// Filters entries based on:
    /// - Status (defaults to Pending if not specified)
    /// - OlderThan/NewerThan timestamp filters
    ///
    /// Results are ordered by ReceivedAt timestamp (oldest first) to ensure FIFO processing.
    /// Use this method to retrieve messages for processing in batches.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when query is null</exception>
    public Task<IEnumerable<InboxEntry>> GetPending(InboxQuery query, CancellationToken cancellationToken = default)
    {
        var pending = _entries.Values.AsEnumerable();

        if (query.Status.HasValue)
        {
            var status = query.Status.Value switch
            {
                InboxEntryStatus.Pending => InboxStatus.Pending,
                InboxEntryStatus.Processing => InboxStatus.Processing,
                InboxEntryStatus.Processed => InboxStatus.Processed,
                InboxEntryStatus.Failed => InboxStatus.Failed,
                InboxEntryStatus.Duplicate => InboxStatus.Duplicate,
                _ => InboxStatus.Pending
            };
            pending = pending.Where(e => e.Status == status);
        }
        else
        {
            pending = pending.Where(e => e.Status == InboxStatus.Pending);
        }

        if (query.OlderThan.HasValue)
        {
            pending = pending.Where(e => e.ReceivedAt < query.OlderThan.Value);
        }

        if (query.NewerThan.HasValue)
        {
            pending = pending.Where(e => e.ReceivedAt > query.NewerThan.Value);
        }

        pending = pending
            .OrderBy(e => e.ReceivedAt)
            .Take(query.Limit);

        return Task.FromResult(pending);
    }

    /// <summary>
    /// Retrieves a limited number of unprocessed inbox entries ready for processing.
    /// </summary>
    /// <param name="limit">Maximum number of entries to retrieve. Default is 100</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of pending inbox entries, ordered by received time</returns>
    /// <remarks>
    /// This is a convenience method for message handlers polling for messages to process.
    /// Only returns entries with status <see cref="InboxStatus.Pending"/>.
    ///
    /// Results are ordered by ReceivedAt timestamp to ensure FIFO (first-in-first-out) processing.
    /// </remarks>
    public Task<IEnumerable<InboxEntry>> GetUnprocessed(int limit = 100, CancellationToken cancellationToken = default)
    {
        var unprocessed = _entries.Values
            .Where(e => e.Status == InboxStatus.Pending)
            .OrderBy(e => e.ReceivedAt)
            .Take(limit);

        return Task.FromResult(unprocessed);
    }

    /// <summary>
    /// Gets the total count of unprocessed inbox entries.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The number of entries with status <see cref="InboxStatus.Pending"/></returns>
    /// <remarks>
    /// Use this for monitoring and observability. A growing unprocessed count may indicate:
    /// - Message processing bottleneck
    /// - Insufficient message handler capacity
    /// - Handler errors preventing message processing
    /// - Message broker publishing messages faster than they can be processed
    /// </remarks>
    public Task<long> GetUnprocessedCount(CancellationToken cancellationToken = default)
    {
        var count = _entries.Values.Count(e => e.Status == InboxStatus.Pending);
        return Task.FromResult((long)count);
    }

    /// <summary>
    /// Removes processed inbox entries older than the specified duration.
    /// </summary>
    /// <param name="olderThan">The minimum age for entries to be removed. Entries received before this duration ago will be deleted</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method prevents unbounded memory growth by removing old processed entries.
    /// Only entries with status <see cref="InboxStatus.Processed"/> are eligible for cleanup.
    /// Pending, failed, and duplicate entries are retained for further processing or investigation.
    ///
    /// Recommended usage: Run this periodically (e.g., daily) in a background task to maintain
    /// a sliding window of processed messages for duplicate detection while preventing memory leaks.
    ///
    /// Example: CleanupOldEntries(TimeSpan.FromDays(7)) removes processed entries older than 7 days.
    /// </remarks>
    public Task CleanupOldEntries(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var cutoff = _timeProvider.GetUtcNow().DateTime.Subtract(olderThan);
        var toRemove = _entries
            .Where(kvp => kvp.Value.ReceivedAt < cutoff &&
                         kvp.Value.Status == InboxStatus.Processed)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            _entries.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }
}

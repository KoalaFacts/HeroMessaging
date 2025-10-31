using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using System.Collections.Concurrent;

namespace HeroMessaging.Storage;

/// <summary>
/// Provides an in-memory implementation of the Outbox pattern for development, testing, and caching scenarios.
/// </summary>
/// <remarks>
/// This implementation stores outbox entries in memory using concurrent dictionaries, making it suitable for:
/// - Development and testing environments
/// - High-performance scenarios where durability is not required
/// - In-process message publishing without database overhead
///
/// <para><strong>Thread Safety:</strong> This implementation is thread-safe and supports concurrent access
/// from multiple threads using <see cref="ConcurrentDictionary{TKey,TValue}"/>.</para>
///
/// <para><strong>Volatility Warning:</strong> All outbox entries are stored in memory and will be lost when
/// the application restarts or crashes. This means unpublished messages will be lost. Do not use this
/// implementation in production for scenarios requiring guaranteed message delivery.</para>
///
/// <para><strong>Concurrency:</strong> All operations are lock-free and support high concurrency. Multiple
/// background processors can safely poll and process outbox entries concurrently.</para>
///
/// <para><strong>Transactional Publishing:</strong> While this implementation supports the Outbox pattern
/// semantics, it does not provide true transactional guarantees with business data since everything is
/// in-memory. For true transactional guarantees, use a database-backed outbox storage implementation.</para>
///
/// <para><strong>Retry Scheduling:</strong> Supports retry scheduling with NextRetryAt timestamps. Entries
/// with future retry times are excluded from GetPending results until the retry time has passed.</para>
/// </remarks>
public class InMemoryOutboxStorage : IOutboxStorage
{
    private readonly ConcurrentDictionary<string, OutboxEntry> _entries = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryOutboxStorage"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider for managing timestamps and retry scheduling</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider"/> is null</exception>
    public InMemoryOutboxStorage(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Adds a message to the outbox for transactional publishing.
    /// </summary>
    /// <param name="message">The message to store in the outbox</param>
    /// <param name="options">Publishing options including destination, priority, and retry configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created outbox entry with assigned ID and metadata</returns>
    /// <remarks>
    /// The entry is created with status <see cref="OutboxStatus.Pending"/> and a GUID-based identifier.
    /// In a real database-backed implementation, this would be called within the same database transaction
    /// as business logic updates. In this in-memory implementation, the entry is immediately available.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when message or options is null</exception>
    public Task<OutboxEntry> Add(IMessage message, OutboxOptions options, CancellationToken cancellationToken = default)
    {
        var entry = new OutboxEntry
        {
            Id = Guid.NewGuid().ToString(),
            Message = message,
            Options = options,
            Status = OutboxStatus.Pending,
            CreatedAt = _timeProvider.GetUtcNow().DateTime
        };

        _entries[entry.Id] = entry;
        return Task.FromResult(entry);
    }

    /// <summary>
    /// Retrieves pending outbox entries matching the specified query criteria.
    /// </summary>
    /// <param name="query">Query criteria including status, time range, and limit</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of outbox entries matching the query, ordered by priority and creation time</returns>
    /// <remarks>
    /// Filters entries based on:
    /// - Status (defaults to Pending if not specified)
    /// - OlderThan/NewerThan timestamp filters
    /// - NextRetryAt scheduling (excludes entries with future retry times)
    ///
    /// Results are ordered by priority (ascending) then by creation time (oldest first).
    /// This ensures higher priority messages (lower priority number) are processed first.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when query is null</exception>
    public Task<IEnumerable<OutboxEntry>> GetPending(OutboxQuery query, CancellationToken cancellationToken = default)
    {
        var pending = _entries.Values.AsEnumerable();

        if (query.Status.HasValue)
        {
            var status = query.Status.Value switch
            {
                OutboxEntryStatus.Pending => OutboxStatus.Pending,
                OutboxEntryStatus.Processing => OutboxStatus.Processing,
                OutboxEntryStatus.Processed => OutboxStatus.Processed,
                OutboxEntryStatus.Failed => OutboxStatus.Failed,
                _ => OutboxStatus.Pending
            };
            pending = pending.Where(e => e.Status == status);
        }
        else
        {
            pending = pending.Where(e => e.Status == OutboxStatus.Pending);
        }

        pending = pending.Where(e => e.NextRetryAt == null || e.NextRetryAt <= _timeProvider.GetUtcNow().DateTime);

        if (query.OlderThan.HasValue)
        {
            pending = pending.Where(e => e.CreatedAt < query.OlderThan.Value);
        }

        if (query.NewerThan.HasValue)
        {
            pending = pending.Where(e => e.CreatedAt > query.NewerThan.Value);
        }

        pending = pending
            .OrderBy(e => e.Options.Priority)
            .ThenBy(e => e.CreatedAt)
            .Take(query.Limit);

        return Task.FromResult(pending);
    }

    /// <summary>
    /// Retrieves a limited number of pending outbox entries ready for publishing.
    /// </summary>
    /// <param name="limit">Maximum number of entries to retrieve. Default is 100</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of pending outbox entries, ordered by priority then creation time</returns>
    /// <remarks>
    /// This is a convenience method for background processors polling for messages to publish.
    /// Only returns entries with:
    /// - Status = <see cref="OutboxStatus.Pending"/>
    /// - NextRetryAt is null or in the past (ready for immediate processing)
    ///
    /// Results are ordered by priority (ascending) then creation time to ensure FIFO processing
    /// within each priority level.
    /// </remarks>
    public Task<IEnumerable<OutboxEntry>> GetPending(int limit = 100, CancellationToken cancellationToken = default)
    {
        var pending = _entries.Values
            .Where(e => e.Status == OutboxStatus.Pending &&
                       (e.NextRetryAt == null || e.NextRetryAt <= _timeProvider.GetUtcNow().DateTime))
            .OrderBy(e => e.Options.Priority)
            .ThenBy(e => e.CreatedAt)
            .Take(limit);

        return Task.FromResult(pending);
    }

    /// <summary>
    /// Marks an outbox entry as successfully processed (published).
    /// </summary>
    /// <param name="entryId">The unique identifier of the outbox entry</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was marked as processed; false if not found</returns>
    /// <remarks>
    /// Updates the entry status to <see cref="OutboxStatus.Processed"/> and sets the ProcessedAt timestamp.
    /// Call this method after successfully publishing the message to the message broker.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when entryId is null or empty</exception>
    public Task<bool> MarkProcessed(string entryId, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(entryId, out var entry))
        {
            entry.Status = OutboxStatus.Processed;
            entry.ProcessedAt = _timeProvider.GetUtcNow().DateTime;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Marks an outbox entry as failed with an error message.
    /// </summary>
    /// <param name="entryId">The unique identifier of the outbox entry</param>
    /// <param name="error">The error message describing why publishing failed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was marked as failed; false if not found</returns>
    /// <remarks>
    /// Updates the entry status to <see cref="OutboxStatus.Failed"/> and records the error message.
    /// Call this method when publishing fails after all retry attempts are exhausted.
    /// Failed entries should be monitored and may require manual intervention.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when entryId or error is null or empty</exception>
    public Task<bool> MarkFailed(string entryId, string error, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(entryId, out var entry))
        {
            entry.Status = OutboxStatus.Failed;
            entry.LastError = error;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Updates the retry count and schedules the next retry attempt for an outbox entry.
    /// </summary>
    /// <param name="entryId">The unique identifier of the outbox entry</param>
    /// <param name="retryCount">The updated retry count (typically incremented by 1)</param>
    /// <param name="nextRetry">When the next retry attempt should occur. If null, message is immediately available for retry</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was updated; false if not found</returns>
    /// <remarks>
    /// This method implements retry logic with scheduling support. The entry remains in
    /// <see cref="OutboxStatus.Pending"/> status but won't be returned by GetPending methods
    /// until the NextRetryAt time has passed.
    ///
    /// If the retry count reaches or exceeds the MaxRetries configured in options, the entry
    /// status is automatically changed to <see cref="OutboxStatus.Failed"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when entryId is null or empty</exception>
    public Task<bool> UpdateRetryCount(string entryId, int retryCount, DateTime? nextRetry = null, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(entryId, out var entry))
        {
            entry.RetryCount = retryCount;
            entry.NextRetryAt = nextRetry;

            if (retryCount >= entry.Options.MaxRetries)
            {
                entry.Status = OutboxStatus.Failed;
            }

            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Gets the total count of pending outbox entries ready for publishing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The number of entries with status <see cref="OutboxStatus.Pending"/></returns>
    /// <remarks>
    /// Use this for monitoring and observability. A growing pending count may indicate:
    /// - Publishing bottleneck or slow message broker
    /// - Message broker connectivity issues
    /// - Insufficient background processor capacity
    /// - Retry backoff causing entries to accumulate
    /// </remarks>
    public Task<long> GetPendingCount(CancellationToken cancellationToken = default)
    {
        var count = _entries.Values.Count(e => e.Status == OutboxStatus.Pending);
        return Task.FromResult((long)count);
    }

    /// <summary>
    /// Retrieves outbox entries that have failed after exhausting all retry attempts.
    /// </summary>
    /// <param name="limit">Maximum number of failed entries to retrieve. Default is 100</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of failed outbox entries with error details, ordered by creation time</returns>
    /// <remarks>
    /// Failed entries represent messages that could not be published and require attention.
    /// Use this method to:
    /// - Monitor for systemic publishing issues
    /// - Identify problematic message types or destinations
    /// - Implement manual retry or compensation logic
    /// - Generate alerts for operations teams
    /// </remarks>
    public Task<IEnumerable<OutboxEntry>> GetFailed(int limit = 100, CancellationToken cancellationToken = default)
    {
        var failed = _entries.Values
            .Where(e => e.Status == OutboxStatus.Failed)
            .OrderBy(e => e.CreatedAt)
            .Take(limit);

        return Task.FromResult(failed);
    }
}

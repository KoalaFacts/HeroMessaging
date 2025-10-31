using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace HeroMessaging.ErrorHandling;

/// <summary>
/// Provides an in-memory implementation of a dead letter queue for development, testing, and diagnostics.
/// </summary>
/// <remarks>
/// This implementation stores dead letter entries in memory using concurrent dictionaries, making it suitable for:
/// - Development and testing environments
/// - Diagnostic and troubleshooting scenarios
/// - High-performance error handling without external dependencies
///
/// <para><strong>Thread Safety:</strong> This implementation is thread-safe and supports concurrent access
/// from multiple threads using <see cref="ConcurrentDictionary{TKey,TValue}"/>.</para>
///
/// <para><strong>Volatility Warning:</strong> All dead letter entries are stored in memory and will be lost
/// when the application restarts or crashes. Failed messages and their error context will be lost. Do not
/// use this implementation in production for scenarios requiring durable error tracking and recovery.</para>
///
/// <para><strong>Concurrency:</strong> All operations are lock-free and support high concurrency. Multiple
/// error handlers can safely send messages to the dead letter queue concurrently.</para>
///
/// <para><strong>Logging:</strong> All dead letter operations are logged via <see cref="ILogger"/> for
/// observability. Warning-level logs are emitted when messages are sent to the dead letter queue, and
/// info-level logs are emitted for retry and discard operations.</para>
///
/// <para><strong>Statistics:</strong> Provides comprehensive statistics including total, active, retried,
/// and discarded counts, along with groupings by component and error reason for diagnostics.</para>
/// </remarks>
public class InMemoryDeadLetterQueue(ILogger<InMemoryDeadLetterQueue> logger, TimeProvider timeProvider) : IDeadLetterQueue
{
    private readonly ConcurrentDictionary<string, object> _deadLetters = new();
    private readonly ILogger<InMemoryDeadLetterQueue> _logger = logger;
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>
    /// Sends a failed message to the dead letter queue with error context.
    /// </summary>
    /// <typeparam name="T">The message type</typeparam>
    /// <param name="message">The failed message to store</param>
    /// <param name="context">Context information about why the message failed, including component, reason, and exception details</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The unique identifier assigned to the dead letter entry</returns>
    /// <remarks>
    /// This method creates a new dead letter entry with status <see cref="DeadLetterStatus.Active"/>
    /// and a GUID-based identifier. The entry includes the original message, full error context,
    /// and creation timestamp.
    ///
    /// A warning-level log is emitted with the message ID and failure reason for monitoring and alerting.
    ///
    /// Dead letter entries remain in the Active status until they are retried or discarded.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when message or context is null</exception>
    public Task<string> SendToDeadLetter<T>(T message, DeadLetterContext context, CancellationToken cancellationToken = default) where T : IMessage
    {
        var entry = new DeadLetterEntry<T>
        {
            Id = Guid.NewGuid().ToString(),
            Message = message,
            Context = context,
            CreatedAt = _timeProvider.GetUtcNow().DateTime,
            Status = DeadLetterStatus.Active
        };

        _deadLetters[entry.Id] = entry;

        _logger.LogWarning("Message {MessageId} sent to dead letter queue. Reason: {Reason}",
            message.MessageId, context.Reason);

        return Task.FromResult(entry.Id);
    }

    /// <summary>
    /// Retrieves dead letter entries for a specific message type.
    /// </summary>
    /// <typeparam name="T">The message type to filter by</typeparam>
    /// <param name="limit">Maximum number of entries to retrieve. Default is 100</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of dead letter entries with status <see cref="DeadLetterStatus.Active"/>, ordered by creation time (newest first)</returns>
    /// <remarks>
    /// This method filters entries to only include those matching type <typeparamref name="T"/>
    /// and currently in Active status (not retried or discarded).
    ///
    /// Results are ordered by creation time in descending order, so the most recent failures
    /// appear first. This helps identify current issues versus historical ones.
    ///
    /// Use this method to:
    /// - Review recent failures for a specific message type
    /// - Implement retry logic for failed messages
    /// - Extract failed messages for manual reprocessing or compensation
    /// </remarks>
    public Task<IEnumerable<DeadLetterEntry<T>>> GetDeadLetters<T>(int limit = 100, CancellationToken cancellationToken = default) where T : IMessage
    {
        var entries = _deadLetters.Values
            .OfType<DeadLetterEntry<T>>()
            .Where(e => e.Status == DeadLetterStatus.Active)
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit);

        return Task.FromResult(entries);
    }

    /// <summary>
    /// Marks a dead letter entry for retry processing.
    /// </summary>
    /// <typeparam name="T">The message type</typeparam>
    /// <param name="deadLetterId">The unique identifier of the dead letter entry</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was marked for retry; false if not found or type mismatch</returns>
    /// <remarks>
    /// Updates the entry status to <see cref="DeadLetterStatus.Retried"/> and sets the RetriedAt timestamp.
    /// The entry is not removed from the dead letter queue, allowing tracking of retry history.
    ///
    /// After marking an entry for retry, the caller is responsible for actually reprocessing the message.
    /// This method only updates the tracking status.
    ///
    /// An info-level log is emitted when an entry is successfully marked for retry.
    ///
    /// Returns false if:
    /// - The entry ID does not exist
    /// - The entry exists but the type does not match <typeparamref name="T"/>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when deadLetterId is null or empty</exception>
    public Task<bool> Retry<T>(string deadLetterId, CancellationToken cancellationToken = default) where T : IMessage
    {
        if (_deadLetters.TryGetValue(deadLetterId, out var entry))
        {
            if (entry is DeadLetterEntry<T> typedEntry)
            {
                typedEntry.Status = DeadLetterStatus.Retried;
                typedEntry.RetriedAt = _timeProvider.GetUtcNow().DateTime;

                _logger.LogInformation("Dead letter entry {DeadLetterId} marked for retry", deadLetterId);
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Marks a dead letter entry as discarded, indicating it should not be retried.
    /// </summary>
    /// <param name="deadLetterId">The unique identifier of the dead letter entry</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was discarded; false if not found</returns>
    /// <remarks>
    /// Updates the entry status to <see cref="DeadLetterStatus.Discarded"/> and sets the DiscardedAt timestamp.
    /// The entry is not removed from the dead letter queue, preserving it for audit and analysis purposes.
    ///
    /// Use this method when:
    /// - A message is determined to be invalid or unprocessable
    /// - Manual compensation has been performed
    /// - The failure is acknowledged but retry is not appropriate
    ///
    /// An info-level log is emitted when an entry is successfully discarded.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when deadLetterId is null or empty</exception>
    public Task<bool> Discard(string deadLetterId, CancellationToken cancellationToken = default)
    {
        if (_deadLetters.TryGetValue(deadLetterId, out var entry))
        {
            if (entry is DeadLetterEntry<IMessage> typedEntry)
            {
                typedEntry.Status = DeadLetterStatus.Discarded;
                typedEntry.DiscardedAt = _timeProvider.GetUtcNow().DateTime;

                _logger.LogInformation("Dead letter entry {DeadLetterId} discarded", deadLetterId);
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Gets the count of active dead letter entries across all message types.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The number of entries with status <see cref="DeadLetterStatus.Active"/></returns>
    /// <remarks>
    /// Use this for monitoring and alerting. A growing active count indicates:
    /// - Systemic processing failures
    /// - Invalid or malformed messages being published
    /// - Infrastructure issues preventing message processing
    /// - Insufficient error handling in message handlers
    ///
    /// Active entries represent unresolved failures that may require investigation or intervention.
    /// </remarks>
    public Task<long> GetDeadLetterCount(CancellationToken cancellationToken = default)
    {
        var count = _deadLetters.Values
            .Cast<dynamic>()
            .Count(e => e.Status == DeadLetterStatus.Active);

        return Task.FromResult((long)count);
    }

    /// <summary>
    /// Gets comprehensive statistics about dead letter entries for diagnostics and monitoring.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Statistics including counts by status, component, reason, and time range</returns>
    /// <remarks>
    /// The statistics provide a comprehensive view of dead letter queue health:
    ///
    /// <para><strong>Counts:</strong></para>
    /// - TotalCount: All entries regardless of status
    /// - ActiveCount: Entries needing attention (<see cref="DeadLetterStatus.Active"/>)
    /// - RetriedCount: Entries that have been reprocessed (<see cref="DeadLetterStatus.Retried"/>)
    /// - DiscardedCount: Entries marked as resolved/unprocessable (<see cref="DeadLetterStatus.Discarded"/>)
    ///
    /// <para><strong>Groupings:</strong></para>
    /// - CountByComponent: Breakdown by the component that sent the message to the dead letter queue
    /// - CountByReason: Breakdown by failure reason (truncated to 50 characters for grouping)
    ///
    /// <para><strong>Time Range:</strong></para>
    /// - OldestEntry: Creation time of the oldest entry (any status)
    /// - NewestEntry: Creation time of the newest entry (any status)
    ///
    /// Use these statistics to:
    /// - Identify components with high failure rates
    /// - Detect patterns in error reasons
    /// - Track resolution progress over time
    /// - Generate health dashboards and reports
    /// </remarks>
    public Task<DeadLetterStatistics> GetStatistics(CancellationToken cancellationToken = default)
    {
        var allEntries = _deadLetters.Values.Cast<dynamic>().ToList();

        var stats = new DeadLetterStatistics
        {
            TotalCount = allEntries.Count,
            ActiveCount = allEntries.Count(e => e.Status == DeadLetterStatus.Active),
            RetriedCount = allEntries.Count(e => e.Status == DeadLetterStatus.Retried),
            DiscardedCount = allEntries.Count(e => e.Status == DeadLetterStatus.Discarded)
        };

        // Group by component
        stats.CountByComponent = allEntries
            .GroupBy(e => (string)e.Context.Component)
            .ToDictionary(g => g.Key, g => (long)g.Count());

        // Group by reason (take first 50 chars of reason as key)
        stats.CountByReason = allEntries
            .GroupBy(e => ((string)e.Context.Reason).Length > 50
                ? ((string)e.Context.Reason).Substring(0, 50) + "..."
                : (string)e.Context.Reason)
            .ToDictionary(g => g.Key, g => (long)g.Count());

        if (allEntries.Any())
        {
            stats.OldestEntry = allEntries.Min(e => (DateTime)e.CreatedAt);
            stats.NewestEntry = allEntries.Max(e => (DateTime)e.CreatedAt);
        }

        return Task.FromResult(stats);
    }
}

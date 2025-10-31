using System.Collections.Concurrent;
using HeroMessaging.Abstractions.Scheduling;

namespace HeroMessaging.Scheduling;

/// <summary>
/// In-memory implementation of scheduled message storage.
/// </summary>
/// <remarks>
/// This implementation uses ConcurrentDictionary for thread-safe storage and is suitable
/// for development and testing. Messages are not persisted and will be lost on restart.
/// </remarks>
public sealed class InMemoryScheduledMessageStorage : IScheduledMessageStorage
{
    private readonly ConcurrentDictionary<Guid, ScheduledMessageEntry> _storage;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryScheduledMessageStorage"/> class.
    /// </summary>
    public InMemoryScheduledMessageStorage()
    {
        _storage = new ConcurrentDictionary<Guid, ScheduledMessageEntry>();
    }

    /// <summary>
    /// Adds a scheduled message to the in-memory storage.
    /// </summary>
    /// <param name="message">The scheduled message to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created storage entry containing the message and metadata.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a message with the same schedule ID already exists.</exception>
    public Task<ScheduledMessageEntry> AddAsync(ScheduledMessage message, CancellationToken cancellationToken = default)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        var entry = new ScheduledMessageEntry
        {
            ScheduleId = message.ScheduleId,
            Message = message,
            Status = ScheduledMessageStatus.Pending,
            LastUpdated = DateTimeOffset.UtcNow
        };

        if (!_storage.TryAdd(message.ScheduleId, entry))
        {
            throw new InvalidOperationException($"Message with ScheduleId {message.ScheduleId} already exists");
        }

        return Task.FromResult(entry);
    }

    /// <summary>
    /// Retrieves scheduled messages that are due for delivery as of the specified time.
    /// Messages are ordered by delivery time (earliest first), then by priority (highest first).
    /// </summary>
    /// <param name="asOf">The time to check for due messages. Messages scheduled at or before this time are returned.</param>
    /// <param name="limit">The maximum number of due messages to return. Defaults to 100.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of due messages, limited to the specified count.</returns>
    public Task<IReadOnlyList<ScheduledMessageEntry>> GetDueAsync(
        DateTimeOffset asOf,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var dueMessages = _storage.Values
            .Where(e => e.Status == ScheduledMessageStatus.Pending && e.Message.DeliverAt <= asOf)
            .OrderBy(e => e.Message.DeliverAt)
            .ThenByDescending(e => e.Message.Options.Priority)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<ScheduledMessageEntry>>(dueMessages);
    }

    /// <summary>
    /// Retrieves a scheduled message entry by its schedule ID.
    /// </summary>
    /// <param name="scheduleId">The unique identifier of the scheduled message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The scheduled message entry if found; otherwise, null.</returns>
    public Task<ScheduledMessageEntry?> GetAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        _storage.TryGetValue(scheduleId, out var entry);
        return Task.FromResult(entry);
    }

    /// <summary>
    /// Cancels a pending scheduled message.
    /// Only messages with status Pending can be cancelled.
    /// </summary>
    /// <param name="scheduleId">The unique identifier of the scheduled message to cancel.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the message was successfully cancelled; false if not found or already delivered/cancelled.</returns>
    public Task<bool> CancelAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        if (_storage.TryGetValue(scheduleId, out var entry))
        {
            // Only cancel if pending
            if (entry.Status == ScheduledMessageStatus.Pending)
            {
                entry.Status = ScheduledMessageStatus.Cancelled;
                entry.LastUpdated = DateTimeOffset.UtcNow;
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Marks a scheduled message as successfully delivered.
    /// Updates the message status to Delivered and records the delivery timestamp.
    /// </summary>
    /// <param name="scheduleId">The unique identifier of the scheduled message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the message was marked as delivered; false if not found.</returns>
    public Task<bool> MarkDeliveredAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        if (_storage.TryGetValue(scheduleId, out var entry))
        {
            entry.Status = ScheduledMessageStatus.Delivered;
            entry.DeliveredAt = DateTimeOffset.UtcNow;
            entry.LastUpdated = DateTimeOffset.UtcNow;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Marks a scheduled message as failed with an error message.
    /// Updates the message status to Failed and records the error details.
    /// </summary>
    /// <param name="scheduleId">The unique identifier of the scheduled message.</param>
    /// <param name="error">The error message describing the failure.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the message was marked as failed; false if not found.</returns>
    public Task<bool> MarkFailedAsync(Guid scheduleId, string error, CancellationToken cancellationToken = default)
    {
        if (_storage.TryGetValue(scheduleId, out var entry))
        {
            entry.Status = ScheduledMessageStatus.Failed;
            entry.ErrorMessage = error;
            entry.LastUpdated = DateTimeOffset.UtcNow;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Gets the total count of scheduled messages with Pending status.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of pending scheduled messages.</returns>
    public Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        var count = _storage.Values.Count(e => e.Status == ScheduledMessageStatus.Pending);
        return Task.FromResult((long)count);
    }

    /// <summary>
    /// Queries scheduled messages based on the specified criteria.
    /// Supports filtering by status, destination, message type, delivery time range, and pagination.
    /// </summary>
    /// <param name="query">The query parameters for filtering and pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of scheduled messages matching the query criteria.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is null.</exception>
    public Task<IReadOnlyList<ScheduledMessageEntry>> QueryAsync(
        ScheduledMessageQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));

        var results = _storage.Values.AsEnumerable();

        // Apply filters
        if (query.Status.HasValue)
        {
            results = results.Where(e => e.Status == query.Status.Value);
        }

        if (!string.IsNullOrEmpty(query.Destination))
        {
            results = results.Where(e => e.Message.Options.Destination == query.Destination);
        }

        if (!string.IsNullOrEmpty(query.MessageType))
        {
            results = results.Where(e => e.Message.Message.GetType().Name == query.MessageType);
        }

        if (query.DeliverAfter.HasValue)
        {
            results = results.Where(e => e.Message.DeliverAt >= query.DeliverAfter.Value);
        }

        if (query.DeliverBefore.HasValue)
        {
            results = results.Where(e => e.Message.DeliverAt <= query.DeliverBefore.Value);
        }

        // Apply paging
        if (query.Offset > 0)
        {
            results = results.Skip(query.Offset);
        }

        if (query.Limit > 0)
        {
            results = results.Take(query.Limit);
        }

        var list = results.ToList();
        return Task.FromResult<IReadOnlyList<ScheduledMessageEntry>>(list);
    }

    /// <summary>
    /// Removes delivered and cancelled scheduled messages that were last updated before the specified time.
    /// This helps manage memory usage by removing old completed messages.
    /// </summary>
    /// <param name="olderThan">The cutoff time. Messages last updated before this time will be removed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of messages removed from storage.</returns>
    public Task<long> CleanupAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        var toRemove = _storage.Values
            .Where(e => (e.Status == ScheduledMessageStatus.Delivered || e.Status == ScheduledMessageStatus.Cancelled)
                        && e.LastUpdated < olderThan)
            .Select(e => e.ScheduleId)
            .ToList();

        var removed = 0;
        foreach (var scheduleId in toRemove)
        {
            if (_storage.TryRemove(scheduleId, out _))
            {
                removed++;
            }
        }

        return Task.FromResult((long)removed);
    }
}

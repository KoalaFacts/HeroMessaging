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
    private readonly TimeProvider _timeProvider;

    public InMemoryScheduledMessageStorage(TimeProvider timeProvider)
    {
        _storage = new ConcurrentDictionary<Guid, ScheduledMessageEntry>();
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public Task<ScheduledMessageEntry> AddAsync(ScheduledMessage message, CancellationToken cancellationToken = default)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        var entry = new ScheduledMessageEntry
        {
            ScheduleId = message.ScheduleId,
            Message = message,
            Status = ScheduledMessageStatus.Pending,
            LastUpdated = _timeProvider.GetUtcNow()
        };

        if (!_storage.TryAdd(message.ScheduleId, entry))
        {
            throw new InvalidOperationException($"Message with ScheduleId {message.ScheduleId} already exists");
        }

        return Task.FromResult(entry);
    }

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

    public Task<ScheduledMessageEntry?> GetAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        _storage.TryGetValue(scheduleId, out var entry);
        return Task.FromResult(entry);
    }

    public Task<bool> CancelAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        if (_storage.TryGetValue(scheduleId, out var entry))
        {
            // Only cancel if pending
            if (entry.Status == ScheduledMessageStatus.Pending)
            {
                entry.Status = ScheduledMessageStatus.Cancelled;
                entry.LastUpdated = _timeProvider.GetUtcNow();
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    public Task<bool> MarkDeliveredAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        if (_storage.TryGetValue(scheduleId, out var entry))
        {
            entry.Status = ScheduledMessageStatus.Delivered;
            entry.DeliveredAt = _timeProvider.GetUtcNow();
            entry.LastUpdated = _timeProvider.GetUtcNow();
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<bool> MarkFailedAsync(Guid scheduleId, string error, CancellationToken cancellationToken = default)
    {
        if (_storage.TryGetValue(scheduleId, out var entry))
        {
            entry.Status = ScheduledMessageStatus.Failed;
            entry.ErrorMessage = error;
            entry.LastUpdated = _timeProvider.GetUtcNow();
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        var count = _storage.Values.Count(e => e.Status == ScheduledMessageStatus.Pending);
        return Task.FromResult((long)count);
    }

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

using System.Collections.Concurrent;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;

namespace HeroMessaging.Storage;

public class InMemoryInboxStorage : IInboxStorage
{
    private readonly ConcurrentDictionary<string, InboxEntry> _entries = new();
    private readonly TimeProvider _timeProvider;

    public InMemoryInboxStorage(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public Task<InboxEntry?> AddAsync(IMessage message, InboxOptions options, CancellationToken cancellationToken = default)
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
            ReceivedAt = _timeProvider.GetUtcNow()
        };

        _entries[messageId] = entry;
        return Task.FromResult<InboxEntry?>(entry);
    }

    public Task<bool> IsDuplicateAsync(string messageId, TimeSpan? window = null, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(messageId, out var entry))
        {
            if (window.HasValue)
            {
                // Entry is duplicate if received AFTER (not at) the cutoff time
                var cutoff = _timeProvider.GetUtcNow().Subtract(window.Value);
                return Task.FromResult(entry.ReceivedAt > cutoff);
            }

            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<InboxEntry?> GetAsync(string messageId, CancellationToken cancellationToken = default)
    {
        _entries.TryGetValue(messageId, out var entry);
        return Task.FromResult<InboxEntry?>(entry);
    }

    public Task<bool> MarkProcessedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(messageId, out var entry))
        {
            entry.Status = InboxStatus.Processed;
            entry.ProcessedAt = _timeProvider.GetUtcNow();
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<bool> MarkFailedAsync(string messageId, string error, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(messageId, out var entry))
        {
            entry.Status = InboxStatus.Failed;
            entry.Error = error;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<IEnumerable<InboxEntry>> GetPendingAsync(InboxQuery query, CancellationToken cancellationToken = default)
    {
        var pending = _entries.Values.AsEnumerable();

        if (query.Status.HasValue)
        {
            var status = query.Status.Value switch
            {
                InboxStatus.Pending => InboxStatus.Pending,
                InboxStatus.Processing => InboxStatus.Processing,
                InboxStatus.Processed => InboxStatus.Processed,
                InboxStatus.Failed => InboxStatus.Failed,
                InboxStatus.Duplicate => InboxStatus.Duplicate,
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

    public Task<IEnumerable<InboxEntry>> GetUnprocessedAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        var unprocessed = _entries.Values
            .Where(e => e.Status == InboxStatus.Pending)
            .OrderBy(e => e.ReceivedAt)
            .Take(limit);

        return Task.FromResult(unprocessed);
    }

    public Task<long> GetUnprocessedCountAsync(CancellationToken cancellationToken = default)
    {
        var count = _entries.Values.Count(e => e.Status == InboxStatus.Pending);
        return Task.FromResult((long)count);
    }

    public Task CleanupOldEntriesAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var cutoff = _timeProvider.GetUtcNow().Subtract(olderThan);
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
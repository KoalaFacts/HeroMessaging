using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using System.Collections.Concurrent;

namespace HeroMessaging.Storage;

public class InMemoryOutboxStorage : IOutboxStorage
{
    private readonly ConcurrentDictionary<string, OutboxEntry> _entries = new();
    private readonly TimeProvider _timeProvider;

    public InMemoryOutboxStorage(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

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

    public Task<long> GetPendingCount(CancellationToken cancellationToken = default)
    {
        var count = _entries.Values.Count(e => e.Status == OutboxStatus.Pending);
        return Task.FromResult((long)count);
    }

    public Task<IEnumerable<OutboxEntry>> GetFailed(int limit = 100, CancellationToken cancellationToken = default)
    {
        var failed = _entries.Values
            .Where(e => e.Status == OutboxStatus.Failed)
            .OrderBy(e => e.CreatedAt)
            .Take(limit);

        return Task.FromResult(failed);
    }
}
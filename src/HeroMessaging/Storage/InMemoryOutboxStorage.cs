using System.Collections.Concurrent;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;

namespace HeroMessaging.Storage;
/// <summary>
/// Represents the in memory outbox storage type.
/// </summary>

public class InMemoryOutboxStorage : IOutboxStorage
{
    /// <summary>
    /// Represents entries.
    /// </summary>
    private readonly ConcurrentDictionary<string, OutboxEntry> _entries = new();
    private readonly TimeProvider _timeProvider;
    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryOutboxStorage"/> class.
    /// </summary>

    public InMemoryOutboxStorage(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }
    /// <summary>
    /// Executes add async.
    /// </summary>

    public Task<OutboxEntry> AddAsync(IMessage message, OutboxOptions options, CancellationToken cancellationToken = default)
    {
        var entry = new OutboxEntry
        {
            Id = Guid.NewGuid().ToString(),
            Message = message,
            Options = options,
            Status = OutboxStatus.Pending,
            CreatedAt = _timeProvider.GetUtcNow()
        };

        _entries[entry.Id] = entry;
        return Task.FromResult(entry);
    }
    /// <summary>
    /// Executes get pending async.
    /// </summary>

    public Task<IEnumerable<OutboxEntry>> GetPendingAsync(OutboxQuery query, CancellationToken cancellationToken = default)
    {
        var pending = _entries.Values.AsEnumerable();

        if (query.Status.HasValue)
        {
            var status = query.Status.Value switch
            {
                OutboxStatus.Pending => OutboxStatus.Pending,
                OutboxStatus.Processing => OutboxStatus.Processing,
                OutboxStatus.Processed => OutboxStatus.Processed,
                OutboxStatus.Failed => OutboxStatus.Failed,
                _ => OutboxStatus.Pending
            };
            pending = pending.Where(e => e.Status == status);
        }
        else
        {
            pending = pending.Where(e => e.Status == OutboxStatus.Pending);
        }

        pending = pending.Where(e => e.NextRetryAt == null || e.NextRetryAt <= _timeProvider.GetUtcNow());

        if (query.OlderThan.HasValue)
        {
            pending = pending.Where(e => e.CreatedAt < query.OlderThan.Value);
        }

        if (query.NewerThan.HasValue)
        {
            pending = pending.Where(e => e.CreatedAt > query.NewerThan.Value);
        }

        pending = pending
            .OrderByDescending(e => e.Options.Priority)
            .ThenBy(e => e.CreatedAt)
            .Take(query.Limit);

        return Task.FromResult(pending);
    }
    /// <summary>
    /// Executes get pending async.
    /// </summary>

    public Task<IEnumerable<OutboxEntry>> GetPendingAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        var pending = _entries.Values
            .Where(e => e.Status == OutboxStatus.Pending &&
                       (e.NextRetryAt == null || e.NextRetryAt <= _timeProvider.GetUtcNow()))
            .OrderByDescending(e => e.Options.Priority)
            .ThenBy(e => e.CreatedAt)
            .Take(limit);

        return Task.FromResult(pending);
    }
    /// <summary>
    /// Executes mark processed async.
    /// </summary>

    public Task<bool> MarkProcessedAsync(string entryId, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(entryId, out var entry))
        {
            entry.Status = OutboxStatus.Processed;
            entry.ProcessedAt = _timeProvider.GetUtcNow();
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }
    /// <summary>
    /// Executes mark failed async.
    /// </summary>

    public Task<bool> MarkFailedAsync(string entryId, string error, CancellationToken cancellationToken = default)
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
    /// Executes update retry count async.
    /// </summary>

    public Task<bool> UpdateRetryCountAsync(string entryId, int retryCount, DateTimeOffset? nextRetry = null, CancellationToken cancellationToken = default)
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
    /// Executes get pending count async.
    /// </summary>

    public Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        var count = _entries.Values.Count(e => e.Status == OutboxStatus.Pending);
        return Task.FromResult((long)count);
    }
    /// <summary>
    /// Executes get failed async.
    /// </summary>

    public Task<IEnumerable<OutboxEntry>> GetFailedAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        var failed = _entries.Values
            .Where(e => e.Status == OutboxStatus.Failed)
            .OrderBy(e => e.CreatedAt)
            .Take(limit);

        return Task.FromResult(failed);
    }
}

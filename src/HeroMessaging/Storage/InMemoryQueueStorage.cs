using System.Collections.Concurrent;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;

namespace HeroMessaging.Storage;

public class InMemoryQueueStorage : IQueueStorage
{
    private readonly ConcurrentDictionary<string, Queue> _queues = new();
    private readonly TimeProvider _timeProvider;
#if NET9_0_OR_GREATER
    private readonly Lock _dequeueLock = new();
#else
    private readonly object _dequeueLock = new();
#endif

    public InMemoryQueueStorage(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public Task<QueueEntry> EnqueueAsync(string queueName, IMessage message, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        var queue = _queues.GetOrAdd(queueName, _ => new Queue());
        var now = _timeProvider.GetUtcNow();

        var entry = new QueueEntry
        {
            Id = Guid.NewGuid().ToString(),
            Message = message,
            Options = options ?? new EnqueueOptions(),
            EnqueuedAt = now,
            VisibleAt = options?.Delay.HasValue == true
                ? now.Add(options.Delay.Value)
                : now
        };

        queue.Entries[entry.Id] = entry;
        return Task.FromResult(entry);
    }

    public Task<QueueEntry?> DequeueAsync(string queueName, CancellationToken cancellationToken = default)
    {
        if (!_queues.TryGetValue(queueName, out var queue))
        {
            return Task.FromResult<QueueEntry?>(null);
        }

        lock (_dequeueLock)
        {
            var now = _timeProvider.GetUtcNow();
            var entry = queue.Entries.Values
                .Where(e => e.VisibleAt <= now && e.DequeueCount < (queue.Options?.MaxDequeueCount ?? 10))
                .OrderByDescending(e => e.Options.Priority)
                .ThenBy(e => e.EnqueuedAt)
                .FirstOrDefault();

            if (entry != null)
            {
                entry.DequeueCount++;
                entry.VisibleAt = now.Add(queue.Options?.VisibilityTimeout ?? TimeSpan.FromMinutes(1));
            }

            return Task.FromResult(entry);
        }
    }

    public Task<IEnumerable<QueueEntry>> PeekAsync(string queueName, int count = 1, CancellationToken cancellationToken = default)
    {
        if (!_queues.TryGetValue(queueName, out var queue))
        {
            return Task.FromResult(Enumerable.Empty<QueueEntry>());
        }

        var now = _timeProvider.GetUtcNow();
        var entries = queue.Entries.Values
            .Where(e => e.VisibleAt <= now)
            .OrderByDescending(e => e.Options.Priority)
            .ThenBy(e => e.EnqueuedAt)
            .Take(count);

        return Task.FromResult(entries);
    }

    public Task<bool> AcknowledgeAsync(string queueName, string entryId, CancellationToken cancellationToken = default)
    {
        if (_queues.TryGetValue(queueName, out var queue))
        {
            return Task.FromResult(queue.Entries.TryRemove(entryId, out _));
        }

        return Task.FromResult(false);
    }

    public Task<bool> RejectAsync(string queueName, string entryId, bool requeue = false, CancellationToken cancellationToken = default)
    {
        if (_queues.TryGetValue(queueName, out var queue))
        {
            if (queue.Entries.TryGetValue(entryId, out var entry))
            {
                if (requeue)
                {
                    entry.VisibleAt = _timeProvider.GetUtcNow();
                    entry.DequeueCount = 0;
                    return Task.FromResult(true);
                }
                else
                {
                    return Task.FromResult(queue.Entries.TryRemove(entryId, out _));
                }
            }
        }

        return Task.FromResult(false);
    }

    public Task<long> GetQueueDepthAsync(string queueName, CancellationToken cancellationToken = default)
    {
        if (_queues.TryGetValue(queueName, out var queue))
        {
            var count = queue.Entries.Values.Count(e => e.VisibleAt <= _timeProvider.GetUtcNow());
            return Task.FromResult((long)count);
        }

        return Task.FromResult(0L);
    }

    public Task<bool> CreateQueueAsync(string queueName, QueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        var queue = new Queue { Options = options };
        return Task.FromResult(_queues.TryAdd(queueName, queue));
    }

    public Task<bool> DeleteQueueAsync(string queueName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_queues.TryRemove(queueName, out _));
    }

    public Task<IEnumerable<string>> GetQueuesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_queues.Keys.AsEnumerable());
    }

    public Task<bool> QueueExistsAsync(string queueName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_queues.ContainsKey(queueName));
    }

    private class Queue
    {
        public ConcurrentDictionary<string, QueueEntry> Entries { get; } = new();
        public QueueOptions? Options { get; set; }
    }
}

using System.Collections.Concurrent;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;

namespace HeroMessaging.Storage;

public class InMemoryQueueStorage : IQueueStorage
{
    private readonly ConcurrentDictionary<string, Queue> _queues = new();

    public Task<QueueEntry> Enqueue(string queueName, IMessage message, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        var queue = _queues.GetOrAdd(queueName, _ => new Queue());
        
        var entry = new QueueEntry
        {
            Id = Guid.NewGuid().ToString(),
            Message = message,
            Options = options ?? new EnqueueOptions(),
            EnqueuedAt = DateTime.UtcNow,
            VisibleAt = options?.Delay.HasValue == true 
                ? DateTime.UtcNow.Add(options.Delay.Value) 
                : DateTime.UtcNow
        };
        
        queue.Entries[entry.Id] = entry;
        return Task.FromResult(entry);
    }

    public Task<QueueEntry?> Dequeue(string queueName, CancellationToken cancellationToken = default)
    {
        if (!_queues.TryGetValue(queueName, out var queue))
        {
            return Task.FromResult<QueueEntry?>(null);
        }
        
        var now = DateTime.UtcNow;
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

    public Task<IEnumerable<QueueEntry>> Peek(string queueName, int count = 1, CancellationToken cancellationToken = default)
    {
        if (!_queues.TryGetValue(queueName, out var queue))
        {
            return Task.FromResult(Enumerable.Empty<QueueEntry>());
        }
        
        var now = DateTime.UtcNow;
        var entries = queue.Entries.Values
            .Where(e => e.VisibleAt <= now)
            .OrderByDescending(e => e.Options.Priority)
            .ThenBy(e => e.EnqueuedAt)
            .Take(count);
        
        return Task.FromResult(entries);
    }

    public Task<bool> Acknowledge(string queueName, string entryId, CancellationToken cancellationToken = default)
    {
        if (_queues.TryGetValue(queueName, out var queue))
        {
            return Task.FromResult(queue.Entries.TryRemove(entryId, out _));
        }
        
        return Task.FromResult(false);
    }

    public Task<bool> Reject(string queueName, string entryId, bool requeue = false, CancellationToken cancellationToken = default)
    {
        if (_queues.TryGetValue(queueName, out var queue))
        {
            if (queue.Entries.TryGetValue(entryId, out var entry))
            {
                if (requeue)
                {
                    entry.VisibleAt = DateTime.UtcNow;
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

    public Task<long> GetQueueDepth(string queueName, CancellationToken cancellationToken = default)
    {
        if (_queues.TryGetValue(queueName, out var queue))
        {
            var count = queue.Entries.Values.Count(e => e.VisibleAt <= DateTime.UtcNow);
            return Task.FromResult((long)count);
        }
        
        return Task.FromResult(0L);
    }

    public Task<bool> CreateQueue(string queueName, QueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        var queue = new Queue { Options = options };
        return Task.FromResult(_queues.TryAdd(queueName, queue));
    }

    public Task<bool> DeleteQueue(string queueName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_queues.TryRemove(queueName, out _));
    }

    public Task<IEnumerable<string>> GetQueues(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_queues.Keys.AsEnumerable());
    }

    public Task<bool> QueueExists(string queueName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_queues.ContainsKey(queueName));
    }
    
    private class Queue
    {
        public ConcurrentDictionary<string, QueueEntry> Entries { get; } = new();
        public QueueOptions? Options { get; set; }
    }
}
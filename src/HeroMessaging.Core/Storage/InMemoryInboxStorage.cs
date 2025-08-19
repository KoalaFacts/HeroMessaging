using System.Collections.Concurrent;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;

namespace HeroMessaging.Core.Storage;

public class InMemoryInboxStorage : IInboxStorage
{
    private readonly ConcurrentDictionary<string, InboxEntry> _entries = new();

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
            ReceivedAt = DateTime.UtcNow
        };
        
        _entries[messageId] = entry;
        return Task.FromResult<InboxEntry?>(entry);
    }

    public Task<bool> IsDuplicate(string messageId, TimeSpan? window = null, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(messageId, out var entry))
        {
            if (window.HasValue)
            {
                var cutoff = DateTime.UtcNow.Subtract(window.Value);
                return Task.FromResult(entry.ReceivedAt >= cutoff);
            }
            
            return Task.FromResult(true);
        }
        
        return Task.FromResult(false);
    }

    public Task<InboxEntry?> Get(string messageId, CancellationToken cancellationToken = default)
    {
        _entries.TryGetValue(messageId, out var entry);
        return Task.FromResult(entry);
    }

    public Task<bool> MarkProcessed(string messageId, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(messageId, out var entry))
        {
            entry.Status = InboxStatus.Processed;
            entry.ProcessedAt = DateTime.UtcNow;
            return Task.FromResult(true);
        }
        
        return Task.FromResult(false);
    }

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

    public Task<IEnumerable<InboxEntry>> GetUnprocessed(int limit = 100, CancellationToken cancellationToken = default)
    {
        var unprocessed = _entries.Values
            .Where(e => e.Status == InboxStatus.Pending)
            .OrderBy(e => e.ReceivedAt)
            .Take(limit);
        
        return Task.FromResult(unprocessed);
    }

    public Task<long> GetUnprocessedCount(CancellationToken cancellationToken = default)
    {
        var count = _entries.Values.Count(e => e.Status == InboxStatus.Pending);
        return Task.FromResult((long)count);
    }

    public Task CleanupOldEntries(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.Subtract(olderThan);
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
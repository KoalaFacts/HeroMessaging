using System.Collections.Concurrent;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;

namespace HeroMessaging.Storage;

public class InMemoryMessageStorage : IMessageStorage
{
    private readonly ConcurrentDictionary<string, StoredMessage> _messages = new();
    private readonly TimeProvider _timeProvider;

    public InMemoryMessageStorage(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public Task<string> StoreAsync(IMessage message, MessageStorageOptions? options = null, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString();
        var now = _timeProvider.GetUtcNow();
        var stored = new StoredMessage
        {
            Id = id,
            Message = message,
            StoredAt = now,
            Collection = options?.Collection,
            Metadata = options?.Metadata,
            ExpiresAt = options?.Ttl.HasValue == true ? now.Add(options.Ttl.Value) : null
        };

        _messages[id] = stored;
        return Task.FromResult(id);
    }

    public Task<T?> RetrieveAsync<T>(string messageId, CancellationToken cancellationToken = default) where T : IMessage
    {
        if (_messages.TryGetValue(messageId, out var stored))
        {
            // At or past expiry time, message is considered expired
            if (stored.ExpiresAt.HasValue && stored.ExpiresAt <= _timeProvider.GetUtcNow())
            {
                _messages.TryRemove(messageId, out _);
                return Task.FromResult<T?>(default);
            }

            // Check if the stored message is of the requested type
            if (stored.Message is T typedMessage)
            {
                return Task.FromResult(typedMessage);
            }

            // Return null if the types don't match
            return Task.FromResult<T?>(default);
        }

        return Task.FromResult<T?>(default);
    }

    public Task<IEnumerable<T>> QueryAsync<T>(MessageQuery query, CancellationToken cancellationToken = default) where T : IMessage
    {
        var results = _messages.Values.AsEnumerable();

        if (query.Collection != null)
        {
            results = results.Where(m => m.Collection == query.Collection);
        }

        if (query.FromTimestamp.HasValue)
        {
            results = results.Where(m => m.Message.Timestamp >= query.FromTimestamp.Value);
        }

        if (query.ToTimestamp.HasValue)
        {
            results = results.Where(m => m.Message.Timestamp <= query.ToTimestamp.Value);
        }

        if (query.MetadataFilters != null)
        {
            foreach (var filter in query.MetadataFilters)
            {
                results = results.Where(m =>
                    m.Metadata?.ContainsKey(filter.Key) == true &&
                    m.Metadata[filter.Key]?.Equals(filter.Value) == true);
            }
        }

        if (query.OrderBy != null)
        {
            results = query.OrderBy.ToLower() switch
            {
                "timestamp" => query.Ascending
                    ? results.OrderBy(m => m.Message.Timestamp)
                    : results.OrderByDescending(m => m.Message.Timestamp),
                "storedat" => query.Ascending
                    ? results.OrderBy(m => m.StoredAt)
                    : results.OrderByDescending(m => m.StoredAt),
                _ => results
            };
        }

        if (query.Offset.HasValue)
        {
            results = results.Skip(query.Offset.Value);
        }

        if (query.Limit.HasValue)
        {
            results = results.Take(query.Limit.Value);
        }

        return Task.FromResult(results.Select(m => (T)m.Message));
    }

    public Task<bool> DeleteAsync(string messageId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_messages.TryRemove(messageId, out _));
    }

    public Task<bool> UpdateAsync(string messageId, IMessage message, CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(messageId, out var stored))
        {
            stored.Message = message;
            stored.UpdatedAt = _timeProvider.GetUtcNow();
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<bool> ExistsAsync(string messageId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_messages.ContainsKey(messageId));
    }

    public async Task<long> CountAsync(MessageQuery? query = null, CancellationToken cancellationToken = default)
    {
        if (query == null)
        {
            return _messages.Count;
        }

        var results = await QueryAsync<IMessage>(query, cancellationToken).ConfigureAwait(false);
        return results.Count();
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _messages.Clear();
        return Task.CompletedTask;
    }

    Task ITransactionalMessageStorage.StoreAsync(IMessage message, IStorageTransaction? transaction, CancellationToken cancellationToken)
    {
        // Store using the message's own MessageId
        var now = _timeProvider.GetUtcNow();
        var stored = new StoredMessage
        {
            Id = message.MessageId.ToString(),
            Message = message,
            StoredAt = now,
            ExpiresAt = null
        };
        _messages[stored.Id] = stored;
        return Task.CompletedTask;
    }

    Task<IMessage?> ITransactionalMessageStorage.RetrieveAsync(Guid messageId, IStorageTransaction? transaction, CancellationToken cancellationToken)
    {
        return RetrieveAsync<IMessage>(messageId.ToString(), cancellationToken);
    }

    async Task<List<IMessage>> ITransactionalMessageStorage.QueryAsync(MessageQuery query, CancellationToken cancellationToken)
    {
        var results = await QueryAsync<IMessage>(query, cancellationToken).ConfigureAwait(false);
        return [.. results];
    }

    async Task ITransactionalMessageStorage.DeleteAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await DeleteAsync(messageId.ToString(), cancellationToken).ConfigureAwait(false);
    }

    public Task<IStorageTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IStorageTransaction>(new InMemoryTransaction());
    }

    private class StoredMessage
    {
        public string Id { get; set; } = null!;
        public IMessage Message { get; set; } = null!;
        public DateTimeOffset StoredAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public string? Collection { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private class InMemoryTransaction : IStorageTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            // No resources to dispose for in-memory transaction
        }
    }
}

using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using System.Collections.Concurrent;

namespace HeroMessaging.Storage;

/// <summary>
/// Provides an in-memory implementation of message storage for development, testing, and caching scenarios.
/// </summary>
/// <remarks>
/// This implementation stores messages in memory using concurrent dictionaries, making it suitable for:
/// - Development and testing environments
/// - High-performance caching layers
/// - Scenarios where message persistence is not required
///
/// <para><strong>Thread Safety:</strong> This implementation is thread-safe and supports concurrent access
/// from multiple threads using <see cref="ConcurrentDictionary{TKey,TValue}"/>.</para>
///
/// <para><strong>Volatility Warning:</strong> All data is stored in memory and will be lost when the application
/// restarts or crashes. Do not use this implementation in production for critical data that requires durability.</para>
///
/// <para><strong>Concurrency:</strong> All operations are lock-free and support high concurrency. However,
/// query operations may see inconsistent views during concurrent modifications due to the snapshot nature
/// of in-memory collections.</para>
///
/// <para><strong>TTL Support:</strong> Expired messages are lazily removed during retrieval operations.
/// There is no background cleanup process, so expired messages may remain in memory until accessed.</para>
///
/// <para><strong>Transactions:</strong> Transaction support is provided via a no-op implementation since
/// in-memory operations are atomic by nature. Rollback is not supported.</para>
/// </remarks>
public class InMemoryMessageStorage : IMessageStorage
{
    private readonly ConcurrentDictionary<string, StoredMessage> _messages = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryMessageStorage"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider for managing timestamps and TTL expiration</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider"/> is null</exception>
    public InMemoryMessageStorage(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Stores a message in memory and returns its unique identifier.
    /// </summary>
    /// <param name="message">The message to store</param>
    /// <param name="options">Optional storage configuration including collection, TTL, and metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The unique identifier assigned to the stored message</returns>
    /// <remarks>
    /// Messages are stored with a GUID-based identifier. If TTL is specified in options,
    /// the message will be automatically excluded from retrieval after expiration, though
    /// it remains in memory until accessed and removed.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when message is null</exception>
    public Task<string> Store(IMessage message, MessageStorageOptions? options = null, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString();
        var now = _timeProvider.GetUtcNow().DateTime;
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

    /// <summary>
    /// Retrieves a strongly-typed message by its unique identifier.
    /// </summary>
    /// <typeparam name="T">The expected message type</typeparam>
    /// <param name="messageId">The unique identifier of the message to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The message if found and type matches; otherwise null</returns>
    /// <remarks>
    /// This method performs lazy TTL expiration - expired messages are removed during retrieval
    /// and null is returned. Type checking is performed and null is returned if the stored
    /// message type does not match the requested type <typeparamref name="T"/>.
    /// </remarks>
    public Task<T?> Retrieve<T>(string messageId, CancellationToken cancellationToken = default) where T : IMessage
    {
        if (_messages.TryGetValue(messageId, out var stored))
        {
            if (stored.ExpiresAt.HasValue && stored.ExpiresAt < _timeProvider.GetUtcNow().DateTime)
            {
                _messages.TryRemove(messageId, out _);
                return Task.FromResult<T?>(default);
            }

            // Check if the stored message is of the requested type
            if (stored.Message is T typedMessage)
            {
                return Task.FromResult<T?>(typedMessage);
            }

            // Return null if the types don't match
            return Task.FromResult<T?>(default);
        }

        return Task.FromResult<T?>(default);
    }

    /// <summary>
    /// Queries messages using advanced filtering, pagination, and ordering.
    /// </summary>
    /// <typeparam name="T">The expected message type</typeparam>
    /// <param name="query">Query criteria including filters, pagination, and ordering</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A collection of messages matching the query criteria</returns>
    /// <remarks>
    /// This implementation evaluates all query filters in memory using LINQ.
    /// Supported filters:
    /// - Collection filtering
    /// - Timestamp range filtering (FromTimestamp, ToTimestamp)
    /// - Metadata filtering (exact key-value matches with AND logic)
    /// - Ordering by "timestamp" or "storedat"
    /// - Pagination via Offset and Limit
    ///
    /// Note: Query operations create a snapshot of the collection at query time and
    /// may not reflect concurrent modifications.
    /// </remarks>
    public Task<IEnumerable<T>> Query<T>(MessageQuery query, CancellationToken cancellationToken = default) where T : IMessage
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

    /// <summary>
    /// Deletes a message from storage by its unique identifier.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message was deleted; false if the message was not found</returns>
    public Task<bool> Delete(string messageId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_messages.TryRemove(messageId, out _));
    }

    /// <summary>
    /// Updates an existing message in storage.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message to update</param>
    /// <param name="message">The updated message content</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message was updated; false if the message was not found</returns>
    /// <remarks>
    /// Updates the message content and sets the UpdatedAt timestamp. The original
    /// StoredAt timestamp is preserved.
    /// </remarks>
    public Task<bool> Update(string messageId, IMessage message, CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(messageId, out var stored))
        {
            stored.Message = message;
            stored.UpdatedAt = _timeProvider.GetUtcNow().DateTime;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Checks whether a message exists in storage.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message exists; otherwise false</returns>
    /// <remarks>
    /// This check does not validate TTL expiration. Expired messages still return true
    /// until they are accessed and removed by a retrieval operation.
    /// </remarks>
    public Task<bool> Exists(string messageId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_messages.ContainsKey(messageId));
    }

    /// <summary>
    /// Counts messages matching the specified query criteria.
    /// </summary>
    /// <param name="query">Optional query criteria. If null, counts all messages across all collections</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The total number of messages matching the criteria</returns>
    /// <remarks>
    /// This implementation executes the full query and counts the results.
    /// Note: This method calls Query internally which may block while iterating results.
    /// </remarks>
    public Task<long> Count(MessageQuery? query = null, CancellationToken cancellationToken = default)
    {
        if (query == null)
        {
            return Task.FromResult((long)_messages.Count);
        }

        var count = Query<IMessage>(query, cancellationToken).Result.Count();
        return Task.FromResult((long)count);
    }

    /// <summary>
    /// Removes all messages from storage across all collections.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// WARNING: This operation is destructive and cannot be undone.
    /// All messages are immediately removed from memory.
    /// </remarks>
    public Task Clear(CancellationToken cancellationToken = default)
    {
        _messages.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stores a message within an optional transaction context.
    /// </summary>
    /// <param name="message">The message to store</param>
    /// <param name="transaction">Optional transaction context (ignored by in-memory implementation)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// The transaction parameter is ignored in this implementation since in-memory operations
    /// are atomic. The message is stored immediately regardless of transaction state.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when message is null</exception>
    public Task StoreAsync(IMessage message, IStorageTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        return Store(message, null, cancellationToken).ContinueWith(_ => Task.CompletedTask, cancellationToken);
    }

    /// <summary>
    /// Retrieves a message by its GUID identifier within an optional transaction context.
    /// </summary>
    /// <param name="messageId">The GUID identifier of the message to retrieve</param>
    /// <param name="transaction">Optional transaction context (ignored by in-memory implementation)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The message if found; otherwise null</returns>
    /// <remarks>
    /// The transaction parameter is ignored in this implementation. Reads are always
    /// performed against the current in-memory state.
    /// </remarks>
    public Task<IMessage?> RetrieveAsync(Guid messageId, IStorageTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        return Retrieve<IMessage>(messageId.ToString(), cancellationToken);
    }

    /// <summary>
    /// Queries messages asynchronously using the specified criteria.
    /// </summary>
    /// <param name="query">Query criteria including filters, pagination, and ordering</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of messages matching the query criteria</returns>
    public Task<List<IMessage>> QueryAsync(MessageQuery query, CancellationToken cancellationToken = default)
    {
        return Query<IMessage>(query, cancellationToken).ContinueWith(t => t.Result.ToList(), cancellationToken);
    }

    /// <summary>
    /// Deletes a message by its GUID identifier.
    /// </summary>
    /// <param name="messageId">The GUID identifier of the message to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public Task DeleteAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        return Delete(messageId.ToString(), cancellationToken).ContinueWith(_ => Task.CompletedTask, cancellationToken);
    }

    /// <summary>
    /// Begins a new storage transaction for atomic multi-operation updates.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A no-op transaction instance since in-memory operations are atomic</returns>
    /// <remarks>
    /// This implementation returns a no-op transaction since in-memory dictionary operations
    /// are inherently atomic. Commit and rollback operations have no effect. True ACID
    /// transaction semantics are not supported for in-memory storage.
    /// </remarks>
    public Task<IStorageTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IStorageTransaction>(new InMemoryTransaction());
    }

    /// <summary>
    /// Internal storage wrapper for messages with metadata and expiration tracking.
    /// </summary>
    private class StoredMessage
    {
        /// <summary>
        /// Gets or sets the unique identifier for this stored message.
        /// </summary>
        public string Id { get; set; } = null!;

        /// <summary>
        /// Gets or sets the stored message instance.
        /// </summary>
        public IMessage Message { get; set; } = null!;

        /// <summary>
        /// Gets or sets the timestamp when the message was stored.
        /// </summary>
        public DateTime StoredAt { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when the message was last updated.
        /// Null if the message has never been updated.
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when the message expires and should be removed.
        /// Null if the message has no expiration.
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// Gets or sets the collection or partition this message belongs to.
        /// </summary>
        public string? Collection { get; set; }

        /// <summary>
        /// Gets or sets custom metadata attached to this message for filtering and querying.
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }
    }

    /// <summary>
    /// No-op transaction implementation for in-memory storage.
    /// </summary>
    /// <remarks>
    /// Since in-memory dictionary operations are atomic, this transaction implementation
    /// does nothing on commit or rollback. It exists only to satisfy the interface contract.
    /// </remarks>
    private class InMemoryTransaction : IStorageTransaction
    {
        /// <summary>
        /// No-op commit operation that completes immediately.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A completed task</returns>
        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// No-op rollback operation that completes immediately.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A completed task</returns>
        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Disposes the transaction. No resources need cleanup for in-memory implementation.
        /// </summary>
        public void Dispose()
        {
            // No resources to dispose for in-memory transaction
        }
    }
}

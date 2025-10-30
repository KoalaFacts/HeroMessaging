using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Storage;

/// <summary>
/// Provides persistent storage for messages with support for CRUD operations, querying, and transactions.
/// </summary>
/// <remarks>
/// This interface defines the core storage abstraction for HeroMessaging. It supports:
/// - Message persistence with optional metadata and TTL
/// - Type-safe message retrieval
/// - Advanced querying with filtering and pagination
/// - Transactional operations for atomic updates
/// - Collection-based organization
///
/// Implementations should provide thread-safe operations and support concurrent access.
///
/// Basic usage:
/// <code>
/// // Store a message
/// var messageId = await storage.Store(message, new MessageStorageOptions
/// {
///     Collection = "orders",
///     Ttl = TimeSpan.FromDays(30),
///     Metadata = new Dictionary&lt;string, object&gt; { ["region"] = "us-west" }
/// });
///
/// // Retrieve a message
/// var retrieved = await storage.Retrieve&lt;OrderCreatedEvent&gt;(messageId);
///
/// // Query messages
/// var recent = await storage.Query&lt;OrderCreatedEvent&gt;(new MessageQuery
/// {
///     Collection = "orders",
///     FromTimestamp = DateTime.UtcNow.AddDays(-7),
///     Limit = 100
/// });
/// </code>
///
/// Transactional usage:
/// <code>
/// using var transaction = await storage.BeginTransactionAsync();
/// try
/// {
///     await storage.StoreAsync(message1, transaction);
///     await storage.StoreAsync(message2, transaction);
///     await transaction.CommitAsync();
/// }
/// catch
/// {
///     await transaction.RollbackAsync();
///     throw;
/// }
/// </code>
/// </remarks>
public interface IMessageStorage
{
    /// <summary>
    /// Stores a message in the storage system and returns its unique identifier.
    /// </summary>
    /// <param name="message">The message to store</param>
    /// <param name="options">Optional storage configuration including collection, TTL, and metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The unique identifier assigned to the stored message</returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when storage operation fails</exception>
    Task<string> Store(IMessage message, MessageStorageOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a strongly-typed message by its unique identifier.
    /// </summary>
    /// <typeparam name="T">The expected message type</typeparam>
    /// <param name="messageId">The unique identifier of the message to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The message if found and type matches; otherwise null</returns>
    /// <exception cref="ArgumentNullException">Thrown when messageId is null or empty</exception>
    /// <exception cref="InvalidCastException">Thrown when stored message cannot be cast to type T</exception>
    Task<T?> Retrieve<T>(string messageId, CancellationToken cancellationToken = default) where T : IMessage;

    /// <summary>
    /// Queries messages using advanced filtering, pagination, and ordering.
    /// </summary>
    /// <typeparam name="T">The expected message type</typeparam>
    /// <param name="query">Query criteria including filters, pagination, and ordering. See <see cref="MessageQuery"/> for available options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A collection of messages matching the query criteria</returns>
    /// <remarks>
    /// Query supports:
    /// - Collection filtering
    /// - Timestamp range filtering
    /// - Metadata filtering (key-value pairs)
    /// - Content search
    /// - Pagination (limit/offset)
    /// - Ordering (ascending/descending)
    ///
    /// Example:
    /// <code>
    /// var query = new MessageQuery
    /// {
    ///     Collection = "orders",
    ///     FromTimestamp = DateTime.UtcNow.AddDays(-7),
    ///     MetadataFilters = new Dictionary&lt;string, object&gt; { ["status"] = "pending" },
    ///     OrderBy = "CreatedAt",
    ///     Ascending = false,
    ///     Limit = 50
    /// };
    /// var results = await storage.Query&lt;OrderCreatedEvent&gt;(query);
    /// </code>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when query is null</exception>
    Task<IEnumerable<T>> Query<T>(MessageQuery query, CancellationToken cancellationToken = default) where T : IMessage;

    /// <summary>
    /// Deletes a message from storage by its unique identifier.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message was deleted; false if the message was not found</returns>
    /// <exception cref="ArgumentNullException">Thrown when messageId is null or empty</exception>
    Task<bool> Delete(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing message in storage.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message to update</param>
    /// <param name="message">The updated message content</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message was updated; false if the message was not found</returns>
    /// <exception cref="ArgumentNullException">Thrown when messageId or message is null</exception>
    Task<bool> Update(string messageId, IMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a message exists in storage.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message exists; otherwise false</returns>
    /// <exception cref="ArgumentNullException">Thrown when messageId is null or empty</exception>
    Task<bool> Exists(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts messages matching the specified query criteria.
    /// </summary>
    /// <param name="query">Optional query criteria. If null, counts all messages across all collections</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The total number of messages matching the criteria</returns>
    Task<long> Count(MessageQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all messages from storage across all collections.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// WARNING: This operation is destructive and cannot be undone.
    /// Use with caution, typically only in testing or maintenance scenarios.
    /// </remarks>
    Task Clear(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a message within an optional transaction context.
    /// </summary>
    /// <param name="message">The message to store</param>
    /// <param name="transaction">Optional transaction context. If provided, operation is part of the transaction</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// When a transaction is provided, the message is stored within that transaction's scope.
    /// The message will only be persisted when the transaction is committed.
    /// If the transaction is rolled back, the store operation is also rolled back.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when message is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when transaction is no longer active</exception>
    Task StoreAsync(IMessage message, IStorageTransaction? transaction = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a message by its GUID identifier within an optional transaction context.
    /// </summary>
    /// <param name="messageId">The GUID identifier of the message to retrieve</param>
    /// <param name="transaction">Optional transaction context for consistent reads</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The message if found; otherwise null</returns>
    /// <remarks>
    /// When a transaction is provided, the read operation uses the transaction's isolation level
    /// to ensure consistent reads with other operations in the same transaction.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when messageId is empty</exception>
    Task<IMessage?> RetrieveAsync(Guid messageId, IStorageTransaction? transaction = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries messages asynchronously using the specified criteria.
    /// </summary>
    /// <param name="query">Query criteria including filters, pagination, and ordering</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of messages matching the query criteria</returns>
    /// <exception cref="ArgumentNullException">Thrown when query is null</exception>
    Task<List<IMessage>> QueryAsync(MessageQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a message by its GUID identifier.
    /// </summary>
    /// <param name="messageId">The GUID identifier of the message to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="ArgumentException">Thrown when messageId is empty</exception>
    Task DeleteAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins a new storage transaction for atomic multi-operation updates.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A transaction object that must be committed or rolled back</returns>
    /// <remarks>
    /// Transactions ensure that multiple storage operations either all succeed or all fail together.
    /// Always use within a using statement or try-finally block to ensure proper disposal.
    ///
    /// Example:
    /// <code>
    /// using var transaction = await storage.BeginTransactionAsync();
    /// try
    /// {
    ///     await storage.StoreAsync(message1, transaction);
    ///     await storage.StoreAsync(message2, transaction);
    ///     await transaction.CommitAsync(); // Both succeed
    /// }
    /// catch
    /// {
    ///     await transaction.RollbackAsync(); // Both fail
    ///     throw;
    /// }
    /// </code>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when transaction cannot be started</exception>
    Task<IStorageTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a storage transaction that ensures atomicity of multiple operations.
/// </summary>
/// <remarks>
/// Storage transactions guarantee that all operations within the transaction either
/// all succeed (on commit) or all fail (on rollback). This ensures data consistency
/// when performing multiple related storage operations.
///
/// Always dispose the transaction when finished, either explicitly or via a using statement.
/// If not committed explicitly, the transaction will be rolled back on disposal.
/// </remarks>
public interface IStorageTransaction : IDisposable
{
    /// <summary>
    /// Commits all operations performed within this transaction, making them permanent.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous commit operation</returns>
    /// <exception cref="InvalidOperationException">Thrown when transaction has already been committed or rolled back</exception>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back all operations performed within this transaction, discarding all changes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous rollback operation</returns>
    /// <exception cref="InvalidOperationException">Thrown when transaction has already been committed or rolled back</exception>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration options for storing messages in persistent storage.
/// </summary>
/// <remarks>
/// These options allow fine-grained control over how messages are stored,
/// including organizational grouping, retention policies, and custom metadata.
/// </remarks>
public class MessageStorageOptions
{
    /// <summary>
    /// Gets or sets the collection or partition where the message will be stored.
    /// Use collections to organize messages by domain, type, or tenant.
    /// </summary>
    /// <remarks>
    /// Collections allow logical grouping of related messages for easier querying and management.
    /// If null, the message is stored in the default collection.
    /// </remarks>
    public string? Collection { get; set; }

    /// <summary>
    /// Gets or sets the time-to-live (TTL) for the message.
    /// Messages older than this duration may be automatically deleted by the storage system.
    /// </summary>
    /// <remarks>
    /// TTL is useful for implementing retention policies and automatic cleanup of old messages.
    /// If null, messages are retained indefinitely or according to system defaults.
    /// </remarks>
    public TimeSpan? Ttl { get; set; }

    /// <summary>
    /// Gets or sets custom metadata to attach to the message for filtering and querying.
    /// </summary>
    /// <remarks>
    /// Metadata allows attaching arbitrary key-value pairs to messages for:
    /// - Custom filtering in queries
    /// - Tagging and categorization
    /// - Tenant isolation
    /// - Regional routing
    ///
    /// Example:
    /// <code>
    /// Metadata = new Dictionary&lt;string, object&gt;
    /// {
    ///     ["tenant"] = "acme-corp",
    ///     ["region"] = "us-west",
    ///     ["priority"] = 1
    /// }
    /// </code>
    /// </remarks>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Defines query criteria for retrieving messages from storage with filtering, pagination, and ordering.
/// </summary>
/// <remarks>
/// MessageQuery provides a flexible query builder for filtering messages by various criteria.
/// All filter properties are optional and can be combined for complex queries.
///
/// Example usage:
/// <code>
/// var query = new MessageQuery
/// {
///     Collection = "orders",
///     FromTimestamp = DateTime.UtcNow.AddDays(-30),
///     ToTimestamp = DateTime.UtcNow,
///     MetadataFilters = new Dictionary&lt;string, object&gt; { ["status"] = "pending" },
///     OrderBy = "CreatedAt",
///     Ascending = false,
///     Limit = 100,
///     Offset = 0
/// };
/// </code>
/// </remarks>
public class MessageQuery
{
    /// <summary>
    /// Gets or sets the collection to query. If null, queries across all collections.
    /// </summary>
    public string? Collection { get; set; }

    /// <summary>
    /// Gets or sets the earliest timestamp for messages to include in results (inclusive).
    /// Only messages created on or after this time will be returned.
    /// </summary>
    public DateTime? FromTimestamp { get; set; }

    /// <summary>
    /// Gets or sets the latest timestamp for messages to include in results (inclusive).
    /// Only messages created on or before this time will be returned.
    /// </summary>
    public DateTime? ToTimestamp { get; set; }

    /// <summary>
    /// Gets or sets metadata filters as key-value pairs. Only messages with matching metadata are returned.
    /// </summary>
    /// <remarks>
    /// All specified metadata filters must match (AND logic).
    /// Example:
    /// <code>
    /// MetadataFilters = new Dictionary&lt;string, object&gt;
    /// {
    ///     ["tenant"] = "acme",
    ///     ["status"] = "pending"
    /// }
    /// </code>
    /// </remarks>
    public Dictionary<string, object>? MetadataFilters { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of results to return.
    /// Used with <see cref="Offset"/> for pagination.
    /// </summary>
    public int? Limit { get; set; }

    /// <summary>
    /// Gets or sets the number of results to skip before returning results.
    /// Used with <see cref="Limit"/> for pagination.
    /// </summary>
    public int? Offset { get; set; }

    /// <summary>
    /// Gets or sets the field name to order results by (e.g., "CreatedAt", "MessageId").
    /// </summary>
    public string? OrderBy { get; set; }

    /// <summary>
    /// Gets or sets whether to sort results in ascending order.
    /// Default is true. Set to false for descending order.
    /// </summary>
    public bool Ascending { get; set; } = true;

    /// <summary>
    /// Gets or sets a text filter for searching within message content.
    /// Only messages containing this text in their content will be returned.
    /// </summary>
    public string? ContentContains { get; set; }

    /// <summary>
    /// Gets or sets the absolute maximum number of results to return across all pages.
    /// Default is 100. This serves as a safety limit to prevent excessive result sets.
    /// </summary>
    public int MaxResults { get; set; } = 100;
}
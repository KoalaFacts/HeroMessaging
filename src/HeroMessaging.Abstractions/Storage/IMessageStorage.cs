using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Storage;

/// <summary>
/// Provides basic message storage capabilities with typed retrieval and querying.
/// </summary>
/// <remarks>
/// <para>
/// This interface provides the core CRUD operations for message storage with string-based
/// message IDs and optional configuration options. For transaction-aware operations with
/// Guid-based IDs, use <see cref="ITransactionalMessageStorage"/>.
/// </para>
/// <para>
/// The full <see cref="IMessageStorage"/> interface inherits from both interfaces,
/// providing backward compatibility while allowing focused dependency injection.
/// </para>
/// </remarks>
public interface IBasicMessageStorage
{
    /// <summary>
    /// Stores a message with optional configuration.
    /// </summary>
    /// <param name="message">The message to store.</param>
    /// <param name="options">Optional storage configuration (collection, TTL, metadata).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The unique identifier assigned to the stored message.</returns>
    Task<string> StoreAsync(IMessage message, MessageStorageOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a message by its identifier with type-safe casting.
    /// </summary>
    /// <typeparam name="T">The expected type of the message.</typeparam>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The message if found and of the correct type, otherwise null.</returns>
    Task<T?> RetrieveAsync<T>(string messageId, CancellationToken cancellationToken = default) where T : IMessage;

    /// <summary>
    /// Queries messages matching the specified criteria with type-safe casting.
    /// </summary>
    /// <typeparam name="T">The expected type of the messages.</typeparam>
    /// <param name="query">The query criteria.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An enumerable of messages matching the query.</returns>
    Task<IEnumerable<T>> QueryAsync<T>(MessageQuery query, CancellationToken cancellationToken = default) where T : IMessage;

    /// <summary>
    /// Deletes a message by its identifier.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True if the message was deleted, false if not found.</returns>
    Task<bool> DeleteAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing message.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message to update.</param>
    /// <param name="message">The updated message content.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True if the message was updated, false if not found.</returns>
    Task<bool> UpdateAsync(string messageId, IMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a message exists by its identifier.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True if the message exists, false otherwise.</returns>
    Task<bool> ExistsAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts messages matching the optional query criteria.
    /// </summary>
    /// <param name="query">Optional query criteria. If null, counts all messages.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The count of messages matching the criteria.</returns>
    Task<long> CountAsync(MessageQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all messages from storage.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides message storage capabilities combining basic and transactional operations.
/// </summary>
/// <remarks>
/// <para>
/// This is the full-featured message storage interface that combines:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="IBasicMessageStorage"/> - Basic CRUD with string IDs and typed retrieval</description></item>
/// <item><description><see cref="ITransactionalMessageStorage"/> - Transaction-aware operations with Guid IDs</description></item>
/// </list>
/// <para>
/// For more focused dependency injection, depend on one of the segregated interfaces instead.
/// </para>
/// </remarks>
public interface IMessageStorage : IBasicMessageStorage, ITransactionalMessageStorage
{
    // All members are inherited from the segregated interfaces.
    // This interface serves as a unified facade for implementations that support all capabilities.
}

/// <summary>
/// Interface for storage transactions
/// </summary>
public interface IStorageTransaction : IDisposable
{
    /// <summary>
    /// Commits the transaction
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the transaction
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

public class MessageStorageOptions
{
    public string? Collection { get; set; }
    public TimeSpan? Ttl { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

public class MessageQuery
{
    public string? Collection { get; set; }
    public DateTimeOffset? FromTimestamp { get; set; }
    public DateTimeOffset? ToTimestamp { get; set; }
    public Dictionary<string, object>? MetadataFilters { get; set; }
    public int? Limit { get; set; }
    public int? Offset { get; set; }
    public string? OrderBy { get; set; }
    public bool Ascending { get; set; } = true;
    public string? ContentContains { get; set; }
    public int MaxResults { get; set; } = 100;
}
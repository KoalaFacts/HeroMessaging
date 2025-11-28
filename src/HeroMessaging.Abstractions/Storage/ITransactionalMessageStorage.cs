using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Storage;

/// <summary>
/// Provides transactional message storage capabilities.
/// </summary>
/// <remarks>
/// Use this interface when you need to perform storage operations within a transaction.
/// For basic CRUD operations without transactions, use <see cref="IMessageStorage"/>.
/// </remarks>
public interface ITransactionalMessageStorage
{
    /// <summary>
    /// Stores a message within an optional transaction.
    /// </summary>
    /// <param name="message">The message to store.</param>
    /// <param name="transaction">Optional transaction to participate in.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StoreAsync(IMessage message, IStorageTransaction? transaction = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a message by its identifier within an optional transaction.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="transaction">Optional transaction to participate in.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The message if found, otherwise null.</returns>
    Task<IMessage?> RetrieveAsync(Guid messageId, IStorageTransaction? transaction = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries messages matching the specified criteria.
    /// </summary>
    /// <param name="query">The query criteria.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of messages matching the query.</returns>
    Task<List<IMessage>> QueryAsync(MessageQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a message by its identifier.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DeleteAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins a new storage transaction.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A new transaction that can be used for atomic operations.</returns>
    Task<IStorageTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

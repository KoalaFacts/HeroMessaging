using System.Data;

namespace HeroMessaging.Abstractions.Storage;

/// <summary>
/// Provides transactional consistency across multiple storage operations
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// Gets the current transaction isolation level
    /// </summary>
    IsolationLevel IsolationLevel { get; }
    
    /// <summary>
    /// Indicates whether a transaction is currently active
    /// </summary>
    bool IsTransactionActive { get; }
    
    /// <summary>
    /// Begins a new database transaction with the specified isolation level
    /// </summary>
    /// <param name="isolationLevel">The isolation level for the transaction</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Commits the current transaction
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    Task CommitAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Rolls back the current transaction
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    Task RollbackAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Creates a savepoint within the current transaction
    /// </summary>
    /// <param name="savepointName">Name of the savepoint</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    Task SavepointAsync(string savepointName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Rolls back to a specific savepoint
    /// </summary>
    /// <param name="savepointName">Name of the savepoint</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    Task RollbackToSavepointAsync(string savepointName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the outbox storage within this unit of work
    /// </summary>
    IOutboxStorage OutboxStorage { get; }
    
    /// <summary>
    /// Gets the inbox storage within this unit of work
    /// </summary>
    IInboxStorage InboxStorage { get; }
    
    /// <summary>
    /// Gets the queue storage within this unit of work
    /// </summary>
    IQueueStorage QueueStorage { get; }
    
    /// <summary>
    /// Gets the message storage within this unit of work
    /// </summary>
    IMessageStorage MessageStorage { get; }
}

/// <summary>
/// Factory for creating unit of work instances
/// </summary>
public interface IUnitOfWorkFactory
{
    /// <summary>
    /// Creates a new unit of work instance
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A new unit of work instance</returns>
    Task<IUnitOfWork> CreateAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Creates a new unit of work instance with a specific isolation level
    /// </summary>
    /// <param name="isolationLevel">The isolation level for the transaction</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A new unit of work instance</returns>
    Task<IUnitOfWork> CreateAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default);
}

/// <summary>
/// Extension methods for IUnitOfWork to provide common transaction patterns
/// </summary>
public static class UnitOfWorkExtensions
{
    /// <summary>
    /// Executes a function within a transaction, automatically handling commit/rollback
    /// </summary>
    /// <typeparam name="T">Return type</typeparam>
    /// <param name="unitOfWork">The unit of work instance</param>
    /// <param name="operation">The operation to execute</param>
    /// <param name="isolationLevel">Transaction isolation level</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result of the operation</returns>
    public static async Task<T> ExecuteInTransactionAsync<T>(
        this IUnitOfWork unitOfWork,
        Func<IUnitOfWork, CancellationToken, Task<T>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        await unitOfWork.BeginTransactionAsync(isolationLevel, cancellationToken);
        
        try
        {
            var result = await operation(unitOfWork, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            if (unitOfWork.IsTransactionActive)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
            }
            throw;
        }
    }
    
    /// <summary>
    /// Executes an action within a transaction, automatically handling commit/rollback
    /// </summary>
    /// <param name="unitOfWork">The unit of work instance</param>
    /// <param name="operation">The operation to execute</param>
    /// <param name="isolationLevel">Transaction isolation level</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the operation</returns>
    public static async Task ExecuteInTransactionAsync(
        this IUnitOfWork unitOfWork,
        Func<IUnitOfWork, CancellationToken, Task> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        await unitOfWork.BeginTransactionAsync(isolationLevel, cancellationToken);
        
        try
        {
            await operation(unitOfWork, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            if (unitOfWork.IsTransactionActive)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
            }
            throw;
        }
    }
}
using System.Data;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Abstractions.Storage;

/// <summary>
/// Executes operations within a database transaction with automatic commit/rollback
/// </summary>
public interface ITransactionExecutor
{
    /// <summary>
    /// Executes an async operation within a transaction
    /// </summary>
    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        string operationName,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an async operation with a return value within a transaction
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        string operationName,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation of transaction executor
/// </summary>
public class TransactionExecutor : ITransactionExecutor
{
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly ILogger<TransactionExecutor> _logger;

    public TransactionExecutor(IUnitOfWorkFactory unitOfWorkFactory, ILogger<TransactionExecutor> logger)
    {
        _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        string operationName,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = await _unitOfWorkFactory.CreateAsync(isolationLevel, cancellationToken);

        try
        {
            _logger.LogDebug("Starting transaction for {OperationName}", operationName);

            await operation(cancellationToken);

            await unitOfWork.CommitAsync(cancellationToken);

            _logger.LogDebug("Transaction committed successfully for {OperationName}", operationName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Transaction rollback for {OperationName}", operationName);
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        string operationName,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = await _unitOfWorkFactory.CreateAsync(isolationLevel, cancellationToken);

        try
        {
            _logger.LogDebug("Starting transaction for {OperationName}", operationName);

            var result = await operation(cancellationToken);

            await unitOfWork.CommitAsync(cancellationToken);

            _logger.LogDebug("Transaction committed successfully for {OperationName}", operationName);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Transaction rollback for {OperationName}", operationName);
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}

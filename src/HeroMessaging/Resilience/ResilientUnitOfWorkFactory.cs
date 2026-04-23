using System.Data;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Resilience;

/// <summary>
/// Resilient decorator for IUnitOfWorkFactory that adds connection resilience patterns
/// </summary>
public class ResilientUnitOfWorkFactory(
    IUnitOfWorkFactory inner,
    IConnectionResiliencePolicy resiliencePolicy,
    ILogger<ResilientUnitOfWorkFactory> logger) : IUnitOfWorkFactory
{
    private readonly IUnitOfWorkFactory _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IConnectionResiliencePolicy _resiliencePolicy = resiliencePolicy ?? throw new ArgumentNullException(nameof(resiliencePolicy));
    private readonly ILogger<ResilientUnitOfWorkFactory> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    /// <summary>
    /// Executes create async.
    /// </summary>

    public async Task<IUnitOfWork> CreateAsync(CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
        {
            _logger.LogDebug("Creating unit of work");

            var unitOfWork = await _inner.CreateAsync(cancellationToken);

            // Wrap the unit of work with resilience if needed
            return new ResilientUnitOfWork(unitOfWork, _resiliencePolicy, _logger);
        }, "CreateUnitOfWork", cancellationToken);
    }
    /// <summary>
    /// Executes create async.
    /// </summary>

    public async Task<IUnitOfWork> CreateAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
        {
            _logger.LogDebug("Creating unit of work with isolation level {IsolationLevel}", isolationLevel);

            var unitOfWork = await _inner.CreateAsync(isolationLevel, cancellationToken);

            // Wrap the unit of work with resilience if needed
            return new ResilientUnitOfWork(unitOfWork, _resiliencePolicy, _logger);
        }, "CreateUnitOfWork", cancellationToken);
    }
}

/// <summary>
/// Resilient decorator for IUnitOfWork that adds connection resilience patterns
/// </summary>
public class ResilientUnitOfWork(
    IUnitOfWork inner,
    IConnectionResiliencePolicy resiliencePolicy,
    ILogger logger) : IUnitOfWork
{
    private readonly IUnitOfWork _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    /// <summary>
    /// Represents resilience policy.
    /// </summary>
    private readonly IConnectionResiliencePolicy _resiliencePolicy = resiliencePolicy ?? throw new ArgumentNullException(nameof(resiliencePolicy));
    /// <summary>
    /// Represents logger.
    /// </summary>
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    /// <summary>
    /// Gets isolation level.
    /// </summary>

    public IsolationLevel IsolationLevel => _inner.IsolationLevel;
    /// <summary>
    /// Gets is transaction active.
    /// </summary>
    public bool IsTransactionActive => _inner.IsTransactionActive;
    /// <summary>
    /// Gets outbox storage.
    /// </summary>
    public IOutboxStorage OutboxStorage => _inner.OutboxStorage;
    /// <summary>
    /// Gets inbox storage.
    /// </summary>
    public IInboxStorage InboxStorage => _inner.InboxStorage;
    /// <summary>
    /// Gets queue storage.
    /// </summary>
    public IQueueStorage QueueStorage => _inner.QueueStorage;
    /// <summary>
    /// Gets message storage.
    /// </summary>
    public IMessageStorage MessageStorage => _inner.MessageStorage;
    /// <summary>
    /// Executes begin transaction async.
    /// </summary>

    public async Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            _logger.LogDebug("Beginning transaction with isolation level {IsolationLevel}", isolationLevel);
            await _inner.BeginTransactionAsync(isolationLevel, cancellationToken);
        }, "BeginTransaction", cancellationToken);
    }
    /// <summary>
    /// Executes commit async.
    /// </summary>

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            _logger.LogDebug("Committing transaction with resilience");
            await _inner.CommitAsync(cancellationToken);
        }, "Commit", cancellationToken);
    }
    /// <summary>
    /// Executes rollback async.
    /// </summary>

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            _logger.LogDebug("Rolling back transaction with resilience");
            await _inner.RollbackAsync(cancellationToken);
        }, "Rollback", cancellationToken);
    }
    /// <summary>
    /// Executes savepoint async.
    /// </summary>

    public async Task SavepointAsync(string savepointName, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            _logger.LogDebug("Creating savepoint {SavepointName}", savepointName);
            await _inner.SavepointAsync(savepointName, cancellationToken);
        }, $"Savepoint-{savepointName}", cancellationToken);
    }
    /// <summary>
    /// Executes rollback to savepoint async.
    /// </summary>

    public async Task RollbackToSavepointAsync(string savepointName, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            _logger.LogDebug("Rolling back to savepoint {SavepointName}", savepointName);
            await _inner.RollbackToSavepointAsync(savepointName, cancellationToken);
        }, $"RollbackToSavepoint-{savepointName}", cancellationToken);
    }
    /// <summary>
    /// Executes dispose async.
    /// </summary>

    public async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();
    }
}

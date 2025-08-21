using System.Data;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Resilience;

/// <summary>
/// Resilient decorator for IUnitOfWorkFactory that adds connection resilience patterns
/// </summary>
public class ResilientUnitOfWorkFactory : IUnitOfWorkFactory
{
    private readonly IUnitOfWorkFactory _inner;
    private readonly IConnectionResiliencePolicy _resiliencePolicy;
    private readonly ILogger<ResilientUnitOfWorkFactory> _logger;

    public ResilientUnitOfWorkFactory(
        IUnitOfWorkFactory inner,
        IConnectionResiliencePolicy resiliencePolicy,
        ILogger<ResilientUnitOfWorkFactory> logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _resiliencePolicy = resiliencePolicy ?? throw new ArgumentNullException(nameof(resiliencePolicy));
        _logger = logger;
    }

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
public class ResilientUnitOfWork : IUnitOfWork
{
    private readonly IUnitOfWork _inner;
    private readonly IConnectionResiliencePolicy _resiliencePolicy;
    private readonly ILogger _logger;

    public ResilientUnitOfWork(
        IUnitOfWork inner,
        IConnectionResiliencePolicy resiliencePolicy,
        ILogger logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _resiliencePolicy = resiliencePolicy ?? throw new ArgumentNullException(nameof(resiliencePolicy));
        _logger = logger;
    }

    public IsolationLevel IsolationLevel => _inner.IsolationLevel;
    public bool IsTransactionActive => _inner.IsTransactionActive;
    public IOutboxStorage OutboxStorage => _inner.OutboxStorage;
    public IInboxStorage InboxStorage => _inner.InboxStorage;
    public IQueueStorage QueueStorage => _inner.QueueStorage;
    public IMessageStorage MessageStorage => _inner.MessageStorage;

    public async Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            _logger.LogDebug("Beginning transaction with isolation level {IsolationLevel}", isolationLevel);
            await _inner.BeginTransactionAsync(isolationLevel, cancellationToken);
        }, "BeginTransaction", cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            _logger.LogDebug("Committing transaction with resilience");
            await _inner.CommitAsync(cancellationToken);
        }, "Commit", cancellationToken);
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            _logger.LogDebug("Rolling back transaction with resilience");
            await _inner.RollbackAsync(cancellationToken);
        }, "Rollback", cancellationToken);
    }

    public async Task SavepointAsync(string savepointName, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            _logger.LogDebug("Creating savepoint {SavepointName}", savepointName);
            await _inner.SavepointAsync(savepointName, cancellationToken);
        }, $"Savepoint-{savepointName}", cancellationToken);
    }

    public async Task RollbackToSavepointAsync(string savepointName, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            _logger.LogDebug("Rolling back to savepoint {SavepointName}", savepointName);
            await _inner.RollbackToSavepointAsync(savepointName, cancellationToken);
        }, $"RollbackToSavepoint-{savepointName}", cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();
    }
}
using HeroMessaging.Abstractions.Storage;
using Npgsql;
using System.Data;

namespace HeroMessaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of the Unit of Work pattern
/// </summary>
public class PostgreSqlUnitOfWork : IUnitOfWork
{
    private readonly NpgsqlConnection _connection;
    private NpgsqlTransaction? _transaction;
    private readonly List<string> _savepoints = new();
    private bool _disposed;

    private readonly Lazy<IOutboxStorage> _outboxStorage;
    private readonly Lazy<IInboxStorage> _inboxStorage;
    private readonly Lazy<IQueueStorage> _queueStorage;
    private readonly Lazy<IMessageStorage> _messageStorage;

    public PostgreSqlUnitOfWork(string connectionString)
    {
        _connection = new NpgsqlConnection(connectionString);
        
        // Initialize storage implementations lazily with the shared connection/transaction
        _outboxStorage = new Lazy<IOutboxStorage>(() => new PostgreSqlOutboxStorage(_connection, _transaction));
        _inboxStorage = new Lazy<IInboxStorage>(() => new PostgreSqlInboxStorage(_connection, _transaction));
        _queueStorage = new Lazy<IQueueStorage>(() => new PostgreSqlQueueStorage(_connection, _transaction));
        _messageStorage = new Lazy<IMessageStorage>(() => new PostgreSqlMessageStorage(_connection, _transaction));
    }

    public IsolationLevel IsolationLevel => _transaction?.IsolationLevel ?? IsolationLevel.Unspecified;
    public bool IsTransactionActive => _transaction != null && _connection.State == ConnectionState.Open;

    public IOutboxStorage OutboxStorage => _outboxStorage.Value;
    public IInboxStorage InboxStorage => _inboxStorage.Value;
    public IQueueStorage QueueStorage => _queueStorage.Value;
    public IMessageStorage MessageStorage => _messageStorage.Value;

    public async Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            throw new InvalidOperationException("Transaction is already active");
        }

        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(cancellationToken);
        }

        _transaction = await _connection.BeginTransactionAsync(isolationLevel, cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
        {
            throw new InvalidOperationException("No active transaction to commit");
        }

        try
        {
            await _transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
            _savepoints.Clear();
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
        {
            throw new InvalidOperationException("No active transaction to rollback");
        }

        try
        {
            await _transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
            _savepoints.Clear();
        }
    }

    public async Task SavepointAsync(string savepointName, CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
        {
            throw new InvalidOperationException("No active transaction for savepoint");
        }

        if (string.IsNullOrWhiteSpace(savepointName))
        {
            throw new ArgumentException("Savepoint name cannot be null or empty", nameof(savepointName));
        }

        if (_savepoints.Contains(savepointName))
        {
            throw new InvalidOperationException($"Savepoint '{savepointName}' already exists");
        }

        await _transaction.SaveAsync(savepointName, cancellationToken);
        _savepoints.Add(savepointName);
    }

    public async Task RollbackToSavepointAsync(string savepointName, CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
        {
            throw new InvalidOperationException("No active transaction for savepoint rollback");
        }

        if (!_savepoints.Contains(savepointName))
        {
            throw new InvalidOperationException($"Savepoint '{savepointName}' does not exist");
        }

        await _transaction.RollbackAsync(savepointName, cancellationToken);
        
        // Remove this savepoint and all later ones
        var index = _savepoints.IndexOf(savepointName);
        _savepoints.RemoveRange(index, _savepoints.Count - index);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        try
        {
            if (_transaction != null)
            {
                await _transaction.RollbackAsync();
                await _transaction.DisposeAsync();
            }
        }
        catch
        {
            // Ignore exceptions during dispose
        }
        finally
        {
            if (_connection?.State == ConnectionState.Open)
            {
                await _connection.CloseAsync();
            }
            if (_connection != null)
            {
                await _connection.DisposeAsync();
            }
            _disposed = true;
        }
    }
}

/// <summary>
/// Factory for creating PostgreSQL unit of work instances
/// </summary>
public class PostgreSqlUnitOfWorkFactory(string connectionString) : IUnitOfWorkFactory
{
    private readonly string _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    public async Task<IUnitOfWork> CreateAsync(CancellationToken cancellationToken = default)
    {
        var unitOfWork = new PostgreSqlUnitOfWork(_connectionString);
        await unitOfWork.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        return unitOfWork;
    }

    public async Task<IUnitOfWork> CreateAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
    {
        var unitOfWork = new PostgreSqlUnitOfWork(_connectionString);
        await unitOfWork.BeginTransactionAsync(isolationLevel, cancellationToken);
        return unitOfWork;
    }
}
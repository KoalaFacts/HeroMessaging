using System.Data;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Npgsql;

namespace HeroMessaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of the Unit of Work pattern
/// </summary>
public class PostgreSqlUnitOfWork : IUnitOfWork
{
    private readonly NpgsqlConnection _connection;
    private NpgsqlTransaction? _transaction;
    private readonly List<string> _savepoints = [];
    private readonly TimeProvider _timeProvider;
    private readonly IJsonSerializer _jsonSerializer;
    private bool _disposed;

    private readonly Lazy<IOutboxStorage> _outboxStorage;
    private readonly Lazy<IInboxStorage> _inboxStorage;
    private readonly Lazy<IQueueStorage> _queueStorage;
    private readonly Lazy<IMessageStorage> _messageStorage;
    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlUnitOfWork"/> class.
    /// </summary>

    public PostgreSqlUnitOfWork(string connectionString, TimeProvider? timeProvider = null, IJsonSerializer? jsonSerializer = null)
    {
        _connection = new NpgsqlConnection(connectionString);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _jsonSerializer = jsonSerializer ?? new DefaultJsonSerializer(new DefaultBufferPoolManager());

        // Initialize storage implementations lazily with the shared connection/transaction
        _outboxStorage = new Lazy<IOutboxStorage>(() => new PostgreSqlOutboxStorage(_connection, _transaction, _timeProvider, _jsonSerializer));
        _inboxStorage = new Lazy<IInboxStorage>(() => new PostgreSqlInboxStorage(_connection, _transaction, _timeProvider, _jsonSerializer));
        _queueStorage = new Lazy<IQueueStorage>(() => new PostgreSqlQueueStorage(_connection, _transaction, _timeProvider, _jsonSerializer));
        _messageStorage = new Lazy<IMessageStorage>(() => new PostgreSqlMessageStorage(_connection, _transaction, _timeProvider, _jsonSerializer));
    }
    /// <summary>
    /// Gets isolation level.
    /// </summary>

    public IsolationLevel IsolationLevel => _transaction?.IsolationLevel ?? IsolationLevel.Unspecified;
    /// <summary>
    /// Gets is transaction active.
    /// </summary>
    public bool IsTransactionActive => _transaction != null && _connection.State == ConnectionState.Open;
    /// <summary>
    /// Gets outbox storage.
    /// </summary>

    public IOutboxStorage OutboxStorage => _outboxStorage.Value;
    /// <summary>
    /// Gets inbox storage.
    /// </summary>
    public IInboxStorage InboxStorage => _inboxStorage.Value;
    /// <summary>
    /// Gets queue storage.
    /// </summary>
    public IQueueStorage QueueStorage => _queueStorage.Value;
    /// <summary>
    /// Gets message storage.
    /// </summary>
    public IMessageStorage MessageStorage => _messageStorage.Value;
    /// <summary>
    /// Executes begin transaction async.
    /// </summary>

    public async Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            throw new InvalidOperationException("Transaction is already active");
        }

        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        _transaction = await _connection.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
    }
    /// <summary>
    /// Executes commit async.
    /// </summary>

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
        {
            throw new InvalidOperationException("No active transaction to commit");
        }

        try
        {
            await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
            _savepoints.Clear();
        }
    }
    /// <summary>
    /// Executes rollback async.
    /// </summary>

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
        {
            throw new InvalidOperationException("No active transaction to rollback");
        }

        try
        {
            await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
            _savepoints.Clear();
        }
    }
    /// <summary>
    /// Executes savepoint async.
    /// </summary>

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

        await _transaction.SaveAsync(savepointName, cancellationToken).ConfigureAwait(false);
        _savepoints.Add(savepointName);
    }
    /// <summary>
    /// Executes rollback to savepoint async.
    /// </summary>

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

        await _transaction.RollbackAsync(savepointName, cancellationToken).ConfigureAwait(false);

        // Remove this savepoint and all later ones
        var index = _savepoints.IndexOf(savepointName);
        _savepoints.RemoveRange(index, _savepoints.Count - index);
    }
    /// <summary>
    /// Executes dispose async.
    /// </summary>

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        try
        {
            if (_transaction != null)
            {
                await _transaction.RollbackAsync().ConfigureAwait(false);
                await _transaction.DisposeAsync().ConfigureAwait(false);
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
                await _connection.CloseAsync().ConfigureAwait(false);
            }
            if (_connection != null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
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
    /// <summary>
    /// Executes create async.
    /// </summary>

    public async Task<IUnitOfWork> CreateAsync(CancellationToken cancellationToken = default)
    {
        var unitOfWork = new PostgreSqlUnitOfWork(_connectionString);
        await unitOfWork.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        return unitOfWork;
    }
    /// <summary>
    /// Executes create async.
    /// </summary>

    public async Task<IUnitOfWork> CreateAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
    {
        var unitOfWork = new PostgreSqlUnitOfWork(_connectionString);
        await unitOfWork.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
        return unitOfWork;
    }
}

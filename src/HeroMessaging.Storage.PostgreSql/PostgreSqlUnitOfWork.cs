using HeroMessaging.Abstractions.Storage;
using Npgsql;
using System.Data;

namespace HeroMessaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of the Unit of Work pattern
/// </summary>
/// <remarks>
/// Provides transactional coordination across multiple storage implementations using a shared
/// PostgreSQL connection and transaction. Supports nested transactions via savepoints.
/// All storage instances (Outbox, Inbox, Queue, Message) share the same connection and transaction
/// context, ensuring atomicity across multiple operations.
/// </remarks>
public class PostgreSqlUnitOfWork : IUnitOfWork
{
    private readonly NpgsqlConnection _connection;
    private NpgsqlTransaction? _transaction;
    private readonly List<string> _savepoints = new();
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    private readonly Lazy<IOutboxStorage> _outboxStorage;
    private readonly Lazy<IInboxStorage> _inboxStorage;
    private readonly Lazy<IQueueStorage> _queueStorage;
    private readonly Lazy<IMessageStorage> _messageStorage;

    /// <summary>
    /// Initializes a new instance of the PostgreSQL unit of work
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string for database connectivity</param>
    /// <param name="timeProvider">Optional time provider for testable time-based operations. Defaults to system time</param>
    /// <exception cref="ArgumentNullException">Thrown when connectionString is null</exception>
    public PostgreSqlUnitOfWork(string connectionString, TimeProvider? timeProvider = null)
    {
        _connection = new NpgsqlConnection(connectionString);
        _timeProvider = timeProvider ?? TimeProvider.System;

        // Initialize storage implementations lazily with the shared connection/transaction
        _outboxStorage = new Lazy<IOutboxStorage>(() => new PostgreSqlOutboxStorage(_connection, _transaction, _timeProvider));
        _inboxStorage = new Lazy<IInboxStorage>(() => new PostgreSqlInboxStorage(_connection, _transaction, _timeProvider));
        _queueStorage = new Lazy<IQueueStorage>(() => new PostgreSqlQueueStorage(_connection, _transaction, _timeProvider));
        _messageStorage = new Lazy<IMessageStorage>(() => new PostgreSqlMessageStorage(_connection, _transaction, _timeProvider));
    }

    /// <summary>
    /// Gets the isolation level of the current transaction
    /// </summary>
    /// <value>
    /// The isolation level if a transaction is active; otherwise <see cref="IsolationLevel.Unspecified"/>
    /// </value>
    public IsolationLevel IsolationLevel => _transaction?.IsolationLevel ?? IsolationLevel.Unspecified;

    /// <summary>
    /// Gets a value indicating whether a transaction is currently active
    /// </summary>
    /// <value>
    /// True if a transaction exists and the connection is open; otherwise false
    /// </value>
    public bool IsTransactionActive => _transaction != null && _connection.State == ConnectionState.Open;

    /// <summary>
    /// Gets the outbox storage instance participating in this unit of work
    /// </summary>
    /// <value>
    /// An <see cref="IOutboxStorage"/> instance using the shared connection and transaction
    /// </value>
    public IOutboxStorage OutboxStorage => _outboxStorage.Value;

    /// <summary>
    /// Gets the inbox storage instance participating in this unit of work
    /// </summary>
    /// <value>
    /// An <see cref="IInboxStorage"/> instance using the shared connection and transaction
    /// </value>
    public IInboxStorage InboxStorage => _inboxStorage.Value;

    /// <summary>
    /// Gets the queue storage instance participating in this unit of work
    /// </summary>
    /// <value>
    /// An <see cref="IQueueStorage"/> instance using the shared connection and transaction
    /// </value>
    public IQueueStorage QueueStorage => _queueStorage.Value;

    /// <summary>
    /// Gets the message storage instance participating in this unit of work
    /// </summary>
    /// <value>
    /// An <see cref="IMessageStorage"/> instance using the shared connection and transaction
    /// </value>
    public IMessageStorage MessageStorage => _messageStorage.Value;

    /// <summary>
    /// Begins a new database transaction with the specified isolation level
    /// </summary>
    /// <param name="isolationLevel">The transaction isolation level. Defaults to ReadCommitted</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="InvalidOperationException">Thrown when a transaction is already active</exception>
    /// <remarks>
    /// Opens the database connection if not already open before starting the transaction.
    /// All storage operations performed after this call will participate in the transaction.
    /// </remarks>
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

    /// <summary>
    /// Commits the active transaction, persisting all changes made within its scope
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="InvalidOperationException">Thrown when no transaction is active</exception>
    /// <remarks>
    /// After commit, the transaction is disposed and all savepoints are cleared.
    /// If commit fails, the transaction is automatically rolled back.
    /// </remarks>
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

    /// <summary>
    /// Rolls back the active transaction, discarding all changes made within its scope
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="InvalidOperationException">Thrown when no transaction is active</exception>
    /// <remarks>
    /// After rollback, the transaction is disposed and all savepoints are cleared.
    /// All changes made since BeginTransactionAsync are discarded.
    /// </remarks>
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

    /// <summary>
    /// Creates a named savepoint within the current transaction for partial rollback support
    /// </summary>
    /// <param name="savepointName">Unique name for the savepoint</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="InvalidOperationException">Thrown when no transaction is active or savepoint name already exists</exception>
    /// <exception cref="ArgumentException">Thrown when savepointName is null or whitespace</exception>
    /// <remarks>
    /// Savepoints allow rolling back to a specific point within a transaction without
    /// discarding all changes. Useful for implementing nested transaction semantics.
    /// </remarks>
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

    /// <summary>
    /// Rolls back the transaction to a previously created savepoint, discarding changes made after that point
    /// </summary>
    /// <param name="savepointName">Name of the savepoint to roll back to</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="InvalidOperationException">Thrown when no transaction is active or savepoint does not exist</exception>
    /// <remarks>
    /// All savepoints created after the specified savepoint are also removed.
    /// Changes made before the savepoint are preserved.
    /// </remarks>
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

    /// <summary>
    /// Asynchronously disposes the unit of work, rolling back any active transaction and closing the connection
    /// </summary>
    /// <returns>A task representing the asynchronous disposal operation</returns>
    /// <remarks>
    /// If a transaction is active, it will be automatically rolled back.
    /// The connection is closed and disposed. Safe to call multiple times.
    /// </remarks>
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
/// <remarks>
/// Creates unit of work instances with an active transaction, ready for immediate use.
/// Each created instance manages its own database connection and transaction lifecycle.
/// </remarks>
/// <param name="connectionString">PostgreSQL connection string for database connectivity</param>
public class PostgreSqlUnitOfWorkFactory(string connectionString) : IUnitOfWorkFactory
{
    private readonly string _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    /// <summary>
    /// Creates a new unit of work with ReadCommitted isolation level
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A unit of work instance with an active transaction at ReadCommitted isolation level</returns>
    /// <exception cref="InvalidOperationException">Thrown when unable to establish database connection or start transaction</exception>
    /// <remarks>
    /// The returned unit of work has an active transaction and is ready for immediate use.
    /// The caller is responsible for committing or rolling back the transaction and disposing the unit of work.
    /// </remarks>
    public async Task<IUnitOfWork> CreateAsync(CancellationToken cancellationToken = default)
    {
        var unitOfWork = new PostgreSqlUnitOfWork(_connectionString);
        await unitOfWork.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        return unitOfWork;
    }

    /// <summary>
    /// Creates a new unit of work with the specified isolation level
    /// </summary>
    /// <param name="isolationLevel">The transaction isolation level to use</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A unit of work instance with an active transaction at the specified isolation level</returns>
    /// <exception cref="InvalidOperationException">Thrown when unable to establish database connection or start transaction</exception>
    /// <remarks>
    /// The returned unit of work has an active transaction and is ready for immediate use.
    /// The caller is responsible for committing or rolling back the transaction and disposing the unit of work.
    /// Higher isolation levels (Serializable, RepeatableRead) may impact performance and concurrency.
    /// </remarks>
    public async Task<IUnitOfWork> CreateAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
    {
        var unitOfWork = new PostgreSqlUnitOfWork(_connectionString);
        await unitOfWork.BeginTransactionAsync(isolationLevel, cancellationToken);
        return unitOfWork;
    }
}
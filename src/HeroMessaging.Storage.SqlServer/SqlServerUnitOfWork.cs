using HeroMessaging.Abstractions.Storage;
using Microsoft.Data.SqlClient;
using System.Data;

namespace HeroMessaging.Storage.SqlServer;

/// <summary>
/// SQL Server implementation of the Unit of Work pattern
/// </summary>
/// <remarks>
/// Manages transactional boundaries for SQL Server storage operations, ensuring ACID compliance.
/// Provides lazy initialization of storage implementations that share the same connection and transaction.
/// Supports nested savepoints for fine-grained transaction control within SQL Server.
/// </remarks>
public class SqlServerUnitOfWork : IUnitOfWork
{
    private readonly SqlConnection _connection;
    private SqlTransaction? _transaction;
    private readonly List<string> _savepoints = new();
    private bool _disposed;
    private readonly TimeProvider _timeProvider;

    private readonly Lazy<IOutboxStorage> _outboxStorage;
    private readonly Lazy<IInboxStorage> _inboxStorage;
    private readonly Lazy<IQueueStorage> _queueStorage;
    private readonly Lazy<IMessageStorage> _messageStorage;

    /// <summary>
    /// Initializes a new instance of the SqlServerUnitOfWork class
    /// </summary>
    /// <param name="connectionString">The connection string for the SQL Server database</param>
    /// <param name="timeProvider">The time provider for testable time-based operations</param>
    /// <exception cref="ArgumentNullException">Thrown when timeProvider is null</exception>
    public SqlServerUnitOfWork(string connectionString, TimeProvider timeProvider)
    {
        _connection = new SqlConnection(connectionString);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        // Initialize storage implementations lazily with the shared connection/transaction
        _outboxStorage = new Lazy<IOutboxStorage>(() => new SqlServerOutboxStorage(_connection, _transaction, _timeProvider));
        _inboxStorage = new Lazy<IInboxStorage>(() => new SqlServerInboxStorage(_connection, _transaction, _timeProvider));
        _queueStorage = new Lazy<IQueueStorage>(() => new SqlServerQueueStorage(_connection, _transaction, _timeProvider));
        _messageStorage = new Lazy<IMessageStorage>(() => new SqlServerMessageStorage(_connection, _transaction, _timeProvider));
    }

    /// <summary>
    /// Gets the isolation level of the current transaction
    /// </summary>
    /// <value>The isolation level if a transaction is active; otherwise, IsolationLevel.Unspecified</value>
    public IsolationLevel IsolationLevel => _transaction?.IsolationLevel ?? IsolationLevel.Unspecified;

    /// <summary>
    /// Gets a value indicating whether a transaction is currently active
    /// </summary>
    /// <value>True if a transaction is active and the connection is open; otherwise, false</value>
    public bool IsTransactionActive => _transaction != null && _connection.State == ConnectionState.Open;

    /// <summary>
    /// Gets the outbox storage implementation that shares this unit of work's transaction
    /// </summary>
    public IOutboxStorage OutboxStorage => _outboxStorage.Value;

    /// <summary>
    /// Gets the inbox storage implementation that shares this unit of work's transaction
    /// </summary>
    public IInboxStorage InboxStorage => _inboxStorage.Value;

    /// <summary>
    /// Gets the queue storage implementation that shares this unit of work's transaction
    /// </summary>
    public IQueueStorage QueueStorage => _queueStorage.Value;

    /// <summary>
    /// Gets the message storage implementation that shares this unit of work's transaction
    /// </summary>
    public IMessageStorage MessageStorage => _messageStorage.Value;

    /// <summary>
    /// Begins a new SQL Server transaction with the specified isolation level
    /// </summary>
    /// <param name="isolationLevel">The isolation level for the transaction (defaults to ReadCommitted)</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <exception cref="InvalidOperationException">Thrown when a transaction is already active</exception>
    /// <returns>A task representing the asynchronous operation</returns>
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

        _transaction = (SqlTransaction)await _connection.BeginTransactionAsync(isolationLevel, cancellationToken);
    }

    /// <summary>
    /// Commits the current transaction, persisting all changes to the database
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <exception cref="InvalidOperationException">Thrown when no transaction is active</exception>
    /// <returns>A task representing the asynchronous operation</returns>
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
    /// Rolls back the current transaction, discarding all uncommitted changes
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <exception cref="InvalidOperationException">Thrown when no transaction is active</exception>
    /// <returns>A task representing the asynchronous operation</returns>
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
    /// Creates a named savepoint within the current transaction
    /// </summary>
    /// <param name="savepointName">The unique name for the savepoint</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <exception cref="InvalidOperationException">Thrown when no transaction is active or savepoint name already exists</exception>
    /// <exception cref="ArgumentException">Thrown when savepointName is null or empty</exception>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// Savepoints allow partial rollback of changes within a transaction in SQL Server.
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
    /// Rolls back to a previously created savepoint, discarding changes made after the savepoint
    /// </summary>
    /// <param name="savepointName">The name of the savepoint to rollback to</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <exception cref="InvalidOperationException">Thrown when no transaction is active or savepoint does not exist</exception>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// Rolling back to a savepoint invalidates all savepoints created after it.
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
    /// Asynchronously disposes the unit of work, rolling back any uncommitted transactions
    /// </summary>
    /// <returns>A task representing the asynchronous disposal operation</returns>
    /// <remarks>
    /// If a transaction is active, it will be rolled back automatically.
    /// The database connection will be closed and disposed.
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
            _connection?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Factory for creating SQL Server unit of work instances
/// </summary>
/// <remarks>
/// Creates unit of work instances with active transactions using the configured SQL Server connection string.
/// Each created unit of work uses an independent database connection.
/// </remarks>
public class SqlServerUnitOfWorkFactory(string connectionString, TimeProvider timeProvider) : IUnitOfWorkFactory
{
    private readonly string _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>
    /// Creates a new unit of work with a ReadCommitted isolation level transaction
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A new unit of work instance with an active transaction</returns>
    public async Task<IUnitOfWork> CreateAsync(CancellationToken cancellationToken = default)
    {
        var unitOfWork = new SqlServerUnitOfWork(_connectionString, _timeProvider);
        await unitOfWork.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        return unitOfWork;
    }

    /// <summary>
    /// Creates a new unit of work with the specified isolation level
    /// </summary>
    /// <param name="isolationLevel">The isolation level for the transaction</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A new unit of work instance with an active transaction at the specified isolation level</returns>
    public async Task<IUnitOfWork> CreateAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
    {
        var unitOfWork = new SqlServerUnitOfWork(_connectionString, _timeProvider);
        await unitOfWork.BeginTransactionAsync(isolationLevel, cancellationToken);
        return unitOfWork;
    }
}
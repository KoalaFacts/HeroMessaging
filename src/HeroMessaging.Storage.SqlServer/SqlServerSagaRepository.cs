using HeroMessaging.Abstractions.Sagas;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HeroMessaging.Storage.SqlServer;

/// <summary>
/// SQL Server implementation of saga repository using pure ADO.NET
/// Supports optimistic concurrency control via versioning
/// </summary>
/// <typeparam name="TSaga">Type of saga to persist</typeparam>
public class SqlServerSagaRepository<TSaga> : ISagaRepository<TSaga>, IDisposable
    where TSaga : class, ISaga
{
    private static readonly JsonSerializerOptions SharedJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly SqlServerStorageOptions _options;
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly TimeProvider _timeProvider;
    private readonly string _sagaTypeName;
    private Task? _initializationTask;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the SqlServerSagaRepository
    /// </summary>
    /// <param name="options">Configuration options for SQL Server storage</param>
    /// <param name="timeProvider">Time provider for testable time-based operations</param>
    /// <exception cref="ArgumentNullException">Thrown when options or timeProvider is null</exception>
    /// <exception cref="ArgumentException">Thrown when connection string is invalid or identifiers contain invalid characters</exception>
    public SqlServerSagaRepository(SqlServerStorageOptions options, TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        // Validate connection string
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new ArgumentException("Connection string cannot be empty", nameof(options));
        _connectionString = options.ConnectionString;

        // Validate schema and table names to prevent SQL injection
        ValidateSqlIdentifier(options.Schema, nameof(options.Schema));
        ValidateSqlIdentifier(options.SagasTableName, nameof(options.SagasTableName));

        _tableName = _options.GetFullTableName(_options.SagasTableName);
        _sagaTypeName = typeof(TSaga).FullName ?? typeof(TSaga).Name;
    }

    /// <summary>
    /// Validates that an identifier (schema or table name) contains only safe characters
    /// </summary>
    /// <param name="identifier">The identifier to validate</param>
    /// <param name="parameterName">The parameter name for error messages</param>
    /// <exception cref="ArgumentException">Thrown when identifier contains invalid characters</exception>
    private static void ValidateSqlIdentifier(string identifier, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Identifier cannot be empty", parameterName);

        // Only allow alphanumeric characters and underscores, must start with letter or underscore
        if (!Regex.IsMatch(identifier, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
        {
            throw new ArgumentException(
                $"Invalid identifier '{identifier}'. Only alphanumeric characters and underscores are allowed, and it must start with a letter or underscore.",
                parameterName);
        }
    }

    /// <summary>
    /// Ensures the database schema and tables are initialized
    /// </summary>
    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.AutoCreateTables)
            return;

        if (_initializationTask != null)
        {
            await _initializationTask;
            return;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initializationTask == null)
                _initializationTask = InitializeDatabase();
            await _initializationTask;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task InitializeDatabase()
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Create schema if it doesn't exist
        if (!string.IsNullOrEmpty(_options.Schema) && _options.Schema != "dbo")
        {
            var createSchemaSql = $"""
                IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{_options.Schema}')
                BEGIN
                    EXEC('CREATE SCHEMA [{_options.Schema}]')
                END
                """;

            using var schemaCommand = new SqlCommand(createSchemaSql, connection);
            await schemaCommand.ExecuteNonQueryAsync();
        }

        var createTableSql = $"""
            IF NOT EXISTS (SELECT * FROM sys.tables t
                          JOIN sys.schemas s ON t.schema_id = s.schema_id
                          WHERE s.name = '{_options.Schema}' AND t.name = '{_options.SagasTableName}')
            BEGIN
                CREATE TABLE {_tableName} (
                    CorrelationId UNIQUEIDENTIFIER PRIMARY KEY,
                    SagaType NVARCHAR(500) NOT NULL,
                    CurrentState NVARCHAR(100) NOT NULL,
                    CreatedAt DATETIME2 NOT NULL,
                    UpdatedAt DATETIME2 NOT NULL,
                    IsCompleted BIT NOT NULL DEFAULT 0,
                    Version INT NOT NULL DEFAULT 0,
                    SagaData NVARCHAR(MAX) NOT NULL,
                    INDEX IX_{_options.SagasTableName}_State (CurrentState),
                    INDEX IX_{_options.SagasTableName}_UpdatedAt (UpdatedAt),
                    INDEX IX_{_options.SagasTableName}_Type_State (SagaType, CurrentState),
                    INDEX IX_{_options.SagasTableName}_IsCompleted_UpdatedAt (IsCompleted, UpdatedAt)
                )
            END
            """;

        using var command = new SqlCommand(createTableSql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Finds a saga instance by its correlation ID
    /// </summary>
    /// <param name="correlationId">The unique identifier for the saga instance</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The saga instance if found; otherwise null</returns>
    /// <exception cref="SqlException">Thrown when a database error occurs</exception>
    public async Task<TSaga?> FindAsync(Guid correlationId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT SagaData
            FROM {_tableName}
            WHERE CorrelationId = @CorrelationId AND SagaType = @SagaType
            """;

        using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = _options.CommandTimeout;
        command.Parameters.Add("@CorrelationId", SqlDbType.UniqueIdentifier).Value = correlationId;
        command.Parameters.Add("@SagaType", SqlDbType.NVarChar, 500).Value = _sagaTypeName;

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var sagaData = reader.GetString(0);
            return JsonSerializer.Deserialize<TSaga>(sagaData, SharedJsonOptions);
        }

        return null;
    }

    /// <summary>
    /// Finds saga instances by their current state
    /// Returns up to 1000 results to prevent memory exhaustion
    /// </summary>
    /// <param name="state">The state to search for</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Collection of saga instances in the specified state (limited to 1000 results)</returns>
    /// <exception cref="ArgumentException">Thrown when state is null or empty</exception>
    /// <exception cref="SqlException">Thrown when a database error occurs</exception>
    public async Task<IEnumerable<TSaga>> FindByStateAsync(string state, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(state))
            throw new ArgumentException("State cannot be empty", nameof(state));

        await EnsureInitializedAsync(cancellationToken);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT TOP (1000) SagaData
            FROM {_tableName}
            WHERE CurrentState = @State AND SagaType = @SagaType
            ORDER BY UpdatedAt DESC
            """;

        using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = _options.CommandTimeout;
        command.Parameters.Add("@State", SqlDbType.NVarChar, 100).Value = state;
        command.Parameters.Add("@SagaType", SqlDbType.NVarChar, 500).Value = _sagaTypeName;

        var sagas = new List<TSaga>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var sagaData = reader.GetString(0);
            var saga = JsonSerializer.Deserialize<TSaga>(sagaData, SharedJsonOptions);
            if (saga != null)
            {
                sagas.Add(saga);
            }
        }

        return sagas;
    }

    /// <summary>
    /// Saves a new saga instance to the database
    /// Automatically sets CreatedAt and UpdatedAt timestamps
    /// </summary>
    /// <param name="saga">The saga instance to save</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <exception cref="ArgumentNullException">Thrown when saga is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when saga with the same correlation ID already exists</exception>
    /// <exception cref="SqlException">Thrown when a database error occurs</exception>
    public async Task SaveAsync(TSaga saga, CancellationToken cancellationToken = default)
    {
        if (saga == null)
            throw new ArgumentNullException(nameof(saga));

        await EnsureInitializedAsync(cancellationToken);

        // Set timestamps for new saga
        var now = _timeProvider.GetUtcNow().DateTime;
        saga.CreatedAt = now;
        saga.UpdatedAt = now;

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            INSERT INTO {_tableName}
            (CorrelationId, SagaType, CurrentState, CreatedAt, UpdatedAt, IsCompleted, Version, SagaData)
            VALUES (@CorrelationId, @SagaType, @CurrentState, @CreatedAt, @UpdatedAt, @IsCompleted, @Version, @SagaData)
            """;

        using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = _options.CommandTimeout;
        command.Parameters.Add("@CorrelationId", SqlDbType.UniqueIdentifier).Value = saga.CorrelationId;
        command.Parameters.Add("@SagaType", SqlDbType.NVarChar, 500).Value = _sagaTypeName;
        command.Parameters.Add("@CurrentState", SqlDbType.NVarChar, 100).Value = saga.CurrentState;
        command.Parameters.Add("@CreatedAt", SqlDbType.DateTime2).Value = saga.CreatedAt;
        command.Parameters.Add("@UpdatedAt", SqlDbType.DateTime2).Value = saga.UpdatedAt;
        command.Parameters.Add("@IsCompleted", SqlDbType.Bit).Value = saga.IsCompleted;
        command.Parameters.Add("@Version", SqlDbType.Int).Value = saga.Version;
        command.Parameters.Add("@SagaData", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(saga, SharedJsonOptions);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number == 2627) // Primary key violation
        {
            throw new InvalidOperationException(
                $"Saga with correlation ID {saga.CorrelationId} already exists. Use UpdateAsync to modify existing sagas.", ex);
        }
    }

    /// <summary>
    /// Updates an existing saga instance with optimistic concurrency control
    /// Automatically increments version and updates UpdatedAt timestamp
    /// </summary>
    /// <param name="saga">The saga instance to update</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <exception cref="ArgumentNullException">Thrown when saga is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when saga is not found</exception>
    /// <exception cref="SagaConcurrencyException">Thrown when version mismatch detected (concurrent modification)</exception>
    /// <exception cref="SqlException">Thrown when a database error occurs</exception>
    public async Task UpdateAsync(TSaga saga, CancellationToken cancellationToken = default)
    {
        if (saga == null)
            throw new ArgumentNullException(nameof(saga));

        await EnsureInitializedAsync(cancellationToken);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Start transaction for read-update consistency
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);

        try
        {
            // Read current version for optimistic concurrency check
            var selectSql = $"""
                SELECT Version
                FROM {_tableName}
                WHERE CorrelationId = @CorrelationId AND SagaType = @SagaType
                """;

            using var selectCommand = new SqlCommand(selectSql, connection, transaction);
            selectCommand.CommandTimeout = _options.CommandTimeout;
            selectCommand.Parameters.Add("@CorrelationId", SqlDbType.UniqueIdentifier).Value = saga.CorrelationId;
            selectCommand.Parameters.Add("@SagaType", SqlDbType.NVarChar, 500).Value = _sagaTypeName;

            var result = await selectCommand.ExecuteScalarAsync(cancellationToken);
            if (result == null)
            {
                throw new InvalidOperationException(
                    $"Saga with correlation ID {saga.CorrelationId} not found. Use SaveAsync to create new sagas.");
            }

            var currentVersion = Convert.ToInt32(result);

            // Optimistic concurrency check
            if (saga.Version != currentVersion)
            {
                throw new SagaConcurrencyException(
                    saga.CorrelationId,
                    expectedVersion: currentVersion,
                    actualVersion: saga.Version);
            }

            // Increment version and update timestamp
            saga.Version++;
            saga.UpdatedAt = _timeProvider.GetUtcNow().DateTime;

            // Update saga
            var updateSql = $"""
                UPDATE {_tableName}
                SET CurrentState = @CurrentState,
                    UpdatedAt = @UpdatedAt,
                    IsCompleted = @IsCompleted,
                    Version = @Version,
                    SagaData = @SagaData
                WHERE CorrelationId = @CorrelationId AND SagaType = @SagaType
                """;

            using var updateCommand = new SqlCommand(updateSql, connection, transaction);
            updateCommand.CommandTimeout = _options.CommandTimeout;
            updateCommand.Parameters.Add("@CorrelationId", SqlDbType.UniqueIdentifier).Value = saga.CorrelationId;
            updateCommand.Parameters.Add("@SagaType", SqlDbType.NVarChar, 500).Value = _sagaTypeName;
            updateCommand.Parameters.Add("@CurrentState", SqlDbType.NVarChar, 100).Value = saga.CurrentState;
            updateCommand.Parameters.Add("@UpdatedAt", SqlDbType.DateTime2).Value = saga.UpdatedAt;
            updateCommand.Parameters.Add("@IsCompleted", SqlDbType.Bit).Value = saga.IsCompleted;
            updateCommand.Parameters.Add("@Version", SqlDbType.Int).Value = saga.Version;
            updateCommand.Parameters.Add("@SagaData", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(saga, SharedJsonOptions);

            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException
                                    and not StackOverflowException
                                    and not ThreadAbortException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Deletes a saga instance from the database
    /// Typically used for cleanup of completed sagas
    /// </summary>
    /// <param name="correlationId">The correlation ID of the saga to delete</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <exception cref="SqlException">Thrown when a database error occurs</exception>
    public async Task DeleteAsync(Guid correlationId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            DELETE FROM {_tableName}
            WHERE CorrelationId = @CorrelationId AND SagaType = @SagaType
            """;

        using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = _options.CommandTimeout;
        command.Parameters.Add("@CorrelationId", SqlDbType.UniqueIdentifier).Value = correlationId;
        command.Parameters.Add("@SagaType", SqlDbType.NVarChar, 500).Value = _sagaTypeName;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Finds saga instances that have not been updated within the specified timespan
    /// Only returns incomplete sagas (IsCompleted = false)
    /// Useful for detecting stuck or timed-out sagas
    /// Returns up to 1000 results to prevent memory exhaustion
    /// </summary>
    /// <param name="olderThan">Find sagas last updated before this duration ago</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Collection of stale saga instances (limited to 1000 results), ordered by UpdatedAt ascending (oldest first)</returns>
    /// <exception cref="SqlException">Thrown when a database error occurs</exception>
    public async Task<IEnumerable<TSaga>> FindStaleAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var cutoffTime = _timeProvider.GetUtcNow().DateTime - olderThan;

        var sql = $"""
            SELECT TOP (1000) SagaData
            FROM {_tableName}
            WHERE SagaType = @SagaType
              AND IsCompleted = 0
              AND UpdatedAt < @CutoffTime
            ORDER BY UpdatedAt ASC
            """;

        using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = _options.CommandTimeout;
        command.Parameters.Add("@SagaType", SqlDbType.NVarChar, 500).Value = _sagaTypeName;
        command.Parameters.Add("@CutoffTime", SqlDbType.DateTime2).Value = cutoffTime;

        var sagas = new List<TSaga>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var sagaData = reader.GetString(0);
            var saga = JsonSerializer.Deserialize<TSaga>(sagaData, SharedJsonOptions);
            if (saga != null)
            {
                sagas.Add(saga);
            }
        }

        return sagas;
    }

    /// <summary>
    /// Releases resources used by the repository
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and optionally managed resources
    /// </summary>
    /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _initLock.Dispose();
        }

        _disposed = true;
    }
}

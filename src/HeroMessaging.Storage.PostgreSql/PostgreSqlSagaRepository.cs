using HeroMessaging.Abstractions.Sagas;
using Npgsql;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HeroMessaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of saga repository using pure ADO.NET
/// Supports optimistic concurrency control via versioning
/// </summary>
/// <typeparam name="TSaga">Type of saga to persist</typeparam>
public class PostgreSqlSagaRepository<TSaga> : ISagaRepository<TSaga>
    where TSaga : class, ISaga
{
    private static readonly JsonSerializerOptions SharedJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly PostgreSqlStorageOptions _options;
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly TimeProvider _timeProvider;
    private readonly string _sagaTypeName;
    private Task? _initializationTask;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the PostgreSqlSagaRepository
    /// </summary>
    /// <param name="options">Configuration options for PostgreSQL storage</param>
    /// <param name="timeProvider">Time provider for testable time-based operations</param>
    /// <exception cref="ArgumentNullException">Thrown when options or timeProvider is null</exception>
    /// <exception cref="ArgumentException">Thrown when connection string is invalid or identifiers contain invalid characters</exception>
    public PostgreSqlSagaRepository(PostgreSqlStorageOptions options, TimeProvider timeProvider)
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
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        // Create schema if it doesn't exist
        if (!string.IsNullOrEmpty(_options.Schema) && _options.Schema != "public")
        {
            var createSchemaSql = $"CREATE SCHEMA IF NOT EXISTS {_options.Schema}";
            using var schemaCommand = new NpgsqlCommand(createSchemaSql, connection);
            await schemaCommand.ExecuteNonQueryAsync();
        }

        var createTableSql = $"""
            CREATE TABLE IF NOT EXISTS {_tableName} (
                correlation_id UUID PRIMARY KEY,
                saga_type VARCHAR(500) NOT NULL,
                current_state VARCHAR(100) NOT NULL,
                created_at TIMESTAMP NOT NULL,
                updated_at TIMESTAMP NOT NULL,
                is_completed BOOLEAN NOT NULL DEFAULT FALSE,
                version INTEGER NOT NULL DEFAULT 0,
                saga_data JSONB NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_{_options.SagasTableName}_state
                ON {_tableName}(current_state);
            CREATE INDEX IF NOT EXISTS idx_{_options.SagasTableName}_updated_at
                ON {_tableName}(updated_at);
            CREATE INDEX IF NOT EXISTS idx_{_options.SagasTableName}_type_state
                ON {_tableName}(saga_type, current_state);
            CREATE INDEX IF NOT EXISTS idx_{_options.SagasTableName}_completed_updated
                ON {_tableName}(is_completed, updated_at);
            """;

        using var command = new NpgsqlCommand(createTableSql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Finds a saga instance by its correlation ID
    /// </summary>
    /// <param name="correlationId">The unique identifier for the saga instance</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The saga instance if found; otherwise null</returns>
    /// <exception cref="PostgresException">Thrown when a database error occurs</exception>
    public async Task<TSaga?> FindAsync(Guid correlationId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT saga_data
            FROM {_tableName}
            WHERE correlation_id = @CorrelationId AND saga_type = @SagaType
            """;

        using var command = new NpgsqlCommand(sql, connection);
        command.CommandTimeout = _options.CommandTimeout;
        command.Parameters.AddWithValue("@CorrelationId", correlationId);
        command.Parameters.AddWithValue("@SagaType", _sagaTypeName);

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
    /// </summary>
    /// <param name="state">The state to search for</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Collection of saga instances in the specified state (limited to 1000 results)</returns>
    /// <exception cref="ArgumentException">Thrown when state is null or empty</exception>
    /// <exception cref="PostgresException">Thrown when a database error occurs</exception>
    public async Task<IEnumerable<TSaga>> FindByStateAsync(string state, CancellationToken cancellationToken = default)
    {
        return await FindByStateAsync(state, 1000, cancellationToken);
    }

    /// <summary>
    /// Finds saga instances by their current state with a result limit
    /// </summary>
    /// <param name="state">The state to search for</param>
    /// <param name="maxResults">Maximum number of results to return</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Collection of saga instances in the specified state</returns>
    /// <exception cref="ArgumentException">Thrown when state is null or empty, or maxResults is less than 1</exception>
    /// <exception cref="PostgresException">Thrown when a database error occurs</exception>
    private async Task<IEnumerable<TSaga>> FindByStateAsync(string state, int maxResults, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(state))
            throw new ArgumentException("State cannot be empty", nameof(state));
        if (maxResults < 1)
            throw new ArgumentException("MaxResults must be at least 1", nameof(maxResults));

        await EnsureInitializedAsync(cancellationToken);

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT saga_data
            FROM {_tableName}
            WHERE current_state = @State AND saga_type = @SagaType
            ORDER BY updated_at DESC
            LIMIT @MaxResults
            """;

        using var command = new NpgsqlCommand(sql, connection);
        command.CommandTimeout = _options.CommandTimeout;
        command.Parameters.AddWithValue("@State", state);
        command.Parameters.AddWithValue("@SagaType", _sagaTypeName);
        command.Parameters.AddWithValue("@MaxResults", maxResults);

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
    /// <exception cref="PostgresException">Thrown when a database error occurs</exception>
    public async Task SaveAsync(TSaga saga, CancellationToken cancellationToken = default)
    {
        if (saga == null)
            throw new ArgumentNullException(nameof(saga));

        await EnsureInitializedAsync(cancellationToken);

        // Set timestamps for new saga
        var now = _timeProvider.GetUtcNow().DateTime;
        saga.CreatedAt = now;
        saga.UpdatedAt = now;

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            INSERT INTO {_tableName}
            (correlation_id, saga_type, current_state, created_at, updated_at, is_completed, version, saga_data)
            VALUES (@CorrelationId, @SagaType, @CurrentState, @CreatedAt, @UpdatedAt, @IsCompleted, @Version, @SagaData::jsonb)
            """;

        using var command = new NpgsqlCommand(sql, connection);
        command.CommandTimeout = _options.CommandTimeout;
        command.Parameters.AddWithValue("@CorrelationId", saga.CorrelationId);
        command.Parameters.AddWithValue("@SagaType", _sagaTypeName);
        command.Parameters.AddWithValue("@CurrentState", saga.CurrentState);
        command.Parameters.AddWithValue("@CreatedAt", saga.CreatedAt);
        command.Parameters.AddWithValue("@UpdatedAt", saga.UpdatedAt);
        command.Parameters.AddWithValue("@IsCompleted", saga.IsCompleted);
        command.Parameters.AddWithValue("@Version", saga.Version);
        command.Parameters.AddWithValue("@SagaData", JsonSerializer.Serialize(saga, SharedJsonOptions));

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
        {
            throw new InvalidOperationException(
                $"Saga with correlation ID {saga.CorrelationId} already exists. Use UpdateAsync to modify existing sagas.", ex);
        }
    }

    /// <summary>
    /// Updates an existing saga instance with optimistic concurrency control
    /// Automatically increments version and updates UpdatedAt timestamp
    /// Uses FOR UPDATE NOWAIT to prevent blocking on locked rows
    /// </summary>
    /// <param name="saga">The saga instance to update</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <exception cref="ArgumentNullException">Thrown when saga is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when saga is not found</exception>
    /// <exception cref="SagaConcurrencyException">Thrown when version mismatch detected (concurrent modification)</exception>
    /// <exception cref="PostgresException">Thrown when a database error occurs or row is locked</exception>
    public async Task UpdateAsync(TSaga saga, CancellationToken cancellationToken = default)
    {
        if (saga == null)
            throw new ArgumentNullException(nameof(saga));

        await EnsureInitializedAsync(cancellationToken);

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Start transaction for read-update consistency
        using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            // Set lock timeout to prevent indefinite blocking
            using var timeoutCommand = new NpgsqlCommand("SET LOCAL lock_timeout = '5s'", connection, transaction);
            await timeoutCommand.ExecuteNonQueryAsync(cancellationToken);

            // Read current version for optimistic concurrency check with row lock
            var selectSql = $"""
                SELECT version
                FROM {_tableName}
                WHERE correlation_id = @CorrelationId AND saga_type = @SagaType
                FOR UPDATE NOWAIT
                """;

            using var selectCommand = new NpgsqlCommand(selectSql, connection, transaction);
            selectCommand.CommandTimeout = _options.CommandTimeout;
            selectCommand.Parameters.AddWithValue("@CorrelationId", saga.CorrelationId);
            selectCommand.Parameters.AddWithValue("@SagaType", _sagaTypeName);

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
                SET current_state = @CurrentState,
                    updated_at = @UpdatedAt,
                    is_completed = @IsCompleted,
                    version = @Version,
                    saga_data = @SagaData::jsonb
                WHERE correlation_id = @CorrelationId AND saga_type = @SagaType
                """;

            using var updateCommand = new NpgsqlCommand(updateSql, connection, transaction);
            updateCommand.CommandTimeout = _options.CommandTimeout;
            updateCommand.Parameters.AddWithValue("@CorrelationId", saga.CorrelationId);
            updateCommand.Parameters.AddWithValue("@SagaType", _sagaTypeName);
            updateCommand.Parameters.AddWithValue("@CurrentState", saga.CurrentState);
            updateCommand.Parameters.AddWithValue("@UpdatedAt", saga.UpdatedAt);
            updateCommand.Parameters.AddWithValue("@IsCompleted", saga.IsCompleted);
            updateCommand.Parameters.AddWithValue("@Version", saga.Version);
            updateCommand.Parameters.AddWithValue("@SagaData", JsonSerializer.Serialize(saga, SharedJsonOptions));

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
    /// <exception cref="PostgresException">Thrown when a database error occurs</exception>
    public async Task DeleteAsync(Guid correlationId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            DELETE FROM {_tableName}
            WHERE correlation_id = @CorrelationId AND saga_type = @SagaType
            """;

        using var command = new NpgsqlCommand(sql, connection);
        command.CommandTimeout = _options.CommandTimeout;
        command.Parameters.AddWithValue("@CorrelationId", correlationId);
        command.Parameters.AddWithValue("@SagaType", _sagaTypeName);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Finds saga instances that have not been updated within the specified timespan
    /// Only returns incomplete sagas (is_completed = false)
    /// Useful for detecting stuck or timed-out sagas
    /// </summary>
    /// <param name="olderThan">Find sagas last updated before this duration ago</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Collection of stale saga instances (limited to 1000 results), ordered by updated_at ascending (oldest first)</returns>
    /// <exception cref="PostgresException">Thrown when a database error occurs</exception>
    public async Task<IEnumerable<TSaga>> FindStaleAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        return await FindStaleAsync(olderThan, 1000, cancellationToken);
    }

    /// <summary>
    /// Finds stale saga instances with a result limit
    /// </summary>
    private async Task<IEnumerable<TSaga>> FindStaleAsync(TimeSpan olderThan, int maxResults, CancellationToken cancellationToken = default)
    {
        if (maxResults < 1)
            throw new ArgumentException("MaxResults must be at least 1", nameof(maxResults));

        await EnsureInitializedAsync(cancellationToken);

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var cutoffTime = _timeProvider.GetUtcNow().DateTime - olderThan;

        var sql = $"""
            SELECT saga_data
            FROM {_tableName}
            WHERE saga_type = @SagaType
              AND is_completed = FALSE
              AND updated_at < @CutoffTime
            ORDER BY updated_at ASC
            LIMIT @MaxResults
            """;

        using var command = new NpgsqlCommand(sql, connection);
        command.CommandTimeout = _options.CommandTimeout;
        command.Parameters.AddWithValue("@SagaType", _sagaTypeName);
        command.Parameters.AddWithValue("@CutoffTime", cutoffTime);
        command.Parameters.AddWithValue("@MaxResults", maxResults);

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
}

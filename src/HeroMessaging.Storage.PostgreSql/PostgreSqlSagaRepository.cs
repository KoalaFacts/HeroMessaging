using HeroMessaging.Abstractions.Sagas;
using Npgsql;
using System.Data;
using System.Text.Json;

namespace HeroMessaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of saga repository using pure ADO.NET
/// Supports optimistic concurrency control via versioning
/// </summary>
/// <typeparam name="TSaga">Type of saga to persist</typeparam>
public class PostgreSqlSagaRepository<TSaga> : ISagaRepository<TSaga>
    where TSaga : class, ISaga
{
    private readonly PostgreSqlStorageOptions _options;
    private readonly string _connectionString;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _tableName;
    private readonly TimeProvider _timeProvider;
    private readonly string _sagaTypeName;

    public PostgreSqlSagaRepository(PostgreSqlStorageOptions options, TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connectionString = options.ConnectionString;
        _tableName = _options.GetFullTableName(_options.SagasTableName);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _sagaTypeName = typeof(TSaga).FullName ?? typeof(TSaga).Name;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        if (_options.AutoCreateTables)
        {
            InitializeDatabase().GetAwaiter().GetResult();
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

    public async Task<TSaga?> FindAsync(Guid correlationId, CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT saga_data
            FROM {_tableName}
            WHERE correlation_id = @CorrelationId AND saga_type = @SagaType
            """;

        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@CorrelationId", correlationId);
        command.Parameters.AddWithValue("@SagaType", _sagaTypeName);

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var sagaData = reader.GetString(0);
            return JsonSerializer.Deserialize<TSaga>(sagaData, _jsonOptions);
        }

        return null;
    }

    public async Task<IEnumerable<TSaga>> FindByStateAsync(string state, CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT saga_data
            FROM {_tableName}
            WHERE current_state = @State AND saga_type = @SagaType
            ORDER BY updated_at DESC
            """;

        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@State", state);
        command.Parameters.AddWithValue("@SagaType", _sagaTypeName);

        var sagas = new List<TSaga>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var sagaData = reader.GetString(0);
            var saga = JsonSerializer.Deserialize<TSaga>(sagaData, _jsonOptions);
            if (saga != null)
            {
                sagas.Add(saga);
            }
        }

        return sagas;
    }

    public async Task SaveAsync(TSaga saga, CancellationToken cancellationToken = default)
    {
        if (saga == null)
            throw new ArgumentNullException(nameof(saga));

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
        command.Parameters.AddWithValue("@CorrelationId", saga.CorrelationId);
        command.Parameters.AddWithValue("@SagaType", _sagaTypeName);
        command.Parameters.AddWithValue("@CurrentState", saga.CurrentState);
        command.Parameters.AddWithValue("@CreatedAt", saga.CreatedAt);
        command.Parameters.AddWithValue("@UpdatedAt", saga.UpdatedAt);
        command.Parameters.AddWithValue("@IsCompleted", saga.IsCompleted);
        command.Parameters.AddWithValue("@Version", saga.Version);
        command.Parameters.AddWithValue("@SagaData", JsonSerializer.Serialize(saga, _jsonOptions));

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

    public async Task UpdateAsync(TSaga saga, CancellationToken cancellationToken = default)
    {
        if (saga == null)
            throw new ArgumentNullException(nameof(saga));

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Start transaction for read-update consistency
        using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            // Read current version for optimistic concurrency check
            var selectSql = $"""
                SELECT version
                FROM {_tableName}
                WHERE correlation_id = @CorrelationId AND saga_type = @SagaType
                FOR UPDATE
                """;

            using var selectCommand = new NpgsqlCommand(selectSql, connection, transaction);
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
            updateCommand.Parameters.AddWithValue("@CorrelationId", saga.CorrelationId);
            updateCommand.Parameters.AddWithValue("@SagaType", _sagaTypeName);
            updateCommand.Parameters.AddWithValue("@CurrentState", saga.CurrentState);
            updateCommand.Parameters.AddWithValue("@UpdatedAt", saga.UpdatedAt);
            updateCommand.Parameters.AddWithValue("@IsCompleted", saga.IsCompleted);
            updateCommand.Parameters.AddWithValue("@Version", saga.Version);
            updateCommand.Parameters.AddWithValue("@SagaData", JsonSerializer.Serialize(saga, _jsonOptions));

            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task DeleteAsync(Guid correlationId, CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            DELETE FROM {_tableName}
            WHERE correlation_id = @CorrelationId AND saga_type = @SagaType
            """;

        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@CorrelationId", correlationId);
        command.Parameters.AddWithValue("@SagaType", _sagaTypeName);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IEnumerable<TSaga>> FindStaleAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
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
            """;

        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@SagaType", _sagaTypeName);
        command.Parameters.AddWithValue("@CutoffTime", cutoffTime);

        var sagas = new List<TSaga>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var sagaData = reader.GetString(0);
            var saga = JsonSerializer.Deserialize<TSaga>(sagaData, _jsonOptions);
            if (saga != null)
            {
                sagas.Add(saga);
            }
        }

        return sagas;
    }
}

using HeroMessaging.Abstractions.Sagas;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace HeroMessaging.Storage.SqlServer;

/// <summary>
/// SQL Server implementation of saga repository using pure ADO.NET
/// Supports optimistic concurrency control via versioning
/// </summary>
/// <typeparam name="TSaga">Type of saga to persist</typeparam>
public class SqlServerSagaRepository<TSaga> : ISagaRepository<TSaga>
    where TSaga : class, ISaga
{
    private readonly SqlServerStorageOptions _options;
    private readonly string _connectionString;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _tableName;
    private readonly TimeProvider _timeProvider;
    private readonly string _sagaTypeName;

    public SqlServerSagaRepository(SqlServerStorageOptions options, TimeProvider timeProvider)
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

    public async Task<TSaga?> FindAsync(Guid correlationId, CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT SagaData
            FROM {_tableName}
            WHERE CorrelationId = @CorrelationId AND SagaType = @SagaType
            """;

        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@CorrelationId", SqlDbType.UniqueIdentifier).Value = correlationId;
        command.Parameters.Add("@SagaType", SqlDbType.NVarChar, 500).Value = _sagaTypeName;

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
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT SagaData
            FROM {_tableName}
            WHERE CurrentState = @State AND SagaType = @SagaType
            ORDER BY UpdatedAt DESC
            """;

        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@State", SqlDbType.NVarChar, 100).Value = state;
        command.Parameters.Add("@SagaType", SqlDbType.NVarChar, 500).Value = _sagaTypeName;

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

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            INSERT INTO {_tableName}
            (CorrelationId, SagaType, CurrentState, CreatedAt, UpdatedAt, IsCompleted, Version, SagaData)
            VALUES (@CorrelationId, @SagaType, @CurrentState, @CreatedAt, @UpdatedAt, @IsCompleted, @Version, @SagaData)
            """;

        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@CorrelationId", SqlDbType.UniqueIdentifier).Value = saga.CorrelationId;
        command.Parameters.Add("@SagaType", SqlDbType.NVarChar, 500).Value = _sagaTypeName;
        command.Parameters.Add("@CurrentState", SqlDbType.NVarChar, 100).Value = saga.CurrentState;
        command.Parameters.Add("@CreatedAt", SqlDbType.DateTime2).Value = saga.CreatedAt;
        command.Parameters.Add("@UpdatedAt", SqlDbType.DateTime2).Value = saga.UpdatedAt;
        command.Parameters.Add("@IsCompleted", SqlDbType.Bit).Value = saga.IsCompleted;
        command.Parameters.Add("@Version", SqlDbType.Int).Value = saga.Version;
        command.Parameters.Add("@SagaData", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(saga, _jsonOptions);

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

    public async Task UpdateAsync(TSaga saga, CancellationToken cancellationToken = default)
    {
        if (saga == null)
            throw new ArgumentNullException(nameof(saga));

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
            updateCommand.Parameters.Add("@CorrelationId", SqlDbType.UniqueIdentifier).Value = saga.CorrelationId;
            updateCommand.Parameters.Add("@SagaType", SqlDbType.NVarChar, 500).Value = _sagaTypeName;
            updateCommand.Parameters.Add("@CurrentState", SqlDbType.NVarChar, 100).Value = saga.CurrentState;
            updateCommand.Parameters.Add("@UpdatedAt", SqlDbType.DateTime2).Value = saga.UpdatedAt;
            updateCommand.Parameters.Add("@IsCompleted", SqlDbType.Bit).Value = saga.IsCompleted;
            updateCommand.Parameters.Add("@Version", SqlDbType.Int).Value = saga.Version;
            updateCommand.Parameters.Add("@SagaData", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(saga, _jsonOptions);

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
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            DELETE FROM {_tableName}
            WHERE CorrelationId = @CorrelationId AND SagaType = @SagaType
            """;

        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@CorrelationId", SqlDbType.UniqueIdentifier).Value = correlationId;
        command.Parameters.Add("@SagaType", SqlDbType.NVarChar, 500).Value = _sagaTypeName;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IEnumerable<TSaga>> FindStaleAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var cutoffTime = _timeProvider.GetUtcNow().DateTime - olderThan;

        var sql = $"""
            SELECT SagaData
            FROM {_tableName}
            WHERE SagaType = @SagaType
              AND IsCompleted = 0
              AND UpdatedAt < @CutoffTime
            ORDER BY UpdatedAt ASC
            """;

        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@SagaType", SqlDbType.NVarChar, 500).Value = _sagaTypeName;
        command.Parameters.Add("@CutoffTime", SqlDbType.DateTime2).Value = cutoffTime;

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

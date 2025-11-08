using System.Data;
using System.Text.Json;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Microsoft.Data.SqlClient;

namespace HeroMessaging.Storage.SqlServer;

/// <summary>
/// SQL Server implementation of queue storage using pure ADO.NET
/// Provides message queueing with priority, visibility timeout, and dead letter support
/// </summary>
public class SqlServerQueueStorage : IQueueStorage
{
    private readonly SqlServerStorageOptions _options;
    private readonly SqlConnection? _sharedConnection;
    private readonly SqlTransaction? _sharedTransaction;
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly TimeProvider _timeProvider;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IJsonSerializer _jsonSerializer;

    public SqlServerQueueStorage(SqlServerStorageOptions options, TimeProvider timeProvider, IJsonSerializer jsonSerializer)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connectionString = options.ConnectionString ?? throw new ArgumentNullException(nameof(options.ConnectionString));
        _tableName = _options.GetFullTableName(_options.QueueTableName);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));

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

    public SqlServerQueueStorage(SqlConnection connection, SqlTransaction? transaction, TimeProvider timeProvider, IJsonSerializer jsonSerializer)
    {
        _sharedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sharedTransaction = transaction;
        _connectionString = connection.ConnectionString;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));

        // Use default options when using shared connection
        _options = new SqlServerStorageOptions { ConnectionString = connection.ConnectionString };
        _tableName = _options.GetFullTableName(_options.QueueTableName);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }

    private async Task<SqlConnection> GetConnectionAsync()
    {
        if (_sharedConnection != null)
        {
            return _sharedConnection;
        }

        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private SqlTransaction? GetTransaction()
    {
        return _sharedTransaction;
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
                          WHERE s.name = '{_options.Schema}' AND t.name = '{_options.QueueTableName}')
            BEGIN
                CREATE TABLE {_tableName} (
                    Id NVARCHAR(100) PRIMARY KEY,
                    QueueName NVARCHAR(200) NOT NULL,
                    MessageType NVARCHAR(500) NOT NULL,
                    Payload NVARCHAR(MAX) NOT NULL,
                    Priority INT NOT NULL DEFAULT 0,
                    EnqueuedAt DATETIME2 NOT NULL,
                    VisibleAt DATETIME2 NULL,
                    DequeueCount INT NOT NULL DEFAULT 0,
                    DelayMinutes INT NULL,
                    Acknowledged BIT NOT NULL DEFAULT 0,
                    INDEX IX_{_options.QueueTableName}_QueueName (QueueName),
                    INDEX IX_{_options.QueueTableName}_Priority (QueueName, Priority DESC, EnqueuedAt),
                    INDEX IX_{_options.QueueTableName}_VisibleAt (QueueName, VisibleAt, Acknowledged)
                )
            END
            """;

        using var command = new SqlCommand(createTableSql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<QueueEntry> Enqueue(string queueName, IMessage message, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        try
        {

            var entryId = Guid.NewGuid().ToString();
            var now = _timeProvider.GetUtcNow().DateTime;
            var visibleAt = options?.Delay.HasValue == true
                ? now.Add(options.Delay.Value)
                : now;

            var sql = $"""
                INSERT INTO {_tableName} (Id, QueueName, MessageType, Payload, Priority, EnqueuedAt, VisibleAt, DelayMinutes)
                VALUES (@Id, @QueueName, @MessageType, @Payload, @Priority, @EnqueuedAt, @VisibleAt, @DelayMinutes)
                """;

            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = entryId;
            command.Parameters.Add("@QueueName", SqlDbType.NVarChar, 200).Value = queueName;
            command.Parameters.Add("@MessageType", SqlDbType.NVarChar, 500).Value = message.GetType().FullName ?? "Unknown";
            command.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = _jsonSerializer.SerializeToString(message, _jsonOptions);
            command.Parameters.Add("@Priority", SqlDbType.Int).Value = options?.Priority ?? 0;
            command.Parameters.Add("@EnqueuedAt", SqlDbType.DateTime2).Value = now;
            command.Parameters.Add("@VisibleAt", SqlDbType.DateTime2).Value = visibleAt;
            command.Parameters.Add("@DelayMinutes", SqlDbType.Int).Value = (object?)options?.Delay?.TotalMinutes ?? DBNull.Value;

            await command.ExecuteNonQueryAsync(cancellationToken);

            return new QueueEntry
            {
                Id = entryId,
                Message = message,
                Options = options ?? new EnqueueOptions(),
                EnqueuedAt = now,
                VisibleAt = visibleAt,
                DequeueCount = 0
            };
        }
        finally
        {
        }
    }

    public async Task<QueueEntry?> Dequeue(string queueName, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        try
        {

            var now = _timeProvider.GetUtcNow().DateTime;

            // Use a transaction to ensure atomic dequeue
            var localTransaction = transaction ?? connection.BeginTransaction(IsolationLevel.ReadCommitted);

            try
            {
                // Find the highest priority message that is visible
                var selectSql = $"""
                    SELECT TOP 1 Id, MessageType, Payload, Priority, EnqueuedAt, VisibleAt, DequeueCount, DelayMinutes
                    FROM {_tableName} WITH (UPDLOCK, READPAST)
                    WHERE QueueName = @QueueName
                    AND Acknowledged = 0
                    AND (VisibleAt IS NULL OR VisibleAt <= @Now)
                    ORDER BY Priority DESC, EnqueuedAt ASC
                    """;

                using var selectCommand = new SqlCommand(selectSql, connection, localTransaction);
                selectCommand.Parameters.Add("@QueueName", SqlDbType.NVarChar, 200).Value = queueName;
                selectCommand.Parameters.Add("@Now", SqlDbType.DateTime2).Value = now;

                using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    if (transaction == null) localTransaction.Rollback();
                    return null;
                }

                var entryId = reader.GetString(0);
                var messageType = reader.GetString(1);
                var payload = reader.GetString(2);
                var priority = reader.GetInt32(3);
                var enqueuedAt = reader.GetDateTime(4);
                var visibleAt = reader.IsDBNull(5) ? (DateTime?)null : reader.GetDateTime(5);
                var dequeueCount = reader.GetInt32(6);
                var delayMinutes = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7);

                await reader.CloseAsync();

                // Update dequeue count and visibility timeout
                var updateSql = $"""
                    UPDATE {_tableName}
                    SET DequeueCount = DequeueCount + 1,
                        VisibleAt = DATEADD(MINUTE, 5, @Now)
                    WHERE Id = @Id
                    """;

                using var updateCommand = new SqlCommand(updateSql, connection, localTransaction);
                updateCommand.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = entryId;
                updateCommand.Parameters.Add("@Now", SqlDbType.DateTime2).Value = now;

                await updateCommand.ExecuteNonQueryAsync(cancellationToken);

                if (transaction == null) localTransaction.Commit();

                var message = _jsonSerializer.DeserializeFromString<IMessage>(payload, _jsonOptions);

                return new QueueEntry
                {
                    Id = entryId,
                    Message = message!,
                    Options = new EnqueueOptions
                    {
                        Priority = priority,
                        Delay = delayMinutes.HasValue ? TimeSpan.FromMinutes(delayMinutes.Value) : null
                    },
                    EnqueuedAt = enqueuedAt,
                    VisibleAt = visibleAt,
                    DequeueCount = dequeueCount + 1
                };
            }
            catch
            {
                if (transaction == null) localTransaction.Rollback();
                throw;
            }
        }
        finally
        {
        }
    }

    public async Task<IEnumerable<QueueEntry>> Peek(string queueName, int count = 1, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        try
        {

            var now = _timeProvider.GetUtcNow().DateTime;

            var sql = $"""
                SELECT TOP (@Count) Id, MessageType, Payload, Priority, EnqueuedAt, VisibleAt, DequeueCount, DelayMinutes
                FROM {_tableName}
                WHERE QueueName = @QueueName
                AND Acknowledged = 0
                AND (VisibleAt IS NULL OR VisibleAt <= @Now)
                ORDER BY Priority DESC, EnqueuedAt ASC
                """;

            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@QueueName", SqlDbType.NVarChar, 200).Value = queueName;
            command.Parameters.Add("@Count", SqlDbType.Int).Value = count;
            command.Parameters.Add("@Now", SqlDbType.DateTime2).Value = now;

            var entries = new List<QueueEntry>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var entryId = reader.GetString(0);
                var messageType = reader.GetString(1);
                var payload = reader.GetString(2);
                var priority = reader.GetInt32(3);
                var enqueuedAt = reader.GetDateTime(4);
                var visibleAt = reader.IsDBNull(5) ? (DateTime?)null : reader.GetDateTime(5);
                var dequeueCount = reader.GetInt32(6);
                var delayMinutes = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7);

                var message = _jsonSerializer.DeserializeFromString<IMessage>(payload, _jsonOptions);

                entries.Add(new QueueEntry
                {
                    Id = entryId,
                    Message = message!,
                    Options = new EnqueueOptions
                    {
                        Priority = priority,
                        Delay = delayMinutes.HasValue ? TimeSpan.FromMinutes(delayMinutes.Value) : null
                    },
                    EnqueuedAt = enqueuedAt,
                    VisibleAt = visibleAt,
                    DequeueCount = dequeueCount
                });
            }

            return entries;
        }
        finally
        {
        }
    }

    public async Task<bool> Acknowledge(string queueName, string entryId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        try
        {

            var sql = $"""
                UPDATE {_tableName}
                SET Acknowledged = 1
                WHERE Id = @Id AND QueueName = @QueueName
                """;

            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = entryId;
            command.Parameters.Add("@QueueName", SqlDbType.NVarChar, 200).Value = queueName;

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            return rowsAffected > 0;
        }
        finally
        {
        }
    }

    public async Task<bool> Reject(string queueName, string entryId, bool requeue = false, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        try
        {

            if (requeue)
            {
                // Make the message visible again immediately
                var sql = $"""
                    UPDATE {_tableName}
                    SET VisibleAt = @Now
                    WHERE Id = @Id AND QueueName = @QueueName
                    """;

                using var command = new SqlCommand(sql, connection, transaction);
                command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = entryId;
                command.Parameters.Add("@QueueName", SqlDbType.NVarChar, 200).Value = queueName;
                command.Parameters.Add("@Now", SqlDbType.DateTime2).Value = _timeProvider.GetUtcNow().DateTime;

                var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
                return rowsAffected > 0;
            }
            else
            {
                // Delete the message
                var sql = $"""
                    DELETE FROM {_tableName}
                    WHERE Id = @Id AND QueueName = @QueueName
                    """;

                using var command = new SqlCommand(sql, connection, transaction);
                command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = entryId;
                command.Parameters.Add("@QueueName", SqlDbType.NVarChar, 200).Value = queueName;

                var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
                return rowsAffected > 0;
            }
        }
        finally
        {
        }
    }

    public async Task<long> GetQueueDepth(string queueName, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        try
        {

            var sql = $"""
                SELECT COUNT(1)
                FROM {_tableName}
                WHERE QueueName = @QueueName
                AND Acknowledged = 0
                """;

            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@QueueName", SqlDbType.NVarChar, 200).Value = queueName;

            var count = (int)await command.ExecuteScalarAsync(cancellationToken);
            return count;
        }
        finally
        {
        }
    }

    public async Task<bool> CreateQueue(string queueName, QueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        // In SQL Server implementation, queues are created implicitly when messages are enqueued
        // This is a no-op but returns true to indicate success
        await Task.CompletedTask;
        return true;
    }

    public async Task<bool> DeleteQueue(string queueName, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        try
        {

            var sql = $"""
                DELETE FROM {_tableName}
                WHERE QueueName = @QueueName
                """;

            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@QueueName", SqlDbType.NVarChar, 200).Value = queueName;

            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        finally
        {
        }
    }

    public async Task<IEnumerable<string>> GetQueues(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        try
        {

            var sql = $"""
                SELECT DISTINCT QueueName
                FROM {_tableName}
                ORDER BY QueueName
                """;

            using var command = new SqlCommand(sql, connection, transaction);

            var queues = new List<string>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                queues.Add(reader.GetString(0));
            }

            return queues;
        }
        finally
        {
        }
    }

    public async Task<bool> QueueExists(string queueName, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        try
        {

            var sql = $"""
                SELECT COUNT(1)
                FROM {_tableName}
                WHERE QueueName = @QueueName
                """;

            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@QueueName", SqlDbType.NVarChar, 200).Value = queueName;

            var count = (int)await command.ExecuteScalarAsync(cancellationToken);
            return count > 0;
        }
        finally
        {
        }
    }
}


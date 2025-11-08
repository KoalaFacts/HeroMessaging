using System.Data;
using System.Text.Json;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Npgsql;

namespace HeroMessaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of queue storage using pure ADO.NET
/// Provides message queueing with priority, visibility timeout, and dead letter support
/// </summary>
public class PostgreSqlQueueStorage : IQueueStorage
{
    private readonly PostgreSqlStorageOptions _options;
    private readonly NpgsqlConnection? _sharedConnection;
    private readonly NpgsqlTransaction? _sharedTransaction;
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly TimeProvider _timeProvider;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IJsonSerializer _jsonSerializer;

    public PostgreSqlQueueStorage(PostgreSqlStorageOptions options, TimeProvider timeProvider, IJsonSerializer jsonSerializer)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connectionString = options.ConnectionString ?? throw new ArgumentNullException(nameof(options), "ConnectionString cannot be null");
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

    public PostgreSqlQueueStorage(NpgsqlConnection connection, NpgsqlTransaction? transaction, TimeProvider timeProvider, IJsonSerializer jsonSerializer)
    {
        _sharedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sharedTransaction = transaction;
        _connectionString = connection.ConnectionString ?? string.Empty;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));

        // Use default options when using shared connection
        _options = new PostgreSqlStorageOptions { ConnectionString = connection.ConnectionString! };
        _tableName = _options.GetFullTableName(_options.QueueTableName);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }

    private async Task<NpgsqlConnection> GetConnectionAsync()
    {
        if (_sharedConnection != null)
        {
            return _sharedConnection;
        }

        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private NpgsqlTransaction? GetTransaction()
    {
        return _sharedTransaction;
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
                id VARCHAR(100) PRIMARY KEY,
                queue_name VARCHAR(200) NOT NULL,
                message_type VARCHAR(500) NOT NULL,
                payload JSONB NOT NULL,
                priority INTEGER NOT NULL DEFAULT 0,
                enqueued_at TIMESTAMP NOT NULL,
                visible_at TIMESTAMP,
                dequeue_count INTEGER NOT NULL DEFAULT 0,
                delay_minutes INTEGER,
                acknowledged BOOLEAN NOT NULL DEFAULT false
            );

            CREATE INDEX IF NOT EXISTS idx_{_options.QueueTableName}_queue_name ON {_tableName}(queue_name);
            CREATE INDEX IF NOT EXISTS idx_{_options.QueueTableName}_priority ON {_tableName}(queue_name, priority DESC, enqueued_at);
            CREATE INDEX IF NOT EXISTS idx_{_options.QueueTableName}_visible_at ON {_tableName}(queue_name, visible_at, acknowledged) WHERE acknowledged = false;
            """;

        using var command = new NpgsqlCommand(createTableSql, connection);
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
                INSERT INTO {_tableName} (id, queue_name, message_type, payload, priority, enqueued_at, visible_at, delay_minutes)
                VALUES (@id, @queue_name, @message_type, @payload::jsonb, @priority, @enqueued_at, @visible_at, @delay_minutes)
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", entryId);
            command.Parameters.AddWithValue("queue_name", queueName);
            command.Parameters.AddWithValue("message_type", message.GetType().FullName ?? "Unknown");
            command.Parameters.AddWithValue("payload", _jsonSerializer.SerializeToString(message, _jsonOptions));
            command.Parameters.AddWithValue("priority", options?.Priority ?? 0);
            command.Parameters.AddWithValue("enqueued_at", now);
            command.Parameters.AddWithValue("visible_at", visibleAt);
            command.Parameters.AddWithValue("delay_minutes", (object?)options?.Delay?.TotalMinutes ?? DBNull.Value);

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
            var localTransaction = transaction ?? await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

            try
            {
                // Find the highest priority message that is visible using FOR UPDATE SKIP LOCKED
                var selectSql = $"""
                    SELECT id, message_type, payload, priority, enqueued_at, visible_at, dequeue_count, delay_minutes
                    FROM {_tableName}
                    WHERE queue_name = @queue_name
                    AND acknowledged = false
                    AND (visible_at IS NULL OR visible_at <= @now)
                    ORDER BY priority DESC, enqueued_at ASC
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                    """;

                using var selectCommand = new NpgsqlCommand(selectSql, connection, localTransaction);
                selectCommand.Parameters.AddWithValue("queue_name", queueName);
                selectCommand.Parameters.AddWithValue("now", now);

                using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    if (transaction == null) await localTransaction.RollbackAsync(cancellationToken);
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
                    SET dequeue_count = dequeue_count + 1,
                        visible_at = @visible_at
                    WHERE id = @id
                    """;

                using var updateCommand = new NpgsqlCommand(updateSql, connection, localTransaction);
                updateCommand.Parameters.AddWithValue("id", entryId);
                updateCommand.Parameters.AddWithValue("visible_at", now.AddMinutes(5));

                await updateCommand.ExecuteNonQueryAsync(cancellationToken);

                if (transaction == null) await localTransaction.CommitAsync(cancellationToken);

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
                if (transaction == null) await localTransaction.RollbackAsync(cancellationToken);
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
                SELECT id, message_type, payload, priority, enqueued_at, visible_at, dequeue_count, delay_minutes
                FROM {_tableName}
                WHERE queue_name = @queue_name
                AND acknowledged = false
                AND (visible_at IS NULL OR visible_at <= @now)
                ORDER BY priority DESC, enqueued_at ASC
                LIMIT @count
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("queue_name", queueName);
            command.Parameters.AddWithValue("count", count);
            command.Parameters.AddWithValue("now", now);

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
                SET acknowledged = true
                WHERE id = @id AND queue_name = @queue_name
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", entryId);
            command.Parameters.AddWithValue("queue_name", queueName);

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
                    SET visible_at = @now
                    WHERE id = @id AND queue_name = @queue_name
                    """;

                using var command = new NpgsqlCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("id", entryId);
                command.Parameters.AddWithValue("queue_name", queueName);
                command.Parameters.AddWithValue("now", _timeProvider.GetUtcNow().DateTime);

                var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
                return rowsAffected > 0;
            }
            else
            {
                // Delete the message
                var sql = $"""
                    DELETE FROM {_tableName}
                    WHERE id = @id AND queue_name = @queue_name
                    """;

                using var command = new NpgsqlCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("id", entryId);
                command.Parameters.AddWithValue("queue_name", queueName);

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
                WHERE queue_name = @queue_name
                AND acknowledged = false
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("queue_name", queueName);

            var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            return count;
        }
        finally
        {
        }
    }

    public async Task<bool> CreateQueue(string queueName, QueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        // In PostgreSQL implementation, queues are created implicitly when messages are enqueued
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
                WHERE queue_name = @queue_name
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("queue_name", queueName);

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
                SELECT DISTINCT queue_name
                FROM {_tableName}
                ORDER BY queue_name
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);

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
                WHERE queue_name = @queue_name
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("queue_name", queueName);

            var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            return count > 0;
        }
        finally
        {
        }
    }
}

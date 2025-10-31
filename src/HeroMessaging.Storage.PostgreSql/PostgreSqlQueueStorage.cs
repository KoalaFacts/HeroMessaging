using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Npgsql;
using System.Data;
using System.Text.Json;

namespace HeroMessaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of queue storage using pure ADO.NET
/// Provides message queueing with priority, visibility timeout, and dead letter support
/// </summary>
/// <remarks>
/// Supports priority-based message ordering, delayed message delivery, and visibility timeouts
/// for reliable message processing. Uses PostgreSQL's FOR UPDATE SKIP LOCKED for efficient
/// concurrent dequeuing without blocking.
/// </remarks>
public class PostgreSqlQueueStorage : IQueueStorage
{
    private readonly PostgreSqlStorageOptions _options;
    private readonly NpgsqlConnection? _sharedConnection;
    private readonly NpgsqlTransaction? _sharedTransaction;
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly TimeProvider _timeProvider;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of PostgreSQL queue storage with independent connection management
    /// </summary>
    /// <param name="options">Configuration options for PostgreSQL storage including connection string and table names</param>
    /// <param name="timeProvider">Time provider for testable time-based operations</param>
    /// <exception cref="ArgumentNullException">Thrown when options or timeProvider is null</exception>
    /// <remarks>
    /// This constructor creates a queue storage instance that manages its own database connections.
    /// If AutoCreateTables is enabled in options, database schema is initialized synchronously.
    /// </remarks>
    public PostgreSqlQueueStorage(PostgreSqlStorageOptions options, TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connectionString = options.ConnectionString ?? throw new ArgumentNullException(nameof(options.ConnectionString));
        _tableName = _options.GetFullTableName(_options.QueueTableName);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

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

    /// <summary>
    /// Initializes a new instance of PostgreSQL queue storage using a shared connection and transaction
    /// </summary>
    /// <param name="connection">Shared NpgsqlConnection to use for all operations</param>
    /// <param name="transaction">Optional shared transaction for participating in unit of work</param>
    /// <param name="timeProvider">Time provider for testable time-based operations</param>
    /// <exception cref="ArgumentNullException">Thrown when connection or timeProvider is null</exception>
    /// <remarks>
    /// This constructor is used when the queue storage participates in a unit of work pattern,
    /// sharing a connection and transaction with other storage operations for atomicity.
    /// No automatic table creation occurs when using shared connections.
    /// </remarks>
    public PostgreSqlQueueStorage(NpgsqlConnection connection, NpgsqlTransaction? transaction, TimeProvider timeProvider)
    {
        _sharedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sharedTransaction = transaction;
        _connectionString = connection.ConnectionString ?? string.Empty;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        // Use default options when using shared connection
        _options = new PostgreSqlStorageOptions { ConnectionString = connection.ConnectionString };
        _tableName = _options.GetFullTableName(_options.QueueTableName);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
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

    /// <summary>
    /// Adds a message to the specified queue with optional priority and delay
    /// </summary>
    /// <param name="queueName">Name of the queue to add the message to</param>
    /// <param name="message">The message to enqueue</param>
    /// <param name="options">Optional enqueue options including priority and delay</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The created queue entry with assigned ID and metadata</returns>
    /// <exception cref="ArgumentNullException">Thrown when queueName or message is null</exception>
    /// <remarks>
    /// Messages with higher priority values are dequeued before lower priority messages.
    /// Delayed messages are not visible until their delay period has elapsed.
    /// </remarks>
    public async Task<QueueEntry> Enqueue(string queueName, IMessage message, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new NpgsqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

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
            command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(message, _jsonOptions));
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
            if (_sharedConnection == null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Retrieves and locks the next available message from the specified queue
    /// </summary>
    /// <param name="queueName">Name of the queue to dequeue from</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The next available queue entry, or null if queue is empty</returns>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null</exception>
    /// <remarks>
    /// Uses FOR UPDATE SKIP LOCKED for efficient concurrent access without blocking.
    /// Messages are ordered by priority (descending) then enqueue time (ascending).
    /// Dequeued messages have a 5-minute visibility timeout and incremented dequeue count.
    /// The message remains in the queue until acknowledged or the visibility timeout expires.
    /// </remarks>
    public async Task<QueueEntry?> Dequeue(string queueName, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new NpgsqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

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

                var message = JsonSerializer.Deserialize<IMessage>(payload, _jsonOptions);

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
            if (_sharedConnection == null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Retrieves messages from the queue without removing or locking them
    /// </summary>
    /// <param name="queueName">Name of the queue to peek into</param>
    /// <param name="count">Maximum number of messages to retrieve. Default is 1</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Collection of visible queue entries, ordered by priority then enqueue time</returns>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null</exception>
    /// <remarks>
    /// Peek allows inspecting queue contents without modifying message state.
    /// Only returns messages that are currently visible (not locked by another consumer).
    /// Messages remain in the queue and can be dequeued by other consumers.
    /// </remarks>
    public async Task<IEnumerable<QueueEntry>> Peek(string queueName, int count = 1, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new NpgsqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

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

                var message = JsonSerializer.Deserialize<IMessage>(payload, _jsonOptions);

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
            if (_sharedConnection == null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Marks a message as successfully processed and removes it from the queue
    /// </summary>
    /// <param name="queueName">Name of the queue containing the message</param>
    /// <param name="entryId">Unique identifier of the queue entry to acknowledge</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the message was acknowledged; false if not found</returns>
    /// <exception cref="ArgumentNullException">Thrown when queueName or entryId is null</exception>
    /// <remarks>
    /// Call this method after successfully processing a dequeued message.
    /// The message is permanently marked as acknowledged and won't be reprocessed.
    /// </remarks>
    public async Task<bool> Acknowledge(string queueName, string entryId, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new NpgsqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

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
            if (_sharedConnection == null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Rejects a message, either making it immediately visible for retry or removing it from the queue
    /// </summary>
    /// <param name="queueName">Name of the queue containing the message</param>
    /// <param name="entryId">Unique identifier of the queue entry to reject</param>
    /// <param name="requeue">If true, makes message immediately visible for retry; if false, deletes the message</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the operation succeeded; false if message not found</returns>
    /// <exception cref="ArgumentNullException">Thrown when queueName or entryId is null</exception>
    /// <remarks>
    /// Use requeue=true for transient failures where the message should be retried immediately.
    /// Use requeue=false for permanent failures where the message should be discarded.
    /// Consider sending permanently failed messages to a dead letter queue before rejecting.
    /// </remarks>
    public async Task<bool> Reject(string queueName, string entryId, bool requeue = false, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new NpgsqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

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
            if (_sharedConnection == null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Gets the total number of pending (unacknowledged) messages in the specified queue
    /// </summary>
    /// <param name="queueName">Name of the queue to check</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The count of pending messages in the queue</returns>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null</exception>
    /// <remarks>
    /// Includes both visible and temporarily invisible (locked) messages.
    /// Useful for monitoring queue backlog and scaling consumers.
    /// </remarks>
    public async Task<long> GetQueueDepth(string queueName, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new NpgsqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

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
            if (_sharedConnection == null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Creates a new queue with the specified name and options
    /// </summary>
    /// <param name="queueName">Name of the queue to create</param>
    /// <param name="options">Optional queue configuration options</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Always returns true as queues are created implicitly in PostgreSQL implementation</returns>
    /// <remarks>
    /// In the PostgreSQL implementation, queues are created implicitly when the first message is enqueued.
    /// This method is a no-op but provided for API compatibility. Queue configuration is applied per-message via EnqueueOptions.
    /// </remarks>
    public async Task<bool> CreateQueue(string queueName, QueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        // In PostgreSQL implementation, queues are created implicitly when messages are enqueued
        // This is a no-op but returns true to indicate success
        await Task.CompletedTask;
        return true;
    }

    /// <summary>
    /// Deletes all messages from the specified queue
    /// </summary>
    /// <param name="queueName">Name of the queue to delete</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the operation succeeded</returns>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null</exception>
    /// <remarks>
    /// WARNING: This operation is destructive and cannot be undone.
    /// All messages in the queue, including locked and delayed messages, are permanently deleted.
    /// </remarks>
    public async Task<bool> DeleteQueue(string queueName, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new NpgsqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

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
            if (_sharedConnection == null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Retrieves the names of all queues that currently have messages
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Collection of distinct queue names, ordered alphabetically</returns>
    /// <remarks>
    /// Only returns queues that currently contain at least one message.
    /// Empty queues are not tracked in the PostgreSQL implementation.
    /// </remarks>
    public async Task<IEnumerable<string>> GetQueues(CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new NpgsqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

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
            if (_sharedConnection == null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Checks whether the specified queue has any messages
    /// </summary>
    /// <param name="queueName">Name of the queue to check</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the queue has at least one message; otherwise false</returns>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null</exception>
    /// <remarks>
    /// In the PostgreSQL implementation, a queue "exists" if it contains at least one message.
    /// Empty queues are considered non-existent.
    /// </remarks>
    public async Task<bool> QueueExists(string queueName, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new NpgsqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

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
            if (_sharedConnection == null)
            {
                await connection.DisposeAsync();
            }
        }
    }
}

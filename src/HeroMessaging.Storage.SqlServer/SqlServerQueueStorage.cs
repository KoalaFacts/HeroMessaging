using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerQueueStorage"/> class with the specified options.
    /// </summary>
    /// <param name="options">SQL Server storage configuration options including connection string and table settings</param>
    /// <param name="timeProvider">Provider for retrieving current time, useful for testing with custom time</param>
    /// <remarks>
    /// This constructor creates a new queue storage instance that manages its own database connections.
    /// If <see cref="SqlServerStorageOptions.AutoCreateTables"/> is true, the queue table and schema
    /// will be created automatically during construction.
    ///
    /// The queue table includes indexes optimized for:
    /// - Priority and FIFO ordering (QueueName, Priority DESC, EnqueuedAt)
    /// - Visibility timeout queries (QueueName, VisibleAt, Acknowledged)
    /// - Queue name lookups for depth and existence checks
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when options, options.ConnectionString, or timeProvider is null</exception>
    public SqlServerQueueStorage(SqlServerStorageOptions options, TimeProvider timeProvider)
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
    /// Initializes a new instance of the <see cref="SqlServerQueueStorage"/> class with a shared connection and optional transaction.
    /// </summary>
    /// <param name="connection">An existing SQL Server connection to use for all operations</param>
    /// <param name="transaction">Optional SQL Server transaction for transactional operations</param>
    /// <param name="timeProvider">Provider for retrieving current time, useful for testing with custom time</param>
    /// <remarks>
    /// This constructor is used for transaction-aware operations where the queue storage participates
    /// in an existing transaction scope. This enables atomic operations across multiple storage systems
    /// within a single database transaction.
    ///
    /// When using a shared connection:
    /// - The connection is not disposed when operations complete
    /// - All operations use the provided transaction if one is specified
    /// - The caller is responsible for connection and transaction lifecycle management
    /// - Tables are NOT automatically created (AutoCreateTables is ignored)
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when connection or timeProvider is null</exception>
    public SqlServerQueueStorage(SqlConnection connection, SqlTransaction? transaction, TimeProvider timeProvider)
    {
        _sharedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sharedTransaction = transaction;
        _connectionString = connection.ConnectionString;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        // Use default options when using shared connection
        _options = new SqlServerStorageOptions { ConnectionString = connection.ConnectionString };
        _tableName = _options.GetFullTableName(_options.QueueTableName);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
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

    /// <summary>
    /// Adds a message to the specified queue for background processing using SQL Server storage.
    /// </summary>
    /// <param name="queueName">The name of the queue to add the message to</param>
    /// <param name="message">The message to enqueue</param>
    /// <param name="options">Optional enqueue configuration including priority, delay, and metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created queue entry with assigned ID and metadata</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Message is serialized to JSON for storage in NVARCHAR(MAX) column
    /// - Generates a new GUID as the entry ID
    /// - VisibleAt is set to now + Delay if delay is specified
    /// - Priority, delay, and enqueue timestamp are stored for ordering
    /// - If using a shared transaction, the insert participates in that transaction
    /// - Queues are implicitly created when first message is enqueued (no explicit CreateQueue required)
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName or message is null</exception>
    public async Task<QueueEntry> Enqueue(string queueName, IMessage message, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new SqlConnection(_connectionString);
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
                INSERT INTO {_tableName} (Id, QueueName, MessageType, Payload, Priority, EnqueuedAt, VisibleAt, DelayMinutes)
                VALUES (@Id, @QueueName, @MessageType, @Payload, @Priority, @EnqueuedAt, @VisibleAt, @DelayMinutes)
                """;

            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = entryId;
            command.Parameters.Add("@QueueName", SqlDbType.NVarChar, 200).Value = queueName;
            command.Parameters.Add("@MessageType", SqlDbType.NVarChar, 500).Value = message.GetType().FullName ?? "Unknown";
            command.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(message, _jsonOptions);
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
            if (_sharedConnection == null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Retrieves and removes the next available message from the queue using SQL Server storage.
    /// </summary>
    /// <param name="queueName">The name of the queue to dequeue from</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The next queue entry if available; otherwise null</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Uses UPDLOCK and READPAST hints for lock-free concurrent dequeuing
    /// - Atomic dequeue operation using a transaction to ensure consistency
    /// - Selects highest priority message first, then FIFO within same priority
    /// - Only returns messages where VisibleAt is NULL or in the past
    /// - Increments DequeueCount and sets new VisibleAt (current time + 5 minutes visibility timeout)
    /// - If using a shared transaction, uses that transaction; otherwise creates a local one
    /// - Message payload is deserialized from JSON
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null</exception>
    public async Task<QueueEntry?> Dequeue(string queueName, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new SqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

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
                if (transaction == null) localTransaction.Rollback();
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
    /// Retrieves messages from the queue without removing them (preview mode) using SQL Server storage.
    /// </summary>
    /// <param name="queueName">The name of the queue to peek into</param>
    /// <param name="count">Maximum number of messages to retrieve. Default is 1</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of queue entries up to the specified count</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Uses SELECT TOP(count) to retrieve specified number of messages
    /// - Messages are returned in dequeue order (priority DESC, then FIFO)
    /// - Only returns visible messages (VisibleAt is NULL or in the past)
    /// - Does not modify message visibility or dequeue count
    /// - All message payloads are deserialized from JSON
    /// - Query participates in shared transaction if one is active
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null</exception>
    public async Task<IEnumerable<QueueEntry>> Peek(string queueName, int count = 1, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new SqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

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
    /// Acknowledges successful processing of a dequeued message, permanently removing it from the queue in SQL Server storage.
    /// </summary>
    /// <param name="queueName">The name of the queue containing the entry</param>
    /// <param name="entryId">The unique identifier of the queue entry to acknowledge</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was acknowledged and removed; false if not found</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Sets Acknowledged flag to 1 (true) to mark message as completed
    /// - Message remains in table but is excluded from dequeue queries
    /// - Consider periodic cleanup of acknowledged messages for storage optimization
    /// - If using a shared transaction, the update participates in that transaction
    /// - Uses composite key (Id + QueueName) for efficient lookup
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName or entryId is null</exception>
    public async Task<bool> Acknowledge(string queueName, string entryId, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new SqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

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
            if (_sharedConnection == null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Rejects a dequeued message, optionally returning it to the queue for retry using SQL Server storage.
    /// </summary>
    /// <param name="queueName">The name of the queue containing the entry</param>
    /// <param name="entryId">The unique identifier of the queue entry to reject</param>
    /// <param name="requeue">If true, message is returned to queue for retry. If false, message is permanently removed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was rejected; false if not found</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - When requeue=true: Updates VisibleAt to current time, making message immediately available for dequeue
    /// - When requeue=false: Deletes the message row from the table permanently
    /// - If using a shared transaction, the operation participates in that transaction
    /// - Uses composite key (Id + QueueName) for efficient lookup
    /// - DequeueCount is not reset when requeuing (preserves retry tracking)
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName or entryId is null</exception>
    public async Task<bool> Reject(string queueName, string entryId, bool requeue = false, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new SqlConnection(_connectionString);
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
            if (_sharedConnection == null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Gets the current number of messages in the queue (pending + invisible) from SQL Server storage.
    /// </summary>
    /// <param name="queueName">The name of the queue to measure</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The total number of messages in the queue, including invisible messages</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Uses COUNT(*) query filtered by QueueName and Acknowledged = 0
    /// - Includes both visible and invisible (being processed) messages
    /// - Excludes acknowledged messages that are pending cleanup
    /// - Efficient indexed query for monitoring and capacity planning
    /// - Query participates in shared transaction if one is active
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null</exception>
    public async Task<long> GetQueueDepth(string queueName, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new SqlConnection(_connectionString);
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
            if (_sharedConnection == null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Creates a new queue with the specified options in SQL Server storage.
    /// </summary>
    /// <param name="queueName">The name of the queue to create</param>
    /// <param name="options">Optional queue configuration including size limits, TTL, and visibility timeout</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the queue was created; false if it already exists</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - This is a no-op operation in the SQL Server implementation
    /// - Queues are implicitly created when the first message is enqueued
    /// - The method always returns true for compatibility with the interface
    /// - Queue options are not persisted; they should be applied at the application level
    /// - No database operations are performed by this method
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null</exception>
    public async Task<bool> CreateQueue(string queueName, QueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        // In SQL Server implementation, queues are created implicitly when messages are enqueued
        // This is a no-op but returns true to indicate success
        await Task.CompletedTask;
        return true;
    }

    /// <summary>
    /// Deletes a queue and all its messages from SQL Server storage.
    /// </summary>
    /// <param name="queueName">The name of the queue to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the queue was deleted; false if it doesn't exist</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Deletes all rows with the specified QueueName from the queue table
    /// - Permanently removes all messages (visible, invisible, and acknowledged)
    /// - If using a shared transaction, the delete participates in that transaction
    /// - Always returns true after deletion (doesn't check if queue existed)
    /// - No separate queue metadata to delete; queue ceases to exist when all messages are deleted
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null</exception>
    public async Task<bool> DeleteQueue(string queueName, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new SqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

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
            if (_sharedConnection == null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Retrieves the names of all existing queues from SQL Server storage.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of queue names</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Uses SELECT DISTINCT QueueName to retrieve unique queue names
    /// - Returns queues that have at least one message (any status)
    /// - Results are ordered alphabetically by queue name
    /// - Query participates in shared transaction if one is active
    /// - Empty queues (no messages) are not returned since queues are implicit
    /// </remarks>
    public async Task<IEnumerable<string>> GetQueues(CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new SqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

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
            if (_sharedConnection == null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Checks whether a queue with the specified name exists in SQL Server storage.
    /// </summary>
    /// <param name="queueName">The name of the queue to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the queue exists; otherwise false</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Uses COUNT(*) query to check if any messages exist for the specified queue name
    /// - Returns true if at least one message exists with the queue name (any status)
    /// - Empty queues (no messages) return false since queues are implicit
    /// - Query participates in shared transaction if one is active
    /// - Efficient indexed query for queue existence checks
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null</exception>
    public async Task<bool> QueueExists(string queueName, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new SqlConnection(_connectionString);
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
                WHERE QueueName = @QueueName
                """;

            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@QueueName", SqlDbType.NVarChar, 200).Value = queueName;

            var count = (int)await command.ExecuteScalarAsync(cancellationToken);
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


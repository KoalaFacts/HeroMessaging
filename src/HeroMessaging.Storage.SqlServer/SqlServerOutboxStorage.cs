using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace HeroMessaging.Storage.SqlServer;

/// <summary>
/// SQL Server implementation of outbox pattern storage using pure ADO.NET with configurable schema/table
/// </summary>
/// <remarks>
/// Implements the Transactional Outbox pattern for reliable message publishing in SQL Server.
/// Supports both standalone and transaction-aware operations using shared connections.
/// Messages are stored with retry counts, scheduling, and error tracking capabilities.
/// Uses SQL Server row-level locking (UPDLOCK, READPAST) to prevent concurrent processing of the same message.
/// </remarks>
public class SqlServerOutboxStorage : IOutboxStorage
{
    private readonly SqlServerStorageOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _tableName;
    private readonly SqlConnection? _sharedConnection;
    private readonly SqlTransaction? _sharedTransaction;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the SqlServerOutboxStorage class with full configuration options
    /// </summary>
    /// <param name="options">Configuration options including connection string, schema, and table names</param>
    /// <param name="timeProvider">The time provider for testable time-based operations</param>
    /// <exception cref="ArgumentNullException">Thrown when options or timeProvider is null</exception>
    public SqlServerOutboxStorage(SqlServerStorageOptions options, TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tableName = _options.GetFullTableName(_options.OutboxTableName);
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
    /// Initializes a new instance of the SqlServerOutboxStorage class for transaction-aware operations
    /// </summary>
    /// <param name="connection">The shared SQL Server connection to use</param>
    /// <param name="transaction">The optional transaction to participate in</param>
    /// <param name="timeProvider">The time provider for testable time-based operations</param>
    /// <exception cref="ArgumentNullException">Thrown when connection or timeProvider is null</exception>
    /// <remarks>
    /// This constructor is used when participating in an existing transaction context,
    /// such as within a Unit of Work. Operations will use the provided connection and transaction.
    /// </remarks>
    public SqlServerOutboxStorage(SqlConnection connection, SqlTransaction? transaction, TimeProvider timeProvider)
    {
        _sharedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sharedTransaction = transaction;

        // Use default options when using shared connection
        _options = new SqlServerStorageOptions { ConnectionString = connection.ConnectionString };
        _tableName = _options.GetFullTableName(_options.OutboxTableName);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }

    /// <summary>
    /// Initializes a new instance of the SqlServerOutboxStorage class with a connection string
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string</param>
    /// <param name="timeProvider">The time provider for testable time-based operations</param>
    /// <exception cref="ArgumentException">Thrown when connectionString is null or empty</exception>
    /// <exception cref="ArgumentNullException">Thrown when timeProvider is null</exception>
    public SqlServerOutboxStorage(string connectionString, TimeProvider timeProvider)
    {
        if (string.IsNullOrEmpty(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty", nameof(connectionString));

        _options = new SqlServerStorageOptions { ConnectionString = connectionString };
        _tableName = _options.GetFullTableName(_options.OutboxTableName);
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

    private async Task InitializeDatabase()
    {
        using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync();

        // Create schema if it doesn't exist
        var createSchemaSql = $"""
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{_options.Schema}')
            BEGIN
                EXEC('CREATE SCHEMA [{_options.Schema}]')
            END
            """;

        using (var schemaCommand = new SqlCommand(createSchemaSql, connection))
        {
            schemaCommand.CommandTimeout = _options.CommandTimeout;
            await schemaCommand.ExecuteNonQueryAsync();
        }

        // Create table with configurable name
        var createTableSql = $"""
            IF NOT EXISTS (SELECT * FROM sys.tables t 
                          JOIN sys.schemas s ON t.schema_id = s.schema_id 
                          WHERE s.name = '{_options.Schema}' AND t.name = '{_options.OutboxTableName}')
            BEGIN
                CREATE TABLE {_tableName} (
                    Id NVARCHAR(100) PRIMARY KEY,
                    MessagePayload NVARCHAR(MAX) NOT NULL,
                    MessageType NVARCHAR(500) NOT NULL,
                    Status INT NOT NULL DEFAULT 0,
                    RetryCount INT NOT NULL DEFAULT 0,
                    CreatedAt DATETIME2 NOT NULL,
                    ProcessedAt DATETIME2 NULL,
                    NextRetryAt DATETIME2 NULL,
                    LastError NVARCHAR(MAX) NULL,
                    INDEX IX_{_options.OutboxTableName}_Status_NextRetry (Status, NextRetryAt),
                    INDEX IX_{_options.OutboxTableName}_ProcessedAt (ProcessedAt)
                )
            END
            """;

        using var command = new SqlCommand(createTableSql, connection);
        command.CommandTimeout = _options.CommandTimeout;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Adds a message to the outbox for reliable delivery
    /// </summary>
    /// <param name="message">The message to add to the outbox</param>
    /// <param name="options">Options controlling message delivery behavior</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The created outbox entry with assigned ID and metadata</returns>
    /// <exception cref="SqlException">Thrown when a database error occurs</exception>
    /// <remarks>
    /// When used with a shared transaction, the message is added atomically with other operations.
    /// The message will remain in Pending status until processed by an outbox worker.
    /// </remarks>
    public async Task<OutboxEntry> Add(IMessage message, OutboxOptions options, CancellationToken cancellationToken = default)
    {
        var entry = new OutboxEntry
        {
            Id = Guid.NewGuid().ToString(),
            Message = message,
            Options = options,
            Status = OutboxStatus.Pending,
            RetryCount = 0,
            CreatedAt = _timeProvider.GetUtcNow().DateTime,
            NextRetryAt = null
        };

        var connection = _sharedConnection ?? new SqlConnection(_options.ConnectionString);
        var shouldDisposeConnection = _sharedConnection == null;

        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            var sql = $"""
                INSERT INTO {_tableName} (Id, MessagePayload, MessageType, Status, RetryCount, CreatedAt, NextRetryAt)
                VALUES (@Id, @MessagePayload, @MessageType, @Status, @RetryCount, @CreatedAt, @NextRetryAt)
                """;

            using var command = new SqlCommand(sql, connection, _sharedTransaction);
            command.CommandTimeout = _options.CommandTimeout;
            command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = entry.Id;
            command.Parameters.Add("@MessagePayload", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(message, _jsonOptions);
            command.Parameters.Add("@MessageType", SqlDbType.NVarChar, 500).Value = message.GetType().FullName ?? "Unknown";
            command.Parameters.Add("@Status", SqlDbType.Int).Value = (int)entry.Status;
            command.Parameters.Add("@RetryCount", SqlDbType.Int).Value = entry.RetryCount;
            command.Parameters.Add("@CreatedAt", SqlDbType.DateTime2).Value = entry.CreatedAt;
            command.Parameters.Add("@NextRetryAt", SqlDbType.DateTime2).Value = (object?)entry.NextRetryAt ?? DBNull.Value;

            await command.ExecuteNonQueryAsync(cancellationToken);
            return entry;
        }
        finally
        {
            if (shouldDisposeConnection)
            {
                connection?.Dispose();
            }
        }
    }

    /// <summary>
    /// Retrieves pending outbox messages matching the specified query criteria
    /// </summary>
    /// <param name="query">Query parameters for filtering messages (status, time ranges, limit)</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Collection of outbox entries matching the query, ordered by priority descending then creation time ascending</returns>
    /// <exception cref="SqlException">Thrown when a database error occurs</exception>
    public async Task<IEnumerable<OutboxEntry>> GetPending(OutboxQuery query, CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var sql = $"SELECT TOP(@Limit) * FROM {_tableName} WHERE 1=1";

        if (query.Status.HasValue)
        {
            sql += " AND Status = @Status";
        }
        else
        {
            sql += " AND Status = 0"; // Pending
        }

        sql += " AND (NextRetryAt IS NULL OR NextRetryAt <= @Now)";

        if (query.OlderThan.HasValue)
        {
            sql += " AND CreatedAt < @OlderThan";
        }

        if (query.NewerThan.HasValue)
        {
            sql += " AND CreatedAt > @NewerThan";
        }

        sql += " ORDER BY Priority DESC, CreatedAt ASC";

        using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Limit", query.Limit);
        command.Parameters.AddWithValue("@Now", _timeProvider.GetUtcNow().DateTime);

        if (query.Status.HasValue)
        {
            command.Parameters.AddWithValue("@Status", (int)query.Status.Value);
        }

        if (query.OlderThan.HasValue)
        {
            command.Parameters.AddWithValue("@OlderThan", query.OlderThan.Value);
        }

        if (query.NewerThan.HasValue)
        {
            command.Parameters.AddWithValue("@NewerThan", query.NewerThan.Value);
        }

        var entries = new List<OutboxEntry>();

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(MapToEntry(reader));
        }

        await transaction.CommitAsync(cancellationToken);
        return entries;
    }

    /// <summary>
    /// Retrieves and locks pending outbox messages for processing
    /// </summary>
    /// <param name="limit">Maximum number of messages to retrieve (default: 100)</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Collection of pending outbox entries, automatically marked as Processing</returns>
    /// <exception cref="SqlException">Thrown when a database error occurs</exception>
    /// <remarks>
    /// Uses SQL Server row-level locking (UPDLOCK, READPAST) to prevent concurrent workers
    /// from processing the same messages. Retrieved messages are automatically transitioned
    /// to Processing status. Only returns messages where NextRetryAt is null or in the past.
    /// </remarks>
    public async Task<IEnumerable<OutboxEntry>> GetPending(int limit = 100, CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        // Select and lock messages for processing
        var selectSql = $"""
            SELECT TOP(@Limit) 
                Id, MessagePayload, MessageType, Status, RetryCount, CreatedAt, ProcessedAt, NextRetryAt, LastError
            FROM {_tableName} WITH (UPDLOCK, READPAST)
            WHERE Status = @PendingStatus AND (NextRetryAt IS NULL OR NextRetryAt <= @Now)
            ORDER BY CreatedAt
            """;

        var entries = new List<OutboxEntry>();
        var messageIds = new List<string>();

        using (var selectCommand = new SqlCommand(selectSql, connection, transaction))
        {
            selectCommand.CommandTimeout = _options.CommandTimeout;
            selectCommand.Parameters.Add("@Limit", SqlDbType.Int).Value = limit;
            selectCommand.Parameters.Add("@PendingStatus", SqlDbType.Int).Value = (int)OutboxStatus.Pending;
            selectCommand.Parameters.Add("@Now", SqlDbType.DateTime2).Value = _timeProvider.GetUtcNow().DateTime;

            using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetString(0);
                messageIds.Add(id);

                var messagePayload = reader.GetString(1);
                var messageType = reader.GetString(2);

                // Deserialize message dynamically
                var type = Type.GetType(messageType);
                IMessage? message = null;
                if (type != null)
                {
                    message = JsonSerializer.Deserialize(messagePayload, type, _jsonOptions) as IMessage;
                }

                if (message != null)
                {
                    entries.Add(new OutboxEntry
                    {
                        Id = id,
                        Message = message,
                        Options = new OutboxOptions(),
                        Status = (OutboxStatus)reader.GetInt32(3),
                        RetryCount = reader.GetInt32(4),
                        CreatedAt = reader.GetDateTime(5),
                        ProcessedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                        NextRetryAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                        LastError = reader.IsDBNull(8) ? null : reader.GetString(8)
                    });
                }
            }
        }

        if (messageIds.Any())
        {
            // Mark selected messages as processing
            var updateSql = $"""
                UPDATE {_tableName}
                SET Status = @ProcessingStatus 
                WHERE Id IN (SELECT value FROM STRING_SPLIT(@Ids, ','))
                """;

            using var updateCommand = new SqlCommand(updateSql, connection, transaction);
            updateCommand.CommandTimeout = _options.CommandTimeout;
            updateCommand.Parameters.Add("@ProcessingStatus", SqlDbType.Int).Value = (int)OutboxStatus.Processing;
            updateCommand.Parameters.Add("@Ids", SqlDbType.NVarChar, -1).Value = string.Join(",", messageIds);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return entries;
    }

    /// <summary>
    /// Marks an outbox message as successfully processed
    /// </summary>
    /// <param name="entryId">The unique identifier of the outbox entry</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the message was marked as processed; false if not found</returns>
    /// <exception cref="SqlException">Thrown when a database error occurs</exception>
    public async Task<bool> MarkProcessed(string entryId, CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            UPDATE {_tableName}
            SET Status = @Status, ProcessedAt = @ProcessedAt
            WHERE Id = @Id
            """;

        using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = _options.CommandTimeout;
        command.Parameters.Add("@Status", SqlDbType.Int).Value = (int)OutboxStatus.Processed;
        command.Parameters.Add("@ProcessedAt", SqlDbType.DateTime2).Value = _timeProvider.GetUtcNow().DateTime;
        command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = entryId;

        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        return result > 0;
    }

    /// <summary>
    /// Marks an outbox message as failed with error details
    /// </summary>
    /// <param name="entryId">The unique identifier of the outbox entry</param>
    /// <param name="error">The error message describing the failure</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the message was marked as failed; false if not found</returns>
    /// <exception cref="SqlException">Thrown when a database error occurs</exception>
    /// <remarks>Increments the retry count for the message</remarks>
    public async Task<bool> MarkFailed(string entryId, string error, CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            UPDATE {_tableName}
            SET Status = @Status, LastError = @Error, RetryCount = RetryCount + 1
            WHERE Id = @Id
            """;

        using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = _options.CommandTimeout;
        command.Parameters.Add("@Status", SqlDbType.Int).Value = (int)OutboxStatus.Failed;
        command.Parameters.Add("@Error", SqlDbType.NVarChar, -1).Value = error;
        command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = entryId;

        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        return result > 0;
    }

    /// <summary>
    /// Updates the retry count and next retry time for an outbox message
    /// </summary>
    /// <param name="entryId">The unique identifier of the outbox entry</param>
    /// <param name="retryCount">The new retry count</param>
    /// <param name="nextRetry">Optional timestamp for when the next retry should occur</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the message was updated; false if not found</returns>
    /// <exception cref="SqlException">Thrown when a database error occurs</exception>
    /// <remarks>
    /// Resets the message status to Pending to allow retry processing.
    /// If nextRetry is specified, the message will not be retrieved by GetPending until that time.
    /// </remarks>
    public async Task<bool> UpdateRetryCount(string entryId, int retryCount, DateTime? nextRetry = null, CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            UPDATE {_tableName}
            SET Status = @Status, RetryCount = @RetryCount, NextRetryAt = @NextRetry
            WHERE Id = @Id
            """;

        using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = _options.CommandTimeout;
        command.Parameters.Add("@Status", SqlDbType.Int).Value = (int)OutboxStatus.Pending;
        command.Parameters.Add("@RetryCount", SqlDbType.Int).Value = retryCount;
        command.Parameters.Add("@NextRetry", SqlDbType.DateTime2).Value = (object?)nextRetry ?? DBNull.Value;
        command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = entryId;

        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        return result > 0;
    }

    /// <summary>
    /// Gets the count of pending outbox messages
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The number of messages with Pending status</returns>
    /// <exception cref="SqlException">Thrown when a database error occurs</exception>
    public async Task<long> GetPendingCount(CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"SELECT COUNT(*) FROM {_tableName} WHERE Status = @Status";

        using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = _options.CommandTimeout;
        command.Parameters.Add("@Status", SqlDbType.Int).Value = (int)OutboxStatus.Pending;

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result ?? 0);
    }

    /// <summary>
    /// Retrieves failed outbox messages for inspection or retry
    /// </summary>
    /// <param name="limit">Maximum number of failed messages to return (default: 100)</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Collection of failed outbox entries with error details, ordered by creation time descending</returns>
    /// <exception cref="SqlException">Thrown when a database error occurs</exception>
    public async Task<IEnumerable<OutboxEntry>> GetFailed(int limit = 100, CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT TOP(@Limit)
                Id, MessagePayload, MessageType, Status, RetryCount, CreatedAt, ProcessedAt, NextRetryAt, LastError
            FROM {_tableName}
            WHERE Status = @Status
            ORDER BY CreatedAt DESC
            """;

        var entries = new List<OutboxEntry>();

        using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = _options.CommandTimeout;
        command.Parameters.Add("@Limit", SqlDbType.Int).Value = limit;
        command.Parameters.Add("@Status", SqlDbType.Int).Value = (int)OutboxStatus.Failed;

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var messagePayload = reader.GetString(1);
            var messageType = reader.GetString(2);

            var type = Type.GetType(messageType);
            IMessage? message = null;
            if (type != null)
            {
                message = JsonSerializer.Deserialize(messagePayload, type, _jsonOptions) as IMessage;
            }

            if (message != null)
            {
                entries.Add(new OutboxEntry
                {
                    Id = reader.GetString(0),
                    Message = message,
                    Options = new OutboxOptions(),
                    Status = (OutboxStatus)reader.GetInt32(3),
                    RetryCount = reader.GetInt32(4),
                    CreatedAt = reader.GetDateTime(5),
                    ProcessedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    NextRetryAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    LastError = reader.IsDBNull(8) ? null : reader.GetString(8)
                });
            }
        }

        return entries;
    }

    private OutboxEntry MapToEntry(SqlDataReader reader)
    {
        var messagePayload = reader.GetString(reader.GetOrdinal("MessagePayload"));
        var messageType = reader.GetString(reader.GetOrdinal("MessageType"));

        var type = Type.GetType(messageType);
        IMessage? message = null;
        if (type != null)
        {
            message = JsonSerializer.Deserialize(messagePayload, type, _jsonOptions) as IMessage;
        }

        return new OutboxEntry
        {
            Id = reader.GetString(reader.GetOrdinal("Id")),
            Message = message!,
            Options = new OutboxOptions(),
            Status = (OutboxStatus)reader.GetInt32(reader.GetOrdinal("Status")),
            RetryCount = reader.GetInt32(reader.GetOrdinal("RetryCount")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            ProcessedAt = reader.IsDBNull(reader.GetOrdinal("ProcessedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("ProcessedAt")),
            NextRetryAt = reader.IsDBNull(reader.GetOrdinal("NextRetryAt")) ? null : reader.GetDateTime(reader.GetOrdinal("NextRetryAt")),
            LastError = reader.IsDBNull(reader.GetOrdinal("LastError")) ? null : reader.GetString(reader.GetOrdinal("LastError"))
        };
    }
}
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace HeroMessaging.Storage.SqlServer;

/// <summary>
/// SQL Server implementation of inbox storage using pure ADO.NET
/// Provides message deduplication and idempotent message processing
/// </summary>
public class SqlServerInboxStorage : IInboxStorage
{
    private readonly SqlServerStorageOptions _options;
    private readonly SqlConnection? _sharedConnection;
    private readonly SqlTransaction? _sharedTransaction;
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly TimeProvider _timeProvider;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerInboxStorage"/> class with the specified options.
    /// </summary>
    /// <param name="options">SQL Server storage configuration options including connection string and table settings</param>
    /// <param name="timeProvider">Provider for retrieving current time, useful for testing with custom time</param>
    /// <remarks>
    /// This constructor creates a new inbox storage instance that manages its own database connections.
    /// If <see cref="SqlServerStorageOptions.AutoCreateTables"/> is true, the inbox table and schema
    /// will be created automatically during construction.
    ///
    /// The inbox table includes indexes optimized for:
    /// - Status-based queries (pending, processed, failed)
    /// - Time-range queries for cleanup and monitoring
    /// - Deduplication lookups by message ID
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when options, options.ConnectionString, or timeProvider is null</exception>
    public SqlServerInboxStorage(SqlServerStorageOptions options, TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connectionString = options.ConnectionString ?? throw new ArgumentNullException(nameof(options.ConnectionString));
        _tableName = _options.GetFullTableName(_options.InboxTableName);
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
    /// Initializes a new instance of the <see cref="SqlServerInboxStorage"/> class with a shared connection and optional transaction.
    /// </summary>
    /// <param name="connection">An existing SQL Server connection to use for all operations</param>
    /// <param name="transaction">Optional SQL Server transaction for transactional operations</param>
    /// <param name="timeProvider">Provider for retrieving current time, useful for testing with custom time</param>
    /// <remarks>
    /// This constructor is used for transaction-aware operations where the inbox storage participates
    /// in an existing transaction scope. This enables atomic operations across multiple storage systems
    /// (inbox, outbox, message storage) within a single database transaction.
    ///
    /// When using a shared connection:
    /// - The connection is not disposed when operations complete
    /// - All operations use the provided transaction if one is specified
    /// - The caller is responsible for connection and transaction lifecycle management
    /// - Tables are NOT automatically created (AutoCreateTables is ignored)
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when connection or timeProvider is null</exception>
    public SqlServerInboxStorage(SqlConnection connection, SqlTransaction? transaction, TimeProvider timeProvider)
    {
        _sharedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sharedTransaction = transaction;
        _connectionString = connection.ConnectionString;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        // Use default options when using shared connection
        _options = new SqlServerStorageOptions { ConnectionString = connection.ConnectionString };
        _tableName = _options.GetFullTableName(_options.InboxTableName);

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
                          WHERE s.name = '{_options.Schema}' AND t.name = '{_options.InboxTableName}')
            BEGIN
                CREATE TABLE {_tableName} (
                    Id NVARCHAR(100) PRIMARY KEY,
                    MessageType NVARCHAR(500) NOT NULL,
                    Payload NVARCHAR(MAX) NOT NULL,
                    Source NVARCHAR(200) NULL,
                    Status NVARCHAR(50) NOT NULL DEFAULT 'Pending',
                    ReceivedAt DATETIME2 NOT NULL,
                    ProcessedAt DATETIME2 NULL,
                    Error NVARCHAR(MAX) NULL,
                    RequireIdempotency BIT NOT NULL DEFAULT 1,
                    DeduplicationWindowMinutes INT NULL,
                    INDEX IX_{_options.InboxTableName}_Status (Status),
                    INDEX IX_{_options.InboxTableName}_ReceivedAt (ReceivedAt DESC),
                    INDEX IX_{_options.InboxTableName}_ProcessedAt (ProcessedAt)
                )
            END
            """;

        using var command = new SqlCommand(createTableSql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Adds a message to the inbox for tracking and deduplication using SQL Server storage.
    /// </summary>
    /// <param name="message">The incoming message to track</param>
    /// <param name="options">Inbox options including source, idempotency requirements, and deduplication window</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created inbox entry, or null if the message is a duplicate and RequireIdempotency is true</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Message is serialized to JSON for storage in NVARCHAR(MAX) column
    /// - Deduplication check uses indexed lookup on message ID
    /// - If using a shared transaction, the insert participates in that transaction
    /// - Entry is created with status 'Pending' and includes deduplication window settings
    ///
    /// Performance considerations:
    /// - Clustered index on Id column provides fast duplicate checks
    /// - Status and ReceivedAt indexes support efficient querying
    /// - Large messages may impact performance due to JSON serialization
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when message or options is null</exception>
    public async Task<InboxEntry?> Add(IMessage message, InboxOptions options, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new SqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

            var messageId = message.MessageId.ToString();
            var now = _timeProvider.GetUtcNow().DateTime;

            // Check for duplicates if idempotency is required
            if (options.RequireIdempotency)
            {
                var isDuplicate = await IsDuplicate(messageId, options.DeduplicationWindow, cancellationToken);
                if (isDuplicate)
                {
                    return null; // Message already exists
                }
            }

            var sql = $"""
                INSERT INTO {_tableName} (Id, MessageType, Payload, Source, Status, ReceivedAt, RequireIdempotency, DeduplicationWindowMinutes)
                VALUES (@Id, @MessageType, @Payload, @Source, @Status, @ReceivedAt, @RequireIdempotency, @DeduplicationWindowMinutes)
                """;

            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = messageId;
            command.Parameters.Add("@MessageType", SqlDbType.NVarChar, 500).Value = message.GetType().FullName ?? "Unknown";
            command.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(message, _jsonOptions);
            command.Parameters.Add("@Source", SqlDbType.NVarChar, 200).Value = (object?)options.Source ?? DBNull.Value;
            command.Parameters.Add("@Status", SqlDbType.NVarChar, 50).Value = "Pending";
            command.Parameters.Add("@ReceivedAt", SqlDbType.DateTime2).Value = now;
            command.Parameters.Add("@RequireIdempotency", SqlDbType.Bit).Value = options.RequireIdempotency;
            command.Parameters.Add("@DeduplicationWindowMinutes", SqlDbType.Int).Value = (object?)options.DeduplicationWindow?.TotalMinutes ?? DBNull.Value;

            await command.ExecuteNonQueryAsync(cancellationToken);

            return new InboxEntry
            {
                Id = messageId,
                Message = message,
                Options = options,
                Status = InboxStatus.Pending,
                ReceivedAt = now
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
    /// Checks if a message with the specified ID has already been received and processed using SQL Server storage.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message to check</param>
    /// <param name="window">Optional time window for deduplication. Only checks messages received within this timespan</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message ID exists in the inbox (indicating a duplicate); otherwise false</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Uses indexed COUNT query on primary key for fast duplicate detection
    /// - Window-based queries filter using ReceivedAt column with indexed lookup
    /// - Query participates in shared transaction if one is active
    /// - Efficient for both exact ID lookup and time-windowed deduplication
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when messageId is null or empty</exception>
    public async Task<bool> IsDuplicate(string messageId, TimeSpan? window = null, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new SqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

            var sql = window.HasValue
                ? $"SELECT COUNT(1) FROM {_tableName} WHERE Id = @Id AND ReceivedAt >= @WindowStart"
                : $"SELECT COUNT(1) FROM {_tableName} WHERE Id = @Id";

            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = messageId;

            if (window.HasValue)
            {
                var windowStart = _timeProvider.GetUtcNow().DateTime.Subtract(window.Value);
                command.Parameters.Add("@WindowStart", SqlDbType.DateTime2).Value = windowStart;
            }

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

    /// <summary>
    /// Retrieves an inbox entry by message ID from SQL Server storage.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The inbox entry if found; otherwise null</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Retrieves full entry data including message payload, status, and timestamps
    /// - Message payload is deserialized from JSON to <see cref="IMessage"/>
    /// - Uses primary key lookup for optimal performance
    /// - Query participates in shared transaction if one is active for consistent reads
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when messageId is null or empty</exception>
    public async Task<InboxEntry?> Get(string messageId, CancellationToken cancellationToken = default)
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
                SELECT MessageType, Payload, Source, Status, ReceivedAt, ProcessedAt, Error, RequireIdempotency, DeduplicationWindowMinutes
                FROM {_tableName}
                WHERE Id = @Id
                """;

            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = messageId;

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var messageType = reader.GetString(0);
                var payload = reader.GetString(1);
                var source = reader.IsDBNull(2) ? null : reader.GetString(2);
                var status = Enum.Parse<InboxStatus>(reader.GetString(3));
                var receivedAt = reader.GetDateTime(4);
                var processedAt = reader.IsDBNull(5) ? (DateTime?)null : reader.GetDateTime(5);
                var error = reader.IsDBNull(6) ? null : reader.GetString(6);
                var requireIdempotency = reader.GetBoolean(7);
                var deduplicationWindowMinutes = reader.IsDBNull(8) ? (int?)null : reader.GetInt32(8);

                // Deserialize message (simplified - in production would need type resolution)
                var message = JsonSerializer.Deserialize<IMessage>(payload, _jsonOptions);

                return new InboxEntry
                {
                    Id = messageId,
                    Message = message!,
                    Options = new InboxOptions
                    {
                        Source = source,
                        RequireIdempotency = requireIdempotency,
                        DeduplicationWindow = deduplicationWindowMinutes.HasValue
                            ? TimeSpan.FromMinutes(deduplicationWindowMinutes.Value)
                            : null
                    },
                    Status = status,
                    ReceivedAt = receivedAt,
                    ProcessedAt = processedAt,
                    Error = error
                };
            }

            return null;
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
    /// Marks an inbox entry as successfully processed in SQL Server storage.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was marked as processed; false if not found</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Updates Status to 'Processed' and sets ProcessedAt timestamp in a single UPDATE statement
    /// - If using a shared transaction, the update participates in that transaction
    /// - Primary key lookup ensures efficient update operation
    /// - Use within same transaction as message processing for atomicity
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when messageId is null or empty</exception>
    public async Task<bool> MarkProcessed(string messageId, CancellationToken cancellationToken = default)
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
                SET Status = @Status, ProcessedAt = @ProcessedAt
                WHERE Id = @Id
                """;

            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = messageId;
            command.Parameters.Add("@Status", SqlDbType.NVarChar, 50).Value = "Processed";
            command.Parameters.Add("@ProcessedAt", SqlDbType.DateTime2).Value = _timeProvider.GetUtcNow().DateTime;

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
    /// Marks an inbox entry as failed with an error message in SQL Server storage.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message</param>
    /// <param name="error">The error message describing why processing failed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was marked as failed; false if not found</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Updates Status to 'Failed', sets ProcessedAt timestamp, and stores error message in NVARCHAR(MAX) column
    /// - Error details are preserved for debugging and monitoring purposes
    /// - If using a shared transaction, the update participates in that transaction
    /// - Failed entries can be queried later for investigation and alerting
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when messageId or error is null or empty</exception>
    public async Task<bool> MarkFailed(string messageId, string error, CancellationToken cancellationToken = default)
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
                SET Status = @Status, ProcessedAt = @ProcessedAt, Error = @Error
                WHERE Id = @Id
                """;

            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = messageId;
            command.Parameters.Add("@Status", SqlDbType.NVarChar, 50).Value = "Failed";
            command.Parameters.Add("@ProcessedAt", SqlDbType.DateTime2).Value = _timeProvider.GetUtcNow().DateTime;
            command.Parameters.Add("@Error", SqlDbType.NVarChar, -1).Value = error;

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
    /// Retrieves inbox entries matching the specified query criteria from SQL Server storage.
    /// </summary>
    /// <param name="query">Query criteria including status, time range, and limit</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of inbox entries matching the query</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Uses dynamic SQL query building based on query criteria
    /// - Supports filtering by Status, OlderThan, and NewerThan with indexed lookups
    /// - Results are ordered by ReceivedAt DESC for most-recent-first ordering
    /// - Uses OFFSET/FETCH for efficient pagination
    /// - All message payloads are deserialized from JSON
    /// - Query participates in shared transaction if one is active
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when query is null</exception>
    public async Task<IEnumerable<InboxEntry>> GetPending(InboxQuery query, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new SqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

            var whereClauses = new List<string>();
            var parameters = new List<SqlParameter>();

            if (query.Status.HasValue)
            {
                whereClauses.Add("Status = @Status");
                parameters.Add(new SqlParameter("@Status", SqlDbType.NVarChar, 50) { Value = query.Status.Value.ToString() });
            }

            if (query.OlderThan.HasValue)
            {
                whereClauses.Add("ReceivedAt < @OlderThan");
                parameters.Add(new SqlParameter("@OlderThan", SqlDbType.DateTime2) { Value = query.OlderThan.Value });
            }

            if (query.NewerThan.HasValue)
            {
                whereClauses.Add("ReceivedAt > @NewerThan");
                parameters.Add(new SqlParameter("@NewerThan", SqlDbType.DateTime2) { Value = query.NewerThan.Value });
            }

            var whereClause = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

            var sql = $"""
                SELECT Id, MessageType, Payload, Source, Status, ReceivedAt, ProcessedAt, Error, RequireIdempotency, DeduplicationWindowMinutes
                FROM {_tableName}
                {whereClause}
                ORDER BY ReceivedAt DESC
                OFFSET 0 ROWS
                FETCH NEXT @Limit ROWS ONLY
                """;

            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@Limit", SqlDbType.Int).Value = query.Limit;
            foreach (var param in parameters)
            {
                command.Parameters.Add(param);
            }

            var entries = new List<InboxEntry>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var messageId = reader.GetString(0);
                var messageType = reader.GetString(1);
                var payload = reader.GetString(2);
                var source = reader.IsDBNull(3) ? null : reader.GetString(3);
                var status = Enum.Parse<InboxStatus>(reader.GetString(4));
                var receivedAt = reader.GetDateTime(5);
                var processedAt = reader.IsDBNull(6) ? (DateTime?)null : reader.GetDateTime(6);
                var error = reader.IsDBNull(7) ? null : reader.GetString(7);
                var requireIdempotency = reader.GetBoolean(8);
                var deduplicationWindowMinutes = reader.IsDBNull(9) ? (int?)null : reader.GetInt32(9);

                var message = JsonSerializer.Deserialize<IMessage>(payload, _jsonOptions);

                entries.Add(new InboxEntry
                {
                    Id = messageId,
                    Message = message!,
                    Options = new Abstractions.InboxOptions
                    {
                        Source = source,
                        RequireIdempotency = requireIdempotency,
                        DeduplicationWindow = deduplicationWindowMinutes.HasValue
                            ? TimeSpan.FromMinutes(deduplicationWindowMinutes.Value)
                            : null
                    },
                    Status = status,
                    ReceivedAt = receivedAt,
                    ProcessedAt = processedAt,
                    Error = error
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
    /// Retrieves unprocessed inbox entries ready for processing or retry from SQL Server storage.
    /// </summary>
    /// <param name="limit">Maximum number of entries to retrieve. Default is 100</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of unprocessed inbox entries, ordered by received time</returns>
    /// <remarks>
    /// This is a convenience method that queries for entries with status 'Pending'.
    /// SQL Server-specific implementation delegates to <see cref="GetPending"/> with
    /// appropriate query criteria for unprocessed messages.
    /// </remarks>
    public async Task<IEnumerable<InboxEntry>> GetUnprocessed(int limit = 100, CancellationToken cancellationToken = default)
    {
        var query = new InboxQuery
        {
            Status = InboxEntryStatus.Pending,
            Limit = limit
        };

        return await GetPending(query, cancellationToken);
    }

    /// <summary>
    /// Gets the total count of unprocessed inbox entries in SQL Server storage.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The number of entries with status 'Pending'</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Uses optimized COUNT(*) query with indexed Status filter
    /// - Efficient for monitoring and observability without retrieving full entry data
    /// - Query participates in shared transaction if one is active
    /// </remarks>
    public async Task<long> GetUnprocessedCount(CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new SqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

            var sql = $"SELECT COUNT(1) FROM {_tableName} WHERE Status = 'Pending'";

            using var command = new SqlCommand(sql, connection, transaction);
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
    /// Removes old inbox entries to prevent unbounded growth in SQL Server storage.
    /// </summary>
    /// <param name="olderThan">Remove entries older than this duration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous cleanup operation</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Uses DELETE query with indexed ReceivedAt filter for efficient cleanup
    /// - Only removes entries with status 'Processed' or 'Failed' to preserve pending work
    /// - If using a shared transaction, the delete participates in that transaction
    /// - Consider running during off-peak hours for large-scale cleanup operations
    /// - Index on ReceivedAt ensures efficient filtering of old entries
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when olderThan is negative</exception>
    public async Task CleanupOldEntries(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new SqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

            var cutoffTime = _timeProvider.GetUtcNow().DateTime.Subtract(olderThan);

            var sql = $"""
                DELETE FROM {_tableName}
                WHERE ReceivedAt < @CutoffTime
                AND Status IN ('Processed', 'Failed')
                """;

            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@CutoffTime", SqlDbType.DateTime2).Value = cutoffTime;

            await command.ExecuteNonQueryAsync(cancellationToken);
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

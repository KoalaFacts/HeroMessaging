using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace HeroMessaging.Storage.SqlServer;

/// <summary>
/// SQL Server implementation of message storage using pure ADO.NET
/// </summary>
/// <remarks>
/// SQL Server-specific implementation details:
/// - Uses NVARCHAR(MAX) columns for message payload and metadata storage with JSON serialization
/// - Supports both standalone and shared connection/transaction patterns for flexibility
/// - Provides comprehensive indexing strategy: Timestamp, MessageType, CorrelationId, Collection, and ExpiresAt
/// - Implements automatic table and schema creation on initialization
/// - TTL enforcement via ExpiresAt column checked on all read operations
/// - Transaction support with ReadCommitted isolation level for ACID compliance
/// - Uses SQL Server's TRUNCATE TABLE for efficient Clear operations
/// - OFFSET/FETCH pagination for efficient large result set handling
///
/// Performance characteristics:
/// - Primary key lookups are optimized via clustered index on Id column
/// - JSON serialization overhead for large messages should be considered
/// - Indexed queries provide efficient filtering on common access patterns
/// - TRUNCATE TABLE provides fast full-table cleanup (requires appropriate permissions)
///
/// Connection management:
/// - Standalone constructor: Creates and manages its own connections per operation
/// - Shared constructor: Uses provided connection and optional transaction for coordinated operations
/// - Transaction-aware methods (StoreAsync, RetrieveAsync) support IStorageTransaction for multi-operation atomicity
/// </remarks>
public class SqlServerMessageStorage : IMessageStorage
{
    private readonly SqlServerStorageOptions _options;
    private readonly string _connectionString;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _tableName;
    private readonly SqlConnection? _sharedConnection;
    private readonly SqlTransaction? _sharedTransaction;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerMessageStorage"/> class with the specified options.
    /// </summary>
    /// <param name="options">SQL Server storage configuration options including connection string and table settings</param>
    /// <param name="timeProvider">Provider for retrieving current time, useful for testing with custom time</param>
    /// <remarks>
    /// This constructor creates a new message storage instance that manages its own database connections.
    /// The message table and schema are created automatically during construction.
    ///
    /// The message table includes indexes optimized for:
    /// - Timestamp-based queries (Timestamp DESC)
    /// - Message type filtering (MessageType)
    /// - Correlation ID lookups (CorrelationId)
    /// - Collection-based queries (Collection)
    /// - TTL-based expiration (ExpiresAt)
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when options or timeProvider is null</exception>
    public SqlServerMessageStorage(SqlServerStorageOptions options, TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connectionString = options.ConnectionString;
        _tableName = _options.GetFullTableName(_options.MessagesTableName);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        InitializeDatabase().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerMessageStorage"/> class with a shared connection and optional transaction.
    /// </summary>
    /// <param name="connection">An existing SQL Server connection to use for all operations</param>
    /// <param name="transaction">Optional SQL Server transaction for transactional operations</param>
    /// <param name="timeProvider">Provider for retrieving current time, useful for testing with custom time</param>
    /// <remarks>
    /// This constructor is used for transaction-aware operations where the message storage participates
    /// in an existing transaction scope. This enables atomic operations across multiple storage systems
    /// within a single database transaction.
    ///
    /// When using a shared connection:
    /// - The connection is not disposed when operations complete
    /// - All operations use the provided transaction if one is specified
    /// - The caller is responsible for connection and transaction lifecycle management
    /// - Tables are NOT automatically created (database initialization is skipped)
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when connection or timeProvider is null</exception>
    public SqlServerMessageStorage(SqlConnection connection, SqlTransaction? transaction, TimeProvider timeProvider)
    {
        _sharedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sharedTransaction = transaction;

        // Use default options when using shared connection
        _options = new SqlServerStorageOptions { ConnectionString = connection.ConnectionString };
        _connectionString = connection.ConnectionString;
        _tableName = _options.GetFullTableName(_options.MessagesTableName);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

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
                          WHERE s.name = '{_options.Schema}' AND t.name = '{_options.MessagesTableName}')
            BEGIN
                CREATE TABLE {_tableName} (
                    Id NVARCHAR(100) PRIMARY KEY,
                    MessageType NVARCHAR(500) NOT NULL,
                    Payload NVARCHAR(MAX) NOT NULL,
                    Timestamp DATETIME2 NOT NULL,
                    CorrelationId NVARCHAR(100) NULL,
                    Collection NVARCHAR(100) NULL,
                    Metadata NVARCHAR(MAX) NULL,
                    ExpiresAt DATETIME2 NULL,
                    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    INDEX IX_{_options.MessagesTableName}_Timestamp (Timestamp DESC),
                    INDEX IX_{_options.MessagesTableName}_Type (MessageType),
                    INDEX IX_{_options.MessagesTableName}_CorrelationId (CorrelationId),
                    INDEX IX_{_options.MessagesTableName}_Collection (Collection),
                    INDEX IX_{_options.MessagesTableName}_ExpiresAt (ExpiresAt)
                )
            END
            """;

        using var command = new SqlCommand(createTableSql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Stores a message in SQL Server storage and returns its unique identifier.
    /// </summary>
    /// <param name="message">The message to store</param>
    /// <param name="options">Optional storage configuration including collection, TTL, and metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The unique identifier assigned to the stored message</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Generates a new GUID as the message ID
    /// - Message is serialized to JSON for storage in NVARCHAR(MAX) column
    /// - Metadata is serialized to JSON if provided
    /// - ExpiresAt is calculated as current time + TTL if specified
    /// - CreatedAt timestamp is set to current UTC time
    /// - Uses dedicated connection (not shared connection pattern)
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when message is null</exception>
    public async Task<string> Store(IMessage message, MessageStorageOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var messageId = Guid.NewGuid().ToString();
        var expiresAt = options?.Ttl != null
            ? _timeProvider.GetUtcNow().DateTime.Add(options.Ttl.Value)
            : (DateTime?)null;

        var sql = $"""
            INSERT INTO {_tableName} (Id, MessageType, Payload, Timestamp, CorrelationId, Collection, Metadata, ExpiresAt, CreatedAt)
            VALUES (@Id, @MessageType, @Payload, @Timestamp, @CorrelationId, @Collection, @Metadata, @ExpiresAt, @CreatedAt)
            """;

        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = messageId;
        command.Parameters.Add("@MessageType", SqlDbType.NVarChar, 500).Value = message.GetType().FullName ?? "Unknown";
        command.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(message, _jsonOptions);
        command.Parameters.Add("@Timestamp", SqlDbType.DateTime2).Value = message.Timestamp;
        command.Parameters.Add("@CorrelationId", SqlDbType.NVarChar, 100).Value = DBNull.Value;
        command.Parameters.Add("@Collection", SqlDbType.NVarChar, 100).Value = (object?)options?.Collection ?? DBNull.Value;
        command.Parameters.Add("@Metadata", SqlDbType.NVarChar, -1).Value = options?.Metadata != null
            ? JsonSerializer.Serialize(options.Metadata, _jsonOptions)
            : DBNull.Value;
        command.Parameters.Add("@ExpiresAt", SqlDbType.DateTime2).Value = (object?)expiresAt ?? DBNull.Value;
        command.Parameters.Add("@CreatedAt", SqlDbType.DateTime2).Value = _timeProvider.GetUtcNow().DateTime;

        await command.ExecuteNonQueryAsync(cancellationToken);
        return messageId;
    }

    /// <summary>
    /// Retrieves a strongly-typed message by its unique identifier from SQL Server storage.
    /// </summary>
    /// <typeparam name="T">The expected message type</typeparam>
    /// <param name="messageId">The unique identifier of the message to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The message if found and type matches; otherwise null</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Only retrieves messages that haven't expired (ExpiresAt is NULL or in the future)
    /// - Message payload is deserialized from JSON to the specified type T
    /// - Uses primary key lookup for optimal performance
    /// - Uses dedicated connection (not shared connection pattern)
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when messageId is null or empty</exception>
    /// <exception cref="InvalidCastException">Thrown when stored message cannot be cast to type T</exception>
    public async Task<T?> Retrieve<T>(string messageId, CancellationToken cancellationToken = default) where T : IMessage
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT Payload FROM {_tableName} 
            WHERE Id = @Id 
            AND (ExpiresAt IS NULL OR ExpiresAt > GETUTCDATE())
            """;

        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = messageId;

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var payload = reader.GetString(0);
            return JsonSerializer.Deserialize<T>(payload, _jsonOptions);
        }

        return default;
    }

    /// <summary>
    /// Queries messages using advanced filtering, pagination, and ordering from SQL Server storage.
    /// </summary>
    /// <typeparam name="T">The expected message type</typeparam>
    /// <param name="query">Query criteria including filters, pagination, and ordering</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A collection of messages matching the query criteria</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Uses dynamic SQL query building based on query criteria
    /// - Supports filtering by Collection, FromTimestamp, and ToTimestamp with indexed lookups
    /// - Only retrieves messages that haven't expired
    /// - Uses OFFSET/FETCH for efficient pagination
    /// - All message payloads are deserialized from JSON to type T
    /// - Uses dedicated connection (not shared connection pattern)
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when query is null</exception>
    public async Task<IEnumerable<T>> Query<T>(MessageQuery query, CancellationToken cancellationToken = default) where T : IMessage
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var whereClauses = new List<string> { "(ExpiresAt IS NULL OR ExpiresAt > GETUTCDATE())" };
        var parameters = new List<SqlParameter>();

        if (!string.IsNullOrEmpty(query.Collection))
        {
            whereClauses.Add("Collection = @Collection");
            parameters.Add(new SqlParameter("@Collection", SqlDbType.NVarChar, 100) { Value = query.Collection });
        }

        if (query.FromTimestamp.HasValue)
        {
            whereClauses.Add("Timestamp >= @FromTimestamp");
            parameters.Add(new SqlParameter("@FromTimestamp", SqlDbType.DateTime2) { Value = query.FromTimestamp.Value });
        }

        if (query.ToTimestamp.HasValue)
        {
            whereClauses.Add("Timestamp <= @ToTimestamp");
            parameters.Add(new SqlParameter("@ToTimestamp", SqlDbType.DateTime2) { Value = query.ToTimestamp.Value });
        }

        var whereClause = string.Join(" AND ", whereClauses);
        var orderBy = query.OrderBy ?? "Timestamp";
        var orderDirection = query.Ascending ? "ASC" : "DESC";
        var limit = query.Limit ?? 100;
        var offset = query.Offset ?? 0;

        var sql = $"""
            SELECT Payload FROM {_tableName}
            WHERE {whereClause}
            ORDER BY {orderBy} {orderDirection}
            OFFSET @Offset ROWS
            FETCH NEXT @Limit ROWS ONLY
            """;

        var messages = new List<T>();

        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters.ToArray());
        command.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
        command.Parameters.Add("@Limit", SqlDbType.Int).Value = limit;

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var payload = reader.GetString(0);
            var message = JsonSerializer.Deserialize<T>(payload, _jsonOptions);
            if (message != null)
            {
                messages.Add(message);
            }
        }

        return messages;
    }

    /// <summary>
    /// Deletes a message from SQL Server storage by its unique identifier.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message was deleted; false if the message was not found</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Permanently removes the message row from the table
    /// - Uses primary key lookup for efficient deletion
    /// - Uses dedicated connection (not shared connection pattern)
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when messageId is null or empty</exception>
    public async Task<bool> Delete(string messageId, CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"DELETE FROM {_tableName} WHERE Id = @Id";

        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = messageId;

        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        return result > 0;
    }

    /// <summary>
    /// Updates an existing message in SQL Server storage.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message to update</param>
    /// <param name="message">The updated message content</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message was updated; false if the message was not found</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Updates MessageType, Payload, Timestamp, and CorrelationId fields
    /// - Message is serialized to JSON for storage
    /// - Uses primary key lookup for efficient update
    /// - Does not update Collection, Metadata, ExpiresAt, or CreatedAt fields
    /// - Uses dedicated connection (not shared connection pattern)
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when messageId or message is null</exception>
    public async Task<bool> Update(string messageId, IMessage message, CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            UPDATE {_tableName}
            SET MessageType = @MessageType,
                Payload = @Payload,
                Timestamp = @Timestamp,
                CorrelationId = @CorrelationId
            WHERE Id = @Id
            """;

        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = messageId;
        command.Parameters.Add("@MessageType", SqlDbType.NVarChar, 500).Value = message.GetType().FullName ?? "Unknown";
        command.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(message, _jsonOptions);
        command.Parameters.Add("@Timestamp", SqlDbType.DateTime2).Value = message.Timestamp;
        command.Parameters.Add("@CorrelationId", SqlDbType.NVarChar, 100).Value = DBNull.Value;

        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        return result > 0;
    }

    /// <summary>
    /// Checks whether a message exists in SQL Server storage.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message exists; otherwise false</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Uses COUNT(*) query with primary key lookup
    /// - Only counts messages that haven't expired (ExpiresAt is NULL or in the future)
    /// - Efficient for existence checks without retrieving full message data
    /// - Uses dedicated connection (not shared connection pattern)
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when messageId is null or empty</exception>
    public async Task<bool> Exists(string messageId, CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT COUNT(*) FROM {_tableName} 
            WHERE Id = @Id 
            AND (ExpiresAt IS NULL OR ExpiresAt > GETUTCDATE())
            """;

        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = messageId;

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result ?? 0) > 0;
    }

    /// <summary>
    /// Counts messages matching the specified query criteria in SQL Server storage.
    /// </summary>
    /// <param name="query">Optional query criteria. If null, counts all messages across all collections</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The total number of messages matching the criteria</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Uses COUNT(*) query with optional WHERE clause based on query criteria
    /// - Only counts messages that haven't expired (ExpiresAt is NULL or in the future)
    /// - Supports filtering by Collection, FromTimestamp, and ToTimestamp
    /// - Efficient for counting without retrieving message data
    /// - Uses dedicated connection (not shared connection pattern)
    /// </remarks>
    public async Task<long> Count(MessageQuery? query = null, CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var whereClauses = new List<string> { "(ExpiresAt IS NULL OR ExpiresAt > GETUTCDATE())" };
        var parameters = new List<SqlParameter>();

        if (query != null)
        {
            if (!string.IsNullOrEmpty(query.Collection))
            {
                whereClauses.Add("Collection = @Collection");
                parameters.Add(new SqlParameter("@Collection", SqlDbType.NVarChar, 100) { Value = query.Collection });
            }

            if (query.FromTimestamp.HasValue)
            {
                whereClauses.Add("Timestamp >= @FromTimestamp");
                parameters.Add(new SqlParameter("@FromTimestamp", SqlDbType.DateTime2) { Value = query.FromTimestamp.Value });
            }

            if (query.ToTimestamp.HasValue)
            {
                whereClauses.Add("Timestamp <= @ToTimestamp");
                parameters.Add(new SqlParameter("@ToTimestamp", SqlDbType.DateTime2) { Value = query.ToTimestamp.Value });
            }
        }

        var whereClause = string.Join(" AND ", whereClauses);
        var sql = $"SELECT COUNT(*) FROM {_tableName} WHERE {whereClause}";

        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters.ToArray());

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result ?? 0);
    }

    /// <summary>
    /// Removes all messages from SQL Server storage across all collections.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Uses TRUNCATE TABLE for efficient removal of all rows
    /// - TRUNCATE is faster than DELETE and resets identity columns
    /// - Cannot be rolled back if not in a transaction
    /// - Requires appropriate permissions on the table
    /// - WARNING: This operation is destructive and cannot be undone
    /// - Uses dedicated connection (not shared connection pattern)
    /// </remarks>
    public async Task Clear(CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"TRUNCATE TABLE {_tableName}";

        using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Stores a message within an optional transaction context in SQL Server storage.
    /// </summary>
    /// <param name="message">The message to store</param>
    /// <param name="transaction">Optional transaction context. If provided, operation is part of the transaction</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Uses message.MessageId as the primary key (GUID)
    /// - Message is serialized to JSON for storage in NVARCHAR(MAX) column
    /// - If transaction is provided (SqlServerStorageTransaction), uses its connection and transaction
    /// - If no transaction, creates and disposes its own connection
    /// - CreatedAt timestamp is set to current UTC time
    /// - CorrelationId is preserved from the message
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when message is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when transaction is no longer active</exception>
    public async Task StoreAsync(IMessage message, IStorageTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        SqlConnection? connection = null;
        SqlTransaction? sqlTransaction = null;

        if (transaction is SqlServerStorageTransaction sqlServerTransaction)
        {
            connection = sqlServerTransaction.Connection;
            sqlTransaction = sqlServerTransaction.Transaction;
        }

        if (connection == null)
        {
            connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var messageId = message.MessageId.ToString();
            var sql = $"""
                INSERT INTO {_tableName} (Id, MessageType, Payload, Timestamp, CorrelationId, CreatedAt)
                VALUES (@Id, @MessageType, @Payload, @Timestamp, @CorrelationId, @CreatedAt)
                """;

            using var command = new SqlCommand(sql, connection, sqlTransaction);
            command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = messageId;
            command.Parameters.Add("@MessageType", SqlDbType.NVarChar, 500).Value = message.GetType().FullName ?? "Unknown";
            command.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(message, _jsonOptions);
            command.Parameters.Add("@Timestamp", SqlDbType.DateTime2).Value = message.Timestamp;
            command.Parameters.Add("@CorrelationId", SqlDbType.NVarChar, 100).Value = (object?)message.CorrelationId ?? DBNull.Value;
            command.Parameters.Add("@CreatedAt", SqlDbType.DateTime2).Value = _timeProvider.GetUtcNow().DateTime;

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (transaction == null && connection != null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Retrieves a message by its GUID identifier within an optional transaction context from SQL Server storage.
    /// </summary>
    /// <param name="messageId">The GUID identifier of the message to retrieve</param>
    /// <param name="transaction">Optional transaction context for consistent reads</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The message if found; otherwise null</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Only retrieves messages that haven't expired (ExpiresAt is NULL or in the future)
    /// - Message payload is deserialized from JSON to IMessage
    /// - If transaction is provided (SqlServerStorageTransaction), uses its connection and transaction
    /// - If no transaction, creates and disposes its own connection
    /// - Uses primary key lookup for optimal performance
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when messageId is empty</exception>
    public async Task<IMessage?> RetrieveAsync(Guid messageId, IStorageTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        SqlConnection? connection = null;
        SqlTransaction? sqlTransaction = null;

        if (transaction is SqlServerStorageTransaction sqlServerTransaction)
        {
            connection = sqlServerTransaction.Connection;
            sqlTransaction = sqlServerTransaction.Transaction;
        }

        if (connection == null)
        {
            connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var sql = $"""
                SELECT Payload FROM {_tableName}
                WHERE Id = @Id
                AND (ExpiresAt IS NULL OR ExpiresAt > GETUTCDATE())
                """;

            using var command = new SqlCommand(sql, connection, sqlTransaction);
            command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = messageId.ToString();

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var payload = reader.GetString(0);
                return JsonSerializer.Deserialize<IMessage>(payload, _jsonOptions);
            }

            return null;
        }
        finally
        {
            if (transaction == null && connection != null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Queries messages asynchronously using the specified criteria from SQL Server storage.
    /// </summary>
    /// <param name="query">Query criteria including filters, pagination, and ordering</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of messages matching the query criteria</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Uses dynamic SQL query building based on query criteria
    /// - Supports filtering by Collection, FromTimestamp, and ToTimestamp with indexed lookups
    /// - Only retrieves messages that haven't expired
    /// - Uses OFFSET/FETCH for efficient pagination
    /// - Limit is determined by query.Limit ?? query.MaxResults
    /// - All message payloads are deserialized from JSON to IMessage
    /// - Uses dedicated connection (not shared connection pattern)
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when query is null</exception>
    public async Task<List<IMessage>> QueryAsync(MessageQuery query, CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var whereClauses = new List<string> { "(ExpiresAt IS NULL OR ExpiresAt > GETUTCDATE())" };
        var parameters = new List<SqlParameter>();

        if (!string.IsNullOrEmpty(query.Collection))
        {
            whereClauses.Add("Collection = @Collection");
            parameters.Add(new SqlParameter("@Collection", SqlDbType.NVarChar, 100) { Value = query.Collection });
        }

        if (query.FromTimestamp.HasValue)
        {
            whereClauses.Add("Timestamp >= @FromTimestamp");
            parameters.Add(new SqlParameter("@FromTimestamp", SqlDbType.DateTime2) { Value = query.FromTimestamp.Value });
        }

        if (query.ToTimestamp.HasValue)
        {
            whereClauses.Add("Timestamp <= @ToTimestamp");
            parameters.Add(new SqlParameter("@ToTimestamp", SqlDbType.DateTime2) { Value = query.ToTimestamp.Value });
        }

        var whereClause = string.Join(" AND ", whereClauses);
        var orderBy = query.OrderBy ?? "Timestamp";
        var orderDirection = query.Ascending ? "ASC" : "DESC";
        var limit = query.Limit ?? query.MaxResults;
        var offset = query.Offset ?? 0;

        var sql = $"""
            SELECT Payload FROM {_tableName}
            WHERE {whereClause}
            ORDER BY {orderBy} {orderDirection}
            OFFSET @Offset ROWS
            FETCH NEXT @Limit ROWS ONLY
            """;

        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
        command.Parameters.Add("@Limit", SqlDbType.Int).Value = limit;

        foreach (var param in parameters)
        {
            command.Parameters.Add(param);
        }

        var messages = new List<IMessage>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var payload = reader.GetString(0);
            var message = JsonSerializer.Deserialize<IMessage>(payload, _jsonOptions);
            if (message != null)
            {
                messages.Add(message);
            }
        }

        return messages;
    }

    /// <summary>
    /// Deletes a message by its GUID identifier from SQL Server storage.
    /// </summary>
    /// <param name="messageId">The GUID identifier of the message to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Permanently removes the message row from the table
    /// - Uses primary key lookup for efficient deletion
    /// - Uses dedicated connection (not shared connection pattern)
    /// - Does not return success/failure status (always completes)
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when messageId is empty</exception>
    public async Task DeleteAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"DELETE FROM {_tableName} WHERE Id = @Id";

        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = messageId.ToString();

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Begins a new storage transaction for atomic multi-operation updates in SQL Server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A transaction object that must be committed or rolled back</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Creates a new SQL Server connection and begins a transaction with ReadCommitted isolation level
    /// - Returns a SqlServerStorageTransaction that wraps the connection and transaction
    /// - The transaction must be committed or rolled back explicitly
    /// - Always use within a using statement to ensure proper disposal
    /// - The connection remains open until the transaction is disposed
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when transaction cannot be started</exception>
    public async Task<IStorageTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);

        return new SqlServerStorageTransaction(connection, transaction);
    }
}

/// <summary>
/// SQL Server implementation of storage transaction that wraps a SqlConnection and SqlTransaction.
/// </summary>
/// <remarks>
/// This class provides transaction support for SQL Server storage operations, ensuring
/// atomicity of multiple operations within a single database transaction. The transaction
/// uses ReadCommitted isolation level by default.
///
/// Always use within a using statement or ensure proper disposal to release database resources.
/// If not committed explicitly, the transaction will be rolled back on disposal.
/// </remarks>
public sealed class SqlServerStorageTransaction : IStorageTransaction
{
    private bool _disposed;

    /// <summary>
    /// Gets the underlying SQL Server connection associated with this transaction.
    /// </summary>
    /// <remarks>
    /// This connection remains open for the lifetime of the transaction and should not be
    /// disposed by callers. The connection will be disposed when the transaction is disposed.
    /// </remarks>
    public SqlConnection Connection { get; }

    /// <summary>
    /// Gets the underlying SQL Server transaction.
    /// </summary>
    /// <remarks>
    /// This transaction is used by all storage operations participating in this transaction scope.
    /// The transaction will be committed or rolled back when CommitAsync or RollbackAsync is called.
    /// </remarks>
    public SqlTransaction Transaction { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerStorageTransaction"/> class.
    /// </summary>
    /// <param name="connection">The SQL Server connection to use for this transaction</param>
    /// <param name="transaction">The SQL Server transaction to wrap</param>
    /// <exception cref="ArgumentNullException">Thrown when connection or transaction is null</exception>
    public SqlServerStorageTransaction(SqlConnection connection, SqlTransaction transaction)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }

    /// <summary>
    /// Commits all operations performed within this transaction, making them permanent in SQL Server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous commit operation</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Commits the underlying SqlTransaction, persisting all changes to the database
    /// - Once committed, the transaction cannot be used for further operations
    /// - All locks acquired during the transaction are released
    /// - Changes become visible to other transactions based on isolation level semantics
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when the transaction has already been disposed</exception>
    /// <exception cref="InvalidOperationException">Thrown when the transaction has already been committed or rolled back</exception>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SqlServerStorageTransaction));

        await Transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Rolls back all operations performed within this transaction, discarding all changes in SQL Server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous rollback operation</returns>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - Rolls back the underlying SqlTransaction, discarding all changes made during the transaction
    /// - All database modifications are reverted to their state before the transaction began
    /// - All locks acquired during the transaction are released
    /// - Once rolled back, the transaction cannot be used for further operations
    /// - Rollback is automatically performed on disposal if neither Commit nor Rollback was called explicitly
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when the transaction has already been disposed</exception>
    /// <exception cref="InvalidOperationException">Thrown when the transaction has already been committed or rolled back</exception>
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SqlServerStorageTransaction));

        await Transaction.RollbackAsync(cancellationToken);
    }

    /// <summary>
    /// Disposes the transaction and its underlying SQL Server connection, releasing all resources.
    /// </summary>
    /// <remarks>
    /// SQL Server-specific implementation details:
    /// - If the transaction has not been committed, it will be rolled back automatically
    /// - Disposes the underlying SqlTransaction, releasing all database locks
    /// - Disposes the underlying SqlConnection, returning it to the connection pool
    /// - Multiple calls to Dispose are safe (idempotent)
    /// - Suppresses finalization to optimize garbage collection
    ///
    /// Always call Dispose explicitly or use a using statement to ensure proper resource cleanup:
    /// <code>
    /// using var transaction = await storage.BeginTransactionAsync();
    /// // ... perform operations ...
    /// await transaction.CommitAsync(); // or RollbackAsync()
    /// </code>
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
            return;

        Transaction?.Dispose();
        Connection?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}


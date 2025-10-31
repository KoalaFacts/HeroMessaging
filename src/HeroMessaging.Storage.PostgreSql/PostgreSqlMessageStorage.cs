using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Npgsql;
using System.Data;
using System.Text.Json;

namespace HeroMessaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of message storage using pure ADO.NET
/// </summary>
/// <remarks>
/// Provides persistent storage for messages with support for collections, metadata, TTL,
/// advanced querying, and transactional operations. Messages are stored as JSONB for
/// efficient querying and indexing.
/// Supports both standalone usage and participation in unit of work patterns.
/// </remarks>
public class PostgreSqlMessageStorage : IMessageStorage
{
    private readonly PostgreSqlStorageOptions _options;
    private readonly NpgsqlConnection? _sharedConnection;
    private readonly NpgsqlTransaction? _sharedTransaction;
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly TimeProvider _timeProvider;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of PostgreSQL message storage with independent connection management
    /// </summary>
    /// <param name="options">Configuration options for PostgreSQL storage including connection string and table names</param>
    /// <param name="timeProvider">Time provider for testable time-based operations</param>
    /// <exception cref="ArgumentNullException">Thrown when options or timeProvider is null</exception>
    /// <remarks>
    /// This constructor creates a message storage instance that manages its own database connections.
    /// If AutoCreateTables is enabled in options, database schema is initialized synchronously.
    /// Supports message collections, TTL-based expiration, and metadata indexing.
    /// </remarks>
    public PostgreSqlMessageStorage(PostgreSqlStorageOptions options, TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connectionString = options.ConnectionString ?? throw new ArgumentNullException(nameof(options.ConnectionString));
        _tableName = _options.GetFullTableName(_options.MessagesTableName);
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
    /// Initializes a new instance of PostgreSQL message storage using a shared connection and transaction
    /// </summary>
    /// <param name="connection">Shared NpgsqlConnection to use for all operations</param>
    /// <param name="transaction">Optional shared transaction for participating in unit of work</param>
    /// <param name="timeProvider">Time provider for testable time-based operations</param>
    /// <exception cref="ArgumentNullException">Thrown when connection or timeProvider is null</exception>
    /// <remarks>
    /// This constructor is used when the message storage participates in a unit of work pattern,
    /// sharing a connection and transaction with other storage operations for atomicity.
    /// No automatic table creation occurs when using shared connections.
    /// </remarks>
    public PostgreSqlMessageStorage(NpgsqlConnection connection, NpgsqlTransaction? transaction, TimeProvider timeProvider)
    {
        _sharedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sharedTransaction = transaction;
        _connectionString = connection.ConnectionString ?? string.Empty;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        // Use default options when using shared connection
        _options = new PostgreSqlStorageOptions { ConnectionString = connection.ConnectionString };
        _tableName = _options.GetFullTableName(_options.MessagesTableName);

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
                message_type VARCHAR(500) NOT NULL,
                payload JSONB NOT NULL,
                timestamp TIMESTAMP NOT NULL,
                correlation_id VARCHAR(100),
                collection VARCHAR(100),
                metadata JSONB,
                expires_at TIMESTAMP,
                created_at TIMESTAMP NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_{_options.MessagesTableName}_timestamp ON {_tableName}(timestamp DESC);
            CREATE INDEX IF NOT EXISTS idx_{_options.MessagesTableName}_type ON {_tableName}(message_type);
            CREATE INDEX IF NOT EXISTS idx_{_options.MessagesTableName}_correlation_id ON {_tableName}(correlation_id);
            CREATE INDEX IF NOT EXISTS idx_{_options.MessagesTableName}_collection ON {_tableName}(collection);
            CREATE INDEX IF NOT EXISTS idx_{_options.MessagesTableName}_expires_at ON {_tableName}(expires_at) WHERE expires_at IS NOT NULL;
            """;

        using var command = new NpgsqlCommand(createTableSql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Stores a message in the database and returns its unique identifier
    /// </summary>
    /// <param name="message">The message to store</param>
    /// <param name="options">Optional storage configuration including collection, TTL, and metadata</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The unique identifier assigned to the stored message</returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null</exception>
    /// <remarks>
    /// Messages are serialized as JSONB for efficient storage and querying.
    /// TTL-based expiration is supported for automatic cleanup of old messages.
    /// Metadata can be attached for flexible filtering and categorization.
    /// </remarks>
    public async Task<string> Store(IMessage message, MessageStorageOptions? options = null, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new NpgsqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

            var messageId = Guid.NewGuid().ToString();
            var expiresAt = options?.Ttl != null
                ? _timeProvider.GetUtcNow().DateTime.Add(options.Ttl.Value)
                : (DateTime?)null;

            var sql = $"""
                INSERT INTO {_tableName} (id, message_type, payload, timestamp, correlation_id, collection, metadata, expires_at, created_at)
                VALUES (@id, @message_type, @payload::jsonb, @timestamp, @correlation_id, @collection, @metadata::jsonb, @expires_at, @created_at)
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", messageId);
            command.Parameters.AddWithValue("message_type", message.GetType().FullName ?? "Unknown");
            command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(message, _jsonOptions));
            command.Parameters.AddWithValue("timestamp", message.Timestamp);
            command.Parameters.AddWithValue("correlation_id", (object?)message.CorrelationId ?? DBNull.Value);
            command.Parameters.AddWithValue("collection", (object?)options?.Collection ?? DBNull.Value);
            command.Parameters.AddWithValue("metadata", options?.Metadata != null
                ? JsonSerializer.Serialize(options.Metadata, _jsonOptions)
                : DBNull.Value);
            command.Parameters.AddWithValue("expires_at", (object?)expiresAt ?? DBNull.Value);
            command.Parameters.AddWithValue("created_at", _timeProvider.GetUtcNow().DateTime);

            await command.ExecuteNonQueryAsync(cancellationToken);
            return messageId;
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
    /// Retrieves a strongly-typed message by its unique identifier
    /// </summary>
    /// <typeparam name="T">The expected message type</typeparam>
    /// <param name="messageId">The unique identifier of the message to retrieve</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The message if found and not expired; otherwise null</returns>
    /// <exception cref="ArgumentNullException">Thrown when messageId is null or empty</exception>
    /// <remarks>
    /// Only returns messages that have not expired (expires_at is null or in the future).
    /// The message payload is deserialized from JSONB to the specified type.
    /// </remarks>
    public async Task<T?> Retrieve<T>(string messageId, CancellationToken cancellationToken = default) where T : IMessage
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
                SELECT payload FROM {_tableName}
                WHERE id = @id
                AND (expires_at IS NULL OR expires_at > NOW())
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", messageId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var payload = reader.GetString(0);
                return JsonSerializer.Deserialize<T>(payload, _jsonOptions);
            }

            return default;
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
    /// Deletes a message from storage by its unique identifier
    /// </summary>
    /// <param name="messageId">The unique identifier of the message to delete</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the message was deleted; false if the message was not found</returns>
    /// <exception cref="ArgumentNullException">Thrown when messageId is null or empty</exception>
    public async Task<bool> Delete(string messageId, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new NpgsqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

            var sql = $"DELETE FROM {_tableName} WHERE id = @id";

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", messageId);

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
    /// Checks whether a message exists in storage and has not expired
    /// </summary>
    /// <param name="messageId">The unique identifier of the message to check</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the message exists and has not expired; otherwise false</returns>
    /// <exception cref="ArgumentNullException">Thrown when messageId is null or empty</exception>
    /// <remarks>
    /// Only returns true for messages that have not reached their expiration time.
    /// </remarks>
    public async Task<bool> Exists(string messageId, CancellationToken cancellationToken = default)
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
                SELECT COUNT(1) FROM {_tableName}
                WHERE id = @id
                AND (expires_at IS NULL OR expires_at > NOW())
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", messageId);

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

    /// <summary>
    /// Queries messages using advanced filtering, pagination, and ordering
    /// </summary>
    /// <typeparam name="T">The expected message type</typeparam>
    /// <param name="query">Query criteria including filters, pagination, and ordering</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A collection of messages matching the query criteria</returns>
    /// <exception cref="ArgumentNullException">Thrown when query is null</exception>
    /// <remarks>
    /// Supports filtering by collection, timestamp range, and custom metadata.
    /// Results are paginated using Limit and Offset parameters.
    /// Only returns messages that have not expired.
    /// Default limit is 100 messages, default offset is 0.
    /// </remarks>
    public async Task<IEnumerable<T>> Query<T>(MessageQuery query, CancellationToken cancellationToken = default) where T : IMessage
    {
        var connection = _sharedConnection ?? new NpgsqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

            var whereClauses = new List<string> { "(expires_at IS NULL OR expires_at > NOW())" };
            var parameters = new List<NpgsqlParameter>();

            if (!string.IsNullOrEmpty(query.Collection))
            {
                whereClauses.Add("collection = @collection");
                parameters.Add(new NpgsqlParameter("collection", query.Collection));
            }

            if (query.FromTimestamp.HasValue)
            {
                whereClauses.Add("timestamp >= @from_timestamp");
                parameters.Add(new NpgsqlParameter("from_timestamp", query.FromTimestamp.Value));
            }

            if (query.ToTimestamp.HasValue)
            {
                whereClauses.Add("timestamp <= @to_timestamp");
                parameters.Add(new NpgsqlParameter("to_timestamp", query.ToTimestamp.Value));
            }

            var whereClause = string.Join(" AND ", whereClauses);
            var orderBy = query.OrderBy ?? "timestamp";
            var orderDirection = query.Ascending ? "ASC" : "DESC";
            var limit = query.Limit ?? 100;
            var offset = query.Offset ?? 0;

            var sql = $"""
                SELECT payload FROM {_tableName}
                WHERE {whereClause}
                ORDER BY {orderBy} {orderDirection}
                LIMIT @limit OFFSET @offset
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("limit", limit);
            command.Parameters.AddWithValue("offset", offset);

            foreach (var param in parameters)
            {
                command.Parameters.Add(param);
            }

            var messages = new List<T>();
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
        finally
        {
            if (_sharedConnection == null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Updates an existing message in storage
    /// </summary>
    /// <param name="messageId">The unique identifier of the message to update</param>
    /// <param name="message">The updated message content</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the message was updated; false if the message was not found</returns>
    /// <exception cref="ArgumentNullException">Thrown when messageId or message is null</exception>
    /// <remarks>
    /// Updates the message payload, type, timestamp, and correlation ID.
    /// Collection, metadata, and expiration settings are not updated.
    /// </remarks>
    public async Task<bool> Update(string messageId, IMessage message, CancellationToken cancellationToken = default)
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
                SET payload = @payload::jsonb,
                    message_type = @message_type,
                    timestamp = @timestamp,
                    correlation_id = @correlation_id
                WHERE id = @id
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", messageId);
            command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(message, _jsonOptions));
            command.Parameters.AddWithValue("message_type", message.GetType().FullName ?? "Unknown");
            command.Parameters.AddWithValue("timestamp", message.Timestamp);
            command.Parameters.AddWithValue("correlation_id", (object?)message.CorrelationId ?? DBNull.Value);

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
    /// Counts messages matching the specified query criteria
    /// </summary>
    /// <param name="query">Optional query criteria. If null, counts all non-expired messages</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The total number of messages matching the criteria</returns>
    /// <remarks>
    /// Only counts messages that have not expired.
    /// Supports filtering by collection and timestamp range.
    /// </remarks>
    public async Task<long> Count(MessageQuery? query = null, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new NpgsqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

            var whereClauses = new List<string> { "(expires_at IS NULL OR expires_at > NOW())" };
            var parameters = new List<NpgsqlParameter>();

            if (query != null)
            {
                if (!string.IsNullOrEmpty(query.Collection))
                {
                    whereClauses.Add("collection = @collection");
                    parameters.Add(new NpgsqlParameter("collection", query.Collection));
                }

                if (query.FromTimestamp.HasValue)
                {
                    whereClauses.Add("timestamp >= @from_timestamp");
                    parameters.Add(new NpgsqlParameter("from_timestamp", query.FromTimestamp.Value));
                }

                if (query.ToTimestamp.HasValue)
                {
                    whereClauses.Add("timestamp <= @to_timestamp");
                    parameters.Add(new NpgsqlParameter("to_timestamp", query.ToTimestamp.Value));
                }
            }

            var whereClause = string.Join(" AND ", whereClauses);

            var sql = $"SELECT COUNT(1) FROM {_tableName} WHERE {whereClause}";

            using var command = new NpgsqlCommand(sql, connection, transaction);

            foreach (var param in parameters)
            {
                command.Parameters.Add(param);
            }

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
    /// Removes all messages from storage across all collections
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// WARNING: This operation is destructive and cannot be undone.
    /// All messages, including those with future expiration times, are permanently deleted.
    /// Use with caution, typically only in testing or maintenance scenarios.
    /// </remarks>
    public async Task Clear(CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new NpgsqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

            var sql = $"TRUNCATE TABLE {_tableName}";

            using var command = new NpgsqlCommand(sql, connection, transaction);
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

    /// <summary>
    /// Stores a message within an optional transaction context
    /// </summary>
    /// <param name="message">The message to store</param>
    /// <param name="transaction">Optional transaction context. If provided, operation is part of the transaction</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null</exception>
    /// <remarks>
    /// When a transaction is provided, the message is stored within that transaction's scope.
    /// The message will only be persisted when the transaction is committed.
    /// Uses the message's MessageId property as the storage identifier.
    /// </remarks>
    public async Task StoreAsync(IMessage message, IStorageTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        NpgsqlConnection? connection = null;
        NpgsqlTransaction? npgsqlTransaction = null;

        if (transaction is PostgreSqlStorageTransaction postgresTransaction)
        {
            connection = postgresTransaction.Connection;
            npgsqlTransaction = postgresTransaction.Transaction;
        }

        if (connection == null)
        {
            connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var messageId = message.MessageId.ToString();
            var sql = $"""
                INSERT INTO {_tableName} (id, message_type, payload, timestamp, correlation_id, created_at)
                VALUES (@id, @message_type, @payload::jsonb, @timestamp, @correlation_id, @created_at)
                """;

            using var command = new NpgsqlCommand(sql, connection, npgsqlTransaction);
            command.Parameters.AddWithValue("id", messageId);
            command.Parameters.AddWithValue("message_type", message.GetType().FullName ?? "Unknown");
            command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(message, _jsonOptions));
            command.Parameters.AddWithValue("timestamp", message.Timestamp);
            command.Parameters.AddWithValue("correlation_id", (object?)message.CorrelationId ?? DBNull.Value);
            command.Parameters.AddWithValue("created_at", _timeProvider.GetUtcNow().DateTime);

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
    /// Retrieves a message by its GUID identifier within an optional transaction context
    /// </summary>
    /// <param name="messageId">The GUID identifier of the message to retrieve</param>
    /// <param name="transaction">Optional transaction context for consistent reads</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The message if found and not expired; otherwise null</returns>
    /// <remarks>
    /// When a transaction is provided, the read operation uses the transaction's isolation level
    /// to ensure consistent reads with other operations in the same transaction.
    /// Only returns messages that have not expired.
    /// </remarks>
    public async Task<IMessage?> RetrieveAsync(Guid messageId, IStorageTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        NpgsqlConnection? connection = null;
        NpgsqlTransaction? npgsqlTransaction = null;

        if (transaction is PostgreSqlStorageTransaction postgresTransaction)
        {
            connection = postgresTransaction.Connection;
            npgsqlTransaction = postgresTransaction.Transaction;
        }

        if (connection == null)
        {
            connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var sql = $"""
                SELECT payload FROM {_tableName}
                WHERE id = @id
                AND (expires_at IS NULL OR expires_at > NOW())
                """;

            using var command = new NpgsqlCommand(sql, connection, npgsqlTransaction);
            command.Parameters.AddWithValue("id", messageId.ToString());

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
    /// Queries messages asynchronously using the specified criteria
    /// </summary>
    /// <param name="query">Query criteria including filters, pagination, and ordering</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A list of messages matching the query criteria</returns>
    /// <exception cref="ArgumentNullException">Thrown when query is null</exception>
    /// <remarks>
    /// Supports filtering by collection, timestamp range, and ordering.
    /// Uses either Limit or MaxResults for pagination (Limit takes precedence).
    /// Only returns messages that have not expired.
    /// </remarks>
    public async Task<List<IMessage>> QueryAsync(MessageQuery query, CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var whereClauses = new List<string> { "(expires_at IS NULL OR expires_at > NOW())" };
        var parameters = new List<NpgsqlParameter>();

        if (!string.IsNullOrEmpty(query.Collection))
        {
            whereClauses.Add("collection = @collection");
            parameters.Add(new NpgsqlParameter("collection", query.Collection));
        }

        if (query.FromTimestamp.HasValue)
        {
            whereClauses.Add("timestamp >= @from_timestamp");
            parameters.Add(new NpgsqlParameter("from_timestamp", query.FromTimestamp.Value));
        }

        if (query.ToTimestamp.HasValue)
        {
            whereClauses.Add("timestamp <= @to_timestamp");
            parameters.Add(new NpgsqlParameter("to_timestamp", query.ToTimestamp.Value));
        }

        var whereClause = string.Join(" AND ", whereClauses);
        var orderBy = query.OrderBy ?? "timestamp";
        var orderDirection = query.Ascending ? "ASC" : "DESC";
        var limit = query.Limit ?? query.MaxResults;
        var offset = query.Offset ?? 0;

        var sql = $"""
            SELECT payload FROM {_tableName}
            WHERE {whereClause}
            ORDER BY {orderBy} {orderDirection}
            LIMIT @limit OFFSET @offset
            """;

        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("offset", offset);

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
    /// Deletes a message by its GUID identifier
    /// </summary>
    /// <param name="messageId">The GUID identifier of the message to delete</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public async Task DeleteAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"DELETE FROM {_tableName} WHERE id = @id";

        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", messageId.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Begins a new storage transaction for atomic multi-operation updates
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A transaction object that must be committed or rolled back</returns>
    /// <exception cref="InvalidOperationException">Thrown when transaction cannot be started</exception>
    /// <remarks>
    /// Transactions ensure that multiple storage operations either all succeed or all fail together.
    /// Always use within a using statement or try-finally block to ensure proper disposal.
    /// The transaction uses ReadCommitted isolation level.
    /// </remarks>
    public async Task<IStorageTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        return new PostgreSqlStorageTransaction(connection, transaction);
    }
}

/// <summary>
/// PostgreSQL implementation of storage transaction
/// </summary>
/// <remarks>
/// Wraps a PostgreSQL connection and transaction to provide atomicity for multiple storage operations.
/// The transaction must be explicitly committed; otherwise, it will be rolled back on disposal.
/// </remarks>
public sealed class PostgreSqlStorageTransaction : IStorageTransaction
{
    private bool _disposed;

    /// <summary>
    /// Gets the underlying PostgreSQL connection used by this transaction
    /// </summary>
    /// <value>The NpgsqlConnection instance</value>
    public NpgsqlConnection Connection { get; }

    /// <summary>
    /// Gets the underlying PostgreSQL transaction
    /// </summary>
    /// <value>The NpgsqlTransaction instance</value>
    public NpgsqlTransaction Transaction { get; }

    /// <summary>
    /// Initializes a new instance of PostgreSQL storage transaction
    /// </summary>
    /// <param name="connection">The database connection to use</param>
    /// <param name="transaction">The database transaction to manage</param>
    /// <exception cref="ArgumentNullException">Thrown when connection or transaction is null</exception>
    public PostgreSqlStorageTransaction(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }

    /// <summary>
    /// Commits all operations performed within this transaction, making them permanent
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the asynchronous commit operation</returns>
    /// <exception cref="ObjectDisposedException">Thrown when transaction has already been disposed</exception>
    /// <exception cref="InvalidOperationException">Thrown when transaction has already been committed or rolled back</exception>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PostgreSqlStorageTransaction));

        await Transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Rolls back all operations performed within this transaction, discarding all changes
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the asynchronous rollback operation</returns>
    /// <exception cref="ObjectDisposedException">Thrown when transaction has already been disposed</exception>
    /// <exception cref="InvalidOperationException">Thrown when transaction has already been committed or rolled back</exception>
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PostgreSqlStorageTransaction));

        await Transaction.RollbackAsync(cancellationToken);
    }

    /// <summary>
    /// Disposes the transaction and its underlying connection
    /// </summary>
    /// <remarks>
    /// If the transaction has not been committed, it will be automatically rolled back.
    /// Safe to call multiple times.
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

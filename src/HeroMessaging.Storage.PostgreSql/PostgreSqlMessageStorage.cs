using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Npgsql;
using System.Data;
using System.Text.Json;

namespace HeroMessaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of message storage using pure ADO.NET
/// </summary>
public class PostgreSqlMessageStorage : IMessageStorage
{
    private readonly PostgreSqlStorageOptions _options;
    private readonly NpgsqlConnection? _sharedConnection;
    private readonly NpgsqlTransaction? _sharedTransaction;
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly TimeProvider _timeProvider;
    private readonly JsonSerializerOptions _jsonOptions;

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

    public PostgreSqlMessageStorage(NpgsqlConnection connection, NpgsqlTransaction? transaction, TimeProvider timeProvider)
    {
        _sharedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sharedTransaction = transaction;
        _connectionString = connection.ConnectionString ?? string.Empty;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        // Use default options when using shared connection
        _options = new PostgreSqlStorageOptions { ConnectionString = connection.ConnectionString! };
        _tableName = _options.GetFullTableName(_options.MessagesTableName);

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

    public async Task<string> Store(IMessage message, MessageStorageOptions? options = null, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        try
        {

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
        }
    }

    public async Task<T?> Retrieve<T>(string messageId, CancellationToken cancellationToken = default) where T : IMessage
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        try
        {

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
        }
    }

    public async Task<bool> Delete(string messageId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        try
        {

            var sql = $"DELETE FROM {_tableName} WHERE id = @id";

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", messageId);

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            return rowsAffected > 0;
        }
        finally
        {
        }
    }

    public async Task<bool> Exists(string messageId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        try
        {

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
        }
    }

    public async Task<IEnumerable<T>> Query<T>(MessageQuery query, CancellationToken cancellationToken = default) where T : IMessage
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        try
        {

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
        }
    }

    public async Task<bool> Update(string messageId, IMessage message, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        try
        {

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
        }
    }

    public async Task<long> Count(MessageQuery? query = null, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        try
        {

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
        }
    }

    public async Task Clear(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        try
        {

            var sql = $"TRUNCATE TABLE {_tableName}";

            using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
        }
    }

    // New interface methods for compatibility with test infrastructure
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
            var messageType = message.GetType();
            command.Parameters.AddWithValue("message_type", messageType.AssemblyQualifiedName ?? "Unknown");
            command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(message, messageType, _jsonOptions));
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
                SELECT payload, message_type FROM {_tableName}
                WHERE id = @id
                AND (expires_at IS NULL OR expires_at > NOW())
                """;

            using var command = new NpgsqlCommand(sql, connection, npgsqlTransaction);
            command.Parameters.AddWithValue("id", messageId.ToString());

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var payload = reader.GetString(0);
                var messageTypeName = reader.GetString(1);

                // Deserialize using the concrete type stored in the database
                var messageType = Type.GetType(messageTypeName);
                if (messageType == null)
                {
                    throw new InvalidOperationException($"Unable to resolve message type: {messageTypeName}");
                }

                var message = JsonSerializer.Deserialize(payload, messageType, _jsonOptions);
                return message as IMessage;
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
            SELECT payload, message_type FROM {_tableName}
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
            var messageTypeName = reader.GetString(1);

            // Deserialize using the concrete type stored in the database
            var messageType = Type.GetType(messageTypeName);
            if (messageType == null)
            {
                throw new InvalidOperationException($"Unable to resolve message type: {messageTypeName}");
            }

            var message = JsonSerializer.Deserialize(payload, messageType, _jsonOptions);
            if (message is IMessage imessage)
            {
                messages.Add(imessage);
            }
        }

        return messages;
    }

    public async Task DeleteAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"DELETE FROM {_tableName} WHERE id = @id";

        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", messageId.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

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
public sealed class PostgreSqlStorageTransaction : IStorageTransaction
{
    private bool _disposed;

    public NpgsqlConnection Connection { get; }
    public NpgsqlTransaction Transaction { get; }

    public PostgreSqlStorageTransaction(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PostgreSqlStorageTransaction));

        await Transaction.CommitAsync(cancellationToken);
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PostgreSqlStorageTransaction));

        await Transaction.RollbackAsync(cancellationToken);
    }

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

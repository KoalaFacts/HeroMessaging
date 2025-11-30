using System.Data;
using System.Text.Json;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Microsoft.Data.SqlClient;

namespace HeroMessaging.Storage.SqlServer;

/// <summary>
/// SQL Server implementation of message storage using pure ADO.NET
/// </summary>
public class SqlServerMessageStorage : IMessageStorage
{
    private readonly SqlServerStorageOptions _options;
    private readonly string _connectionString;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _tableName;
    private readonly SqlConnection? _sharedConnection;
    private readonly SqlTransaction? _sharedTransaction;
    private readonly TimeProvider _timeProvider;
    private readonly IJsonSerializer _jsonSerializer;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public SqlServerMessageStorage(
        SqlServerStorageOptions options,
        TimeProvider timeProvider,
        IJsonSerializer jsonSerializer)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connectionString = options.ConnectionString;
        _tableName = _options.GetFullTableName(_options.MessagesTableName);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        // Validate SQL identifiers to prevent injection
        ValidateSqlIdentifier(_options.Schema, nameof(_options.Schema));
        ValidateSqlIdentifier(_options.MessagesTableName, nameof(_options.MessagesTableName));
    }

    /// <summary>
    /// Constructor for transaction-aware operations with shared connection and transaction
    /// </summary>
    public SqlServerMessageStorage(
        SqlConnection connection,
        SqlTransaction? transaction,
        TimeProvider timeProvider,
        IJsonSerializer jsonSerializer)
    {
        _sharedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sharedTransaction = transaction;

        // Use default options when using shared connection
        _options = new SqlServerStorageOptions { ConnectionString = connection.ConnectionString };
        _connectionString = connection.ConnectionString;
        _tableName = _options.GetFullTableName(_options.MessagesTableName);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }

    /// <summary>
    /// Validates that a SQL identifier (schema/table name) is safe to use in SQL statements.
    /// Prevents SQL injection by rejecting unsafe characters.
    /// </summary>
    private static void ValidateSqlIdentifier(string? identifier, string paramName)
    {
        if (string.IsNullOrEmpty(identifier))
            return;

        // SQL Server identifiers must:
        // - Start with letter, underscore, or @ (for variables)
        // - Contain only letters, digits, underscores
        // - Not contain SQL keywords or special characters
        foreach (var c in identifier)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                throw new ArgumentException(
                    $"Invalid SQL identifier '{identifier}'. Only letters, digits, and underscores are allowed.",
                    paramName);
            }
        }
    }

    /// <summary>
    /// Ensures the database schema is initialized. Uses lazy initialization to avoid blocking constructor.
    /// </summary>
    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            await InitializeDatabase().ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
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

    public async Task<string> StoreAsync(IMessage message, MessageStorageOptions? options = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var messageId = message.MessageId.ToString();
        var expiresAt = options?.Ttl != null
            ? _timeProvider.GetUtcNow().Add(options.Ttl.Value)
            : (DateTimeOffset?)null;

        var sql = $"""
            INSERT INTO {_tableName} (Id, MessageType, Payload, Timestamp, CorrelationId, Collection, Metadata, ExpiresAt, CreatedAt)
            VALUES (@Id, @MessageType, @Payload, @Timestamp, @CorrelationId, @Collection, @Metadata, @ExpiresAt, @CreatedAt)
            """;

        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = messageId;
        command.Parameters.Add("@MessageType", SqlDbType.NVarChar, 500).Value = message.GetType().AssemblyQualifiedName ?? "Unknown";
        command.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = _jsonSerializer.SerializeToString(message, message.GetType(), _jsonOptions);
        command.Parameters.Add("@Timestamp", SqlDbType.DateTime2).Value = message.Timestamp;
        command.Parameters.Add("@CorrelationId", SqlDbType.NVarChar, 100).Value = (object?)message.CorrelationId ?? DBNull.Value;
        command.Parameters.Add("@Collection", SqlDbType.NVarChar, 100).Value = (object?)options?.Collection ?? DBNull.Value;
        command.Parameters.Add("@Metadata", SqlDbType.NVarChar, -1).Value = options?.Metadata != null
            ? _jsonSerializer.SerializeToString(options.Metadata, _jsonOptions)
            : DBNull.Value;
        command.Parameters.Add("@ExpiresAt", SqlDbType.DateTime2).Value = (object?)expiresAt ?? DBNull.Value;
        command.Parameters.Add("@CreatedAt", SqlDbType.DateTime2).Value = _timeProvider.GetUtcNow();

        await command.ExecuteNonQueryAsync(cancellationToken);
        return messageId;
    }

    public async Task<T?> RetrieveAsync<T>(string messageId, CancellationToken cancellationToken = default) where T : IMessage
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

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
            return _jsonSerializer.DeserializeFromString<T>(payload, _jsonOptions);
        }

        return default;
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(MessageQuery query, CancellationToken cancellationToken = default) where T : IMessage
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

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
        command.Parameters.AddRange([.. parameters]);
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

    public async Task<bool> DeleteAsync(string messageId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"DELETE FROM {_tableName} WHERE Id = @Id";

        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = messageId;

        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        return result > 0;
    }

    public async Task<bool> UpdateAsync(string messageId, IMessage message, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

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
        command.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = _jsonSerializer.SerializeToString(message, _jsonOptions);
        command.Parameters.Add("@Timestamp", SqlDbType.DateTime2).Value = message.Timestamp;
        command.Parameters.Add("@CorrelationId", SqlDbType.NVarChar, 100).Value = DBNull.Value;

        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        return result > 0;
    }

    public async Task<bool> ExistsAsync(string messageId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

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

    public async Task<long> CountAsync(MessageQuery? query = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

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
        command.Parameters.AddRange([.. parameters]);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result ?? 0);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"TRUNCATE TABLE {_tableName}";

        using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // New interface methods for compatibility with test infrastructure
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
            var messageType = message.GetType();
            command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = messageId;
            command.Parameters.Add("@MessageType", SqlDbType.NVarChar, 500).Value = messageType.AssemblyQualifiedName ?? "Unknown";
            command.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = _jsonSerializer.SerializeToString(message, messageType, _jsonOptions);
            command.Parameters.Add("@Timestamp", SqlDbType.DateTime2).Value = message.Timestamp;
            command.Parameters.Add("@CorrelationId", SqlDbType.NVarChar, 100).Value = (object?)message.CorrelationId ?? DBNull.Value;
            command.Parameters.Add("@CreatedAt", SqlDbType.DateTime2).Value = _timeProvider.GetUtcNow();

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
                SELECT Payload, MessageType FROM {_tableName}
                WHERE Id = @Id
                AND (ExpiresAt IS NULL OR ExpiresAt > GETUTCDATE())
                """;

            using var command = new SqlCommand(sql, connection, sqlTransaction);
            command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = messageId.ToString();

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var payload = reader.GetString(0);
                var messageTypeName = reader.GetString(1);

                // Deserialize using the concrete type stored in the database
                var messageType = Type.GetType(messageTypeName) ?? throw new InvalidOperationException($"Unable to resolve message type: {messageTypeName}");
                var message = _jsonSerializer.DeserializeFromString(payload, messageType, _jsonOptions);
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
            SELECT Payload, MessageType FROM {_tableName}
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
            var messageTypeName = reader.GetString(1);

            // Deserialize using the concrete type stored in the database
            var messageType = Type.GetType(messageTypeName) ?? throw new InvalidOperationException($"Unable to resolve message type: {messageTypeName}");
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
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"DELETE FROM {_tableName} WHERE Id = @Id";

        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = messageId.ToString();

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IStorageTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);

        return new SqlServerStorageTransaction(connection, transaction);
    }
}

/// <summary>
/// SQL Server implementation of storage transaction
/// </summary>
public sealed class SqlServerStorageTransaction : IStorageTransaction
{
    private bool _disposed;

    public SqlConnection Connection { get; }
    public SqlTransaction Transaction { get; }

    public SqlServerStorageTransaction(SqlConnection connection, SqlTransaction transaction)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SqlServerStorageTransaction));

        await Transaction.CommitAsync(cancellationToken);
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SqlServerStorageTransaction));

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


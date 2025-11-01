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
    private readonly IDbConnectionProvider<SqlConnection, SqlTransaction> _connectionProvider;
    private readonly IDbSchemaInitializer _schemaInitializer;
    private readonly IJsonOptionsProvider _jsonOptionsProvider;
    private readonly string _tableName;
    private readonly TimeProvider _timeProvider;

    public SqlServerInboxStorage(
        SqlServerStorageOptions options,
        TimeProvider timeProvider,
        IDbConnectionProvider<SqlConnection, SqlTransaction>? connectionProvider = null,
        IDbSchemaInitializer? schemaInitializer = null,
        IJsonOptionsProvider? jsonOptionsProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _tableName = _options.GetFullTableName(_options.InboxTableName);

        // Use provided dependencies or create defaults
        _connectionProvider = connectionProvider ?? new SqlServerConnectionProvider(options.ConnectionString ?? throw new ArgumentNullException(nameof(options.ConnectionString)));
        _jsonOptionsProvider = jsonOptionsProvider ?? new DefaultJsonOptionsProvider();
        _schemaInitializer = schemaInitializer ?? new SqlServerSchemaInitializer(_connectionProvider);

        if (_options.AutoCreateTables)
        {
            InitializeDatabase().GetAwaiter().GetResult();
        }
    }

    public SqlServerInboxStorage(
        SqlConnection connection,
        SqlTransaction? transaction,
        TimeProvider timeProvider,
        IJsonOptionsProvider? jsonOptionsProvider = null)
    {
        _connectionProvider = new SqlServerConnectionProvider(connection, transaction);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _jsonOptionsProvider = jsonOptionsProvider ?? new DefaultJsonOptionsProvider();

        // Use default options when using shared connection
        _options = new SqlServerStorageOptions { ConnectionString = connection.ConnectionString };
        _tableName = _options.GetFullTableName(_options.InboxTableName);
        _schemaInitializer = new SqlServerSchemaInitializer(_connectionProvider);
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

    public async Task<InboxEntry?> Add(IMessage message, InboxOptions options, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
        var transaction = _connectionProvider.GetTransaction();

        try
        {

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
            command.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(message, _jsonOptionsProvider.GetOptions());
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
        }
    }

    public async Task<bool> IsDuplicate(string messageId, TimeSpan? window = null, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
        var transaction = _connectionProvider.GetTransaction();

        try
        {

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
        }
    }

    public async Task<InboxEntry?> Get(string messageId, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
        var transaction = _connectionProvider.GetTransaction();

        try
        {

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
                var message = JsonSerializer.Deserialize<IMessage>(payload, _jsonOptionsProvider.GetOptions());

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
        }
    }

    public async Task<bool> MarkProcessed(string messageId, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
        var transaction = _connectionProvider.GetTransaction();

        try
        {

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
        }
    }

    public async Task<bool> MarkFailed(string messageId, string error, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
        var transaction = _connectionProvider.GetTransaction();

        try
        {

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
        }
    }

    public async Task<IEnumerable<InboxEntry>> GetPending(InboxQuery query, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
        var transaction = _connectionProvider.GetTransaction();

        try
        {

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

                var message = JsonSerializer.Deserialize<IMessage>(payload, _jsonOptionsProvider.GetOptions());

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
        }
    }

    public async Task<IEnumerable<InboxEntry>> GetUnprocessed(int limit = 100, CancellationToken cancellationToken = default)
    {
        var query = new InboxQuery
        {
            Status = InboxEntryStatus.Pending,
            Limit = limit
        };

        return await GetPending(query, cancellationToken);
    }

    public async Task<long> GetUnprocessedCount(CancellationToken cancellationToken = default)
    {
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
        var transaction = _connectionProvider.GetTransaction();

        try
        {

            var sql = $"SELECT COUNT(1) FROM {_tableName} WHERE Status = 'Pending'";

            using var command = new SqlCommand(sql, connection, transaction);
            var count = (int)await command.ExecuteScalarAsync(cancellationToken);
            return count;
        }
        finally
        {
        }
    }

    public async Task CleanupOldEntries(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
        var transaction = _connectionProvider.GetTransaction();

        try
        {

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
        }
    }
}

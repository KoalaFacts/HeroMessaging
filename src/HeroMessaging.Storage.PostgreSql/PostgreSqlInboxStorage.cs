using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Npgsql;
using System.Text.Json;

namespace HeroMessaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of inbox storage using pure ADO.NET
/// Provides message deduplication and idempotent message processing
/// </summary>
public class PostgreSqlInboxStorage : IInboxStorage
{
    private readonly PostgreSqlStorageOptions _options;
    private readonly NpgsqlConnection? _sharedConnection;
    private readonly NpgsqlTransaction? _sharedTransaction;
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly TimeProvider _timeProvider;
    private readonly JsonSerializerOptions _jsonOptions;

    public PostgreSqlInboxStorage(PostgreSqlStorageOptions options, TimeProvider timeProvider)
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

    public PostgreSqlInboxStorage(NpgsqlConnection connection, NpgsqlTransaction? transaction, TimeProvider timeProvider)
    {
        _sharedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sharedTransaction = transaction;
        _connectionString = connection.ConnectionString ?? string.Empty;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        // Use default options when using shared connection
        _options = new PostgreSqlStorageOptions { ConnectionString = connection.ConnectionString };
        _tableName = _options.GetFullTableName(_options.InboxTableName);

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
                source VARCHAR(200),
                status VARCHAR(50) NOT NULL DEFAULT 'Pending',
                received_at TIMESTAMP NOT NULL,
                processed_at TIMESTAMP,
                error TEXT,
                require_idempotency BOOLEAN NOT NULL DEFAULT true,
                deduplication_window_minutes INTEGER
            );

            CREATE INDEX IF NOT EXISTS idx_{_options.InboxTableName}_status ON {_tableName}(status);
            CREATE INDEX IF NOT EXISTS idx_{_options.InboxTableName}_received_at ON {_tableName}(received_at DESC);
            CREATE INDEX IF NOT EXISTS idx_{_options.InboxTableName}_processed_at ON {_tableName}(processed_at);
            """;

        using var command = new NpgsqlCommand(createTableSql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<InboxEntry?> Add(IMessage message, InboxOptions options, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new NpgsqlConnection(_connectionString);
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
                INSERT INTO {_tableName} (id, message_type, payload, source, status, received_at, require_idempotency, deduplication_window_minutes)
                VALUES (@id, @message_type, @payload::jsonb, @source, @status, @received_at, @require_idempotency, @deduplication_window_minutes)
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", messageId);
            command.Parameters.AddWithValue("message_type", message.GetType().FullName ?? "Unknown");
            command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(message, _jsonOptions));
            command.Parameters.AddWithValue("source", (object?)options.Source ?? DBNull.Value);
            command.Parameters.AddWithValue("status", "Pending");
            command.Parameters.AddWithValue("received_at", now);
            command.Parameters.AddWithValue("require_idempotency", options.RequireIdempotency);
            command.Parameters.AddWithValue("deduplication_window_minutes", (object?)options.DeduplicationWindow?.TotalMinutes ?? DBNull.Value);

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

    public async Task<bool> IsDuplicate(string messageId, TimeSpan? window = null, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new NpgsqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

            var sql = window.HasValue
                ? $"SELECT COUNT(1) FROM {_tableName} WHERE id = @id AND received_at >= @window_start"
                : $"SELECT COUNT(1) FROM {_tableName} WHERE id = @id";

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", messageId);

            if (window.HasValue)
            {
                var windowStart = _timeProvider.GetUtcNow().DateTime.Subtract(window.Value);
                command.Parameters.AddWithValue("window_start", windowStart);
            }

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

    public async Task<InboxEntry?> Get(string messageId, CancellationToken cancellationToken = default)
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
                SELECT message_type, payload, source, status, received_at, processed_at, error, require_idempotency, deduplication_window_minutes
                FROM {_tableName}
                WHERE id = @id
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", messageId);

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

    public async Task<bool> MarkProcessed(string messageId, CancellationToken cancellationToken = default)
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
                SET status = @status, processed_at = @processed_at
                WHERE id = @id
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", messageId);
            command.Parameters.AddWithValue("status", "Processed");
            command.Parameters.AddWithValue("processed_at", _timeProvider.GetUtcNow().DateTime);

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

    public async Task<bool> MarkFailed(string messageId, string error, CancellationToken cancellationToken = default)
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
                SET status = @status, processed_at = @processed_at, error = @error
                WHERE id = @id
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", messageId);
            command.Parameters.AddWithValue("status", "Failed");
            command.Parameters.AddWithValue("processed_at", _timeProvider.GetUtcNow().DateTime);
            command.Parameters.AddWithValue("error", error);

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

    public async Task<IEnumerable<InboxEntry>> GetPending(InboxQuery query, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new NpgsqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

            var whereClauses = new List<string>();
            var parameters = new List<NpgsqlParameter>();

            if (query.Status.HasValue)
            {
                whereClauses.Add("status = @status");
                parameters.Add(new NpgsqlParameter("status", query.Status.Value.ToString()));
            }

            if (query.OlderThan.HasValue)
            {
                whereClauses.Add("received_at < @older_than");
                parameters.Add(new NpgsqlParameter("older_than", query.OlderThan.Value));
            }

            if (query.NewerThan.HasValue)
            {
                whereClauses.Add("received_at > @newer_than");
                parameters.Add(new NpgsqlParameter("newer_than", query.NewerThan.Value));
            }

            var whereClause = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

            var sql = $"""
                SELECT id, message_type, payload, source, status, received_at, processed_at, error, require_idempotency, deduplication_window_minutes
                FROM {_tableName}
                {whereClause}
                ORDER BY received_at DESC
                LIMIT @limit
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("limit", query.Limit);
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
        var connection = _sharedConnection ?? new NpgsqlConnection(_connectionString);
        var transaction = _sharedTransaction;

        try
        {
            if (_sharedConnection == null)
            {
                await connection.OpenAsync(cancellationToken);
            }

            var sql = $"SELECT COUNT(1) FROM {_tableName} WHERE status = 'Pending'";

            using var command = new NpgsqlCommand(sql, connection, transaction);
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

    public async Task CleanupOldEntries(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var connection = _sharedConnection ?? new NpgsqlConnection(_connectionString);
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
                WHERE received_at < @cutoff_time
                AND status IN ('Processed', 'Failed')
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("cutoff_time", cutoffTime);

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
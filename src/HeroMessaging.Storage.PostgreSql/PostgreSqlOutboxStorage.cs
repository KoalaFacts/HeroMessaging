using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Npgsql;
using System.Text.Json;

namespace HeroMessaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of outbox storage using pure ADO.NET
/// Provides transactional outbox pattern for reliable message delivery
/// </summary>
public class PostgreSqlOutboxStorage : IOutboxStorage
{
    private readonly PostgreSqlStorageOptions _options;
    private readonly NpgsqlConnection? _sharedConnection;
    private readonly NpgsqlTransaction? _sharedTransaction;
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly TimeProvider _timeProvider;
    private readonly JsonSerializerOptions _jsonOptions;

    public PostgreSqlOutboxStorage(PostgreSqlStorageOptions options, TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connectionString = options.ConnectionString ?? throw new ArgumentNullException(nameof(options.ConnectionString));
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

    public PostgreSqlOutboxStorage(NpgsqlConnection connection, NpgsqlTransaction? transaction, TimeProvider timeProvider)
    {
        _sharedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sharedTransaction = transaction;
        _connectionString = connection.ConnectionString ?? string.Empty;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        // Use default options when using shared connection
        _options = new PostgreSqlStorageOptions { ConnectionString = connection.ConnectionString };
        _tableName = _options.GetFullTableName(_options.OutboxTableName);

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
                destination VARCHAR(200),
                status VARCHAR(50) NOT NULL DEFAULT 'Pending',
                retry_count INTEGER NOT NULL DEFAULT 0,
                max_retries INTEGER NOT NULL DEFAULT 3,
                created_at TIMESTAMP NOT NULL,
                processed_at TIMESTAMP,
                next_retry_at TIMESTAMP,
                last_error TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_{_options.OutboxTableName}_status ON {_tableName}(status);
            CREATE INDEX IF NOT EXISTS idx_{_options.OutboxTableName}_next_retry ON {_tableName}(next_retry_at) WHERE status = 'Pending';
            CREATE INDEX IF NOT EXISTS idx_{_options.OutboxTableName}_created_at ON {_tableName}(created_at DESC);
            """;

        using var command = new NpgsqlCommand(createTableSql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<OutboxEntry> Add(IMessage message, OutboxOptions options, CancellationToken cancellationToken = default)
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

            var sql = $"""
                INSERT INTO {_tableName} (id, message_type, payload, destination, status, retry_count, max_retries, created_at)
                VALUES (@id, @message_type, @payload::jsonb, @destination, @status, @retry_count, @max_retries, @created_at)
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", entryId);
            command.Parameters.AddWithValue("message_type", message.GetType().FullName ?? "Unknown");
            command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(message, _jsonOptions));
            command.Parameters.AddWithValue("destination", (object?)options.Destination ?? DBNull.Value);
            command.Parameters.AddWithValue("status", "Pending");
            command.Parameters.AddWithValue("retry_count", 0);
            command.Parameters.AddWithValue("max_retries", options.MaxRetries);
            command.Parameters.AddWithValue("created_at", now);

            await command.ExecuteNonQueryAsync(cancellationToken);

            return new OutboxEntry
            {
                Id = entryId,
                Message = message,
                Options = options,
                Status = OutboxStatus.Pending,
                RetryCount = 0,
                CreatedAt = now
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

    public async Task<IEnumerable<OutboxEntry>> GetPending(OutboxQuery query, CancellationToken cancellationToken = default)
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
                whereClauses.Add("created_at < @older_than");
                parameters.Add(new NpgsqlParameter("older_than", query.OlderThan.Value));
            }

            if (query.NewerThan.HasValue)
            {
                whereClauses.Add("created_at > @newer_than");
                parameters.Add(new NpgsqlParameter("newer_than", query.NewerThan.Value));
            }

            var whereClause = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

            var sql = $"""
                SELECT id, message_type, payload, destination, status, retry_count, max_retries, created_at, processed_at, next_retry_at, last_error
                FROM {_tableName}
                {whereClause}
                ORDER BY created_at ASC
                LIMIT @limit
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("limit", query.Limit);
            foreach (var param in parameters)
            {
                command.Parameters.Add(param);
            }

            var entries = new List<OutboxEntry>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var entryId = reader.GetString(0);
                var messageType = reader.GetString(1);
                var payload = reader.GetString(2);
                var destination = reader.IsDBNull(3) ? null : reader.GetString(3);
                var status = Enum.Parse<OutboxStatus>(reader.GetString(4));
                var retryCount = reader.GetInt32(5);
                var maxRetries = reader.GetInt32(6);
                var createdAt = reader.GetDateTime(7);
                var processedAt = reader.IsDBNull(8) ? (DateTime?)null : reader.GetDateTime(8);
                var nextRetryAt = reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9);
                var lastError = reader.IsDBNull(10) ? null : reader.GetString(10);

                var message = JsonSerializer.Deserialize<IMessage>(payload, _jsonOptions);

                entries.Add(new OutboxEntry
                {
                    Id = entryId,
                    Message = message!,
                    Options = new OutboxOptions
                    {
                        Destination = destination,
                        MaxRetries = maxRetries
                    },
                    Status = status,
                    RetryCount = retryCount,
                    CreatedAt = createdAt,
                    ProcessedAt = processedAt,
                    NextRetryAt = nextRetryAt,
                    LastError = lastError
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

    public async Task<IEnumerable<OutboxEntry>> GetPending(int limit = 100, CancellationToken cancellationToken = default)
    {
        var query = new OutboxQuery
        {
            Status = OutboxEntryStatus.Pending,
            Limit = limit
        };

        return await GetPending(query, cancellationToken);
    }

    public async Task<bool> MarkProcessed(string entryId, CancellationToken cancellationToken = default)
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
            command.Parameters.AddWithValue("id", entryId);
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

    public async Task<bool> MarkFailed(string entryId, string error, CancellationToken cancellationToken = default)
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
                SET status = @status, processed_at = @processed_at, last_error = @last_error
                WHERE id = @id
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", entryId);
            command.Parameters.AddWithValue("status", "Failed");
            command.Parameters.AddWithValue("processed_at", _timeProvider.GetUtcNow().DateTime);
            command.Parameters.AddWithValue("last_error", error);

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

    public async Task<bool> UpdateRetryCount(string entryId, int retryCount, DateTime? nextRetry = null, CancellationToken cancellationToken = default)
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
                SET retry_count = @retry_count, next_retry_at = @next_retry_at
                WHERE id = @id
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", entryId);
            command.Parameters.AddWithValue("retry_count", retryCount);
            command.Parameters.AddWithValue("next_retry_at", (object?)nextRetry ?? DBNull.Value);

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

    public async Task<long> GetPendingCount(CancellationToken cancellationToken = default)
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

    public async Task<IEnumerable<OutboxEntry>> GetFailed(int limit = 100, CancellationToken cancellationToken = default)
    {
        var query = new OutboxQuery
        {
            Status = OutboxEntryStatus.Failed,
            Limit = limit
        };

        return await GetPending(query, cancellationToken);
    }
}
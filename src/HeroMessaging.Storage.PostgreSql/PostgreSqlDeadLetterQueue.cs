using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace HeroMessaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of dead letter queue using pure ADO.NET
/// </summary>
public class PostgreSqlDeadLetterQueue : IDeadLetterQueue
{
    private readonly PostgreSqlStorageOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _tableName;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlDeadLetterQueue(PostgreSqlStorageOptions options, TimeProvider timeProvider)
    {
        _options = options;
        _tableName = _options.GetFullTableName(_options.DeadLetterTableName);
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
        using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync();

        var createTableSql = $"""
            CREATE TABLE IF NOT EXISTS {_tableName} (
                id VARCHAR(100) PRIMARY KEY,
                message_payload JSONB NOT NULL,
                message_type VARCHAR(500) NOT NULL,
                reason TEXT NOT NULL,
                component VARCHAR(200) NOT NULL,
                retry_count INTEGER NOT NULL,
                failure_time TIMESTAMP NOT NULL,
                status INTEGER NOT NULL DEFAULT 0,
                created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                retried_at TIMESTAMP NULL,
                discarded_at TIMESTAMP NULL,
                exception_message TEXT NULL,
                metadata JSONB NULL
            );
            
            CREATE INDEX IF NOT EXISTS idx_{_options.DeadLetterTableName}_status ON {_tableName} (status);
            CREATE INDEX IF NOT EXISTS idx_{_options.DeadLetterTableName}_message_type ON {_tableName} (message_type);
            CREATE INDEX IF NOT EXISTS idx_{_options.DeadLetterTableName}_failure_time ON {_tableName} (failure_time DESC);
            CREATE INDEX IF NOT EXISTS idx_{_options.DeadLetterTableName}_component ON {_tableName} (component);
            """;

        using var command = new NpgsqlCommand(createTableSql, connection)
        {
            CommandTimeout = _options.CommandTimeout
        };
        await command.ExecuteNonQueryAsync();
    }

    public async Task<string> SendToDeadLetter<T>(T message, DeadLetterContext context, CancellationToken cancellationToken = default)
        where T : IMessage
    {
        var deadLetterId = Guid.NewGuid().ToString();

        using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            INSERT INTO {_tableName} (
                id, message_payload, message_type, reason, component, 
                retry_count, failure_time, status, created_at, 
                exception_message, metadata
            )
            VALUES (
                @id, @message_payload::jsonb, @message_type, @reason, @component,
                @retry_count, @failure_time, @status, @created_at,
                @exception_message, @metadata::jsonb
            )
            """;

        using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = _options.CommandTimeout
        };

        command.Parameters.AddWithValue("@id", deadLetterId);
        command.Parameters.AddWithValue("@message_payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(message, _jsonOptions));
        command.Parameters.AddWithValue("@message_type", message.GetType().FullName ?? "Unknown");
        command.Parameters.AddWithValue("@reason", context.Reason);
        command.Parameters.AddWithValue("@component", context.Component);
        command.Parameters.AddWithValue("@retry_count", context.RetryCount);
        command.Parameters.AddWithValue("@failure_time", context.FailureTime);
        command.Parameters.AddWithValue("@status", (int)DeadLetterStatus.Active);
        command.Parameters.AddWithValue("@created_at", _timeProvider.GetUtcNow().DateTime);
        command.Parameters.AddWithValue("@exception_message", context.Exception?.Message ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@metadata", NpgsqlDbType.Jsonb,
            context.Metadata.Any() ? JsonSerializer.Serialize(context.Metadata, _jsonOptions) : (object)DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
        return deadLetterId;
    }

    public async Task<IEnumerable<DeadLetterEntry<T>>> GetDeadLetters<T>(int limit = 100, CancellationToken cancellationToken = default)
        where T : IMessage
    {
        using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT id, message_payload, reason, component, retry_count, failure_time,
                   status, created_at, retried_at, discarded_at, exception_message, metadata
            FROM {_tableName}
            WHERE message_type = @message_type AND status = @active_status
            ORDER BY failure_time DESC
            LIMIT @limit
            """;

        var entries = new List<DeadLetterEntry<T>>();

        using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = _options.CommandTimeout
        };

        command.Parameters.AddWithValue("@message_type", typeof(T).FullName ?? "Unknown");
        command.Parameters.AddWithValue("@active_status", (int)DeadLetterStatus.Active);
        command.Parameters.AddWithValue("@limit", limit);

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var messagePayload = reader.GetString(1);
            var message = JsonSerializer.Deserialize<T>(messagePayload, _jsonOptions);

            if (message != null)
            {
                var metadataJson = reader.IsDBNull(11) ? null : reader.GetString(11);
                var metadata = !string.IsNullOrEmpty(metadataJson)
                    ? JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson, _jsonOptions) ?? new()
                    : new Dictionary<string, object>();

                entries.Add(new DeadLetterEntry<T>
                {
                    Id = reader.GetString(0),
                    Message = message,
                    Context = new DeadLetterContext
                    {
                        Reason = reader.GetString(2),
                        Component = reader.GetString(3),
                        RetryCount = reader.GetInt32(4),
                        FailureTime = reader.GetDateTime(5),
                        Exception = reader.IsDBNull(10) ? null : new Exception(reader.GetString(10)),
                        Metadata = metadata
                    },
                    Status = (DeadLetterStatus)reader.GetInt32(6),
                    CreatedAt = reader.GetDateTime(7),
                    RetriedAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                    DiscardedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9)
                });
            }
        }

        return entries;
    }

    public async Task<bool> Retry<T>(string deadLetterId, CancellationToken cancellationToken = default)
        where T : IMessage
    {
        using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            UPDATE {_tableName}
            SET status = @status, retried_at = @retried_at
            WHERE id = @id AND status = @active_status
            """;

        using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = _options.CommandTimeout
        };

        command.Parameters.AddWithValue("@status", (int)DeadLetterStatus.Retried);
        command.Parameters.AddWithValue("@retried_at", _timeProvider.GetUtcNow().DateTime);
        command.Parameters.AddWithValue("@id", deadLetterId);
        command.Parameters.AddWithValue("@active_status", (int)DeadLetterStatus.Active);

        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        return result > 0;
    }

    public async Task<bool> Discard(string deadLetterId, CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            UPDATE {_tableName}
            SET status = @status, discarded_at = @discarded_at
            WHERE id = @id AND status = @active_status
            """;

        using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = _options.CommandTimeout
        };

        command.Parameters.AddWithValue("@status", (int)DeadLetterStatus.Discarded);
        command.Parameters.AddWithValue("@discarded_at", _timeProvider.GetUtcNow().DateTime);
        command.Parameters.AddWithValue("@id", deadLetterId);
        command.Parameters.AddWithValue("@active_status", (int)DeadLetterStatus.Active);

        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        return result > 0;
    }

    public async Task<long> GetDeadLetterCount(CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"SELECT COUNT(*) FROM {_tableName} WHERE status = @status";

        using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = _options.CommandTimeout
        };

        command.Parameters.AddWithValue("@status", (int)DeadLetterStatus.Active);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result ?? 0);
    }

    public async Task<DeadLetterStatistics> GetStatistics(CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var statistics = new DeadLetterStatistics();

        // Get counts by status
        var statusSql = $"""
            SELECT 
                COUNT(*) FILTER (WHERE status = 0) as active_count,
                COUNT(*) FILTER (WHERE status = 1) as retried_count,
                COUNT(*) FILTER (WHERE status = 2) as discarded_count,
                COUNT(*) as total_count
            FROM {_tableName}
            """;

        using (var statusCommand = new NpgsqlCommand(statusSql, connection) { CommandTimeout = _options.CommandTimeout })
        using (var reader = await statusCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                statistics.ActiveCount = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
                statistics.RetriedCount = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                statistics.DiscardedCount = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                statistics.TotalCount = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);
            }
        }

        // Get counts by component
        var componentSql = $"""
            SELECT component, COUNT(*) as count
            FROM {_tableName}
            WHERE status = 0
            GROUP BY component
            """;

        using (var componentCommand = new NpgsqlCommand(componentSql, connection) { CommandTimeout = _options.CommandTimeout })
        using (var reader = await componentCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                statistics.CountByComponent[reader.GetString(0)] = reader.GetInt64(1);
            }
        }

        // Get counts by reason (top 10)
        var reasonSql = $"""
            SELECT reason, COUNT(*) as count
            FROM {_tableName}
            WHERE status = 0
            GROUP BY reason
            ORDER BY count DESC
            LIMIT 10
            """;

        using (var reasonCommand = new NpgsqlCommand(reasonSql, connection) { CommandTimeout = _options.CommandTimeout })
        using (var reader = await reasonCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                statistics.CountByReason[reader.GetString(0)] = reader.GetInt64(1);
            }
        }

        // Get oldest and newest entries
        var dateSql = $"""
            SELECT 
                MIN(created_at) as oldest_entry,
                MAX(created_at) as newest_entry
            FROM {_tableName}
            WHERE status = 0
            """;

        using (var dateCommand = new NpgsqlCommand(dateSql, connection) { CommandTimeout = _options.CommandTimeout })
        using (var reader = await dateCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                statistics.OldestEntry = reader.IsDBNull(0) ? null : reader.GetDateTime(0);
                statistics.NewestEntry = reader.IsDBNull(1) ? null : reader.GetDateTime(1);
            }
        }

        return statistics;
    }
}
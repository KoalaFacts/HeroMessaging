using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace HeroMessaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of dead letter queue using pure ADO.NET
/// </summary>
/// <remarks>
/// Provides persistent storage for failed messages with retry tracking, error details, and status management.
/// Supports categorization by component, reason, and message type for debugging and monitoring.
/// Failed messages can be retried, discarded, or analyzed for patterns using comprehensive statistics.
/// </remarks>
public class PostgreSqlDeadLetterQueue : IDeadLetterQueue
{
    private readonly PostgreSqlStorageOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _tableName;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of PostgreSQL dead letter queue storage
    /// </summary>
    /// <param name="options">Configuration options for PostgreSQL storage including connection string and table names</param>
    /// <param name="timeProvider">Time provider for testable time-based operations</param>
    /// <exception cref="ArgumentNullException">Thrown when options or timeProvider is null</exception>
    /// <remarks>
    /// If AutoCreateTables is enabled in options, database schema is initialized synchronously during construction.
    /// The dead letter table includes indexes for efficient querying by status, message type, component, and failure time.
    /// </remarks>
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

    /// <summary>
    /// Sends a failed message to the dead letter queue with context information
    /// </summary>
    /// <typeparam name="T">The type of message being sent to dead letter</typeparam>
    /// <param name="message">The failed message to store</param>
    /// <param name="context">Context information including failure reason, retry count, and exception details</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The unique identifier assigned to the dead letter entry</returns>
    /// <exception cref="ArgumentNullException">Thrown when message or context is null</exception>
    /// <remarks>
    /// Stores the complete message payload along with failure metadata for debugging and analysis.
    /// The entry is created with Active status and can be retried or discarded later.
    /// Metadata from context is serialized as JSONB for flexible querying.
    /// </remarks>
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

    /// <summary>
    /// Retrieves active dead letter entries of a specific message type
    /// </summary>
    /// <typeparam name="T">The type of messages to retrieve</typeparam>
    /// <param name="limit">Maximum number of entries to retrieve. Default is 100</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Collection of active dead letter entries, ordered by failure time descending (most recent first)</returns>
    /// <remarks>
    /// Only returns entries with Active status (not Retried or Discarded).
    /// Each entry includes the original message, failure context, and status tracking information.
    /// Useful for processing failed messages of a specific type for retry or analysis.
    /// </remarks>
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

    /// <summary>
    /// Marks a dead letter entry as retried, indicating the message has been reprocessed
    /// </summary>
    /// <typeparam name="T">The type of message being retried</typeparam>
    /// <param name="deadLetterId">The unique identifier of the dead letter entry</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the entry was marked as retried; false if not found or already processed</returns>
    /// <exception cref="ArgumentNullException">Thrown when deadLetterId is null</exception>
    /// <remarks>
    /// Updates the entry status to Retried and sets the RetriedAt timestamp.
    /// Only active entries can be marked as retried. The original message should be
    /// resubmitted to processing before calling this method.
    /// </remarks>
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

    /// <summary>
    /// Marks a dead letter entry as discarded, indicating permanent failure
    /// </summary>
    /// <param name="deadLetterId">The unique identifier of the dead letter entry</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the entry was marked as discarded; false if not found or already processed</returns>
    /// <exception cref="ArgumentNullException">Thrown when deadLetterId is null</exception>
    /// <remarks>
    /// Updates the entry status to Discarded and sets the DiscardedAt timestamp.
    /// Only active entries can be discarded. Use this for messages that cannot be
    /// successfully processed and should be permanently archived.
    /// </remarks>
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

    /// <summary>
    /// Gets the total count of active dead letter entries across all message types
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The number of active dead letter entries</returns>
    /// <remarks>
    /// Only counts entries with Active status. Retried and discarded entries are excluded.
    /// Useful for monitoring the health of message processing and identifying systemic issues.
    /// </remarks>
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

    /// <summary>
    /// Retrieves comprehensive statistics about dead letter entries for monitoring and analysis
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Statistics including counts by status, component, reason, and time range information</returns>
    /// <remarks>
    /// Provides aggregated view of dead letter queue health:
    /// - Counts by status (Active, Retried, Discarded, Total)
    /// - Counts by component (which component generated the failures)
    /// - Top 10 failure reasons by count
    /// - Oldest and newest active entry timestamps
    ///
    /// Use these statistics for:
    /// - Identifying problematic components
    /// - Detecting patterns in failures
    /// - Monitoring queue growth over time
    /// - Alerting on threshold breaches
    /// </remarks>
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
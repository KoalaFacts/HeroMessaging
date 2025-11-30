using System.Text.Json;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Npgsql;

namespace HeroMessaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of inbox storage using pure ADO.NET
/// Provides message deduplication and idempotent message processing
/// </summary>
public class PostgreSqlInboxStorage : IInboxStorage
{
    private readonly PostgreSqlStorageOptions _options;
    private readonly IDbConnectionProvider<NpgsqlConnection, NpgsqlTransaction> _connectionProvider;
    private readonly IDbSchemaInitializer _schemaInitializer;
    private readonly IJsonOptionsProvider _jsonOptionsProvider;
    private readonly string _tableName;
    private readonly TimeProvider _timeProvider;
    private readonly IJsonSerializer _jsonSerializer;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public PostgreSqlInboxStorage(
        PostgreSqlStorageOptions options,
        TimeProvider timeProvider,
        IJsonSerializer jsonSerializer,
        IDbConnectionProvider<NpgsqlConnection, NpgsqlTransaction>? connectionProvider = null,
        IDbSchemaInitializer? schemaInitializer = null,
        IJsonOptionsProvider? jsonOptionsProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
        _tableName = _options.GetFullTableName(_options.InboxTableName);

        // Use provided dependencies or create defaults
        _connectionProvider = connectionProvider ?? new PostgreSqlConnectionProvider(options.ConnectionString ?? throw new ArgumentNullException(nameof(options), "ConnectionString cannot be null"));
        _jsonOptionsProvider = jsonOptionsProvider ?? new DefaultJsonOptionsProvider();
        _schemaInitializer = schemaInitializer ?? new PostgreSqlSchemaInitializer(_connectionProvider);
    }

    public PostgreSqlInboxStorage(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        TimeProvider timeProvider,
        IJsonSerializer jsonSerializer,
        IJsonOptionsProvider? jsonOptionsProvider = null)
    {
        _connectionProvider = new PostgreSqlConnectionProvider(connection, transaction);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
        _jsonOptionsProvider = jsonOptionsProvider ?? new DefaultJsonOptionsProvider();

        // Use default options when using shared connection
        _options = new PostgreSqlStorageOptions { ConnectionString = connection.ConnectionString };
        _tableName = _options.GetFullTableName(_options.InboxTableName);
        _schemaInitializer = new PostgreSqlSchemaInitializer(_connectionProvider);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized || !_options.AutoCreateTables) return;

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
        // Create schema if needed
        if (!string.IsNullOrEmpty(_options.Schema))
        {
            await _schemaInitializer.InitializeSchemaAsync(_options.Schema).ConfigureAwait(false);
        }

        // Create table
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

        await _schemaInitializer.ExecuteSchemaScriptAsync(createTableSql).ConfigureAwait(false);
    }

    public async Task<InboxEntry?> AddAsync(IMessage message, InboxOptions options, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = _connectionProvider.GetTransaction();

        try
        {
            var messageId = message.MessageId.ToString();
            var now = _timeProvider.GetUtcNow();

            // Check for duplicates if idempotency is required
            if (options.RequireIdempotency)
            {
                var isDuplicate = await IsDuplicateAsync(messageId, options.DeduplicationWindow, cancellationToken).ConfigureAwait(false);
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
            command.Parameters.AddWithValue("payload", _jsonSerializer.SerializeToString(message, _jsonOptionsProvider.GetOptions()));
            command.Parameters.AddWithValue("source", (object?)options.Source ?? DBNull.Value);
            command.Parameters.AddWithValue("status", "Pending");
            command.Parameters.AddWithValue("received_at", now);
            command.Parameters.AddWithValue("require_idempotency", options.RequireIdempotency);
            command.Parameters.AddWithValue("deduplication_window_minutes", (object?)options.DeduplicationWindow?.TotalMinutes ?? DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

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
            if (!_connectionProvider.IsSharedConnection)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<bool> IsDuplicateAsync(string messageId, TimeSpan? window = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = _connectionProvider.GetTransaction();

        try
        {
            var sql = window.HasValue
                ? $"SELECT COUNT(1) FROM {_tableName} WHERE id = @id AND received_at >= @window_start"
                : $"SELECT COUNT(1) FROM {_tableName} WHERE id = @id";

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", messageId);

            if (window.HasValue)
            {
                var windowStart = _timeProvider.GetUtcNow().Subtract(window.Value);
                command.Parameters.AddWithValue("window_start", windowStart);
            }

            var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            return count > 0;
        }
        finally
        {
            if (!_connectionProvider.IsSharedConnection)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<InboxEntry?> GetAsync(string messageId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = _connectionProvider.GetTransaction();

        try
        {

            var sql = $"""
                SELECT message_type, payload, source, status, received_at, processed_at, error, require_idempotency, deduplication_window_minutes
                FROM {_tableName}
                WHERE id = @id
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", messageId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var messageType = reader.GetString(0);
                var payload = reader.GetString(1);
                var source = reader.IsDBNull(2) ? null : reader.GetString(2);
                var status = Enum.Parse<InboxStatus>(reader.GetString(3));
                var receivedAt = reader.GetDateTime(4);
                var processedAt = reader.IsDBNull(5) ? (DateTimeOffset?)null : reader.GetDateTime(5);
                var error = reader.IsDBNull(6) ? null : reader.GetString(6);
                var requireIdempotency = reader.GetBoolean(7);
                var deduplicationWindowMinutes = reader.IsDBNull(8) ? (int?)null : reader.GetInt32(8);

                var message = _jsonSerializer.DeserializeFromString<IMessage>(payload, _jsonOptionsProvider.GetOptions());

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

    public async Task<bool> MarkProcessedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = _connectionProvider.GetTransaction();

        try
        {

            var sql = $"""
                UPDATE {_tableName}
                SET status = @status, processed_at = @processed_at
                WHERE id = @id
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", messageId);
            command.Parameters.AddWithValue("status", "Processed");
            command.Parameters.AddWithValue("processed_at", _timeProvider.GetUtcNow());

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return rowsAffected > 0;
        }
        finally
        {
        }
    }

    public async Task<bool> MarkFailedAsync(string messageId, string error, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = _connectionProvider.GetTransaction();

        try
        {

            var sql = $"""
                UPDATE {_tableName}
                SET status = @status, processed_at = @processed_at, error = @error
                WHERE id = @id
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", messageId);
            command.Parameters.AddWithValue("status", "Failed");
            command.Parameters.AddWithValue("processed_at", _timeProvider.GetUtcNow());
            command.Parameters.AddWithValue("error", error);

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return rowsAffected > 0;
        }
        finally
        {
        }
    }

    public async Task<IEnumerable<InboxEntry>> GetPendingAsync(InboxQuery query, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = _connectionProvider.GetTransaction();

        try
        {

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
            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var messageId = reader.GetString(0);
                var messageType = reader.GetString(1);
                var payload = reader.GetString(2);
                var source = reader.IsDBNull(3) ? null : reader.GetString(3);
                var status = Enum.Parse<InboxStatus>(reader.GetString(4));
                var receivedAt = reader.GetDateTime(5);
                var processedAt = reader.IsDBNull(6) ? (DateTimeOffset?)null : reader.GetDateTime(6);
                var error = reader.IsDBNull(7) ? null : reader.GetString(7);
                var requireIdempotency = reader.GetBoolean(8);
                var deduplicationWindowMinutes = reader.IsDBNull(9) ? (int?)null : reader.GetInt32(9);

                var message = _jsonSerializer.DeserializeFromString<IMessage>(payload, _jsonOptionsProvider.GetOptions());

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
        }
    }

    public async Task<IEnumerable<InboxEntry>> GetUnprocessedAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        var query = new InboxQuery
        {
            Status = InboxStatus.Pending,
            Limit = limit
        };

        return await GetPendingAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> GetUnprocessedCountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = _connectionProvider.GetTransaction();

        try
        {

            var sql = $"SELECT COUNT(1) FROM {_tableName} WHERE status = 'Pending'";

            using var command = new NpgsqlCommand(sql, connection, transaction);
            var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            return count;
        }
        finally
        {
        }
    }

    public async Task CleanupOldEntriesAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = _connectionProvider.GetTransaction();

        try
        {

            var cutoffTime = _timeProvider.GetUtcNow().Subtract(olderThan);

            var sql = $"""
                DELETE FROM {_tableName}
                WHERE received_at < @cutoff_time
                AND status IN ('Processed', 'Failed')
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("cutoff_time", cutoffTime);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
        }
    }
}

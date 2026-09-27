using System.Text.Json;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Npgsql;
using NpgsqlTypes;

namespace HeroMessaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of outbox storage using pure ADO.NET
/// Provides transactional outbox pattern for reliable message delivery
/// </summary>
public class PostgreSqlOutboxStorage : IExternalOutboxStorage
{
    /// <inheritdoc />
    public bool SupportsExternalClaims => !_connectionProvider.IsSharedConnection;

    private readonly PostgreSqlStorageOptions _options;
    private readonly IDbConnectionProvider<NpgsqlConnection, NpgsqlTransaction> _connectionProvider;
    private readonly IDbSchemaInitializer _schemaInitializer;
    private readonly IJsonOptionsProvider _jsonOptionsProvider;
    private readonly string _tableName;
    private readonly TimeProvider _timeProvider;
    private readonly IJsonSerializer _jsonSerializer;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlOutboxStorage"/> class.
    /// </summary>

    public PostgreSqlOutboxStorage(
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
        _tableName = _options.GetFullTableName(_options.OutboxTableName);

        // Use provided dependencies or create defaults
        _connectionProvider = connectionProvider ?? new PostgreSqlConnectionProvider(options.ConnectionString ?? throw new ArgumentNullException(nameof(options), "ConnectionString cannot be null"));
        _jsonOptionsProvider = jsonOptionsProvider ?? new DefaultJsonOptionsProvider();
        _schemaInitializer = schemaInitializer ?? new PostgreSqlSchemaInitializer(_connectionProvider);
    }
    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlOutboxStorage"/> class.
    /// </summary>

    public PostgreSqlOutboxStorage(
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
        _tableName = _options.GetFullTableName(_options.OutboxTableName);
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
    /// <summary>
    /// Executes initialize database.
    /// </summary>

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
                destination VARCHAR(200),
                status VARCHAR(50) NOT NULL DEFAULT 'Pending',
                retry_count INTEGER NOT NULL DEFAULT 0,
                max_retries INTEGER NOT NULL DEFAULT 3,
                retry_delay_ms BIGINT,
                created_at TIMESTAMPTZ NOT NULL,
                processed_at TIMESTAMPTZ,
                next_retry_at TIMESTAMPTZ,
                lease_token UUID,
                lease_expires_at TIMESTAMPTZ,
                last_error TEXT
            );

            ALTER TABLE {_tableName} ADD COLUMN IF NOT EXISTS retry_delay_ms BIGINT;
            ALTER TABLE {_tableName} ADD COLUMN IF NOT EXISTS lease_token UUID;
            ALTER TABLE {_tableName} ADD COLUMN IF NOT EXISTS lease_expires_at TIMESTAMPTZ;

            CREATE INDEX IF NOT EXISTS idx_{_options.OutboxTableName}_status ON {_tableName}(status);
            CREATE INDEX IF NOT EXISTS idx_{_options.OutboxTableName}_next_retry ON {_tableName}(next_retry_at) WHERE status = 'Pending';
            CREATE INDEX IF NOT EXISTS idx_{_options.OutboxTableName}_created_at ON {_tableName}(created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_{_options.OutboxTableName}_lease ON {_tableName}(lease_expires_at) WHERE status = 'Processing' AND destination IS NOT NULL;
            """;

        await _schemaInitializer.ExecuteSchemaScriptAsync(createTableSql).ConfigureAwait(false);
    }
    /// <summary>
    /// Executes add async.
    /// </summary>

    public async Task<OutboxEntry> AddAsync(IMessage message, OutboxOptions options, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = _connectionProvider.GetTransaction();

        try
        {

            var entryId = Guid.NewGuid().ToString();
            var now = _timeProvider.GetUtcNow();

            var sql = $"""
                INSERT INTO {_tableName} (id, message_type, payload, destination, status, retry_count, max_retries, retry_delay_ms, created_at)
                VALUES (@id, @message_type, @payload::jsonb, @destination, @status, @retry_count, @max_retries, @retry_delay_ms, @created_at)
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", entryId);
            var messageType = message.GetType();
            command.Parameters.AddWithValue("message_type", messageType.AssemblyQualifiedName ?? throw new InvalidOperationException("Message type cannot be resolved."));
            command.Parameters.AddWithValue("payload", _jsonSerializer.SerializeToString(message, messageType, _jsonOptionsProvider.GetOptions()));
            command.Parameters.AddWithValue("destination", (object?)options.Destination ?? DBNull.Value);
            command.Parameters.AddWithValue("status", "Pending");
            command.Parameters.AddWithValue("retry_count", 0);
            command.Parameters.AddWithValue("max_retries", options.MaxRetries);
            command.Parameters.Add(new NpgsqlParameter("retry_delay_ms", NpgsqlDbType.Bigint)
            {
                Value = options.RetryDelay is null ? DBNull.Value : (long)options.RetryDelay.Value.TotalMilliseconds
            });
            command.Parameters.AddWithValue("created_at", now);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

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
            if (!_connectionProvider.IsSharedConnection)
                await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
    /// <summary>
    /// Executes get pending async.
    /// </summary>

    public Task<IEnumerable<OutboxEntry>> GetPendingAsync(OutboxQuery query, CancellationToken cancellationToken = default)
        => QueryAsync(query, localOnly: false, cancellationToken);

    /// <inheritdoc />
    public Task<IEnumerable<OutboxEntry>> GetLocalPendingAsync(int limit, CancellationToken cancellationToken = default)
        => QueryAsync(new OutboxQuery { Status = OutboxStatus.Pending, Limit = limit }, localOnly: true, cancellationToken);

    private async Task<IEnumerable<OutboxEntry>> QueryAsync(OutboxQuery query, bool localOnly, CancellationToken cancellationToken)
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
                whereClauses.Add("created_at < @older_than");
                parameters.Add(new NpgsqlParameter("older_than", query.OlderThan.Value.ToUniversalTime()));
            }

            if (query.NewerThan.HasValue)
            {
                whereClauses.Add("created_at > @newer_than");
                parameters.Add(new NpgsqlParameter("newer_than", query.NewerThan.Value.ToUniversalTime()));
            }

            if (localOnly)
            {
                whereClauses.Add("destination IS NULL");
                whereClauses.Add("(next_retry_at IS NULL OR next_retry_at <= @now)");
                parameters.Add(new NpgsqlParameter("now", _timeProvider.GetUtcNow()));
            }

            var whereClause = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

            var sql = $"""
                SELECT id, message_type, payload, destination, status, retry_count, max_retries, created_at, processed_at, next_retry_at, last_error, retry_delay_ms
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
            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                entries.Add(ReadEntry(reader));
            }

            return entries;
        }
        finally
        {
            if (!_connectionProvider.IsSharedConnection)
                await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private OutboxEntry ReadEntry(NpgsqlDataReader reader)
    {
        var messageTypeName = reader.GetString(1);
        var messageType = Type.GetType(messageTypeName)
            ?? throw new InvalidOperationException($"Unable to resolve outbox message type: {messageTypeName}");
        var message = _jsonSerializer.DeserializeFromString(reader.GetString(2), messageType, _jsonOptionsProvider.GetOptions()) as IMessage
            ?? throw new InvalidOperationException($"Unable to deserialize outbox message type: {messageTypeName}");

        return new OutboxEntry
        {
            Id = reader.GetString(0),
            Message = message,
            Options = new OutboxOptions
            {
                Destination = reader.IsDBNull(3) ? null : reader.GetString(3),
                MaxRetries = reader.GetInt32(6),
                RetryDelay = reader.IsDBNull(11) ? null : TimeSpan.FromMilliseconds(reader.GetInt64(11))
            },
            Status = Enum.Parse<OutboxStatus>(reader.GetString(4)),
            RetryCount = reader.GetInt32(5),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(7),
            ProcessedAt = reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            NextRetryAt = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
            LastError = reader.IsDBNull(10) ? null : reader.GetString(10)
        };
    }
    /// <summary>
    /// Executes get pending async.
    /// </summary>

    public async Task<IEnumerable<OutboxEntry>> GetPendingAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        var query = new OutboxQuery
        {
            Status = OutboxStatus.Pending,
            Limit = limit
        };

        return await GetPendingAsync(query, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxLease>> ClaimExternalAsync(int limit, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (_connectionProvider.IsSharedConnection)
            throw new InvalidOperationException("External outbox claims require a standalone connection.");

        await using var connection = await _connectionProvider.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();
        var token = Guid.NewGuid();
        var sql = $"""
            WITH candidates AS (
                SELECT id FROM {_tableName}
                WHERE destination IS NOT NULL
                  AND ((status = 'Pending' AND (next_retry_at IS NULL OR next_retry_at <= @now))
                       OR (status = 'Processing' AND lease_expires_at <= @now))
                ORDER BY created_at ASC
                LIMIT @limit
                FOR UPDATE SKIP LOCKED
            )
            UPDATE {_tableName} AS entry
            SET status = 'Processing', lease_token = @token, lease_expires_at = @lease_expires_at,
                next_retry_at = NULL
            FROM candidates
            WHERE entry.id = candidates.id
            RETURNING entry.id, entry.message_type, entry.payload, entry.destination, entry.status,
                      entry.retry_count, entry.max_retries, entry.created_at, entry.processed_at,
                      entry.next_retry_at, entry.last_error, entry.retry_delay_ms
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("token", token);
        command.Parameters.AddWithValue("lease_expires_at", now.Add(leaseDuration));

        var claims = new List<OutboxLease>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            claims.Add(new OutboxLease(ReadEntry(reader), token));
        return claims;
    }

    /// <inheritdoc />
    public async Task<bool> RenewExternalAsync(string entryId, Guid token, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (_connectionProvider.IsSharedConnection)
            throw new InvalidOperationException("External outbox leases require a standalone connection.");

        await using var connection = await _connectionProvider.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();
        var sql = $"""
            UPDATE {_tableName}
            SET lease_expires_at = @lease_expires_at
            WHERE id = @id AND lease_token = @token AND lease_expires_at > @now
              AND status = 'Processing' AND destination IS NOT NULL
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", entryId);
        command.Parameters.AddWithValue("token", token);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("lease_expires_at", now.Add(leaseDuration));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    /// <inheritdoc />
    public Task<bool> CompleteExternalAsync(string entryId, Guid token, CancellationToken cancellationToken = default)
        => UpdateExternalAsync(entryId, token, OutboxStatus.Processed, null, null, null, cancellationToken);

    /// <inheritdoc />
    public Task<bool> RetryExternalAsync(string entryId, Guid token, int retryCount, DateTimeOffset nextRetry, string error, CancellationToken cancellationToken = default)
        => UpdateExternalAsync(entryId, token, OutboxStatus.Pending, retryCount, nextRetry, error, cancellationToken);

    /// <inheritdoc />
    public Task<bool> FailExternalAsync(string entryId, Guid token, int retryCount, string error, CancellationToken cancellationToken = default)
        => UpdateExternalAsync(entryId, token, OutboxStatus.Failed, retryCount, null, error, cancellationToken);

    private async Task<bool> UpdateExternalAsync(
        string entryId, Guid token, OutboxStatus status, int? retryCount,
        DateTimeOffset? nextRetry, string? error, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (_connectionProvider.IsSharedConnection)
            throw new InvalidOperationException("External outbox leases require a standalone connection.");

        await using var connection = await _connectionProvider.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();
        var sql = $"""
            UPDATE {_tableName}
            SET status = @status, retry_count = COALESCE(@retry_count, retry_count),
                next_retry_at = @next_retry_at, last_error = @last_error,
                processed_at = @processed_at, lease_token = NULL, lease_expires_at = NULL
            WHERE id = @id AND lease_token = @token AND lease_expires_at > @now
              AND status = 'Processing' AND destination IS NOT NULL
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", entryId);
        command.Parameters.AddWithValue("token", token);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("status", status.ToString());
        command.Parameters.Add(new NpgsqlParameter("retry_count", NpgsqlDbType.Integer) { Value = retryCount is null ? DBNull.Value : retryCount.Value });
        command.Parameters.Add(new NpgsqlParameter("next_retry_at", NpgsqlDbType.TimestampTz) { Value = nextRetry is null ? DBNull.Value : nextRetry.Value.ToUniversalTime() });
        command.Parameters.Add(new NpgsqlParameter("last_error", NpgsqlDbType.Text) { Value = (object?)error ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("processed_at", NpgsqlDbType.TimestampTz)
        {
            Value = status is OutboxStatus.Processed or OutboxStatus.Failed ? now : DBNull.Value
        });
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }
    /// <summary>
    /// Executes mark processed async.
    /// </summary>

    public async Task<bool> MarkProcessedAsync(string entryId, CancellationToken cancellationToken = default)
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
            command.Parameters.AddWithValue("id", entryId);
            command.Parameters.AddWithValue("status", "Processed");
            command.Parameters.AddWithValue("processed_at", _timeProvider.GetUtcNow());

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return rowsAffected > 0;
        }
        finally
        {
            if (!_connectionProvider.IsSharedConnection)
                await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
    /// <summary>
    /// Executes mark failed async.
    /// </summary>

    public async Task<bool> MarkFailedAsync(string entryId, string error, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = _connectionProvider.GetTransaction();

        try
        {

            var sql = $"""
                UPDATE {_tableName}
                SET status = @status, processed_at = @processed_at, last_error = @last_error
                WHERE id = @id
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", entryId);
            command.Parameters.AddWithValue("status", "Failed");
            command.Parameters.AddWithValue("processed_at", _timeProvider.GetUtcNow());
            command.Parameters.AddWithValue("last_error", error);

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return rowsAffected > 0;
        }
        finally
        {
            if (!_connectionProvider.IsSharedConnection)
                await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
    /// <summary>
    /// Executes update retry count async.
    /// </summary>

    public async Task<bool> UpdateRetryCountAsync(string entryId, int retryCount, DateTimeOffset? nextRetry = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = _connectionProvider.GetTransaction();

        try
        {

            var sql = $"""
                UPDATE {_tableName}
                SET retry_count = @retry_count, next_retry_at = @next_retry_at
                WHERE id = @id
                """;

            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", entryId);
            command.Parameters.AddWithValue("retry_count", retryCount);
            command.Parameters.AddWithValue("next_retry_at", (object?)nextRetry?.ToUniversalTime() ?? DBNull.Value);

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return rowsAffected > 0;
        }
        finally
        {
            if (!_connectionProvider.IsSharedConnection)
                await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
    /// <summary>
    /// Executes get pending count async.
    /// </summary>

    public async Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
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
            if (!_connectionProvider.IsSharedConnection)
                await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
    /// <summary>
    /// Executes get failed async.
    /// </summary>

    public async Task<IEnumerable<OutboxEntry>> GetFailedAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        var query = new OutboxQuery
        {
            Status = OutboxStatus.Failed,
            Limit = limit
        };

        return await GetPendingAsync(query, cancellationToken).ConfigureAwait(false);
    }
}

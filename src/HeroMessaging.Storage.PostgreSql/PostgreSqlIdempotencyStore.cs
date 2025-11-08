using System.Text.Json;
using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Utilities;
using Npgsql;

namespace HeroMessaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of <see cref="IIdempotencyStore"/> for persistent, distributed idempotency tracking.
/// </summary>
/// <remarks>
/// <para>
/// This implementation uses PostgreSQL for durable storage of idempotency responses,
/// enabling exactly-once processing semantics across multiple application instances and restarts.
/// </para>
/// <para>
/// Key features:
/// </para>
/// <list type="bullet">
/// <item><description>Persistent storage using PostgreSQL with Npgsql</description></item>
/// <item><description>JSONB column type for efficient JSON storage and querying</description></item>
/// <item><description>Upsert operations using ON CONFLICT clause for atomic updates</description></item>
/// <item><description>Automatic expiration handling through indexed queries</description></item>
/// <item><description>Thread-safe operations across multiple application instances</description></item>
/// </list>
/// <para>
/// Table schema requirements (snake_case naming convention for PostgreSQL):
/// </para>
/// <code>
/// CREATE TABLE idempotency_responses (
///     idempotency_key VARCHAR(450) NOT NULL PRIMARY KEY,
///     status SMALLINT NOT NULL,
///     success_result JSONB NULL,
///     failure_type VARCHAR(500) NULL,
///     failure_message TEXT NULL,
///     failure_stack_trace TEXT NULL,
///     stored_at TIMESTAMP NOT NULL,
///     expires_at TIMESTAMP NOT NULL
/// );
/// CREATE INDEX idx_idempotency_responses_expires_at ON idempotency_responses(expires_at);
/// </code>
/// <para>
/// Performance characteristics:
/// </para>
/// <list type="bullet">
/// <item><description>Get: O(log n) via primary key index</description></item>
/// <item><description>Store: O(log n) via ON CONFLICT upsert operation</description></item>
/// <item><description>Exists: O(log n) via indexed query with COUNT optimization</description></item>
/// <item><description>Cleanup: O(m log n) where m is expired entries count</description></item>
/// </list>
/// <para>
/// PostgreSQL-specific optimizations:
/// </para>
/// <list type="bullet">
/// <item><description>Uses JSONB for efficient binary JSON storage with indexing support</description></item>
/// <item><description>Positional parameters ($1, $2) instead of named parameters</description></item>
/// <item><description>ON CONFLICT DO UPDATE for atomic upsert operations</description></item>
/// <item><description>RETURNING clause for efficient cleanup row counting</description></item>
/// </list>
/// </remarks>
public sealed class PostgreSqlIdempotencyStore : IIdempotencyStore
{
    private readonly string _connectionString;
    private readonly TimeProvider _timeProvider;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlIdempotencyStore"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="timeProvider">The time provider for timestamp management and expiration checks.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="connectionString"/> or <paramref name="timeProvider"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="connectionString"/> is empty or whitespace.
    /// </exception>
    public PostgreSqlIdempotencyStore(string connectionString, TimeProvider timeProvider)
    {
        if (connectionString == null)
            throw new ArgumentNullException(nameof(connectionString));
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be empty or whitespace.", nameof(connectionString));

        _connectionString = connectionString;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            WriteIndented = false
        };
    }

    /// <inheritdoc />
    public async ValueTask<IdempotencyResponse?> GetAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (idempotencyKey == null)
            throw new ArgumentNullException(nameof(idempotencyKey));
        if (string.IsNullOrEmpty(idempotencyKey))
            throw new ArgumentException("Idempotency key cannot be empty.", nameof(idempotencyKey));

        const string sql = @"
            SELECT
                idempotency_key,
                status,
                success_result,
                failure_type,
                failure_message,
                failure_stack_trace,
                stored_at,
                expires_at
            FROM idempotency_responses
            WHERE idempotency_key = $1
                AND expires_at > $2";

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue(idempotencyKey);
            command.Parameters.AddWithValue(_timeProvider.GetUtcNow().UtcDateTime);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return new IdempotencyResponse
            {
                IdempotencyKey = reader.GetString(0),
                Status = (IdempotencyStatus)reader.GetInt16(1),
                SuccessResult = reader.IsDBNull(2) ? null : DeserializeResult(reader.GetString(2)),
                FailureType = reader.IsDBNull(3) ? null : reader.GetString(3),
                FailureMessage = reader.IsDBNull(4) ? null : reader.GetString(4),
                FailureStackTrace = reader.IsDBNull(5) ? null : reader.GetString(5),
                StoredAt = reader.GetDateTime(6),
                ExpiresAt = reader.GetDateTime(7)
            };
        }
        catch (NpgsqlException ex)
        {
            throw new InvalidOperationException(
                $"Failed to retrieve idempotency response from PostgreSQL for key '{idempotencyKey}'. " +
                $"Error: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask StoreSuccessAsync(
        string idempotencyKey,
        object? result,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        if (idempotencyKey == null)
            throw new ArgumentNullException(nameof(idempotencyKey));
        if (string.IsNullOrEmpty(idempotencyKey))
            throw new ArgumentException("Idempotency key cannot be empty.", nameof(idempotencyKey));

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = now.Add(ttl);
        var serializedResult = SerializeResult(result);

        const string sql = @"
            INSERT INTO idempotency_responses
                (idempotency_key, status, success_result, failure_type, failure_message, failure_stack_trace, stored_at, expires_at)
            VALUES
                ($1, $2, $3::jsonb, NULL, NULL, NULL, $4, $5)
            ON CONFLICT (idempotency_key)
            DO UPDATE SET
                status = EXCLUDED.status,
                success_result = EXCLUDED.success_result,
                failure_type = NULL,
                failure_message = NULL,
                failure_stack_trace = NULL,
                stored_at = EXCLUDED.stored_at,
                expires_at = EXCLUDED.expires_at";

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue(idempotencyKey);
            command.Parameters.AddWithValue((short)IdempotencyStatus.Success);
            command.Parameters.AddWithValue((object?)serializedResult ?? DBNull.Value);
            command.Parameters.AddWithValue(now);
            command.Parameters.AddWithValue(expiresAt);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (NpgsqlException ex)
        {
            throw new InvalidOperationException(
                $"Failed to store success response in PostgreSQL for key '{idempotencyKey}'. " +
                $"Error: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask StoreFailureAsync(
        string idempotencyKey,
        Exception exception,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        if (idempotencyKey == null)
            throw new ArgumentNullException(nameof(idempotencyKey));
        if (string.IsNullOrEmpty(idempotencyKey))
            throw new ArgumentException("Idempotency key cannot be empty.", nameof(idempotencyKey));
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = now.Add(ttl);

        const string sql = @"
            INSERT INTO idempotency_responses
                (idempotency_key, status, success_result, failure_type, failure_message, failure_stack_trace, stored_at, expires_at)
            VALUES
                ($1, $2, NULL, $3, $4, $5, $6, $7)
            ON CONFLICT (idempotency_key)
            DO UPDATE SET
                status = EXCLUDED.status,
                success_result = NULL,
                failure_type = EXCLUDED.failure_type,
                failure_message = EXCLUDED.failure_message,
                failure_stack_trace = EXCLUDED.failure_stack_trace,
                stored_at = EXCLUDED.stored_at,
                expires_at = EXCLUDED.expires_at";

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue(idempotencyKey);
            command.Parameters.AddWithValue((short)IdempotencyStatus.Failure);
            command.Parameters.AddWithValue((object?)exception.GetType().FullName ?? DBNull.Value);
            command.Parameters.AddWithValue((object?)exception.Message ?? DBNull.Value);
            command.Parameters.AddWithValue((object?)exception.StackTrace ?? DBNull.Value);
            command.Parameters.AddWithValue(now);
            command.Parameters.AddWithValue(expiresAt);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (NpgsqlException ex)
        {
            throw new InvalidOperationException(
                $"Failed to store failure response in PostgreSQL for key '{idempotencyKey}'. " +
                $"Error: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> ExistsAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (idempotencyKey == null)
            throw new ArgumentNullException(nameof(idempotencyKey));
        if (string.IsNullOrEmpty(idempotencyKey))
            throw new ArgumentException("Idempotency key cannot be empty.", nameof(idempotencyKey));

        const string sql = @"
            SELECT COUNT(1)
            FROM idempotency_responses
            WHERE idempotency_key = $1
                AND expires_at > $2";

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue(idempotencyKey);
            command.Parameters.AddWithValue(_timeProvider.GetUtcNow().UtcDateTime);

            var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            return count > 0;
        }
        catch (NpgsqlException ex)
        {
            throw new InvalidOperationException(
                $"Failed to check existence in PostgreSQL for key '{idempotencyKey}'. " +
                $"Error: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            WITH deleted AS (
                DELETE FROM idempotency_responses
                WHERE expires_at <= $1
                RETURNING *
            )
            SELECT COUNT(*) FROM deleted";

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue(_timeProvider.GetUtcNow().UtcDateTime);

            var rowCount = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            return rowCount;
        }
        catch (NpgsqlException ex)
        {
            throw new InvalidOperationException(
                $"Failed to cleanup expired entries in PostgreSQL. Error: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Serializes a result object to JSON string.
    /// </summary>
    /// <param name="result">The result object to serialize.</param>
    /// <returns>The JSON string representation, or null if the result is null.</returns>
    private string? SerializeResult(object? result)
    {
        if (result == null)
            return null;

        try
        {
            return JsonSerializationHelper.SerializeToString(result, _jsonOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to serialize result of type {result.GetType().FullName} to JSON. " +
                $"Ensure the type is serializable. Error: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Deserializes a JSON string to an object.
    /// </summary>
    /// <param name="json">The JSON string to deserialize.</param>
    /// <returns>The deserialized object, or null if the JSON is null or empty.</returns>
    private object? DeserializeResult(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializationHelper.DeserializeFromString<object>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize JSON result. The stored data may be corrupted. " +
                $"Error: {ex.Message}", ex);
        }
    }
}

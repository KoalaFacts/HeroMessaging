using System.Data;
using System.Text.Json;
using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Utilities;
using Microsoft.Data.SqlClient;

namespace HeroMessaging.Storage.SqlServer;

/// <summary>
/// SQL Server implementation of <see cref="IIdempotencyStore"/> for persistent, distributed idempotency tracking.
/// </summary>
/// <remarks>
/// <para>
/// This implementation uses SQL Server for durable storage of idempotency responses,
/// enabling exactly-once processing semantics across multiple application instances and restarts.
/// </para>
/// <para>
/// Key features:
/// </para>
/// <list type="bullet">
/// <item><description>Persistent storage using SQL Server with ADO.NET</description></item>
/// <item><description>JSON serialization for successful results using System.Text.Json</description></item>
/// <item><description>Upsert operations using MERGE statement for atomic updates</description></item>
/// <item><description>Automatic expiration handling through indexed queries</description></item>
/// <item><description>Thread-safe operations across multiple application instances</description></item>
/// </list>
/// <para>
/// Table schema requirements:
/// </para>
/// <code>
/// CREATE TABLE IdempotencyResponses (
///     IdempotencyKey NVARCHAR(450) NOT NULL PRIMARY KEY,
///     Status TINYINT NOT NULL,
///     SuccessResult NVARCHAR(MAX) NULL,
///     FailureType NVARCHAR(500) NULL,
///     FailureMessage NVARCHAR(MAX) NULL,
///     FailureStackTrace NVARCHAR(MAX) NULL,
///     StoredAt DATETIME2 NOT NULL,
///     ExpiresAt DATETIME2 NOT NULL,
///     INDEX IX_IdempotencyResponses_ExpiresAt NONCLUSTERED (ExpiresAt ASC)
/// );
/// </code>
/// <para>
/// Performance characteristics:
/// </para>
/// <list type="bullet">
/// <item><description>Get: O(log n) via primary key index</description></item>
/// <item><description>Store: O(log n) via MERGE upsert operation</description></item>
/// <item><description>Exists: O(log n) via indexed query with COUNT optimization</description></item>
/// <item><description>Cleanup: O(m log n) where m is expired entries count</description></item>
/// </list>
/// </remarks>
public sealed class SqlServerIdempotencyStore : IIdempotencyStore
{
    private readonly string _connectionString;
    private readonly TimeProvider _timeProvider;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IJsonSerializer _jsonSerializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerIdempotencyStore"/> class.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="timeProvider">The time provider for timestamp management and expiration checks.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="connectionString"/> or <paramref name="timeProvider"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="connectionString"/> is empty or whitespace.
    /// </exception>
    public SqlServerIdempotencyStore(string connectionString, TimeProvider timeProvider, IJsonSerializer jsonSerializer)
    {
        if (connectionString == null)
            throw new ArgumentNullException(nameof(connectionString));
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be empty or whitespace.", nameof(connectionString));

        _connectionString = connectionString;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
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
                IdempotencyKey,
                Status,
                SuccessResult,
                FailureType,
                FailureMessage,
                FailureStackTrace,
                StoredAt,
                ExpiresAt
            FROM IdempotencyResponses WITH (NOLOCK)
            WHERE IdempotencyKey = @IdempotencyKey
                AND ExpiresAt > @Now";

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@IdempotencyKey", SqlDbType.NVarChar, 450).Value = idempotencyKey;
            command.Parameters.Add("@Now", SqlDbType.DateTime2).Value = _timeProvider.GetUtcNow().UtcDateTime;

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return new IdempotencyResponse
            {
                IdempotencyKey = reader.GetString(0),
                Status = (IdempotencyStatus)reader.GetByte(1),
                SuccessResult = reader.IsDBNull(2) ? null : DeserializeResult(reader.GetString(2)),
                FailureType = reader.IsDBNull(3) ? null : reader.GetString(3),
                FailureMessage = reader.IsDBNull(4) ? null : reader.GetString(4),
                FailureStackTrace = reader.IsDBNull(5) ? null : reader.GetString(5),
                StoredAt = reader.GetDateTime(6),
                ExpiresAt = reader.GetDateTime(7)
            };
        }
        catch (SqlException ex)
        {
            throw new InvalidOperationException(
                $"Failed to retrieve idempotency response from SQL Server for key '{idempotencyKey}'. " +
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
            MERGE IdempotencyResponses AS target
            USING (SELECT @IdempotencyKey AS IdempotencyKey) AS source
            ON target.IdempotencyKey = source.IdempotencyKey
            WHEN MATCHED THEN
                UPDATE SET
                    Status = @Status,
                    SuccessResult = @SuccessResult,
                    FailureType = NULL,
                    FailureMessage = NULL,
                    FailureStackTrace = NULL,
                    StoredAt = @StoredAt,
                    ExpiresAt = @ExpiresAt
            WHEN NOT MATCHED THEN
                INSERT (IdempotencyKey, Status, SuccessResult, FailureType, FailureMessage, FailureStackTrace, StoredAt, ExpiresAt)
                VALUES (@IdempotencyKey, @Status, @SuccessResult, NULL, NULL, NULL, @StoredAt, @ExpiresAt);";

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@IdempotencyKey", SqlDbType.NVarChar, 450).Value = idempotencyKey;
            command.Parameters.Add("@Status", SqlDbType.TinyInt).Value = (byte)IdempotencyStatus.Success;
            command.Parameters.Add("@SuccessResult", SqlDbType.NVarChar).Value = (object?)serializedResult ?? DBNull.Value;
            command.Parameters.Add("@StoredAt", SqlDbType.DateTime2).Value = now;
            command.Parameters.Add("@ExpiresAt", SqlDbType.DateTime2).Value = expiresAt;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            throw new InvalidOperationException(
                $"Failed to store success response in SQL Server for key '{idempotencyKey}'. " +
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
            MERGE IdempotencyResponses AS target
            USING (SELECT @IdempotencyKey AS IdempotencyKey) AS source
            ON target.IdempotencyKey = source.IdempotencyKey
            WHEN MATCHED THEN
                UPDATE SET
                    Status = @Status,
                    SuccessResult = NULL,
                    FailureType = @FailureType,
                    FailureMessage = @FailureMessage,
                    FailureStackTrace = @FailureStackTrace,
                    StoredAt = @StoredAt,
                    ExpiresAt = @ExpiresAt
            WHEN NOT MATCHED THEN
                INSERT (IdempotencyKey, Status, SuccessResult, FailureType, FailureMessage, FailureStackTrace, StoredAt, ExpiresAt)
                VALUES (@IdempotencyKey, @Status, NULL, @FailureType, @FailureMessage, @FailureStackTrace, @StoredAt, @ExpiresAt);";

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@IdempotencyKey", SqlDbType.NVarChar, 450).Value = idempotencyKey;
            command.Parameters.Add("@Status", SqlDbType.TinyInt).Value = (byte)IdempotencyStatus.Failure;
            command.Parameters.Add("@FailureType", SqlDbType.NVarChar, 500).Value = (object?)exception.GetType().FullName ?? DBNull.Value;
            command.Parameters.Add("@FailureMessage", SqlDbType.NVarChar).Value = (object?)exception.Message ?? DBNull.Value;
            command.Parameters.Add("@FailureStackTrace", SqlDbType.NVarChar).Value = (object?)exception.StackTrace ?? DBNull.Value;
            command.Parameters.Add("@StoredAt", SqlDbType.DateTime2).Value = now;
            command.Parameters.Add("@ExpiresAt", SqlDbType.DateTime2).Value = expiresAt;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            throw new InvalidOperationException(
                $"Failed to store failure response in SQL Server for key '{idempotencyKey}'. " +
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
            FROM IdempotencyResponses WITH (NOLOCK)
            WHERE IdempotencyKey = @IdempotencyKey
                AND ExpiresAt > @Now";

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@IdempotencyKey", SqlDbType.NVarChar, 450).Value = idempotencyKey;
            command.Parameters.Add("@Now", SqlDbType.DateTime2).Value = _timeProvider.GetUtcNow().UtcDateTime;

            var count = (int)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return count > 0;
        }
        catch (SqlException ex)
        {
            throw new InvalidOperationException(
                $"Failed to check existence in SQL Server for key '{idempotencyKey}'. " +
                $"Error: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            DELETE FROM IdempotencyResponses
            WHERE ExpiresAt <= @Now;
            SELECT @@ROWCOUNT;";

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Now", SqlDbType.DateTime2).Value = _timeProvider.GetUtcNow().UtcDateTime;

            var rowCount = (int)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return rowCount;
        }
        catch (SqlException ex)
        {
            throw new InvalidOperationException(
                $"Failed to cleanup expired entries in SQL Server. Error: {ex.Message}", ex);
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
            return _jsonSerializer.SerializeToString(result, _jsonOptions);
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
            return _jsonSerializer.DeserializeFromString<object>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize JSON result. The stored data may be corrupted. " +
                $"Error: {ex.Message}", ex);
        }
    }
}

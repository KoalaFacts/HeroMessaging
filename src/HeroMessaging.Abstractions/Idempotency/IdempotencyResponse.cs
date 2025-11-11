namespace HeroMessaging.Abstractions.Idempotency;

/// <summary>
/// Represents a cached idempotency response containing either a successful result or failure information.
/// </summary>
/// <remarks>
/// <para>
/// This class stores the outcome of a message processing operation to enable idempotent behavior.
/// When a duplicate message is detected, the cached response is returned instead of reprocessing.
/// </para>
/// <para>
/// The response contains either:
/// </para>
/// <list type="bullet">
/// <item><description>Success result: <see cref="SuccessResult"/> is populated, status is <see cref="IdempotencyStatus.Success"/></description></item>
/// <item><description>Failure information: <see cref="FailureType"/>, <see cref="FailureMessage"/> are populated, status is <see cref="IdempotencyStatus.Failure"/></description></item>
/// </list>
/// </remarks>
public sealed class IdempotencyResponse
{
    /// <summary>
    /// Gets or initializes the idempotency key that identifies this cached response.
    /// </summary>
    /// <value>The unique idempotency key string.</value>
    public required string IdempotencyKey { get; init; }

    /// <summary>
    /// Gets or initializes the successful processing result, if the operation succeeded.
    /// </summary>
    /// <value>
    /// The result data returned by the handler, or null if the operation had no return value or failed.
    /// Only populated when <see cref="Status"/> is <see cref="IdempotencyStatus.Success"/>.
    /// </value>
    public object? SuccessResult { get; init; }

    /// <summary>
    /// Gets or initializes the fully qualified type name of the exception that caused the failure.
    /// </summary>
    /// <value>
    /// The full type name (e.g., "System.ArgumentException"), or null if the operation succeeded.
    /// Only populated when <see cref="Status"/> is <see cref="IdempotencyStatus.Failure"/>.
    /// </value>
    public string? FailureType { get; init; }

    /// <summary>
    /// Gets or initializes the exception message describing the failure.
    /// </summary>
    /// <value>
    /// The exception message, or null if the operation succeeded.
    /// Only populated when <see cref="Status"/> is <see cref="IdempotencyStatus.Failure"/>.
    /// </value>
    public string? FailureMessage { get; init; }

    /// <summary>
    /// Gets or initializes the exception stack trace for diagnostic purposes.
    /// </summary>
    /// <value>
    /// The stack trace string, or null if the operation succeeded or stack trace was not captured.
    /// Only populated when <see cref="Status"/> is <see cref="IdempotencyStatus.Failure"/>.
    /// </value>
    /// <remarks>
    /// Stack traces can be large. Consider truncating or omitting in production to reduce storage overhead.
    /// </remarks>
    public string? FailureStackTrace { get; init; }

    /// <summary>
    /// Gets or initializes the UTC timestamp when this response was stored in the cache.
    /// </summary>
    /// <value>The UTC date and time when the response was cached.</value>
    public DateTimeOffset StoredAt { get; init; }

    /// <summary>
    /// Gets or initializes the UTC timestamp when this cached response will expire.
    /// </summary>
    /// <value>
    /// The UTC date and time when the response expires. After this time, the cache entry
    /// should be removed and the operation may be reprocessed.
    /// </value>
    /// <remarks>
    /// Calculated as <see cref="StoredAt"/> + TTL from the idempotency policy.
    /// Implementations should automatically clean up expired entries.
    /// </remarks>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Gets or initializes the status of this idempotency entry.
    /// </summary>
    /// <value>
    /// The status indicating whether the operation succeeded, failed, or is currently processing.
    /// </value>
    public IdempotencyStatus Status { get; init; }
}

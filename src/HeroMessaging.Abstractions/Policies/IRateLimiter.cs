namespace HeroMessaging.Abstractions.Policies;

/// <summary>
/// Defines a rate limiter for controlling message processing throughput.
/// Implementations use algorithms like Token Bucket or Sliding Window to enforce rate limits.
/// </summary>
/// <remarks>
/// Rate limiters control the rate at which operations are permitted to proceed.
/// They are useful for protecting downstream services, complying with API rate limits,
/// and managing backpressure in message processing pipelines.
/// Thread-safe implementations allow concurrent access from multiple threads.
/// </remarks>
public interface IRateLimiter
{
    /// <summary>
    /// Attempts to acquire the specified number of permits for rate limiting.
    /// </summary>
    /// <param name="key">Optional key for scoped rate limiting (e.g., message type, tenant ID).
    /// If null, uses global rate limit. When scoped limiting is enabled, each unique key
    /// maintains an independent rate limit bucket.</param>
    /// <param name="permits">Number of permits to acquire. Must be positive. Default is 1.
    /// Some algorithms may reject requests for permits exceeding bucket capacity.</param>
    /// <param name="cancellationToken">Cancellation token to cancel waiting for permits when queuing.</param>
    /// <returns>A <see cref="RateLimitResult"/> indicating whether the request was allowed,
    /// and if throttled, how long to wait before retrying.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="permits"/> is less than 1.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is canceled via <paramref name="cancellationToken"/>.</exception>
    /// <example>
    /// <code>
    /// var rateLimiter = new TokenBucketRateLimiter(new TokenBucketOptions
    /// {
    ///     Capacity = 100,
    ///     RefillRate = 10.0
    /// });
    ///
    /// var result = await rateLimiter.AcquireAsync();
    /// if (result.IsAllowed)
    /// {
    ///     // Process message
    /// }
    /// else
    /// {
    ///     // Rate limited - wait or reject
    ///     await Task.Delay(result.RetryAfter);
    /// }
    /// </code>
    /// </example>
    ValueTask<RateLimitResult> AcquireAsync(
        string? key = null,
        int permits = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current statistics for the rate limiter.
    /// </summary>
    /// <param name="key">Optional key for scoped rate limiting. If null, returns global statistics.</param>
    /// <returns>Statistics including available permits, capacity, and counters.</returns>
    /// <remarks>
    /// Statistics are point-in-time snapshots and may be stale by the time they are read.
    /// Use for observability and monitoring, not for making rate limiting decisions.
    /// </remarks>
    RateLimiterStatistics GetStatistics(string? key = null);
}

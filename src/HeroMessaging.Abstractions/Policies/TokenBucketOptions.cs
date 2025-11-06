namespace HeroMessaging.Abstractions.Policies;

/// <summary>
/// Configuration options for the Token Bucket rate limiting algorithm.
/// </summary>
/// <remarks>
/// The Token Bucket algorithm allows controlled bursts while maintaining a steady-state rate.
/// Tokens are added to the bucket at a fixed rate, and each operation consumes one or more tokens.
/// When the bucket is empty, requests are either queued or rejected based on <see cref="Behavior"/>.
/// </remarks>
public sealed class TokenBucketOptions
{
    /// <summary>
    /// Gets or sets the maximum number of tokens the bucket can hold (burst capacity).
    /// </summary>
    /// <value>
    /// The maximum tokens. Must be positive. Default is 100.
    /// Higher values allow larger bursts of traffic.
    /// </value>
    /// <example>
    /// A capacity of 1000 allows bursting up to 1000 messages before throttling.
    /// </example>
    public long Capacity { get; set; } = 100;

    /// <summary>
    /// Gets or sets the rate at which tokens are added to the bucket (permits per second).
    /// </summary>
    /// <value>
    /// The refill rate in tokens per second. Must be positive. Default is 10.0.
    /// This represents the steady-state throughput rate.
    /// </value>
    /// <example>
    /// A refill rate of 100 allows 100 messages per second in steady state.
    /// </example>
    public double RefillRate { get; set; } = 10.0;

    /// <summary>
    /// Gets or sets the period between token refills.
    /// </summary>
    /// <value>
    /// The refill period. Must be positive. Default is 100 milliseconds.
    /// Shorter periods provide smoother rate limiting but higher overhead.
    /// </value>
    /// <remarks>
    /// This value affects granularity, not the rate. With lazy refill strategy,
    /// tokens are calculated based on elapsed time rather than periodic refills.
    /// </remarks>
    public TimeSpan RefillPeriod { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets or sets the behavior when the rate limit is exceeded.
    /// </summary>
    /// <value>
    /// The rate limit behavior. Default is <see cref="RateLimitBehavior.Queue"/>.
    /// </value>
    public RateLimitBehavior Behavior { get; set; } = RateLimitBehavior.Queue;

    /// <summary>
    /// Gets or sets the maximum time to wait for tokens when queuing.
    /// </summary>
    /// <value>
    /// The maximum wait time. Must be positive. Default is 30 seconds.
    /// Only applies when <see cref="Behavior"/> is <see cref="RateLimitBehavior.Queue"/>.
    /// </value>
    public TimeSpan MaxQueueWait { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets a value indicating whether to enable per-key scoped rate limiting.
    /// </summary>
    /// <value>
    /// <c>true</c> to enable scoped rate limiting; otherwise, <c>false</c>. Default is <c>false</c>.
    /// </value>
    /// <remarks>
    /// When enabled, each unique key gets its own token bucket with independent rate limits.
    /// This allows per-message-type or per-tenant rate limiting.
    /// Increases memory usage proportional to the number of unique keys.
    /// </remarks>
    public bool EnableScoping { get; set; } = false;

    /// <summary>
    /// Gets or sets the maximum number of unique keys to track when scoping is enabled.
    /// </summary>
    /// <value>
    /// The maximum keys. Must be positive. Default is 1000.
    /// When exceeded, oldest keys may be evicted (LRU policy).
    /// </value>
    public int MaxScopedKeys { get; set; } = 1000;

    /// <summary>
    /// Validates the options.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any option is invalid.</exception>
    public void Validate()
    {
        if (Capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(Capacity), Capacity, "Capacity must be positive.");

        if (RefillRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(RefillRate), RefillRate, "RefillRate must be positive.");

        if (RefillPeriod <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RefillPeriod), RefillPeriod, "RefillPeriod must be positive.");

        if (MaxQueueWait <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MaxQueueWait), MaxQueueWait, "MaxQueueWait must be positive.");

        if (MaxScopedKeys <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxScopedKeys), MaxScopedKeys, "MaxScopedKeys must be positive.");
    }
}

/// <summary>
/// Specifies the behavior when a rate limit is exceeded.
/// </summary>
public enum RateLimitBehavior
{
    /// <summary>
    /// Reject the request immediately without waiting.
    /// Returns a throttled result with RetryAfter duration.
    /// </summary>
    Reject = 0,

    /// <summary>
    /// Queue the request and wait for tokens to become available.
    /// Blocks until tokens are available or MaxQueueWait is exceeded.
    /// </summary>
    Queue = 1
}

namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Retry policy configuration for resilient operations
/// Consolidates retry logic from TransportOptions, ConsumerOptions, and Azure Service Bus options
/// </summary>
public class RetryPolicy
{
    /// <summary>
    /// Maximum number of retry attempts (0 = no retries, -1 = infinite)
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Initial delay before the first retry
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum delay between retries
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Whether to use exponential backoff for retries
    /// If false, uses constant delay (InitialDelay)
    /// If true, delay doubles after each retry up to MaxDelay
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// Timeout for each individual attempt
    /// </summary>
    public TimeSpan? AttemptTimeout { get; set; }

    /// <summary>
    /// Calculate the delay for a given retry attempt
    /// </summary>
    /// <param name="attemptNumber">The retry attempt number (1-based)</param>
    /// <returns>The delay to wait before the retry</returns>
    public TimeSpan CalculateDelay(int attemptNumber)
    {
        if (attemptNumber <= 0)
            return TimeSpan.Zero;

        if (!UseExponentialBackoff)
            return InitialDelay;

        // Exponential backoff: InitialDelay * 2^(attemptNumber-1)
        var multiplier = Math.Pow(2, attemptNumber - 1);
        var exponentialDelayMs = InitialDelay.TotalMilliseconds * multiplier;
        var clampedDelayMs = Math.Min(exponentialDelayMs, MaxDelay.TotalMilliseconds);

        return TimeSpan.FromMilliseconds(clampedDelayMs);
    }

    /// <summary>
    /// Check if retry should be attempted
    /// </summary>
    /// <param name="attemptNumber">The retry attempt number (1-based)</param>
    /// <returns>True if retry should be attempted</returns>
    public bool ShouldRetry(int attemptNumber)
    {
        if (MaxAttempts == -1)
            return true; // Infinite retries

        return attemptNumber <= MaxAttempts;
    }

    /// <summary>
    /// No retries policy
    /// </summary>
    public static RetryPolicy None => new()
    {
        MaxAttempts = 0
    };

    /// <summary>
    /// Aggressive retry policy for transient errors
    /// </summary>
    public static RetryPolicy Aggressive => new()
    {
        MaxAttempts = 10,
        InitialDelay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromSeconds(30),
        UseExponentialBackoff = true
    };

    /// <summary>
    /// Conservative retry policy for expensive operations
    /// </summary>
    public static RetryPolicy Conservative => new()
    {
        MaxAttempts = 3,
        InitialDelay = TimeSpan.FromSeconds(30),
        MaxDelay = TimeSpan.FromMinutes(5),
        UseExponentialBackoff = true
    };

    /// <summary>
    /// Linear retry policy with constant delay
    /// </summary>
    public static RetryPolicy Linear(int maxAttempts, TimeSpan delay) => new()
    {
        MaxAttempts = maxAttempts,
        InitialDelay = delay,
        UseExponentialBackoff = false
    };

    /// <summary>
    /// Exponential retry policy
    /// </summary>
    public static RetryPolicy Exponential(int maxAttempts, TimeSpan initialDelay, TimeSpan maxDelay) => new()
    {
        MaxAttempts = maxAttempts,
        InitialDelay = initialDelay,
        MaxDelay = maxDelay,
        UseExponentialBackoff = true
    };

    /// <summary>
    /// Infinite retry policy (use with caution)
    /// </summary>
    public static RetryPolicy Infinite => new()
    {
        MaxAttempts = -1,
        InitialDelay = TimeSpan.FromSeconds(5),
        MaxDelay = TimeSpan.FromMinutes(5),
        UseExponentialBackoff = true
    };
}

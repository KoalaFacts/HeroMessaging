namespace HeroMessaging.Utilities;

/// <summary>
/// Centralized utility for calculating retry delays with exponential backoff and jitter.
/// Consolidates duplicate implementations from RetryDecorator, DefaultErrorHandler,
/// ConnectionResilienceDecorator, and OutboxProcessor.
/// </summary>
public static class RetryDelayCalculator
{
    /// <summary>Default jitter factor (30%)</summary>
    public const double DefaultJitterFactor = 0.3;

    /// <summary>Default base delay</summary>
    public static readonly TimeSpan DefaultBaseDelay = TimeSpan.FromSeconds(1);

    /// <summary>Default maximum delay</summary>
    public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Calculates exponential backoff delay with jitter.
    /// Formula: baseDelay * 2^(attemptNumber) * (1 + random(0, jitterFactor))
    /// </summary>
    /// <param name="attemptNumber">The retry attempt number (0-based or 1-based depending on useZeroBasedAttempt)</param>
    /// <param name="baseDelay">Base delay for first retry. Default: 1 second</param>
    /// <param name="maxDelay">Maximum delay cap. Default: 30 seconds</param>
    /// <param name="jitterFactor">Jitter factor (0-1). Default: 0.3 (30%)</param>
    /// <param name="useZeroBasedAttempt">If true, uses 2^attemptNumber. If false, uses 2^(attemptNumber-1). Default: true</param>
    /// <returns>Calculated delay with jitter, capped at maxDelay</returns>
    public static TimeSpan Calculate(
        int attemptNumber,
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null,
        double jitterFactor = DefaultJitterFactor,
        bool useZeroBasedAttempt = true)
    {
        var actualBaseDelay = baseDelay ?? DefaultBaseDelay;
        var actualMaxDelay = maxDelay ?? DefaultMaxDelay;

        if (attemptNumber <= 0 && !useZeroBasedAttempt)
            return TimeSpan.Zero;

        var exponent = useZeroBasedAttempt ? attemptNumber : attemptNumber - 1;
        var exponentialDelay = actualBaseDelay.TotalMilliseconds * Math.Pow(2, exponent);

        // Add jitter
        var jitter = RandomHelper.Instance.NextDouble() * jitterFactor;
        var delayWithJitter = exponentialDelay * (1 + jitter);

        // Cap at max delay
        var finalDelayMs = Math.Min(delayWithJitter, actualMaxDelay.TotalMilliseconds);

        return TimeSpan.FromMilliseconds(finalDelayMs);
    }

    /// <summary>
    /// Calculates simple exponential backoff without jitter.
    /// Useful for deterministic/testable scenarios.
    /// </summary>
    public static TimeSpan CalculateWithoutJitter(
        int attemptNumber,
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null,
        bool useZeroBasedAttempt = true)
    {
        var actualBaseDelay = baseDelay ?? DefaultBaseDelay;
        var actualMaxDelay = maxDelay ?? DefaultMaxDelay;

        if (attemptNumber <= 0 && !useZeroBasedAttempt)
            return TimeSpan.Zero;

        var exponent = useZeroBasedAttempt ? attemptNumber : attemptNumber - 1;
        var exponentialDelay = actualBaseDelay.TotalMilliseconds * Math.Pow(2, exponent);

        var finalDelayMs = Math.Min(exponentialDelay, actualMaxDelay.TotalMilliseconds);

        return TimeSpan.FromMilliseconds(finalDelayMs);
    }
}

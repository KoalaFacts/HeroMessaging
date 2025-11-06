namespace HeroMessaging.Abstractions.Policies;

/// <summary>
/// Represents the result of a rate limit acquisition attempt.
/// This is a struct to minimize allocations in the hot path.
/// </summary>
public readonly struct RateLimitResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitResult"/> struct.
    /// </summary>
    /// <param name="isAllowed">Whether the request was allowed.</param>
    /// <param name="retryAfter">Time to wait before retrying if throttled.</param>
    /// <param name="remainingPermits">Number of permits remaining after this acquisition.</param>
    /// <param name="reasonPhrase">Optional reason phrase explaining why the request was throttled.</param>
    public RateLimitResult(bool isAllowed, TimeSpan retryAfter, long remainingPermits, string? reasonPhrase = null)
    {
        IsAllowed = isAllowed;
        RetryAfter = retryAfter;
        RemainingPermits = remainingPermits;
        ReasonPhrase = reasonPhrase;
    }

    /// <summary>
    /// Gets a value indicating whether the rate limit request was allowed.
    /// </summary>
    /// <value>
    /// <c>true</c> if the request was allowed and processing should proceed;
    /// <c>false</c> if the request was throttled.
    /// </value>
    public bool IsAllowed { get; }

    /// <summary>
    /// Gets the time to wait before retrying when throttled.
    /// </summary>
    /// <value>
    /// A <see cref="TimeSpan"/> indicating how long to wait.
    /// Zero if <see cref="IsAllowed"/> is <c>true</c>.
    /// </value>
    public TimeSpan RetryAfter { get; }

    /// <summary>
    /// Gets the number of permits remaining after this acquisition.
    /// </summary>
    /// <value>
    /// The number of available permits. May be negative if oversubscribed.
    /// </value>
    public long RemainingPermits { get; }

    /// <summary>
    /// Gets an optional reason phrase explaining why the request was throttled.
    /// </summary>
    /// <value>
    /// A human-readable explanation, or <c>null</c> if not provided.
    /// </value>
    public string? ReasonPhrase { get; }

    /// <summary>
    /// Creates a successful rate limit result.
    /// </summary>
    /// <param name="remainingPermits">Number of permits remaining after acquisition.</param>
    /// <returns>A <see cref="RateLimitResult"/> indicating success.</returns>
    public static RateLimitResult Success(long remainingPermits) =>
        new(isAllowed: true, retryAfter: TimeSpan.Zero, remainingPermits: remainingPermits);

    /// <summary>
    /// Creates a throttled rate limit result.
    /// </summary>
    /// <param name="retryAfter">Time to wait before retrying.</param>
    /// <param name="reason">Optional reason phrase explaining the throttling.</param>
    /// <returns>A <see cref="RateLimitResult"/> indicating the request was throttled.</returns>
    public static RateLimitResult Throttled(TimeSpan retryAfter, string? reason = null) =>
        new(isAllowed: false, retryAfter: retryAfter, remainingPermits: 0, reasonPhrase: reason ?? "Rate limit exceeded");
}

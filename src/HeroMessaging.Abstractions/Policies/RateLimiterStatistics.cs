namespace HeroMessaging.Abstractions.Policies;

/// <summary>
/// Represents statistics for a rate limiter at a point in time.
/// </summary>
/// <remarks>
/// Statistics are snapshots and may be stale by the time they are read.
/// Use for observability, monitoring, and debugging, not for making rate limiting decisions.
/// </remarks>
public sealed class RateLimiterStatistics
{
    /// <summary>
    /// Gets the number of permits currently available.
    /// </summary>
    /// <value>
    /// The number of permits that can be acquired immediately without waiting.
    /// May be fractional for some algorithms (represented as long for simplicity).
    /// </value>
    public long AvailablePermits { get; init; }

    /// <summary>
    /// Gets the maximum capacity of the rate limiter.
    /// </summary>
    /// <value>
    /// The maximum number of permits that can be stored (burst capacity).
    /// </value>
    public long Capacity { get; init; }

    /// <summary>
    /// Gets the refill rate in permits per second.
    /// </summary>
    /// <value>
    /// The steady-state rate at which permits are added over time.
    /// </value>
    public double RefillRate { get; init; }

    /// <summary>
    /// Gets the timestamp of the last refill operation.
    /// </summary>
    /// <value>
    /// The last time permits were added to the bucket.
    /// </value>
    public DateTimeOffset LastRefillTime { get; init; }

    /// <summary>
    /// Gets the total number of permits acquired since creation.
    /// </summary>
    /// <value>
    /// Cumulative count of successful acquisitions.
    /// </value>
    public long TotalAcquired { get; init; }

    /// <summary>
    /// Gets the total number of requests that were throttled.
    /// </summary>
    /// <value>
    /// Cumulative count of throttled requests.
    /// </value>
    public long TotalThrottled { get; init; }

    /// <summary>
    /// Gets the throttle rate (percentage of requests throttled).
    /// </summary>
    /// <value>
    /// Percentage between 0.0 and 1.0, or 0 if no requests have been made.
    /// </value>
    public double ThrottleRate
    {
        get
        {
            var total = TotalAcquired + TotalThrottled;
            return total == 0 ? 0.0 : (double)TotalThrottled / total;
        }
    }
}

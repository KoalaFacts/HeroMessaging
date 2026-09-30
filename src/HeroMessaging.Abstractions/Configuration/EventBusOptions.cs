namespace HeroMessaging.Abstractions.Configuration;

/// <summary>
/// Capacity and concurrency settings for in-process event dispatch.
/// Null values use bounded defaults based on the available processor count.
/// Explicit values may exceed those defaults.
/// </summary>
public sealed class EventBusOptions
{
    /// <summary>
    /// Maximum number of handlers executing concurrently. Defaults to the processor count, capped at 32.
    /// </summary>
    public int? MaxDegreeOfParallelism { get; set; }

    /// <summary>
    /// Maximum number of queued or executing handler invocations. Defaults to 128 per processor, capped at 1000.
    /// </summary>
    public int? BoundedCapacity { get; set; }

    /// <summary>
    /// Maximum number of reusable event envelopes. Defaults to 8 per processor, capped at 64.
    /// Set to zero to disable pooling.
    /// </summary>
    public int? MaxPooledEnvelopes { get; set; }
}

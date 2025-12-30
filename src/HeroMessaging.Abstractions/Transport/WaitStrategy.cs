namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Wait strategy for RingBuffer mode.
/// Controls the trade-off between CPU usage and latency.
/// </summary>
public enum WaitStrategy
{
    /// <summary>
    /// Block using Monitor.Wait (lowest CPU, ~1-5ms latency).
    /// Best for scenarios where CPU efficiency is critical.
    /// </summary>
    Blocking,

    /// <summary>
    /// Progressive backoff: spin, yield, sleep (balanced, ~100μs-1ms latency).
    /// Best for general purpose use where both latency and CPU matter.
    /// Recommended default for RingBuffer mode.
    /// </summary>
    Sleeping,

    /// <summary>
    /// Spin then yield (low latency, ~50-100μs).
    /// Best for scenarios where low latency is important but 100% CPU is not acceptable.
    /// </summary>
    Yielding,

    /// <summary>
    /// Busy spin (ultra-low latency, less than 10μs, 100% CPU).
    /// Best for ultra-low latency trading systems or real-time applications.
    /// WARNING: Uses 100% of a CPU core.
    /// </summary>
    BusySpin,

    /// <summary>
    /// Block with timeout (deadlock detection).
    /// Best for scenarios where you want to detect if events aren't being processed.
    /// </summary>
    TimeoutBlocking
}

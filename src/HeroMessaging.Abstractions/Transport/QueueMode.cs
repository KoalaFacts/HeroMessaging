namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Queue implementation mode for in-memory queues.
/// Determines the underlying transport mechanism.
/// </summary>
public enum QueueMode
{
    /// <summary>
    /// Use System.Threading.Channels (async/await, flexible backpressure).
    /// Default mode - best for general purpose use.
    /// Characteristics:
    /// - Async/await based
    /// - Flexible backpressure options
    /// - ~1ms p99 latency
    /// - ~100K msg/s throughput
    /// </summary>
    Channel,

    /// <summary>
    /// Use lock-free ring buffer (pre-allocated, zero GC, ultra-low latency).
    /// Opt-in mode - best for high-performance scenarios.
    /// Characteristics:
    /// - Lock-free coordination
    /// - Pre-allocated memory (zero allocation in hot path)
    /// - Less than 50μs p99 latency
    /// - Greater than 500K msg/s throughput
    /// - Requires power-of-2 buffer sizes
    /// </summary>
    RingBuffer
}

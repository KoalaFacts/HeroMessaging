namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Producer mode for RingBuffer.
/// Determines the coordination strategy for publishing events.
/// </summary>
public enum ProducerMode
{
    /// <summary>
    /// Single producer - only one thread publishes to the ring buffer.
    /// Faster as it avoids Compare-And-Swap (CAS) operations.
    /// Use when you have a single publishing thread.
    /// </summary>
    Single,

    /// <summary>
    /// Multiple producers - multiple threads can publish concurrently.
    /// Uses CAS operations for coordination, slightly slower but thread-safe.
    /// Use when multiple threads need to publish events.
    /// </summary>
    Multi
}

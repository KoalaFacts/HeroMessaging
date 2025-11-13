namespace HeroMessaging.RingBuffer;

/// <summary>
/// Defines the type of producer for the ring buffer.
/// This determines the coordination strategy used for publishing events.
/// </summary>
public enum ProducerType
{
    /// <summary>
    /// Single producer mode - only one thread publishes to the ring buffer.
    /// This is faster as it avoids Compare-And-Swap (CAS) operations.
    /// Use when you have a single publishing thread.
    /// </summary>
    Single,

    /// <summary>
    /// Multi-producer mode - multiple threads can publish concurrently.
    /// Uses CAS operations for coordination, slightly slower but thread-safe.
    /// Use when multiple threads need to publish events.
    /// </summary>
    Multi
}

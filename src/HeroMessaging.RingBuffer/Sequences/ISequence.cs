namespace HeroMessaging.RingBuffer.Sequences;

/// <summary>
/// Represents a sequence number that can be tracked and updated.
/// Used by producers and consumers to coordinate access to the ring buffer.
/// </summary>
public interface ISequence
{
    /// <summary>
    /// Gets or sets the current sequence value
    /// </summary>
    long Value { get; set; }
}

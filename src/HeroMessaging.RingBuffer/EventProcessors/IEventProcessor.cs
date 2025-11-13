using HeroMessaging.RingBuffer.Sequences;

namespace HeroMessaging.RingBuffer.EventProcessors;

/// <summary>
/// Processes events from the ring buffer.
/// Event processors consume events and track their progress via a sequence.
/// </summary>
public interface IEventProcessor : IDisposable
{
    /// <summary>
    /// Get the current sequence being processed.
    /// This sequence is used by the ring buffer to determine backpressure.
    /// </summary>
    ISequence Sequence { get; }

    /// <summary>
    /// Start processing events from the ring buffer
    /// </summary>
    void Start();

    /// <summary>
    /// Stop processing events
    /// </summary>
    void Stop();

    /// <summary>
    /// Check if the processor is currently running
    /// </summary>
    bool IsRunning { get; }
}

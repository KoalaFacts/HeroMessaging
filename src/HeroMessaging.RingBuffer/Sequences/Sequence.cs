using System.Threading;

namespace HeroMessaging.RingBuffer.Sequences;

/// <summary>
/// Thread-safe sequence implementation that wraps a padded long value.
/// Used to track sequence numbers for producers and consumers in the ring buffer.
/// </summary>
public sealed class Sequence : ISequence
{
    private long _value;

    /// <summary>
    /// Creates a new sequence with the specified initial value
    /// </summary>
    /// <param name="initialValue">The initial sequence value</param>
    public Sequence(long initialValue)
    {
        _value = initialValue;
    }

    /// <summary>
    /// Gets or sets the current sequence value
    /// </summary>
    public long Value
    {
        get => Volatile.Read(ref _value);
        set => Volatile.Write(ref _value, value);
    }

    /// <summary>
    /// Atomically compares and exchanges the sequence value.
    /// Used for lock-free multi-producer coordination.
    /// </summary>
    /// <param name="expected">The expected current value</param>
    /// <param name="update">The new value if current matches expected</param>
    /// <returns>The original value before the operation</returns>
    public long CompareExchange(long expected, long update)
    {
        return Interlocked.CompareExchange(ref _value, update, expected);
    }
}

using System.Runtime.InteropServices;
using System.Threading;

namespace HeroMessaging.RingBuffer.Sequences;

/// <summary>
/// Thread-safe sequence implementation with cache line padding to prevent false sharing.
/// Used to track sequence numbers for producers and consumers in the ring buffer.
/// </summary>
/// <remarks>
/// Cache line padding prevents false sharing when multiple Sequence instances
/// are accessed by different threads. On most modern CPUs, a cache line is 64 bytes.
/// Without padding, sequences allocated near each other in memory could share a
/// cache line, causing cache invalidations when either is modified.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public sealed class Sequence : ISequence
{
    // Left padding (56 bytes: 7 longs) to push _value to separate cache line
    private long _p1, _p2, _p3, _p4, _p5, _p6, _p7;

    private long _value;

    // Right padding (56 bytes) to prevent next object from sharing cache line
    private long _p8, _p9, _p10, _p11, _p12, _p13, _p14;

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

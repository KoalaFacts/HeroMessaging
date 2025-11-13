using System.Runtime.InteropServices;

namespace HeroMessaging.RingBuffer.Sequences;

/// <summary>
/// Cache-line padded long value to prevent false sharing.
/// Modern CPUs have 64-byte cache lines, so we pad to 128 bytes to ensure
/// that this value doesn't share a cache line with any other data.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 128)]
internal struct PaddedLong
{
    /// <summary>
    /// The actual long value, offset to the middle of the cache line
    /// </summary>
    [FieldOffset(56)]
    public long Value;

    /// <summary>
    /// Creates a new padded long with the specified initial value
    /// </summary>
    public PaddedLong(long value)
    {
        Value = value;
    }
}

using System;

namespace HeroMessaging.Utilities;

#if NET9_0_OR_GREATER && ENABLE_REF_STRUCT_INTERFACES // C# 13 feature - ref struct as interface return type
/// <summary>
/// Interface for managing pooled buffers using ArrayPool to reduce allocations.
/// Provides RAII-style buffer management with automatic return to pool.
/// NOTE: Requires C# 13 support for ref struct interface members
/// </summary>
public interface IBufferPoolManager
{
    /// <summary>
    /// Small buffer threshold (1KB) - use stack allocation when possible.
    /// </summary>
    int SmallBufferThreshold { get; }

    /// <summary>
    /// Medium buffer threshold (64KB) - typical message size.
    /// </summary>
    int MediumBufferThreshold { get; }

    /// <summary>
    /// Large buffer threshold (1MB) - use pooling but consider chunking.
    /// </summary>
    int LargeBufferThreshold { get; }

    /// <summary>
    /// Rents a buffer from the pool with the specified minimum size.
    /// The returned buffer may be larger than requested.
    /// </summary>
    /// <param name="minimumSize">Minimum required buffer size</param>
    /// <returns>Disposable wrapper around pooled buffer</returns>
    PooledBuffer Rent(int minimumSize);

    /// <summary>
    /// Rents a buffer and copies source data into it.
    /// Useful when you need to work with pooled copy of existing data.
    /// </summary>
    PooledBuffer RentAndCopy(ReadOnlySpan<byte> source);

    /// <summary>
    /// Helper to determine the best buffering strategy based on size.
    /// </summary>
    BufferingStrategy GetStrategy(int size);
}
#endif

/// <summary>
/// Buffering strategies for different message sizes.
/// </summary>
public enum BufferingStrategy
{
    /// <summary>
    /// Use stack allocation (stackalloc) for small buffers (1KB or less).
    /// Zero heap allocation, fastest, but limited size.
    /// </summary>
    StackAlloc,

    /// <summary>
    /// Use ArrayPool for medium buffers (1KB-64KB).
    /// Reduces allocations, good for most messages.
    /// </summary>
    Pooled,

    /// <summary>
    /// Use ArrayPool with chunking for large buffers (64KB-1MB).
    /// Process in chunks to avoid large contiguous allocations.
    /// </summary>
    PooledWithChunking,

    /// <summary>
    /// Use stream-based approach for very large data (greater than 1MB).
    /// Consider RecyclableMemoryStream or file-based storage.
    /// </summary>
    StreamBased
}

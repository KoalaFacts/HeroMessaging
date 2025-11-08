using System;
using System.Buffers;

namespace HeroMessaging.Utilities;

/// <summary>
/// Helper for managing pooled buffers using ArrayPool to reduce allocations.
/// Provides RAII-style buffer management with automatic return to pool.
/// </summary>
public static class PooledBufferHelper
{
    /// <summary>
    /// Shared ArrayPool instance for all pooled buffers.
    /// Uses the default shared pool which is automatically sized based on usage patterns.
    /// </summary>
    private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;

    /// <summary>
    /// Small buffer threshold (1KB) - use stack allocation when possible.
    /// </summary>
    public const int SmallBufferThreshold = 1024;

    /// <summary>
    /// Medium buffer threshold (64KB) - typical message size.
    /// </summary>
    public const int MediumBufferThreshold = 64 * 1024;

    /// <summary>
    /// Large buffer threshold (1MB) - use pooling but consider chunking.
    /// </summary>
    public const int LargeBufferThreshold = 1024 * 1024;

    /// <summary>
    /// Rents a buffer from the pool with the specified minimum size.
    /// The returned buffer may be larger than requested.
    /// </summary>
    /// <param name="minimumSize">Minimum required buffer size</param>
    /// <returns>Disposable wrapper around pooled buffer</returns>
    public static PooledBuffer Rent(int minimumSize)
    {
        var buffer = Pool.Rent(minimumSize);
        return new PooledBuffer(buffer, minimumSize);
    }

    /// <summary>
    /// Rents a buffer and copies source data into it.
    /// Useful when you need to work with pooled copy of existing data.
    /// </summary>
    public static PooledBuffer RentAndCopy(ReadOnlySpan<byte> source)
    {
        var buffer = Rent(source.Length);
        source.CopyTo(buffer.Span);
        return buffer;
    }

    /// <summary>
    /// Returns a buffer to the pool manually.
    /// Prefer using PooledBuffer.Dispose() for automatic return.
    /// </summary>
    public static void Return(byte[] buffer, bool clearArray = false)
    {
        Pool.Return(buffer, clearArray);
    }

    /// <summary>
    /// RAII-style wrapper for pooled byte buffers.
    /// Automatically returns buffer to pool when disposed.
    /// </summary>
    public ref struct PooledBuffer
    {
        private byte[]? _array;
        private readonly int _length;
        private bool _disposed;

        internal PooledBuffer(byte[] array, int length)
        {
            _array = array;
            _length = length;
            _disposed = false;
        }

        /// <summary>
        /// Gets the underlying array (may be larger than requested size).
        /// </summary>
        public byte[] Array => _array ?? throw new ObjectDisposedException(nameof(PooledBuffer));

        /// <summary>
        /// Gets the usable length of the buffer (the requested minimum size).
        /// </summary>
        public int Length => _length;

        /// <summary>
        /// Gets a span over the usable portion of the buffer.
        /// </summary>
        public Span<byte> Span => _disposed
            ? throw new ObjectDisposedException(nameof(PooledBuffer))
            : _array.AsSpan(0, _length);

        /// <summary>
        /// Gets the full span including any extra space.
        /// </summary>
        public Span<byte> FullSpan => _disposed
            ? throw new ObjectDisposedException(nameof(PooledBuffer))
            : _array.AsSpan();

        /// <summary>
        /// Returns the buffer to the pool.
        /// Buffer should not be used after disposal.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed && _array != null)
            {
                Pool.Return(_array, clearArray: false);
                _array = null;
                _disposed = true;
            }
        }

        /// <summary>
        /// Returns the buffer to the pool with optional clearing.
        /// Use clearArray:true when buffer contained sensitive data.
        /// </summary>
        public void Dispose(bool clearArray)
        {
            if (!_disposed && _array != null)
            {
                Pool.Return(_array, clearArray);
                _array = null;
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Helper to determine the best buffering strategy based on size.
    /// </summary>
    public static BufferingStrategy GetStrategy(int size)
    {
        if (size <= SmallBufferThreshold)
            return BufferingStrategy.StackAlloc;
        else if (size <= MediumBufferThreshold)
            return BufferingStrategy.Pooled;
        else if (size <= LargeBufferThreshold)
            return BufferingStrategy.PooledWithChunking;
        else
            return BufferingStrategy.StreamBased;
    }

    /// <summary>
    /// Buffering strategies for different message sizes.
    /// </summary>
    public enum BufferingStrategy
    {
        /// <summary>
        /// Use stack allocation (stackalloc) for small buffers (<=1KB).
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
        /// Use stream-based approach for very large data (>1MB).
        /// Consider RecyclableMemoryStream or file-based storage.
        /// </summary>
        StreamBased
    }
}

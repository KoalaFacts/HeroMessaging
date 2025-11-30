using System;
using System.Buffers;

namespace HeroMessaging.Utilities;

#if NET9_0_OR_GREATER && ENABLE_REF_STRUCT_INTERFACES
/// <summary>
/// Default implementation of IBufferPoolManager using ArrayPool.
/// Provides RAII-style buffer management with automatic return to pool.
/// NOTE: Requires C# 13 support for ref struct interface members
/// </summary>
public sealed class DefaultBufferPoolManager : IBufferPoolManager
#else
/// <summary>
/// Buffer pool manager using ArrayPool for reduced allocations.
/// Provides RAII-style buffer management with automatic return to pool.
/// </summary>
public sealed class DefaultBufferPoolManager
#endif
{
    /// <summary>
    /// Shared ArrayPool instance for all pooled buffers.
    /// Uses the default shared pool which is automatically sized based on usage patterns.
    /// </summary>
    private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;

    /// <summary>
    /// Small buffer threshold (1KB) - use stack allocation when possible.
    /// </summary>
    public int SmallBufferThreshold => 1024;

    /// <summary>
    /// Medium buffer threshold (64KB) - typical message size.
    /// </summary>
    public int MediumBufferThreshold => 64 * 1024;

    /// <summary>
    /// Large buffer threshold (1MB) - use pooling but consider chunking.
    /// </summary>
    public int LargeBufferThreshold => 1024 * 1024;

    /// <summary>
    /// Rents a buffer from the pool with the specified minimum size.
    /// The returned buffer may be larger than requested.
    /// </summary>
    /// <param name="minimumSize">Minimum required buffer size</param>
    /// <returns>Disposable wrapper around pooled buffer</returns>
    public PooledBuffer Rent(int minimumSize)
    {
        var buffer = Pool.Rent(minimumSize);
        return new PooledBuffer(buffer, minimumSize);
    }

    /// <summary>
    /// Rents a buffer and copies source data into it.
    /// Useful when you need to work with pooled copy of existing data.
    /// </summary>
    public PooledBuffer RentAndCopy(ReadOnlySpan<byte> source)
    {
        var buffer = Rent(source.Length);
        source.CopyTo(buffer.Span);
        return buffer;
    }

    /// <summary>
    /// Helper to determine the best buffering strategy based on size.
    /// </summary>
    public BufferingStrategy GetStrategy(int size)
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
    /// Returns a buffer to the pool manually.
    /// Prefer using PooledBuffer.Dispose() for automatic return.
    /// </summary>
    internal static void Return(byte[] buffer, bool clearArray = false)
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
        private bool _disposed;

        internal PooledBuffer(byte[] array, int length)
        {
            _array = array;
            Length = length;
            _disposed = false;
        }

        /// <summary>
        /// Gets the underlying array (may be larger than requested size).
        /// </summary>
        public readonly byte[] Array => _array ?? throw new ObjectDisposedException(nameof(PooledBuffer));

        /// <summary>
        /// Gets the usable length of the buffer (the requested minimum size).
        /// </summary>
        public int Length { get; }

        /// <summary>
        /// Gets a span over the usable portion of the buffer.
        /// </summary>
        public readonly Span<byte> Span => _disposed
            ? throw new ObjectDisposedException(nameof(PooledBuffer))
            : _array.AsSpan(0, Length);

        /// <summary>
        /// Gets the full span including any extra space.
        /// </summary>
        public readonly Span<byte> FullSpan => _disposed
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
                Return(_array, clearArray: false);
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
                Return(_array, clearArray);
                _array = null;
                _disposed = true;
            }
        }
    }
}

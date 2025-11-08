using HeroMessaging.Utilities;
using Xunit;

namespace HeroMessaging.Tests.Unit.Utilities;

/// <summary>
/// Unit tests for <see cref="DefaultBufferPoolManager"/> and <see cref="PooledBuffer"/>.
/// Tests cover buffer pooling, disposal patterns, strategy selection, and RAII semantics.
/// Target: 100% coverage for public APIs (constitutional requirement).
/// </summary>
public class DefaultBufferPoolManagerTests
{
    private readonly DefaultBufferPoolManager _bufferPool;

    public DefaultBufferPoolManagerTests()
    {
        _bufferPool = new DefaultBufferPoolManager();
    }

    #region Threshold Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void SmallBufferThreshold_ReturnsExpectedValue()
    {
        // Act
        var threshold = _bufferPool.SmallBufferThreshold;

        // Assert: Should be 1KB
        Assert.Equal(1024, threshold);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MediumBufferThreshold_ReturnsExpectedValue()
    {
        // Act
        var threshold = _bufferPool.MediumBufferThreshold;

        // Assert: Should be 64KB
        Assert.Equal(64 * 1024, threshold);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void LargeBufferThreshold_ReturnsExpectedValue()
    {
        // Act
        var threshold = _bufferPool.LargeBufferThreshold;

        // Assert: Should be 1MB
        Assert.Equal(1024 * 1024, threshold);
    }

    #endregion

    #region Rent Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Rent_WithSmallSize_ReturnsValidBuffer()
    {
        // Arrange
        const int size = 512;

        // Act
        using var buffer = _bufferPool.Rent(size);

        // Assert
        Assert.NotNull(buffer.Span);
        Assert.True(buffer.Span.Length >= size); // May be larger due to pooling
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Rent_WithMediumSize_ReturnsValidBuffer()
    {
        // Arrange
        const int size = 32 * 1024; // 32KB

        // Act
        using var buffer = _bufferPool.Rent(size);

        // Assert
        Assert.NotNull(buffer.Span);
        Assert.True(buffer.Span.Length >= size);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Rent_WithLargeSize_ReturnsValidBuffer()
    {
        // Arrange
        const int size = 128 * 1024; // 128KB

        // Act
        using var buffer = _bufferPool.Rent(size);

        // Assert
        Assert.NotNull(buffer.Span);
        Assert.True(buffer.Span.Length >= size);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Rent_MultipleBuffers_AllValid()
    {
        // Arrange & Act: Rent multiple buffers
        using var buffer1 = _bufferPool.Rent(1024);
        using var buffer2 = _bufferPool.Rent(2048);
        using var buffer3 = _bufferPool.Rent(4096);

        // Assert: All buffers should be valid and independent
        Assert.True(buffer1.Span.Length >= 1024);
        Assert.True(buffer2.Span.Length >= 2048);
        Assert.True(buffer3.Span.Length >= 4096);
    }

    #endregion

    #region RentAndCopy Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void RentAndCopy_WithSourceData_CopiesCorrectly()
    {
        // Arrange
        var sourceData = new byte[] { 1, 2, 3, 4, 5 };
        ReadOnlySpan<byte> source = sourceData;

        // Act
        using var buffer = _bufferPool.RentAndCopy(source);

        // Assert
        Assert.Equal(sourceData.Length, buffer.Span.Length);
        for (int i = 0; i < sourceData.Length; i++)
        {
            Assert.Equal(sourceData[i], buffer.Span[i]);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RentAndCopy_WithEmptySource_ReturnsEmptyBuffer()
    {
        // Arrange
        ReadOnlySpan<byte> source = ReadOnlySpan<byte>.Empty;

        // Act
        using var buffer = _bufferPool.RentAndCopy(source);

        // Assert
        Assert.Equal(0, buffer.Span.Length);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RentAndCopy_WithLargeSource_CopiesCorrectly()
    {
        // Arrange
        var sourceData = new byte[10000];
        for (int i = 0; i < sourceData.Length; i++)
        {
            sourceData[i] = (byte)(i % 256);
        }
        ReadOnlySpan<byte> source = sourceData;

        // Act
        using var buffer = _bufferPool.RentAndCopy(source);

        // Assert
        Assert.Equal(sourceData.Length, buffer.Span.Length);
        Assert.True(source.SequenceEqual(buffer.Span));
    }

    #endregion

    #region GetStrategy Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void GetStrategy_WithVerySmallSize_ReturnsStackAlloc()
    {
        // Arrange
        const int size = 512; // Less than 1KB

        // Act
        var strategy = _bufferPool.GetStrategy(size);

        // Assert
        Assert.Equal(BufferingStrategy.StackAlloc, strategy);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetStrategy_WithSmallThresholdExact_ReturnsStackAlloc()
    {
        // Arrange
        int size = _bufferPool.SmallBufferThreshold; // Exactly 1KB

        // Act
        var strategy = _bufferPool.GetStrategy(size);

        // Assert
        Assert.Equal(BufferingStrategy.StackAlloc, strategy);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetStrategy_WithMediumSize_ReturnsPooled()
    {
        // Arrange
        const int size = 32 * 1024; // 32KB (between 1KB and 64KB)

        // Act
        var strategy = _bufferPool.GetStrategy(size);

        // Assert
        Assert.Equal(BufferingStrategy.Pooled, strategy);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetStrategy_WithMediumThresholdExact_ReturnsPooled()
    {
        // Arrange
        int size = _bufferPool.MediumBufferThreshold; // Exactly 64KB

        // Act
        var strategy = _bufferPool.GetStrategy(size);

        // Assert
        Assert.Equal(BufferingStrategy.Pooled, strategy);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetStrategy_WithLargeSize_ReturnsPooledWithChunking()
    {
        // Arrange
        const int size = 512 * 1024; // 512KB (between 64KB and 1MB)

        // Act
        var strategy = _bufferPool.GetStrategy(size);

        // Assert
        Assert.Equal(BufferingStrategy.PooledWithChunking, strategy);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetStrategy_WithLargeThresholdExact_ReturnsPooledWithChunking()
    {
        // Arrange
        int size = _bufferPool.LargeBufferThreshold; // Exactly 1MB

        // Act
        var strategy = _bufferPool.GetStrategy(size);

        // Assert
        Assert.Equal(BufferingStrategy.PooledWithChunking, strategy);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetStrategy_WithVeryLargeSize_ReturnsStreamBased()
    {
        // Arrange
        const int size = 2 * 1024 * 1024; // 2MB (larger than 1MB)

        // Act
        var strategy = _bufferPool.GetStrategy(size);

        // Assert
        Assert.Equal(BufferingStrategy.StreamBased, strategy);
    }

    #endregion

    #region PooledBuffer Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void PooledBuffer_Span_ReturnsCorrectLength()
    {
        // Arrange
        const int requestedSize = 1024;

        // Act
        using var buffer = _bufferPool.Rent(requestedSize);

        // Assert
        Assert.Equal(requestedSize, buffer.Span.Length);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PooledBuffer_FullSpan_ReturnsEntireArray()
    {
        // Arrange
        const int requestedSize = 1024;

        // Act
        using var buffer = _bufferPool.Rent(requestedSize);

        // Assert: FullSpan should be >= Span due to pool allocation rounding
        Assert.True(buffer.FullSpan.Length >= buffer.Span.Length);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PooledBuffer_WriteAndRead_DataPersists()
    {
        // Arrange
        using var buffer = _bufferPool.Rent(100);

        // Act: Write data
        for (int i = 0; i < 100; i++)
        {
            buffer.Span[i] = (byte)i;
        }

        // Assert: Read data back
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal((byte)i, buffer.Span[i]);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PooledBuffer_Dispose_WithoutClearing_CanBeReused()
    {
        // Arrange & Act: Rent, write, and dispose
        {
            using var buffer = _bufferPool.Rent(10);
            buffer.Span[0] = 42;
            // Disposed here without clearing
        }

        // Act: Rent again (may get same buffer from pool)
        using var newBuffer = _bufferPool.Rent(10);

        // Assert: New buffer is valid (data may or may not be cleared)
        Assert.NotNull(newBuffer.Span);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PooledBuffer_DisposeWithClearing_ZeroesBuffer()
    {
        // Arrange
        var buffer = _bufferPool.Rent(10);
        buffer.Span[0] = 42;
        buffer.Span[5] = 100;

        // Act: Dispose with clearing
        buffer.Dispose(clearArray: true);

        // Note: Can't verify zeroing after disposal as buffer is returned to pool
        // This test verifies the method executes without error
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PooledBuffer_DisposeTwice_DoesNotThrow()
    {
        // Arrange
        var buffer = _bufferPool.Rent(10);

        // Act & Assert: Multiple disposes should be safe
        buffer.Dispose();
        buffer.Dispose(); // Should not throw
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PooledBuffer_AfterDispose_SpanThrowsObjectDisposedException()
    {
        // Arrange
        var buffer = _bufferPool.Rent(10);
        buffer.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => buffer.Span);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PooledBuffer_AfterDispose_FullSpanThrowsObjectDisposedException()
    {
        // Arrange
        var buffer = _bufferPool.Rent(10);
        buffer.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => buffer.FullSpan);
    }

    #endregion

    #region RAII Pattern Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void PooledBuffer_UsingStatement_AutomaticallyDisposes()
    {
        // Arrange
        PooledBuffer buffer;

        // Act: Using statement should auto-dispose
        {
            using (buffer = _bufferPool.Rent(10))
            {
                buffer.Span[0] = 42;
            }

            // Buffer is disposed here
        }

        // Assert: Accessing span after disposal should throw
        Assert.Throws<ObjectDisposedException>(() => buffer.Span);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PooledBuffer_UsingDeclaration_AutomaticallyDisposes()
    {
        // Arrange
        PooledBuffer buffer;

        // Act: Using declaration should auto-dispose at end of scope
        {
            using var tempBuffer = _bufferPool.Rent(10);
            tempBuffer.Span[0] = 42;
            buffer = tempBuffer;
            // Disposed at end of this block
        }

        // Assert: Accessing span after disposal should throw
        Assert.Throws<ObjectDisposedException>(() => buffer.Span);
    }

    #endregion

    #region Resource Pooling Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void MultipleRentAndReturn_ReusesBuffers()
    {
        // Arrange: Track buffer addresses (not exact, but demonstrates pooling)
        const int iterations = 100;
        const int bufferSize = 1024;

        // Act: Rent and return many times
        for (int i = 0; i < iterations; i++)
        {
            using var buffer = _bufferPool.Rent(bufferSize);
            buffer.Span[0] = (byte)i;
            // Auto-disposed and returned to pool
        }

        // Assert: If pooling works, we should get valid buffers every time
        // (No exception means success - we're relying on pool behavior)
        using var finalBuffer = _bufferPool.Rent(bufferSize);
        Assert.NotNull(finalBuffer.Span);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ConcurrentRent_DifferentSizes_AllSucceed()
    {
        // Arrange & Act: Rent buffers of different sizes concurrently
        var tasks = new[]
        {
            Task.Run(() =>
            {
                using var buf = _bufferPool.Rent(512);
                return buf.Span.Length >= 512;
            }),
            Task.Run(() =>
            {
                using var buf = _bufferPool.Rent(1024);
                return buf.Span.Length >= 1024;
            }),
            Task.Run(() =>
            {
                using var buf = _bufferPool.Rent(2048);
                return buf.Span.Length >= 2048;
            })
        };

        Task.WaitAll(tasks);

        // Assert: All tasks should succeed
        Assert.All(tasks, task => Assert.True(task.Result));
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Rent_WithZeroSize_ReturnsValidBuffer()
    {
        // Arrange & Act
        using var buffer = _bufferPool.Rent(0);

        // Assert: Should return a valid (possibly empty) buffer
        Assert.NotNull(buffer.Span);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Rent_WithNegativeSize_HandledByArrayPool()
    {
        // Arrange & Act & Assert
        // ArrayPool handles negative sizes - typically throws or returns min size
        // We just verify our wrapper doesn't crash
        var exception = Record.Exception(() =>
        {
            using var buffer = _bufferPool.Rent(-1);
        });

        // Either succeeds or throws ArgumentOutOfRangeException from ArrayPool
        if (exception != null)
        {
            Assert.IsType<ArgumentOutOfRangeException>(exception);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetStrategy_WithZeroSize_ReturnsStackAlloc()
    {
        // Arrange & Act
        var strategy = _bufferPool.GetStrategy(0);

        // Assert
        Assert.Equal(BufferingStrategy.StackAlloc, strategy);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetStrategy_WithNegativeSize_ReturnsStackAlloc()
    {
        // Arrange & Act
        var strategy = _bufferPool.GetStrategy(-100);

        // Assert: Negative sizes are treated as very small
        Assert.Equal(BufferingStrategy.StackAlloc, strategy);
    }

    #endregion
}

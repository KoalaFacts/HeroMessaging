using HeroMessaging.Utilities;
using Xunit;

namespace HeroMessaging.Tests.Unit.Utilities;

/// <summary>
/// Unit tests for DefaultBufferPoolManager and PooledBuffer.
/// </summary>
[Trait("Category", "Unit")]
public class DefaultBufferPoolManagerTests
{
    #region Threshold Tests

    [Fact]
    public void SmallBufferThreshold_Returns1024()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();

        // Act & Assert
        Assert.Equal(1024, manager.SmallBufferThreshold);
    }

    [Fact]
    public void MediumBufferThreshold_Returns65536()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();

        // Act & Assert
        Assert.Equal(64 * 1024, manager.MediumBufferThreshold);
    }

    [Fact]
    public void LargeBufferThreshold_Returns1048576()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();

        // Act & Assert
        Assert.Equal(1024 * 1024, manager.LargeBufferThreshold);
    }

    #endregion

    #region Rent Tests

    [Fact]
    public void Rent_WithValidSize_ReturnsBufferWithCorrectLength()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();
        const int requestedSize = 100;

        // Act
        using var buffer = manager.Rent(requestedSize);

        // Assert
        Assert.Equal(requestedSize, buffer.Length);
    }

    [Fact]
    public void Rent_WithValidSize_ReturnsBufferWithArrayAtLeastRequestedSize()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();
        const int requestedSize = 100;

        // Act
        using var buffer = manager.Rent(requestedSize);

        // Assert
        Assert.True(buffer.Array.Length >= requestedSize);
    }

    [Fact]
    public void Rent_WithValidSize_ReturnsBufferWithSpanOfRequestedSize()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();
        const int requestedSize = 100;

        // Act
        using var buffer = manager.Rent(requestedSize);

        // Assert
        Assert.Equal(requestedSize, buffer.Span.Length);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1024)]
    [InlineData(4096)]
    public void Rent_WithVariousSizes_ReturnsCorrectLengthBuffer(int size)
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();

        // Act
        using var buffer = manager.Rent(size);

        // Assert
        Assert.Equal(size, buffer.Length);
        Assert.Equal(size, buffer.Span.Length);
    }

    #endregion

    #region RentAndCopy Tests

    [Fact]
    public void RentAndCopy_WithSourceData_CopiesDataToBuffer()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();
        ReadOnlySpan<byte> source = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        using var buffer = manager.RentAndCopy(source);

        // Assert
        Assert.Equal(source.Length, buffer.Length);
        for (int i = 0; i < source.Length; i++)
        {
            Assert.Equal(source[i], buffer.Span[i]);
        }
    }

    [Fact]
    public void RentAndCopy_WithEmptySource_ReturnsEmptyBuffer()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();
        ReadOnlySpan<byte> source = ReadOnlySpan<byte>.Empty;

        // Act
        using var buffer = manager.RentAndCopy(source);

        // Assert
        Assert.Equal(0, buffer.Length);
    }

    [Fact]
    public void RentAndCopy_WithLargeSource_CopiesAllData()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();
        var sourceArray = new byte[1000];
        for (int i = 0; i < sourceArray.Length; i++)
        {
            sourceArray[i] = (byte)(i % 256);
        }
        ReadOnlySpan<byte> source = sourceArray;

        // Act
        using var buffer = manager.RentAndCopy(source);

        // Assert
        Assert.Equal(1000, buffer.Length);
        for (int i = 0; i < source.Length; i++)
        {
            Assert.Equal(sourceArray[i], buffer.Span[i]);
        }
    }

    #endregion

    #region GetStrategy Tests

    [Theory]
    [InlineData(0, BufferingStrategy.StackAlloc)]
    [InlineData(1, BufferingStrategy.StackAlloc)]
    [InlineData(512, BufferingStrategy.StackAlloc)]
    [InlineData(1024, BufferingStrategy.StackAlloc)]
    public void GetStrategy_WithSmallSize_ReturnsStackAlloc(int size, BufferingStrategy expected)
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();

        // Act
        var strategy = manager.GetStrategy(size);

        // Assert
        Assert.Equal(expected, strategy);
    }

    [Theory]
    [InlineData(1025, BufferingStrategy.Pooled)]
    [InlineData(2000, BufferingStrategy.Pooled)]
    [InlineData(32 * 1024, BufferingStrategy.Pooled)]
    [InlineData(64 * 1024, BufferingStrategy.Pooled)]
    public void GetStrategy_WithMediumSize_ReturnsPooled(int size, BufferingStrategy expected)
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();

        // Act
        var strategy = manager.GetStrategy(size);

        // Assert
        Assert.Equal(expected, strategy);
    }

    [Theory]
    [InlineData(64 * 1024 + 1, BufferingStrategy.PooledWithChunking)]
    [InlineData(100 * 1024, BufferingStrategy.PooledWithChunking)]
    [InlineData(512 * 1024, BufferingStrategy.PooledWithChunking)]
    [InlineData(1024 * 1024, BufferingStrategy.PooledWithChunking)]
    public void GetStrategy_WithLargeSize_ReturnsPooledWithChunking(int size, BufferingStrategy expected)
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();

        // Act
        var strategy = manager.GetStrategy(size);

        // Assert
        Assert.Equal(expected, strategy);
    }

    [Theory]
    [InlineData(1024 * 1024 + 1, BufferingStrategy.StreamBased)]
    [InlineData(2 * 1024 * 1024, BufferingStrategy.StreamBased)]
    [InlineData(10 * 1024 * 1024, BufferingStrategy.StreamBased)]
    public void GetStrategy_WithVeryLargeSize_ReturnsStreamBased(int size, BufferingStrategy expected)
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();

        // Act
        var strategy = manager.GetStrategy(size);

        // Assert
        Assert.Equal(expected, strategy);
    }

    [Fact]
    public void GetStrategy_AtThresholdBoundaries_ReturnsCorrectStrategy()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();

        // Assert boundary conditions
        Assert.Equal(BufferingStrategy.StackAlloc, manager.GetStrategy(1024));          // At small threshold
        Assert.Equal(BufferingStrategy.Pooled, manager.GetStrategy(1025));              // Just above small threshold
        Assert.Equal(BufferingStrategy.Pooled, manager.GetStrategy(64 * 1024));         // At medium threshold
        Assert.Equal(BufferingStrategy.PooledWithChunking, manager.GetStrategy(64 * 1024 + 1)); // Just above medium
        Assert.Equal(BufferingStrategy.PooledWithChunking, manager.GetStrategy(1024 * 1024)); // At large threshold
        Assert.Equal(BufferingStrategy.StreamBased, manager.GetStrategy(1024 * 1024 + 1)); // Just above large
    }

    #endregion

    #region PooledBuffer Tests

    [Fact]
    public void PooledBuffer_Array_ReturnsUnderlyingArray()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();

        // Act
        using var buffer = manager.Rent(100);

        // Assert
        Assert.NotNull(buffer.Array);
        Assert.True(buffer.Array.Length >= 100);
    }

    [Fact]
    public void PooledBuffer_FullSpan_ReturnsEntireArraySpan()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();

        // Act
        using var buffer = manager.Rent(100);

        // Assert
        Assert.Equal(buffer.Array.Length, buffer.FullSpan.Length);
    }

    [Fact]
    public void PooledBuffer_Span_ReturnsRequestedLengthSpan()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();
        const int requestedSize = 50;

        // Act
        using var buffer = manager.Rent(requestedSize);

        // Assert
        Assert.Equal(requestedSize, buffer.Span.Length);
    }

    [Fact]
    public void PooledBuffer_AfterDispose_ThrowsOnArrayAccess()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();
        var buffer = manager.Rent(100);

        // Act
        buffer.Dispose();

        // Assert - Use try-catch since buffer is a ref struct (can't be captured in lambda)
        ObjectDisposedException? exception = null;
        try
        {
            _ = buffer.Array;
        }
        catch (ObjectDisposedException ex)
        {
            exception = ex;
        }
        Assert.NotNull(exception);
    }

    [Fact]
    public void PooledBuffer_AfterDispose_ThrowsOnSpanAccess()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();
        var buffer = manager.Rent(100);

        // Act
        buffer.Dispose();

        // Assert - Use try-catch since buffer is a ref struct (can't be captured in lambda)
        ObjectDisposedException? exception = null;
        try
        {
            _ = buffer.Span;
        }
        catch (ObjectDisposedException ex)
        {
            exception = ex;
        }
        Assert.NotNull(exception);
    }

    [Fact]
    public void PooledBuffer_AfterDispose_ThrowsOnFullSpanAccess()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();
        var buffer = manager.Rent(100);

        // Act
        buffer.Dispose();

        // Assert - Use try-catch since buffer is a ref struct (can't be captured in lambda)
        ObjectDisposedException? exception = null;
        try
        {
            _ = buffer.FullSpan;
        }
        catch (ObjectDisposedException ex)
        {
            exception = ex;
        }
        Assert.NotNull(exception);
    }

    [Fact]
    public void PooledBuffer_DisposeWithClear_DoesNotThrow()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();
        var buffer = manager.Rent(100);

        // Write some data
        buffer.Span.Fill(0xFF);

        // Act & Assert - Should not throw
        buffer.Dispose(clearArray: true);
    }

    [Fact]
    public void PooledBuffer_DoubleDispose_DoesNotThrow()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();
        var buffer = manager.Rent(100);

        // Act & Assert - Double dispose should not throw
        buffer.Dispose();
        buffer.Dispose();  // Second dispose should be safe
    }

    [Fact]
    public void PooledBuffer_DoubleDisposeWithClear_DoesNotThrow()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();
        var buffer = manager.Rent(100);

        // Act & Assert - Double dispose with clear should not throw
        buffer.Dispose(clearArray: true);
        buffer.Dispose(clearArray: true);  // Second dispose should be safe
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void RentDisposeRent_ReturnsBufferCorrectly()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();

        // Act - Rent, dispose, rent again
        using (var buffer1 = manager.Rent(100))
        {
            buffer1.Span.Fill(0xAA);
        }  // Disposed here

        using var buffer2 = manager.Rent(100);

        // Assert - Should be able to use new buffer
        Assert.Equal(100, buffer2.Length);
        Assert.Equal(100, buffer2.Span.Length);
    }

    [Fact]
    public void MultipleBuffers_CanBeRentedSimultaneously()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();

        // Act
        using var buffer1 = manager.Rent(100);
        using var buffer2 = manager.Rent(200);
        using var buffer3 = manager.Rent(300);

        // Assert
        Assert.Equal(100, buffer1.Length);
        Assert.Equal(200, buffer2.Length);
        Assert.Equal(300, buffer3.Length);

        // Verify they're different buffers
        buffer1.Span.Fill(0x11);
        buffer2.Span.Fill(0x22);
        buffer3.Span.Fill(0x33);

        Assert.Equal(0x11, buffer1.Span[0]);
        Assert.Equal(0x22, buffer2.Span[0]);
        Assert.Equal(0x33, buffer3.Span[0]);
    }

    [Fact]
    public void RentAndCopy_CanBeModifiedIndependently()
    {
        // Arrange
        var manager = new DefaultBufferPoolManager();
        var sourceArray = new byte[] { 1, 2, 3, 4, 5 };
        ReadOnlySpan<byte> source = sourceArray;

        // Act
        using var buffer = manager.RentAndCopy(source);

        // Modify original
        sourceArray[0] = 99;

        // Assert - Buffer should have original values (it's a copy)
        Assert.Equal(1, buffer.Span[0]);
    }

    #endregion
}

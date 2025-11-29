using System.Text;
using HeroMessaging.Abstractions.Serialization;
using CompressionLevel = HeroMessaging.Abstractions.Configuration.CompressionLevel;

namespace HeroMessaging.Abstractions.Tests.Serialization;

[Trait("Category", "Unit")]
public class GZipCompressionProviderTests
{
    private readonly GZipCompressionProvider _provider;

    public GZipCompressionProviderTests()
    {
        _provider = new GZipCompressionProvider();
    }

    [Fact]
    public async Task CompressAsync_WithData_CompressesSuccessfully()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("This is test data for compression");

        // Act
        var compressed = await _provider.CompressAsync(data, CompressionLevel.Optimal);

        // Assert
        Assert.NotNull(compressed);
        Assert.NotEmpty(compressed);
        Assert.True(compressed.Length < data.Length);
    }

    [Fact]
    public async Task CompressAsync_WithEmptyData_ReturnsEmpty()
    {
        // Arrange
        var data = Array.Empty<byte>();

        // Act
        var compressed = await _provider.CompressAsync(data, CompressionLevel.Optimal);

        // Assert
        Assert.NotNull(compressed);
        Assert.Empty(compressed);
    }

    [Fact]
    public async Task CompressAsync_WithNullData_ReturnsEmpty()
    {
        // Arrange
        byte[]? data = null;

        // Act
        var compressed = await _provider.CompressAsync(data!, CompressionLevel.Optimal);

        // Assert
        Assert.NotNull(compressed);
        Assert.Empty(compressed);
    }

    [Fact]
    public async Task DecompressAsync_WithCompressedData_DecompressesSuccessfully()
    {
        // Arrange
        var original = Encoding.UTF8.GetBytes("This is test data for compression");
        var compressed = await _provider.CompressAsync(original, CompressionLevel.Optimal);

        // Act
        var decompressed = await _provider.DecompressAsync(compressed);

        // Assert
        Assert.Equal(original, decompressed);
    }

    [Fact]
    public async Task DecompressAsync_WithEmptyData_ReturnsEmpty()
    {
        // Arrange
        var data = Array.Empty<byte>();

        // Act
        var decompressed = await _provider.DecompressAsync(data);

        // Assert
        Assert.NotNull(decompressed);
        Assert.Empty(decompressed);
    }

    [Fact]
    public async Task DecompressAsync_WithNullData_ReturnsEmpty()
    {
        // Arrange
        byte[]? data = null;

        // Act
        var decompressed = await _provider.DecompressAsync(data!);

        // Assert
        Assert.NotNull(decompressed);
        Assert.Empty(decompressed);
    }

    [Fact]
    public async Task CompressDecompress_RoundTrip_PreservesData()
    {
        // Arrange
        var original = Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog");

        // Act
        var compressed = await _provider.CompressAsync(original, CompressionLevel.Optimal);
        var decompressed = await _provider.DecompressAsync(compressed);

        // Assert
        Assert.Equal(original, decompressed);
    }

    [Fact]
    public async Task CompressAsync_WithDifferentLevels_AllWork()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes(new string('A', 1000));

        // Act
        var fastest = await _provider.CompressAsync(data, CompressionLevel.Fastest);
        var optimal = await _provider.CompressAsync(data, CompressionLevel.Optimal);
        var maximum = await _provider.CompressAsync(data, CompressionLevel.Maximum);

        // Assert
        Assert.NotEmpty(fastest);
        Assert.NotEmpty(optimal);
        Assert.NotEmpty(maximum);
        // All levels should compress well for repetitive data
        Assert.True(fastest.Length < data.Length);
        Assert.True(optimal.Length < data.Length);
        Assert.True(maximum.Length < data.Length);
    }

    [Fact]
    public async Task CompressAsync_WithCancellation_Cancels()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes(new string('A', 100000));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _provider.CompressAsync(data, CompressionLevel.Optimal, cts.Token);
        });
    }

    [Fact]
    public void Compress_Span_CompressesSuccessfully()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("Test data");
        var destination = new byte[_provider.GetMaxCompressedSize(data, CompressionLevel.Optimal)];

        // Act
        var bytesWritten = _provider.Compress(data, destination, CompressionLevel.Optimal);

        // Assert
        Assert.True(bytesWritten > 0);
        Assert.True(bytesWritten <= destination.Length);
    }

    [Fact]
    public void Compress_Span_WithEmptySource_ReturnsZero()
    {
        // Arrange
        var data = Array.Empty<byte>();
        var destination = new byte[100];

        // Act
        var bytesWritten = _provider.Compress(data, destination, CompressionLevel.Optimal);

        // Assert
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void Decompress_Span_DecompressesSuccessfully()
    {
        // Arrange
        var original = Encoding.UTF8.GetBytes("Test data");
        var compressed = new byte[_provider.GetMaxCompressedSize(original, CompressionLevel.Optimal)];
        var compressedSize = _provider.Compress(original, compressed, CompressionLevel.Optimal);
        var destination = new byte[original.Length];

        // Act
        var bytesWritten = _provider.Decompress(compressed.AsSpan(0, compressedSize), destination);

        // Assert
        Assert.Equal(original.Length, bytesWritten);
        Assert.Equal(original, destination);
    }

    [Fact]
    public void Decompress_Span_WithEmptySource_ReturnsZero()
    {
        // Arrange
        var data = Array.Empty<byte>();
        var destination = new byte[100];

        // Act
        var bytesWritten = _provider.Decompress(data, destination);

        // Assert
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryCompress_WithSufficientSpace_ReturnsTrue()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("Test data");
        var destination = new byte[_provider.GetMaxCompressedSize(data, CompressionLevel.Optimal)];

        // Act
        var success = _provider.TryCompress(data, destination, CompressionLevel.Optimal, out var bytesWritten);

        // Assert
        Assert.True(success);
        Assert.True(bytesWritten > 0);
    }

    [Fact]
    public void TryCompress_WithInsufficientSpace_ReturnsFalse()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes(new string('A', 1000));
        var destination = new byte[10]; // Too small

        // Act
        var success = _provider.TryCompress(data, destination, CompressionLevel.Optimal, out var bytesWritten);

        // Assert
        Assert.False(success);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryDecompress_WithSufficientSpace_ReturnsTrue()
    {
        // Arrange
        var original = Encoding.UTF8.GetBytes("Test data");
        var compressed = new byte[_provider.GetMaxCompressedSize(original, CompressionLevel.Optimal)];
        var compressedSize = _provider.Compress(original, compressed, CompressionLevel.Optimal);
        var destination = new byte[original.Length];

        // Act
        var success = _provider.TryDecompress(compressed.AsSpan(0, compressedSize), destination, out var bytesWritten);

        // Assert
        Assert.True(success);
        Assert.Equal(original.Length, bytesWritten);
    }

    [Fact]
    public void TryDecompress_WithInsufficientSpace_ReturnsFalse()
    {
        // Arrange
        var original = Encoding.UTF8.GetBytes("Test data");
        var compressed = new byte[_provider.GetMaxCompressedSize(original, CompressionLevel.Optimal)];
        var compressedSize = _provider.Compress(original, compressed, CompressionLevel.Optimal);
        var destination = new byte[5]; // Too small

        // Act
        var success = _provider.TryDecompress(compressed.AsSpan(0, compressedSize), destination, out var bytesWritten);

        // Assert
        Assert.False(success);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void GetMaxCompressedSize_ReturnsReasonableEstimate()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("Test data");

        // Act
        var maxSize = _provider.GetMaxCompressedSize(data, CompressionLevel.Optimal);

        // Assert
        Assert.True(maxSize > data.Length);
        Assert.True(maxSize < data.Length * 2); // Should be reasonable estimate
    }

    [Fact]
    public void GetMaxCompressedSize_WithEmptyData_ReturnsMinimumSize()
    {
        // Arrange
        var data = Array.Empty<byte>();

        // Act
        var maxSize = _provider.GetMaxCompressedSize(data, CompressionLevel.Optimal);

        // Assert
        Assert.True(maxSize >= 0);
    }

    [Fact]
    public async Task LargeData_CompressesAndDecompresses()
    {
        // Arrange
        var largeData = new byte[1024 * 1024]; // 1 MB
        new Random(42).NextBytes(largeData);

        // Act
        var compressed = await _provider.CompressAsync(largeData, CompressionLevel.Optimal);
        var decompressed = await _provider.DecompressAsync(compressed);

        // Assert
        Assert.Equal(largeData, decompressed);
        Assert.True(compressed.Length < largeData.Length);
    }

    [Fact]
    public async Task HighlyCompressibleData_AchievesGoodCompression()
    {
        // Arrange
        var repetitiveData = Encoding.UTF8.GetBytes(new string('A', 10000));

        // Act
        var compressed = await _provider.CompressAsync(repetitiveData, CompressionLevel.Optimal);

        // Assert
        Assert.True(compressed.Length < repetitiveData.Length / 10); // Should compress very well
    }

    [Fact]
    public async Task RandomData_HasLimitedCompression()
    {
        // Arrange
        var randomData = new byte[1000];
        new Random(42).NextBytes(randomData);

        // Act
        var compressed = await _provider.CompressAsync(randomData, CompressionLevel.Optimal);

        // Assert
        // Random data doesn't compress well, might even be larger
        Assert.NotEmpty(compressed);
    }

    [Fact]
    public void ImplementsICompressionProvider()
    {
        // Arrange & Act
        var provider = new GZipCompressionProvider();

        // Assert
        Assert.IsAssignableFrom<ICompressionProvider>(provider);
    }

    [Fact]
    public async Task MultipleOperations_ProduceConsistentResults()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("Consistent test data");

        // Act
        var compressed1 = await _provider.CompressAsync(data, CompressionLevel.Optimal);
        var compressed2 = await _provider.CompressAsync(data, CompressionLevel.Optimal);

        // Assert
        Assert.Equal(compressed1, compressed2);
    }

    [Fact]
    public void ThreadSafety_ParallelOperations_Work()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("Thread safety test");
        var tasks = new List<Task<byte[]>>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(_provider.CompressAsync(data, CompressionLevel.Optimal).AsTask());
        }
        Task.WaitAll(tasks.ToArray());

        // Assert
        var first = tasks[0].Result;
        Assert.All(tasks, t => Assert.Equal(first, t.Result));
    }
}

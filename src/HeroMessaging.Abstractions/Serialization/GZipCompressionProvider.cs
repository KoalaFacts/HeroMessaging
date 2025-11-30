using System.IO.Compression;
using HeroMessaging.Abstractions.Configuration;
using SysCompressionLevel = System.IO.Compression.CompressionLevel;
using CompressionLevel = HeroMessaging.Abstractions.Configuration.CompressionLevel;

namespace HeroMessaging.Abstractions.Serialization;

/// <summary>
/// Provides GZip compression and decompression for message serialization
/// </summary>
public class GZipCompressionProvider : ICompressionProvider
{
    /// <summary>
    /// Maximum decompressed size to prevent decompression bomb attacks (100MB default)
    /// </summary>
    public const int DefaultMaxDecompressedSize = 100 * 1024 * 1024;

    private readonly int _maxDecompressedSize;

    /// <summary>
    /// Creates a new GZipCompressionProvider with default maximum decompressed size (100MB)
    /// </summary>
    public GZipCompressionProvider() : this(DefaultMaxDecompressedSize)
    {
    }

    /// <summary>
    /// Creates a new GZipCompressionProvider with the specified maximum decompressed size
    /// </summary>
    /// <param name="maxDecompressedSize">Maximum allowed decompressed size in bytes (default: 100MB)</param>
    public GZipCompressionProvider(int maxDecompressedSize)
    {
        _maxDecompressedSize = maxDecompressedSize > 0 ? maxDecompressedSize : DefaultMaxDecompressedSize;
    }

    /// <inheritdoc />
    public async ValueTask<byte[]> CompressAsync(
        byte[] data,
        CompressionLevel level,
        CancellationToken cancellationToken = default)
    {
        if (data == null || data.Length == 0)
        {
            return [];
        }

        using var output = new MemoryStream();

        var gzipLevel = MapCompressionLevel(level);

        using (var gzip = new GZipStream(output, gzipLevel))
        {
            await gzip.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        }

        return output.ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<byte[]> DecompressAsync(
        byte[] data,
        CancellationToken cancellationToken = default)
    {
        if (data == null || data.Length == 0)
        {
            return [];
        }

        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        using var gzip = new GZipStream(input, CompressionMode.Decompress);

        // Use a limited copy to prevent decompression bomb attacks
        var buffer = new byte[81920]; // 80KB buffer
        int bytesRead;
        long totalBytesRead = 0;

        while ((bytesRead = await gzip.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            totalBytesRead += bytesRead;
            if (totalBytesRead > _maxDecompressedSize)
            {
                throw new InvalidOperationException(
                    $"Decompressed data exceeds maximum allowed size of {_maxDecompressedSize} bytes. " +
                    "This may indicate a decompression bomb attack.");
            }

            await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
        }

        return output.ToArray();
    }

    /// <inheritdoc />
    public int Compress(ReadOnlySpan<byte> source, Span<byte> destination, CompressionLevel level)
    {
        if (source.IsEmpty) return 0;

        var gzipLevel = MapCompressionLevel(level);

        using var output = new MemoryStream(destination.Length);
        using (var gzip = new GZipStream(output, gzipLevel, leaveOpen: true))
        {
            gzip.Write(source);
        }

        var bytesWritten = (int)output.Position;
        if (bytesWritten > destination.Length)
        {
            throw new ArgumentException($"Destination buffer too small. Required: {bytesWritten}, Available: {destination.Length}");
        }

        output.Position = 0;
        output.Read(destination);
        return bytesWritten;
    }

    /// <inheritdoc />
    public bool TryCompress(ReadOnlySpan<byte> source, Span<byte> destination, CompressionLevel level, out int bytesWritten)
    {
        try
        {
            bytesWritten = Compress(source, destination, level);
            return true;
        }
        catch (ArgumentException)
        {
            // Buffer too small - expected failure case
            bytesWritten = 0;
            return false;
        }
        // Let critical exceptions (OutOfMemoryException, etc.) propagate
    }

    /// <inheritdoc />
    public int GetMaxCompressedSize(ReadOnlySpan<byte> source, CompressionLevel level)
    {
        // GZip worst case: input size + 18 bytes header + 8 bytes footer + 0.1% for deflate overhead
        // Conservative estimate: input * 1.01 + 64 bytes
        return (int)(source.Length * 1.01) + 64;
    }

    /// <inheritdoc />
    public int Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (source.IsEmpty) return 0;

        using var input = new MemoryStream(source.ToArray());
        using var gzip = new GZipStream(input, CompressionMode.Decompress);

        var totalRead = 0;
        int bytesRead;
        while ((bytesRead = gzip.Read(destination[totalRead..])) > 0)
        {
            totalRead += bytesRead;
            if (totalRead > _maxDecompressedSize)
            {
                throw new InvalidOperationException(
                    $"Decompressed data exceeds maximum allowed size of {_maxDecompressedSize} bytes.");
            }
        }

        return totalRead;
    }

    /// <inheritdoc />
    public bool TryDecompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
    {
        try
        {
            bytesWritten = Decompress(source, destination);
            return true;
        }
        catch (ArgumentException)
        {
            // Buffer too small - expected failure case
            bytesWritten = 0;
            return false;
        }
        catch (InvalidOperationException)
        {
            // Decompression bomb protection triggered - expected failure case
            bytesWritten = 0;
            return false;
        }
        // Let critical exceptions propagate
    }

    /// <summary>
    /// Maps the HeroMessaging compression level to System.IO.Compression level
    /// </summary>
    private static SysCompressionLevel MapCompressionLevel(CompressionLevel level)
    {
        // Note: Maximum is an alias for SmallestSize, so they map to the same value
        return level switch
        {
            CompressionLevel.None => SysCompressionLevel.NoCompression,
            CompressionLevel.Fastest => SysCompressionLevel.Fastest,
            CompressionLevel.Optimal => SysCompressionLevel.Optimal,
            CompressionLevel.SmallestSize => SysCompressionLevel.SmallestSize,
            _ => SysCompressionLevel.Optimal
        };
    }
}

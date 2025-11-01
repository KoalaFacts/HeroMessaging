using System.IO.Compression;

namespace HeroMessaging.Abstractions.Serialization;

/// <summary>
/// Provides compression and decompression utilities for message serialization
/// </summary>
public static class CompressionHelper
{
    /// <summary>
    /// Compresses data using GZip compression
    /// </summary>
    /// <param name="data">The data to compress</param>
    /// <param name="level">The compression level to use</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Compressed data</returns>
    public static async ValueTask<byte[]> CompressAsync(
        byte[] data,
        CompressionLevel level,
        CancellationToken cancellationToken = default)
    {
        if (data == null || data.Length == 0)
        {
            return Array.Empty<byte>();
        }

        using var output = new MemoryStream();

        var gzipLevel = MapCompressionLevel(level);

        using (var gzip = new GZipStream(output, gzipLevel))
        {
#if NETSTANDARD2_0
            await gzip.WriteAsync(data, 0, data.Length, cancellationToken);
#else
            await gzip.WriteAsync(data, cancellationToken);
#endif
        }

        return output.ToArray();
    }

    /// <summary>
    /// Decompresses GZip-compressed data
    /// </summary>
    /// <param name="data">The compressed data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Decompressed data</returns>
    public static async ValueTask<byte[]> DecompressAsync(
        byte[] data,
        CancellationToken cancellationToken = default)
    {
        if (data == null || data.Length == 0)
        {
            return Array.Empty<byte>();
        }

        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        using var gzip = new GZipStream(input, CompressionMode.Decompress);

#if NETSTANDARD2_0
        await gzip.CopyToAsync(output);
#else
        await gzip.CopyToAsync(output, cancellationToken);
#endif

        return output.ToArray();
    }

    /// <summary>
    /// Maps the HeroMessaging compression level to System.IO.Compression level
    /// </summary>
    private static System.IO.Compression.CompressionLevel MapCompressionLevel(CompressionLevel level)
    {
        return level switch
        {
            CompressionLevel.None => System.IO.Compression.CompressionLevel.NoCompression,
            CompressionLevel.Fastest => System.IO.Compression.CompressionLevel.Fastest,
            CompressionLevel.Optimal => System.IO.Compression.CompressionLevel.Optimal,
            CompressionLevel.Maximum => System.IO.Compression.CompressionLevel.Optimal,
            _ => System.IO.Compression.CompressionLevel.Optimal
        };
    }
}

using System;
using System.Buffers;
using System.IO.Compression;

namespace HeroMessaging.Abstractions.Serialization;

/// <summary>
/// Provides GZip compression and decompression for message serialization
/// </summary>
public class GZipCompressionProvider : ICompressionProvider
{
    /// <inheritdoc />
    public async ValueTask<byte[]> CompressAsync(
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

    /// <inheritdoc />
    public async ValueTask<byte[]> DecompressAsync(
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

    /// <inheritdoc />
    public int Compress(ReadOnlySpan<byte> source, Span<byte> destination, CompressionLevel level)
    {
        if (source.IsEmpty) return 0;

        var gzipLevel = MapCompressionLevel(level);

        using var output = new MemoryStream(destination.Length);
        using (var gzip = new GZipStream(output, gzipLevel, leaveOpen: true))
        {
#if NETSTANDARD2_0
            gzip.Write(source.ToArray(), 0, source.Length);
#else
            gzip.Write(source);
#endif
        }

        var bytesWritten = (int)output.Position;
        if (bytesWritten > destination.Length)
        {
            throw new ArgumentException($"Destination buffer too small. Required: {bytesWritten}, Available: {destination.Length}");
        }

        output.Position = 0;
#if NETSTANDARD2_0
        var buffer = destination.ToArray();
        output.Read(buffer, 0, destination.Length);
        buffer.CopyTo(destination);
#else
        output.Read(destination);
#endif
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
        catch
        {
            bytesWritten = 0;
            return false;
        }
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

#if NETSTANDARD2_0
        var buffer = destination.ToArray();
        var bytesRead = gzip.Read(buffer, 0, destination.Length);
        buffer.AsSpan(0, bytesRead).CopyTo(destination);
        return bytesRead;
#else
        return gzip.Read(destination);
#endif
    }

    /// <inheritdoc />
    public bool TryDecompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
    {
        try
        {
            bytesWritten = Decompress(source, destination);
            return true;
        }
        catch
        {
            bytesWritten = 0;
            return false;
        }
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

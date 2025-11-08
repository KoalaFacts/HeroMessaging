using System;

namespace HeroMessaging.Abstractions.Serialization;

/// <summary>
/// Provides compression and decompression services for message serialization
/// </summary>
public interface ICompressionProvider
{
    /// <summary>
    /// Compresses data (async, allocates array)
    /// </summary>
    /// <param name="data">The data to compress</param>
    /// <param name="level">The compression level to use</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Compressed data</returns>
    ValueTask<byte[]> CompressAsync(
        byte[] data,
        CompressionLevel level,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decompresses data (async, allocates array)
    /// </summary>
    /// <param name="data">The compressed data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Decompressed data</returns>
    ValueTask<byte[]> DecompressAsync(
        byte[] data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compresses data to a destination span (zero-allocation synchronous).
    /// Returns the number of bytes written to the destination.
    /// </summary>
    int Compress(ReadOnlySpan<byte> source, Span<byte> destination, CompressionLevel level);

    /// <summary>
    /// Try to compress data to a destination span.
    /// </summary>
    bool TryCompress(ReadOnlySpan<byte> source, Span<byte> destination, CompressionLevel level, out int bytesWritten);

    /// <summary>
    /// Get the maximum compressed size for the given source data.
    /// This is an estimate; actual compressed size may be smaller.
    /// </summary>
    int GetMaxCompressedSize(ReadOnlySpan<byte> source, CompressionLevel level);

    /// <summary>
    /// Decompresses data from a source span to a destination span (zero-allocation synchronous).
    /// Returns the number of bytes written to the destination.
    /// </summary>
    int Decompress(ReadOnlySpan<byte> source, Span<byte> destination);

    /// <summary>
    /// Try to decompress data from a source span to a destination span.
    /// </summary>
    bool TryDecompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten);
}

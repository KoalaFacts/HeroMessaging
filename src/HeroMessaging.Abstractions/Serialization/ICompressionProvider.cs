namespace HeroMessaging.Abstractions.Serialization;

/// <summary>
/// Provides compression and decompression services for message serialization
/// </summary>
public interface ICompressionProvider
{
    /// <summary>
    /// Compresses data
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
    /// Decompresses data
    /// </summary>
    /// <param name="data">The compressed data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Decompressed data</returns>
    ValueTask<byte[]> DecompressAsync(
        byte[] data,
        CancellationToken cancellationToken = default);
}

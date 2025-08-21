using System.IO.Compression;
using HeroMessaging.Abstractions.Serialization;

namespace HeroMessaging.Serialization;

/// <summary>
/// Base class for message serializers with compression support
/// </summary>
public abstract class BaseMessageSerializer : IMessageSerializer
{
    protected readonly SerializationOptions Options;
    
    protected BaseMessageSerializer(SerializationOptions? options = null)
    {
        Options = options ?? new SerializationOptions();
    }
    
    public abstract string ContentType { get; }
    
    public async ValueTask<byte[]> SerializeAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        if (message == null)
        {
            return Array.Empty<byte>();
        }
        
        var data = await SerializeCore(message, cancellationToken);
        
        if (Options.MaxMessageSize > 0 && data.Length > Options.MaxMessageSize)
        {
            throw new InvalidOperationException($"Serialized message size ({data.Length} bytes) exceeds maximum allowed size ({Options.MaxMessageSize} bytes)");
        }
        
        if (Options.EnableCompression)
        {
            data = await CompressAsync(data, cancellationToken);
        }
        
        return data;
    }
    
    public async ValueTask<T> DeserializeAsync<T>(byte[] data, CancellationToken cancellationToken = default) where T : class
    {
        if (data == null || data.Length == 0)
        {
            return default(T)!;
        }
        
        if (Options.EnableCompression)
        {
            data = await DecompressAsync(data, cancellationToken);
        }
        
        return await DeserializeCore<T>(data, cancellationToken);
    }
    
    public async ValueTask<object?> DeserializeAsync(byte[] data, Type messageType, CancellationToken cancellationToken = default)
    {
        if (data == null || data.Length == 0)
        {
            return null;
        }
        
        if (Options.EnableCompression)
        {
            data = await DecompressAsync(data, cancellationToken);
        }
        
        return await DeserializeCore(data, messageType, cancellationToken);
    }
    
    protected abstract ValueTask<byte[]> SerializeCore<T>(T message, CancellationToken cancellationToken);
    protected abstract ValueTask<T> DeserializeCore<T>(byte[] data, CancellationToken cancellationToken) where T : class;
    protected abstract ValueTask<object?> DeserializeCore(byte[] data, Type messageType, CancellationToken cancellationToken);
    
    private async ValueTask<byte[]> CompressAsync(byte[] data, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        
        var compressionLevel = Options.CompressionLevel switch
        {
            Abstractions.Serialization.CompressionLevel.None => System.IO.Compression.CompressionLevel.NoCompression,
            Abstractions.Serialization.CompressionLevel.Fastest => System.IO.Compression.CompressionLevel.Fastest,
            Abstractions.Serialization.CompressionLevel.Optimal => System.IO.Compression.CompressionLevel.Optimal,
            Abstractions.Serialization.CompressionLevel.Maximum => System.IO.Compression.CompressionLevel.Optimal,
            _ => System.IO.Compression.CompressionLevel.Optimal
        };
        
        using (var gzip = new GZipStream(output, compressionLevel))
        {
            await gzip.WriteAsync(data, 0, data.Length, cancellationToken);
        }
        
        return output.ToArray();
    }
    
    private async ValueTask<byte[]> DecompressAsync(byte[] data, CancellationToken cancellationToken)
    {
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
}
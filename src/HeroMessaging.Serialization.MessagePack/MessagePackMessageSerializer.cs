using System.Buffers;
using System.IO.Compression;
using HeroMessaging.Abstractions.Serialization;
using MessagePack;
using MessagePack.Resolvers;

namespace HeroMessaging.Serialization.MessagePack;

/// <summary>
/// MessagePack serializer for high-performance binary serialization
/// </summary>
public class MessagePackMessageSerializer(SerializationOptions? options = null, MessagePackSerializerOptions? messagePackOptions = null) : IMessageSerializer
{
    private readonly SerializationOptions _options = options ?? new SerializationOptions();
    private readonly MessagePackSerializerOptions _messagePackOptions = messagePackOptions ?? CreateDefaultOptions();

    ///<inheritdoc/>
    public string ContentType => "application/x-msgpack";

    ///<inheritdoc/>
    public async ValueTask<byte[]> SerializeAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        if (message == null)
        {
            return Array.Empty<byte>();
        }

        var data = MessagePackSerializer.Serialize(message, _messagePackOptions, cancellationToken);

        if (_options.MaxMessageSize > 0 && data.Length > _options.MaxMessageSize)
        {
            throw new InvalidOperationException($"Serialized message size ({data.Length} bytes) exceeds maximum allowed size ({_options.MaxMessageSize} bytes)");
        }

        if (_options.EnableCompression)
        {
            data = await CompressAsync(data, cancellationToken);
        }

        return data;
    }

    ///<inheritdoc/>
    public async ValueTask<T> DeserializeAsync<T>(byte[] data, CancellationToken cancellationToken = default) where T : class
    {
        if (data == null || data.Length == 0)
        {
            return default(T)!;
        }

        if (_options.EnableCompression)
        {
            data = await DecompressAsync(data, cancellationToken);
        }

        var result = MessagePackSerializer.Deserialize<T>(data, _messagePackOptions, cancellationToken);
        return result!;
    }

    ///<inheritdoc/>
    public async ValueTask<object?> DeserializeAsync(byte[] data, Type messageType, CancellationToken cancellationToken = default)
    {
        if (data == null || data.Length == 0)
        {
            return null;
        }

        if (_options.EnableCompression)
        {
            data = await DecompressAsync(data, cancellationToken);
        }

        var result = MessagePackSerializer.Deserialize(messageType, data, _messagePackOptions, cancellationToken);
        return result;
    }

    public int Serialize<T>(T message, Span<byte> destination)
    {
        if (message == null) return 0;

        var bufferWriter = new ArrayBufferWriter<byte>(destination.Length);
        MessagePackSerializer.Serialize(bufferWriter, message, _messagePackOptions);

        var written = bufferWriter.WrittenSpan;
        if (written.Length > destination.Length)
        {
            throw new ArgumentException($"Destination buffer too small. Required: {written.Length}, Available: {destination.Length}");
        }

        written.CopyTo(destination);
        return written.Length;
    }

    public bool TrySerialize<T>(T message, Span<byte> destination, out int bytesWritten)
    {
        try
        {
            bytesWritten = Serialize(message, destination);
            return true;
        }
        catch
        {
            bytesWritten = 0;
            return false;
        }
    }

    public int GetRequiredBufferSize<T>(T message)
    {
        // MessagePack is typically compact - estimate 2KB for most messages
        return 2048;
    }

    public T Deserialize<T>(ReadOnlySpan<byte> data) where T : class
    {
        if (data.IsEmpty) return default(T)!;

        var memory = new ReadOnlyMemory<byte>(data.ToArray());
        return MessagePackSerializer.Deserialize<T>(memory, _messagePackOptions)!;
    }

    public object? Deserialize(ReadOnlySpan<byte> data, Type messageType)
    {
        if (data.IsEmpty) return null;

        var memory = new ReadOnlyMemory<byte>(data.ToArray());
        return MessagePackSerializer.Deserialize(messageType, memory, _messagePackOptions);
    }

    private static MessagePackSerializerOptions CreateDefaultOptions()
    {
        return MessagePackSerializerOptions.Standard
            .WithResolver(ContractlessStandardResolver.Instance)
            .WithCompression(MessagePackCompression.Lz4BlockArray)
            .WithSecurity(MessagePackSecurity.UntrustedData);
    }

    private async ValueTask<byte[]> CompressAsync(byte[] data, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();

        var compressionLevel = _options.CompressionLevel switch
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

        await gzip.CopyToAsync(output, cancellationToken);
        return output.ToArray();
    }
}

/// <summary>
/// MessagePack serializer with type-safe contracts (requires MessagePack attributes)
/// </summary>
public class ContractMessagePackSerializer(SerializationOptions? options = null, MessagePackSerializerOptions? messagePackOptions = null) : IMessageSerializer
{
    private readonly SerializationOptions _options = options ?? new SerializationOptions();
    private readonly MessagePackSerializerOptions _messagePackOptions = messagePackOptions ?? CreateDefaultOptions();


    public string ContentType => "application/x-msgpack-contract";

    public async ValueTask<byte[]> SerializeAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        if (message == null)
        {
            return Array.Empty<byte>();
        }

        var data = MessagePackSerializer.Serialize(message, _messagePackOptions, cancellationToken);

        if (_options.MaxMessageSize > 0 && data.Length > _options.MaxMessageSize)
        {
            throw new InvalidOperationException($"Serialized message size ({data.Length} bytes) exceeds maximum allowed size ({_options.MaxMessageSize} bytes)");
        }

        if (_options.EnableCompression)
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

        if (_options.EnableCompression)
        {
            data = await DecompressAsync(data, cancellationToken);
        }

        var result = MessagePackSerializer.Deserialize<T>(data, _messagePackOptions, cancellationToken);
        return result!;
    }

    public async ValueTask<object?> DeserializeAsync(byte[] data, Type messageType, CancellationToken cancellationToken = default)
    {
        if (data == null || data.Length == 0)
        {
            return null;
        }

        if (_options.EnableCompression)
        {
            data = await DecompressAsync(data, cancellationToken);
        }

        var result = MessagePackSerializer.Deserialize(messageType, data, _messagePackOptions, cancellationToken);
        return result;
    }

    public int Serialize<T>(T message, Span<byte> destination)
    {
        if (message == null) return 0;

        var bufferWriter = new ArrayBufferWriter<byte>(destination.Length);
        MessagePackSerializer.Serialize(bufferWriter, message, _messagePackOptions);

        var written = bufferWriter.WrittenSpan;
        if (written.Length > destination.Length)
        {
            throw new ArgumentException($"Destination buffer too small. Required: {written.Length}, Available: {destination.Length}");
        }

        written.CopyTo(destination);
        return written.Length;
    }

    public bool TrySerialize<T>(T message, Span<byte> destination, out int bytesWritten)
    {
        try
        {
            bytesWritten = Serialize(message, destination);
            return true;
        }
        catch
        {
            bytesWritten = 0;
            return false;
        }
    }

    public int GetRequiredBufferSize<T>(T message)
    {
        // MessagePack is typically compact - estimate 2KB for most messages
        return 2048;
    }

    public T Deserialize<T>(ReadOnlySpan<byte> data) where T : class
    {
        if (data.IsEmpty) return default(T)!;

        var memory = new ReadOnlyMemory<byte>(data.ToArray());
        return MessagePackSerializer.Deserialize<T>(memory, _messagePackOptions)!;
    }

    public object? Deserialize(ReadOnlySpan<byte> data, Type messageType)
    {
        if (data.IsEmpty) return null;

        var memory = new ReadOnlyMemory<byte>(data.ToArray());
        return MessagePackSerializer.Deserialize(messageType, memory, _messagePackOptions);
    }

    private static MessagePackSerializerOptions CreateDefaultOptions()
    {
        // Use standard resolver which requires MessagePack attributes for better performance
        return MessagePackSerializerOptions.Standard
            .WithResolver(StandardResolver.Instance)
            .WithCompression(MessagePackCompression.Lz4BlockArray)
            .WithSecurity(MessagePackSecurity.UntrustedData);
    }

    private async ValueTask<byte[]> CompressAsync(byte[] data, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();

        var compressionLevel = _options.CompressionLevel switch
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

        await gzip.CopyToAsync(output, cancellationToken);
        return output.ToArray();
    }
}

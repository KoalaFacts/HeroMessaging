using HeroMessaging.Abstractions.Serialization;
using ProtoBuf;
using ProtoBuf.Meta;
using System;
using System.Buffers;

namespace HeroMessaging.Serialization.Protobuf;

/// <summary>
/// Protocol Buffers serializer for efficient binary serialization
/// </summary>
public class ProtobufMessageSerializer(
    SerializationOptions? options = null,
    RuntimeTypeModel? typeModel = null,
    ICompressionProvider? compressionProvider = null) : IMessageSerializer
{
    private readonly SerializationOptions _options = options ?? new SerializationOptions();
    private readonly RuntimeTypeModel _typeModel = typeModel ?? RuntimeTypeModel.Default;
    private readonly ICompressionProvider _compressionProvider = compressionProvider ?? new GZipCompressionProvider();

    public string ContentType => "application/x-protobuf";

    public async ValueTask<byte[]> SerializeAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        if (message == null)
        {
            return Array.Empty<byte>();
        }

        using var stream = new MemoryStream();
        _typeModel.Serialize(stream, message);
        var data = stream.ToArray();

        if (_options.MaxMessageSize > 0 && data.Length > _options.MaxMessageSize)
        {
            throw new InvalidOperationException($"Serialized message size ({data.Length} bytes) exceeds maximum allowed size ({_options.MaxMessageSize} bytes)");
        }

        if (_options.EnableCompression)
        {
            data = await _compressionProvider.CompressAsync(data, _options.CompressionLevel, cancellationToken);
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
            data = await _compressionProvider.DecompressAsync(data, cancellationToken);
        }

        using var stream = new MemoryStream(data);
        var result = _typeModel.Deserialize<T>(stream);
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
            data = await _compressionProvider.DecompressAsync(data, cancellationToken);
        }

        using var stream = new MemoryStream(data);
        var result = _typeModel.Deserialize(stream, null, messageType);
        return result;
    }

    public int Serialize<T>(T message, Span<byte> destination)
    {
        if (message == null) return 0;

        using var stream = new MemoryStream();
        _typeModel.Serialize(stream, message);

        var bytesWritten = (int)stream.Position;
        if (bytesWritten > destination.Length)
        {
            throw new ArgumentException($"Destination buffer too small. Required: {bytesWritten}, Available: {destination.Length}");
        }

        stream.Position = 0;
        stream.Read(destination);
        return bytesWritten;
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
        // Protobuf is compact - estimate 2KB for most messages
        return 2048;
    }

    public T Deserialize<T>(ReadOnlySpan<byte> data) where T : class
    {
        if (data.IsEmpty) return default(T)!;

        using var stream = new MemoryStream(data.ToArray());
        return _typeModel.Deserialize<T>(stream)!;
    }

    public object? Deserialize(ReadOnlySpan<byte> data, Type messageType)
    {
        if (data.IsEmpty) return null;

        using var stream = new MemoryStream(data.ToArray());
        return _typeModel.Deserialize(stream, null, messageType);
    }
}

/// <summary>
/// Protobuf serializer with type information included for polymorphic scenarios
/// </summary>
public class TypedProtobufMessageSerializer(
    SerializationOptions? options = null,
    RuntimeTypeModel? typeModel = null,
    ICompressionProvider? compressionProvider = null) : IMessageSerializer
{
    private readonly SerializationOptions _options = options ?? new SerializationOptions();
    private readonly RuntimeTypeModel _typeModel = typeModel ?? RuntimeTypeModel.Default;
    private readonly ICompressionProvider _compressionProvider = compressionProvider ?? new GZipCompressionProvider();

    public string ContentType => "application/x-protobuf-typed";

    public async ValueTask<byte[]> SerializeAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        if (message == null)
        {
            return Array.Empty<byte>();
        }

        using var stream = new MemoryStream();

        if (_options.IncludeTypeInformation && message != null)
        {
            // Write type information
            var typeName = message.GetType().AssemblyQualifiedName ?? "";
            Serializer.SerializeWithLengthPrefix(stream, typeName, PrefixStyle.Base128);
        }

        // Write the actual message
        _typeModel.SerializeWithLengthPrefix(stream, message, typeof(T), PrefixStyle.Base128, 0);

        var data = stream.ToArray();

        if (_options.MaxMessageSize > 0 && data.Length > _options.MaxMessageSize)
        {
            throw new InvalidOperationException($"Serialized message size ({data.Length} bytes) exceeds maximum allowed size ({_options.MaxMessageSize} bytes)");
        }

        if (_options.EnableCompression)
        {
            data = await _compressionProvider.CompressAsync(data, _options.CompressionLevel, cancellationToken);
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
            data = await _compressionProvider.DecompressAsync(data, cancellationToken);
        }

        using var stream = new MemoryStream(data);

        if (_options.IncludeTypeInformation)
        {
            // Skip type information if present
            Serializer.DeserializeWithLengthPrefix<string>(stream, PrefixStyle.Base128);
        }

        var result = (T?)_typeModel.DeserializeWithLengthPrefix(stream, null, typeof(T), PrefixStyle.Base128, 0);
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
            data = await _compressionProvider.DecompressAsync(data, cancellationToken);
        }

        using var stream = new MemoryStream(data);

        if (_options.IncludeTypeInformation)
        {
            // Read type information
            var typeName = Serializer.DeserializeWithLengthPrefix<string>(stream, PrefixStyle.Base128);
            if (!string.IsNullOrEmpty(typeName))
            {
                var actualType = Type.GetType(typeName);
                if (actualType != null)
                {
                    messageType = actualType;
                }
            }
        }

        var result = _typeModel.DeserializeWithLengthPrefix(stream, null, messageType, PrefixStyle.Base128, 0);
        return result;
    }

    public int Serialize<T>(T message, Span<byte> destination)
    {
        if (message == null) return 0;

        using var stream = new MemoryStream();

        if (_options.IncludeTypeInformation && message != null)
        {
            var typeName = message.GetType().AssemblyQualifiedName ?? "";
            Serializer.SerializeWithLengthPrefix(stream, typeName, PrefixStyle.Base128);
        }

        _typeModel.SerializeWithLengthPrefix(stream, message, typeof(T), PrefixStyle.Base128, 0);

        var bytesWritten = (int)stream.Position;
        if (bytesWritten > destination.Length)
        {
            throw new ArgumentException($"Destination buffer too small. Required: {bytesWritten}, Available: {destination.Length}");
        }

        stream.Position = 0;
        stream.Read(destination);
        return bytesWritten;
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
        // Protobuf with type info - estimate 2KB + overhead for type names
        return 2048 + 256;
    }

    public T Deserialize<T>(ReadOnlySpan<byte> data) where T : class
    {
        if (data.IsEmpty) return default(T)!;

        using var stream = new MemoryStream(data.ToArray());

        if (_options.IncludeTypeInformation)
        {
            Serializer.DeserializeWithLengthPrefix<string>(stream, PrefixStyle.Base128);
        }

        return (T?)_typeModel.DeserializeWithLengthPrefix(stream, null, typeof(T), PrefixStyle.Base128, 0)!;
    }

    public object? Deserialize(ReadOnlySpan<byte> data, Type messageType)
    {
        if (data.IsEmpty) return null;

        using var stream = new MemoryStream(data.ToArray());

        if (_options.IncludeTypeInformation)
        {
            var typeName = Serializer.DeserializeWithLengthPrefix<string>(stream, PrefixStyle.Base128);
            if (!string.IsNullOrEmpty(typeName))
            {
                var actualType = Type.GetType(typeName);
                if (actualType != null)
                {
                    messageType = actualType;
                }
            }
        }

        return _typeModel.DeserializeWithLengthPrefix(stream, null, messageType, PrefixStyle.Base128, 0);
    }
}
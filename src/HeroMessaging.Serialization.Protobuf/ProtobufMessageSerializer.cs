using HeroMessaging.Abstractions.Serialization;
using ProtoBuf;
using ProtoBuf.Meta;

namespace HeroMessaging.Serialization.Protobuf;

/// <summary>
/// Protocol Buffers serializer for efficient binary serialization
/// </summary>
public class ProtobufMessageSerializer(SerializationOptions? options = null, RuntimeTypeModel? typeModel = null) : IMessageSerializer
{
    private readonly SerializationOptions _options = options ?? new SerializationOptions();
    private readonly RuntimeTypeModel _typeModel = typeModel ?? RuntimeTypeModel.Default;


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
            data = await CompressionHelper.CompressAsync(data, _options.CompressionLevel, cancellationToken);
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
            data = await CompressionHelper.DecompressAsync(data, cancellationToken);
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
            data = await CompressionHelper.DecompressAsync(data, cancellationToken);
        }

        using var stream = new MemoryStream(data);
        var result = _typeModel.Deserialize(stream, null, messageType);
        return result;
    }
}

/// <summary>
/// Protobuf serializer with type information included for polymorphic scenarios
/// </summary>
public class TypedProtobufMessageSerializer(SerializationOptions? options = null, RuntimeTypeModel? typeModel = null) : IMessageSerializer
{
    private readonly SerializationOptions _options = options ?? new SerializationOptions();
    private readonly RuntimeTypeModel _typeModel = typeModel ?? RuntimeTypeModel.Default;


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
            data = await CompressionHelper.CompressAsync(data, _options.CompressionLevel, cancellationToken);
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
            data = await CompressionHelper.DecompressAsync(data, cancellationToken);
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
            data = await CompressionHelper.DecompressAsync(data, cancellationToken);
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
}
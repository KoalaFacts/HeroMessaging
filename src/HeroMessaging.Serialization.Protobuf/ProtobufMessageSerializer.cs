using HeroMessaging.Abstractions.Serialization;
using ProtoBuf;
using ProtoBuf.Meta;
using System;
using System.Buffers;
using System.Collections.Concurrent;

namespace HeroMessaging.Serialization.Protobuf;

/// <summary>
/// Type registry for safe type resolution during deserialization.
/// Only explicitly registered types can be deserialized to prevent type injection attacks.
/// </summary>
public sealed class ProtobufTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _allowedTypes = new();

    /// <summary>
    /// Registers a type as allowed for deserialization
    /// </summary>
    public ProtobufTypeRegistry Register<T>() where T : class
    {
        var type = typeof(T);
        var typeName = type.AssemblyQualifiedName;
        if (!string.IsNullOrEmpty(typeName))
        {
            _allowedTypes[typeName] = type;
        }
        // Also register by full name for flexibility
        _allowedTypes[type.FullName ?? type.Name] = type;
        return this;
    }

    /// <summary>
    /// Registers a type as allowed for deserialization
    /// </summary>
    public ProtobufTypeRegistry Register(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var typeName = type.AssemblyQualifiedName;
        if (!string.IsNullOrEmpty(typeName))
        {
            _allowedTypes[typeName] = type;
        }
        _allowedTypes[type.FullName ?? type.Name] = type;
        return this;
    }

    /// <summary>
    /// Attempts to resolve a type name to an allowed type.
    /// Returns null if the type is not registered (safe default).
    /// </summary>
    public Type? TryResolve(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return null;

        return _allowedTypes.TryGetValue(typeName, out var type) ? type : null;
    }

    /// <summary>
    /// Creates a new registry with common HeroMessaging types pre-registered
    /// </summary>
    public static ProtobufTypeRegistry CreateDefault() => new();
}

/// <summary>
/// Protocol Buffers serializer for efficient binary serialization
/// </summary>
public class ProtobufMessageSerializer(
    SerializationOptions? options = null,
    RuntimeTypeModel? typeModel = null,
    ICompressionProvider? compressionProvider = null) : IMessageSerializer
{
    private readonly SerializationOptions _options = options ?? new SerializationOptions();
    private readonly RuntimeTypeModel _typeModel = typeModel ?? CreateDefaultTypeModel();
    private readonly ICompressionProvider _compressionProvider = compressionProvider ?? new GZipCompressionProvider();

    private static RuntimeTypeModel CreateDefaultTypeModel()
    {
        var model = RuntimeTypeModel.Create();
        // Add DateTimeOffset surrogate support
        model.Add(typeof(DateTimeOffset), false)
            .SetSurrogate(typeof(DateTimeOffsetSurrogate));
        return model;
    }
    /// <summary>
    /// Gets content type.
    /// </summary>

    public string ContentType => "application/x-protobuf";
    /// <summary>
    /// Executes serialize async.
    /// </summary>

    public async ValueTask<byte[]> SerializeAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (message == null)
        {
            return [];
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
            data = await _compressionProvider.CompressAsync(data, _options.CompressionLevel, cancellationToken).ConfigureAwait(false);
        }

        return data;
    }
    /// <summary>
    /// Executes deserialize async.
    /// </summary>

    public async ValueTask<T> DeserializeAsync<T>(byte[] data, CancellationToken cancellationToken = default) where T : class
    {
        if (data == null || data.Length == 0)
        {
            return default!;
        }

        if (_options.EnableCompression)
        {
            data = await _compressionProvider.DecompressAsync(data, cancellationToken).ConfigureAwait(false);
        }

        using var stream = new MemoryStream(data);
        var result = _typeModel.Deserialize<T>(stream);
        return result!;
    }
    /// <summary>
    /// Executes deserialize async.
    /// </summary>

    public async ValueTask<object?> DeserializeAsync(byte[] data, Type messageType, CancellationToken cancellationToken = default)
    {
        if (data == null || data.Length == 0)
        {
            return null;
        }

        if (_options.EnableCompression)
        {
            data = await _compressionProvider.DecompressAsync(data, cancellationToken).ConfigureAwait(false);
        }

        using var stream = new MemoryStream(data);
        var result = _typeModel.Deserialize(stream, null, messageType);
        return result;
    }
    /// <summary>
    /// Executes serialize.
    /// </summary>

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
    /// <summary>
    /// Executes try serialize.
    /// </summary>

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
    /// <summary>
    /// Executes get required buffer size.
    /// </summary>

    public int GetRequiredBufferSize<T>(T message)
    {
        // Protobuf is compact - estimate 2KB for most messages
        return 2048;
    }
    /// <summary>
    /// Executes deserialize.
    /// </summary>

    public T Deserialize<T>(ReadOnlySpan<byte> data) where T : class
    {
        if (data.IsEmpty) return default!;

        using var stream = new MemoryStream(data.ToArray());
        return _typeModel.Deserialize<T>(stream)!;
    }
    /// <summary>
    /// Executes deserialize.
    /// </summary>

    public object? Deserialize(ReadOnlySpan<byte> data, Type messageType)
    {
        if (data.IsEmpty) return null;

        using var stream = new MemoryStream(data.ToArray());
        return _typeModel.Deserialize(stream, null, messageType);
    }
}

/// <summary>
/// Protobuf serializer with type information included for polymorphic scenarios.
/// SECURITY: Uses ProtobufTypeRegistry to prevent type injection attacks.
/// Only explicitly registered types can be deserialized.
/// </summary>
public class TypedProtobufMessageSerializer(
    SerializationOptions? options = null,
    RuntimeTypeModel? typeModel = null,
    ICompressionProvider? compressionProvider = null,
    ProtobufTypeRegistry? typeRegistry = null) : IMessageSerializer
{
    private readonly SerializationOptions _options = options ?? new SerializationOptions();
    private readonly RuntimeTypeModel _typeModel = typeModel ?? CreateDefaultTypeModel();
    private readonly ICompressionProvider _compressionProvider = compressionProvider ?? new GZipCompressionProvider();
    private readonly ProtobufTypeRegistry _typeRegistry = typeRegistry ?? ProtobufTypeRegistry.CreateDefault();
    /// <summary>
    /// Executes create default type model.
    /// </summary>

    private static RuntimeTypeModel CreateDefaultTypeModel()
    {
        var model = RuntimeTypeModel.Create();
        // Add DateTimeOffset surrogate support
        model.Add(typeof(DateTimeOffset), false)
            .SetSurrogate(typeof(DateTimeOffsetSurrogate));
        return model;
    }
    /// <summary>
    /// Gets content type.
    /// </summary>

    public string ContentType => "application/x-protobuf-typed";
    /// <summary>
    /// Executes serialize async.
    /// </summary>

    public async ValueTask<byte[]> SerializeAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (message == null)
        {
            return [];
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
            data = await _compressionProvider.CompressAsync(data, _options.CompressionLevel, cancellationToken).ConfigureAwait(false);
        }

        return data;
    }
    /// <summary>
    /// Executes deserialize async.
    /// </summary>

    public async ValueTask<T> DeserializeAsync<T>(byte[] data, CancellationToken cancellationToken = default) where T : class
    {
        if (data == null || data.Length == 0)
        {
            return default!;
        }

        if (_options.EnableCompression)
        {
            data = await _compressionProvider.DecompressAsync(data, cancellationToken).ConfigureAwait(false);
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
    /// <summary>
    /// Executes deserialize async.
    /// </summary>

    public async ValueTask<object?> DeserializeAsync(byte[] data, Type messageType, CancellationToken cancellationToken = default)
    {
        if (data == null || data.Length == 0)
        {
            return null;
        }

        if (_options.EnableCompression)
        {
            data = await _compressionProvider.DecompressAsync(data, cancellationToken).ConfigureAwait(false);
        }

        using var stream = new MemoryStream(data);

        if (_options.IncludeTypeInformation)
        {
            // Read type information - SECURITY: Only allow registered types
            var typeName = Serializer.DeserializeWithLengthPrefix<string>(stream, PrefixStyle.Base128);
            var resolvedType = _typeRegistry.TryResolve(typeName);
            if (resolvedType != null)
            {
                messageType = resolvedType;
            }
            // If type not in registry, fall back to the provided messageType (safe default)
        }

        var result = _typeModel.DeserializeWithLengthPrefix(stream, null, messageType, PrefixStyle.Base128, 0);
        return result;
    }
    /// <summary>
    /// Executes serialize.
    /// </summary>

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
    /// <summary>
    /// Executes try serialize.
    /// </summary>

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
    /// <summary>
    /// Executes get required buffer size.
    /// </summary>

    public int GetRequiredBufferSize<T>(T message)
    {
        // Protobuf with type info - estimate 2KB + overhead for type names
        return 2048 + 256;
    }
    /// <summary>
    /// Executes deserialize.
    /// </summary>

    public T Deserialize<T>(ReadOnlySpan<byte> data) where T : class
    {
        if (data.IsEmpty) return default!;

        using var stream = new MemoryStream(data.ToArray());

        if (_options.IncludeTypeInformation)
        {
            Serializer.DeserializeWithLengthPrefix<string>(stream, PrefixStyle.Base128);
        }

        return (T?)_typeModel.DeserializeWithLengthPrefix(stream, null, typeof(T), PrefixStyle.Base128, 0)!;
    }
    /// <summary>
    /// Executes deserialize.
    /// </summary>

    public object? Deserialize(ReadOnlySpan<byte> data, Type messageType)
    {
        if (data.IsEmpty) return null;

        using var stream = new MemoryStream(data.ToArray());

        if (_options.IncludeTypeInformation)
        {
            // Read type information - SECURITY: Only allow registered types
            var typeName = Serializer.DeserializeWithLengthPrefix<string>(stream, PrefixStyle.Base128);
            var resolvedType = _typeRegistry.TryResolve(typeName);
            if (resolvedType != null)
            {
                messageType = resolvedType;
            }
            // If type not in registry, fall back to the provided messageType (safe default)
        }

        return _typeModel.DeserializeWithLengthPrefix(stream, null, messageType, PrefixStyle.Base128, 0);
    }
}

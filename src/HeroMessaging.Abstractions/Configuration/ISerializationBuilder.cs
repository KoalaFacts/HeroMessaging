using HeroMessaging.Abstractions.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Abstractions.Configuration;

/// <summary>
/// Builder for configuring serialization plugins
/// </summary>
public interface ISerializationBuilder
{
    /// <summary>
    /// Use JSON serialization
    /// </summary>
    ISerializationBuilder UseJson(Action<JsonSerializationOptions>? configure = null);

    /// <summary>
    /// Use Protocol Buffers serialization
    /// </summary>
    ISerializationBuilder UseProtobuf(Action<ProtobufSerializationOptions>? configure = null);

    /// <summary>
    /// Use MessagePack serialization
    /// </summary>
    ISerializationBuilder UseMessagePack(Action<MessagePackSerializationOptions>? configure = null);

    /// <summary>
    /// Use custom serializer implementation
    /// </summary>
    ISerializationBuilder UseCustom<T>() where T : class, IMessageSerializer;

    /// <summary>
    /// Use custom serializer instance
    /// </summary>
    ISerializationBuilder UseCustom(IMessageSerializer serializer);

    /// <summary>
    /// Add serializer for specific message type
    /// </summary>
    ISerializationBuilder AddTypeSerializer<TMessage, TSerializer>()
        where TSerializer : class, IMessageSerializer;

    /// <summary>
    /// Set default serializer
    /// </summary>
    ISerializationBuilder SetDefault<T>() where T : class, IMessageSerializer;

    /// <summary>
    /// Enable compression for all serializers
    /// </summary>
    ISerializationBuilder WithCompression(CompressionLevel level = CompressionLevel.Optimal);

    /// <summary>
    /// Set maximum message size
    /// </summary>
    ISerializationBuilder WithMaxMessageSize(int maxSizeInBytes);

    /// <summary>
    /// Build and return the service collection
    /// </summary>
    IServiceCollection Build();
}

/// <summary>
/// Configuration options for JSON serialization.
/// </summary>
public class JsonSerializationOptions
{
    /// <summary>
    /// Whether to format JSON with indentation for readability. Default: false.
    /// </summary>
    public bool Indented { get; set; } = false;

    /// <summary>
    /// Whether to use camelCase for property names. Default: true.
    /// </summary>
    public bool CamelCase { get; set; } = true;

    /// <summary>
    /// Maximum depth for nested objects during serialization. Default: 32.
    /// </summary>
    public int MaxDepth { get; set; } = 32;

    /// <summary>
    /// Whether to include type information in serialized output. Default: false.
    /// </summary>
    public bool IncludeTypeInfo { get; set; } = false;
}

/// <summary>
/// Configuration options for Protocol Buffers serialization.
/// </summary>
public class ProtobufSerializationOptions
{
    /// <summary>
    /// Whether to include type information in serialized output. Default: false.
    /// </summary>
    public bool IncludeTypeInfo { get; set; } = false;

    /// <summary>
    /// Whether to apply compression to serialized data. Default: false.
    /// </summary>
    public bool UseCompression { get; set; } = false;
}

/// <summary>
/// Configuration options for MessagePack serialization.
/// </summary>
public class MessagePackSerializationOptions
{
    /// <summary>
    /// Whether to apply LZ4 compression to serialized data. Default: true.
    /// </summary>
    public bool UseCompression { get; set; } = true;

    /// <summary>
    /// Whether to use contractless resolver for serialization without attributes. Default: true.
    /// </summary>
    public bool ContractlessResolve { get; set; } = true;
}

/// <summary>
/// Compression level for message serialization
/// </summary>
public enum CompressionLevel
{
    /// <summary>
    /// No compression
    /// </summary>
    None = 0,

    /// <summary>
    /// Fastest compression (lowest ratio)
    /// </summary>
    Fastest = 1,

    /// <summary>
    /// Balanced compression
    /// </summary>
    Optimal = 2,

    /// <summary>
    /// Maximum compression (highest ratio, slowest)
    /// </summary>
    SmallestSize = 3,

    /// <summary>
    /// Alias for SmallestSize for backward compatibility
    /// </summary>
    Maximum = SmallestSize
}

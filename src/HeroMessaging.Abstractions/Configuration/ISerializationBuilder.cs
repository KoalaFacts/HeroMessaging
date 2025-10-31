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

// These option classes would normally be in their respective plugin projects
/// <summary>
/// Configuration options for JSON serialization
/// </summary>
public class JsonSerializationOptions
{
    /// <summary>
    /// Whether to format JSON with indentation for readability. Default is false for compact output.
    /// </summary>
    public bool Indented { get; set; } = false;

    /// <summary>
    /// Whether to use camelCase naming for properties. Default is true for JavaScript compatibility.
    /// </summary>
    public bool CamelCase { get; set; } = true;

    /// <summary>
    /// Maximum depth for nested objects to prevent stack overflow. Default is 32.
    /// </summary>
    public int MaxDepth { get; set; } = 32;

    /// <summary>
    /// Whether to include .NET type information in serialized JSON for polymorphic deserialization. Default is false.
    /// </summary>
    public bool IncludeTypeInfo { get; set; } = false;
}

/// <summary>
/// Configuration options for Protocol Buffers (Protobuf) serialization
/// </summary>
public class ProtobufSerializationOptions
{
    /// <summary>
    /// Whether to include .NET type information for polymorphic deserialization. Default is false.
    /// </summary>
    public bool IncludeTypeInfo { get; set; } = false;

    /// <summary>
    /// Whether to compress serialized data using GZip compression. Default is false.
    /// </summary>
    public bool UseCompression { get; set; } = false;
}

/// <summary>
/// Configuration options for MessagePack serialization
/// </summary>
public class MessagePackSerializationOptions
{
    /// <summary>
    /// Whether to compress serialized data using LZ4 compression. Default is true for smaller message sizes.
    /// </summary>
    public bool UseCompression { get; set; } = true;

    /// <summary>
    /// Whether to resolve types without explicit contracts for dynamic scenarios. Default is true.
    /// </summary>
    public bool ContractlessResolve { get; set; } = true;
}

/// <summary>
/// Compression level for serialized message data
/// </summary>
public enum CompressionLevel
{
    /// <summary>
    /// No compression applied
    /// </summary>
    None = 0,

    /// <summary>
    /// Fastest compression speed with moderate compression ratio
    /// </summary>
    Fastest = 1,

    /// <summary>
    /// Balanced compression speed and ratio (recommended default)
    /// </summary>
    Optimal = 2,

    /// <summary>
    /// Maximum compression ratio with slower compression speed
    /// </summary>
    SmallestSize = 3
}
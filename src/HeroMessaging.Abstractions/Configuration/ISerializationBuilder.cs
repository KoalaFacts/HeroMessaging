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
public class JsonSerializationOptions
{
    public bool Indented { get; set; } = false;
    public bool CamelCase { get; set; } = true;
    public int MaxDepth { get; set; } = 32;
    public bool IncludeTypeInfo { get; set; } = false;
}

public class ProtobufSerializationOptions
{
    public bool IncludeTypeInfo { get; set; } = false;
    public bool UseCompression { get; set; } = false;
}

public class MessagePackSerializationOptions
{
    public bool UseCompression { get; set; } = true;
    public bool ContractlessResolve { get; set; } = true;
}

public enum CompressionLevel
{
    None = 0,
    Fastest = 1,
    Optimal = 2,
    SmallestSize = 3
}
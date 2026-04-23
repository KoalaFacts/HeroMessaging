using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Configuration;

/// <summary>
/// Implementation of serialization builder for configuring serialization plugins
/// </summary>
public class SerializationBuilder : ISerializationBuilder
{
    private readonly IServiceCollection _services;
    /// <summary>
    /// Initializes a new instance of the <see cref="SerializationBuilder"/> class.
    /// </summary>

    public SerializationBuilder(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }
    /// <summary>
    /// Executes use json.
    /// </summary>

    public ISerializationBuilder UseJson(Action<JsonSerializationOptions>? configure = null)
    {
        var options = new JsonSerializationOptions();
        configure?.Invoke(options);

        _services.AddSingleton(options);

        // When JSON serializer plugin is available, it would be registered here
        // For now, using a placeholder that would be replaced by the actual implementation
        _services.AddSingleton<IMessageSerializer>(sp =>
            throw new NotImplementedException("JSON serializer plugin not installed. Install HeroMessaging.Serialization.Json package."));

        return this;
    }
    /// <summary>
    /// Executes use protobuf.
    /// </summary>

    public ISerializationBuilder UseProtobuf(Action<ProtobufSerializationOptions>? configure = null)
    {
        var options = new ProtobufSerializationOptions();
        configure?.Invoke(options);

        _services.AddSingleton(options);

        // When Protobuf serializer plugin is available, it would be registered here
        _services.AddSingleton<IMessageSerializer>(sp =>
            throw new NotImplementedException("Protobuf serializer plugin not installed. Install HeroMessaging.Serialization.Protobuf package."));

        return this;
    }
    /// <summary>
    /// Executes use message pack.
    /// </summary>

    public ISerializationBuilder UseMessagePack(Action<MessagePackSerializationOptions>? configure = null)
    {
        var options = new MessagePackSerializationOptions();
        configure?.Invoke(options);

        _services.AddSingleton(options);

        // When MessagePack serializer plugin is available, it would be registered here
        _services.AddSingleton<IMessageSerializer>(sp =>
            throw new NotImplementedException("MessagePack serializer plugin not installed. Install HeroMessaging.Serialization.MessagePack package."));

        return this;
    }
    /// <summary>
    /// Executes use custom.
    /// </summary>

    public ISerializationBuilder UseCustom<T>() where T : class, IMessageSerializer
    {
        _services.AddSingleton<IMessageSerializer, T>();
        return this;
    }
    /// <summary>
    /// Executes use custom.
    /// </summary>

    public ISerializationBuilder UseCustom(IMessageSerializer serializer)
    {
        _services.AddSingleton(serializer);
        return this;
    }
    /// <summary>
    /// Executes add type serializer.
    /// </summary>

    public ISerializationBuilder AddTypeSerializer<TMessage, TSerializer>()
        where TSerializer : class, IMessageSerializer
    {
        // Register a serializer for a specific message type
        _services.AddSingleton<TSerializer>();

        // This would be used by a message type resolver service
        _services.Configure<SerializationTypeMapping>(options =>
        {
            options.TypeSerializers[typeof(TMessage)] = typeof(TSerializer);
        });

        return this;
    }
    /// <summary>
    /// Executes set default.
    /// </summary>

    public ISerializationBuilder SetDefault<T>() where T : class, IMessageSerializer
    {
        _services.AddSingleton<IMessageSerializer, T>();
        return this;
    }
    /// <summary>
    /// Executes with compression.
    /// </summary>

    public ISerializationBuilder WithCompression(CompressionLevel level = CompressionLevel.Optimal)
    {
        _services.Configure<SerializationCompressionOptions>(options =>
        {
            options.EnableCompression = true;
            options.CompressionLevel = level;
        });
        return this;
    }
    /// <summary>
    /// Executes with max message size.
    /// </summary>

    public ISerializationBuilder WithMaxMessageSize(int maxSizeInBytes)
    {
        _services.Configure<SerializationOptions>(options =>
        {
            options.MaxMessageSize = maxSizeInBytes;
        });
        return this;
    }
    /// <summary>
    /// Executes build.
    /// </summary>

    public IServiceCollection Build()
    {
        // If no serializer was configured, we don't add a default one
        // The application should explicitly choose a serializer
        return _services;
    }
}
/// <summary>
/// Represents the serialization options type.
/// </summary>

// Configuration option classes
public class SerializationOptions
{
    /// <summary>
    /// Gets or sets max message size.
    /// </summary>
    public int MaxMessageSize { get; set; } = 1024 * 1024 * 10; // 10MB default
}
/// <summary>
/// Represents the serialization compression options type.
/// </summary>

public class SerializationCompressionOptions : SerializationOptions
{
    /// <summary>
    /// Gets or sets enable compression.
    /// </summary>
    public bool EnableCompression { get; set; }
    /// <summary>
    /// Gets or sets compression level.
    /// </summary>
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;
}
/// <summary>
/// Represents the serialization type mapping type.
/// </summary>

public class SerializationTypeMapping
{
    /// <summary>
    /// Gets the serializer type mappings keyed by the message type.
    /// </summary>
    public Dictionary<Type, Type> TypeSerializers { get; } = [];
}

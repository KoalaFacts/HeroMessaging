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

    public SerializationBuilder(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

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

    public ISerializationBuilder UseCustom<T>() where T : class, IMessageSerializer
    {
        _services.AddSingleton<IMessageSerializer, T>();
        return this;
    }

    public ISerializationBuilder UseCustom(IMessageSerializer serializer)
    {
        _services.AddSingleton(serializer);
        return this;
    }

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

    public ISerializationBuilder SetDefault<T>() where T : class, IMessageSerializer
    {
        _services.AddSingleton<IMessageSerializer, T>();
        return this;
    }

    public ISerializationBuilder WithCompression(CompressionLevel level = CompressionLevel.Optimal)
    {
        _services.Configure<SerializationCompressionOptions>(options =>
        {
            options.EnableCompression = true;
            options.CompressionLevel = level;
        });
        return this;
    }

    public ISerializationBuilder WithMaxMessageSize(int maxSizeInBytes)
    {
        _services.Configure<SerializationOptions>(options =>
        {
            options.MaxMessageSize = maxSizeInBytes;
        });
        return this;
    }

    public IServiceCollection Build()
    {
        // If no serializer was configured, we don't add a default one
        // The application should explicitly choose a serializer
        return _services;
    }
}

// Configuration option classes
public class SerializationOptions
{
    public int MaxMessageSize { get; set; } = 1024 * 1024 * 10; // 10MB default
}

public class SerializationCompressionOptions : SerializationOptions
{
    public bool EnableCompression { get; set; }
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;
}

public class SerializationTypeMapping
{
    public Dictionary<Type, Type> TypeSerializers { get; } = [];
}

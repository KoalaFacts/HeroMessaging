using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Configuration;

/// <summary>
/// Builder for configuring message serialization in HeroMessaging.
/// </summary>
/// <remarks>
/// This builder provides a fluent API for configuring:
/// - Serialization formats (JSON, Protobuf, MessagePack)
/// - Compression settings
/// - Message size limits
/// - Type-specific serializers
///
/// Serialization affects message size, performance, and inter operability.
/// Choose the format that best matches your requirements:
/// - JSON: Human-readable, widely supported, larger size
/// - Protobuf: Compact binary, fast, requires schema
/// - MessagePack: Compact binary, very fast, schema-less
///
/// Example:
/// <code>
/// var serializationBuilder = new SerializationBuilder(services);
/// serializationBuilder
///     .UseJson()
///     .WithCompression(CompressionLevel.Optimal)
///     .WithMaxMessageSize(10 * 1024 * 1024)
///     .Build();
/// </code>
/// </remarks>
public class SerializationBuilder : ISerializationBuilder
{
    private readonly IServiceCollection _services;

    /// <summary>
    /// Initializes a new instance of the SerializationBuilder class.
    /// </summary>
    /// <param name="services">The service collection to register serialization services with</param>
    /// <exception cref="ArgumentNullException">Thrown when services is null</exception>
    public SerializationBuilder(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Configures HeroMessaging to use JSON serialization for messages.
    /// </summary>
    /// <param name="configure">Optional configuration action for JSON serialization options</param>
    /// <returns>The serialization builder for method chaining</returns>
    /// <remarks>
    /// JSON serialization is human-readable and widely supported but produces larger messages.
    ///
    /// Requirements:
    /// - HeroMessaging.Serialization.Json package must be installed
    ///
    /// Example:
    /// <code>
    /// serializationBuilder.UseJson(options =>
    /// {
    ///     options.WriteIndented = false;
    ///     options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    /// });
    /// </code>
    /// </remarks>
    public ISerializationBuilder UseJson(Action<Abstractions.Configuration.JsonSerializationOptions>? configure = null)
    {
        var options = new Abstractions.Configuration.JsonSerializationOptions();
        configure?.Invoke(options);

        _services.AddSingleton(options);

        // When JSON serializer plugin is available, it would be registered here
        // For now, using a placeholder that would be replaced by the actual implementation
        _services.AddSingleton<IMessageSerializer>(sp =>
            throw new NotImplementedException("JSON serializer plugin not installed. Install HeroMessaging.Serialization.Json package."));

        return this;
    }

    /// <summary>
    /// Configures HeroMessaging to use Protocol Buffers (Protobuf) serialization for messages.
    /// </summary>
    /// <param name="configure">Optional configuration action for Protobuf serialization options</param>
    /// <returns>The serialization builder for method chaining</returns>
    /// <remarks>
    /// Protobuf provides compact binary serialization with strong schema enforcement.
    ///
    /// Requirements:
    /// - HeroMessaging.Serialization.Protobuf package must be installed
    /// - Message types must have Protobuf contracts
    ///
    /// Example:
    /// <code>
    /// serializationBuilder.UseProtobuf(options =>
    /// {
    ///     options.PreferLengthPrefix = true;
    /// });
    /// </code>
    /// </remarks>
    public ISerializationBuilder UseProtobuf(Action<Abstractions.Configuration.ProtobufSerializationOptions>? configure = null)
    {
        var options = new Abstractions.Configuration.ProtobufSerializationOptions();
        configure?.Invoke(options);

        _services.AddSingleton(options);

        // When Protobuf serializer plugin is available, it would be registered here
        _services.AddSingleton<IMessageSerializer>(sp =>
            throw new NotImplementedException("Protobuf serializer plugin not installed. Install HeroMessaging.Serialization.Protobuf package."));

        return this;
    }

    /// <summary>
    /// Configures HeroMessaging to use MessagePack serialization for messages.
    /// </summary>
    /// <param name="configure">Optional configuration action for MessagePack serialization options</param>
    /// <returns>The serialization builder for method chaining</returns>
    /// <remarks>
    /// MessagePack provides very fast binary serialization without requiring schema definitions.
    ///
    /// Requirements:
    /// - HeroMessaging.Serialization.MessagePack package must be installed
    ///
    /// Example:
    /// <code>
    /// serializationBuilder.UseMessagePack(options =>
    /// {
    ///     options.Compression = MessagePackCompression.Lz4BlockArray;
    /// });
    /// </code>
    /// </remarks>
    public ISerializationBuilder UseMessagePack(Action<Abstractions.Configuration.MessagePackSerializationOptions>? configure = null)
    {
        var options = new Abstractions.Configuration.MessagePackSerializationOptions();
        configure?.Invoke(options);

        _services.AddSingleton(options);

        // When MessagePack serializer plugin is available, it would be registered here
        _services.AddSingleton<IMessageSerializer>(sp =>
            throw new NotImplementedException("MessagePack serializer plugin not installed. Install HeroMessaging.Serialization.MessagePack package."));

        return this;
    }

    /// <summary>
    /// Registers a custom message serializer implementation.
    /// </summary>
    /// <typeparam name="T">The custom serializer implementation type</typeparam>
    /// <returns>The serialization builder for method chaining</returns>
    /// <remarks>
    /// Use this to register a custom IMessageSerializer implementation.
    ///
    /// Example:
    /// <code>
    /// serializationBuilder.UseCustom&lt;MyCustomSerializer&gt;();
    /// </code>
    /// </remarks>
    public ISerializationBuilder UseCustom<T>() where T : class, IMessageSerializer
    {
        _services.AddSingleton<IMessageSerializer, T>();
        return this;
    }

    /// <summary>
    /// Registers a specific message serializer instance.
    /// </summary>
    /// <param name="serializer">The serializer instance to use</param>
    /// <returns>The serialization builder for method chaining</returns>
    /// <remarks>
    /// Use this to register a pre-configured serializer instance.
    ///
    /// Example:
    /// <code>
    /// var serializer = new MyCustomSerializer(options);
    /// serializationBuilder.UseCustom(serializer);
    /// </code>
    /// </remarks>
    public ISerializationBuilder UseCustom(IMessageSerializer serializer)
    {
        _services.AddSingleton(serializer);
        return this;
    }

    /// <summary>
    /// Registers a type-specific serializer for a particular message type.
    /// </summary>
    /// <typeparam name="TMessage">The message type to serialize</typeparam>
    /// <typeparam name="TSerializer">The serializer to use for this message type</typeparam>
    /// <returns>The serialization builder for method chaining</returns>
    /// <remarks>
    /// Use this when you need different serializers for different message types.
    /// For example, using Protobuf for compact domain events and JSON for audit messages.
    ///
    /// Example:
    /// <code>
    /// serializationBuilder
    ///     .AddTypeSerializer&lt;OrderCreatedEvent, ProtobufSerializer&gt;()
    ///     .AddTypeSerializer&lt;AuditLogEntry, JsonSerializer&gt;();
    /// </code>
    /// </remarks>
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
    /// Sets the default serializer to use when no type-specific serializer is registered.
    /// </summary>
    /// <typeparam name="T">The default serializer implementation type</typeparam>
    /// <returns>The serialization builder for method chaining</returns>
    /// <remarks>
    /// The default serializer is used for messages that don't have a type-specific serializer.
    ///
    /// Example:
    /// <code>
    /// serializationBuilder.SetDefault&lt;JsonSerializer&gt;();
    /// </code>
    /// </remarks>
    public ISerializationBuilder SetDefault<T>() where T : class, IMessageSerializer
    {
        _services.AddSingleton<IMessageSerializer, T>();
        return this;
    }

    /// <summary>
    /// Enables compression for serialized messages.
    /// </summary>
    /// <param name="level">Compression level (default: Optimal)</param>
    /// <returns>The serialization builder for method chaining</returns>
    /// <remarks>
    /// Compression reduces message size at the cost of CPU time.
    ///
    /// Compression levels:
    /// - Fastest: Less compression, faster processing
    /// - Optimal: Balanced compression and speed (default)
    /// - SmallestSize: Maximum compression, slower processing
    ///
    /// Best used with text-based formats like JSON. Binary formats like Protobuf
    /// and MessagePack are already compact and may not benefit from compression.
    ///
    /// Example:
    /// <code>
    /// serializationBuilder.WithCompression(CompressionLevel.Optimal);
    /// </code>
    /// </remarks>
    public ISerializationBuilder WithCompression(Abstractions.Configuration.CompressionLevel level = Abstractions.Configuration.CompressionLevel.Optimal)
    {
        _services.Configure<SerializationCompressionOptions>(options =>
        {
            options.EnableCompression = true;
            options.CompressionLevel = level;
        });
        return this;
    }

    /// <summary>
    /// Sets the maximum allowed message size in bytes.
    /// </summary>
    /// <param name="maxSizeInBytes">Maximum message size in bytes</param>
    /// <returns>The serialization builder for method chaining</returns>
    /// <remarks>
    /// This prevents excessively large messages from being processed.
    /// Messages exceeding this size will be rejected during serialization.
    ///
    /// Default: 10MB (10 * 1024 * 1024 bytes)
    ///
    /// Recommended limits:
    /// - Small messages: 1MB
    /// - Normal messages: 10MB (default)
    /// - Large messages: 100MB
    ///
    /// Example:
    /// <code>
    /// serializationBuilder.WithMaxMessageSize(5 * 1024 * 1024); // 5MB
    /// </code>
    /// </remarks>
    public ISerializationBuilder WithMaxMessageSize(int maxSizeInBytes)
    {
        _services.Configure<SerializationOptions>(options =>
        {
            options.MaxMessageSize = maxSizeInBytes;
        });
        return this;
    }

    /// <summary>
    /// Completes serialization configuration and returns the service collection.
    /// </summary>
    /// <returns>The configured service collection</returns>
    /// <remarks>
    /// This method finalizes serialization configuration.
    /// Unlike storage, no default serializer is registered if none is configured.
    /// Applications should explicitly choose a serialization format.
    ///
    /// Example:
    /// <code>
    /// serializationBuilder
    ///     .UseJson()
    ///     .WithCompression()
    ///     .Build();
    /// </code>
    /// </remarks>
    public IServiceCollection Build()
    {
        // If no serializer was configured, we don't add a default one
        // The application should explicitly choose a serializer
        return _services;
    }
}

// Configuration option classes

/// <summary>
/// Base configuration options for message serialization.
/// </summary>
/// <remarks>
/// Contains common settings that apply to all serialization formats.
/// </remarks>
public class SerializationOptions
{
    /// <summary>
    /// Gets or sets the maximum allowed message size in bytes.
    /// </summary>
    /// <remarks>
    /// Messages exceeding this size will be rejected during serialization.
    /// Default is 10MB (10 * 1024 * 1024 bytes).
    /// </remarks>
    public int MaxMessageSize { get; set; } = 1024 * 1024 * 10; // 10MB default
}

/// <summary>
/// Configuration options for message serialization with compression support.
/// </summary>
/// <remarks>
/// Extends <see cref="SerializationOptions"/> to include compression settings.
/// Compression reduces message size at the cost of CPU time for compression/decompression.
/// </remarks>
public class SerializationCompressionOptions : SerializationOptions
{
    /// <summary>
    /// Gets or sets whether compression is enabled for serialized messages.
    /// </summary>
    /// <remarks>
    /// When enabled, messages will be compressed using the specified <see cref="CompressionLevel"/>.
    /// Default is false.
    /// </remarks>
    public bool EnableCompression { get; set; }

    /// <summary>
    /// Gets or sets the compression level to use when compression is enabled.
    /// </summary>
    /// <remarks>
    /// Default is <see cref="Abstractions.Configuration.CompressionLevel.Optimal"/> for balanced
    /// compression ratio and performance.
    /// </remarks>
    public Abstractions.Configuration.CompressionLevel CompressionLevel { get; set; } = Abstractions.Configuration.CompressionLevel.Optimal;
}

/// <summary>
/// Mapping configuration for type-specific serializers.
/// </summary>
/// <remarks>
/// Allows different message types to use different serializers.
/// For example, domain events can use Protobuf while audit logs use JSON.
/// </remarks>
public class SerializationTypeMapping
{
    /// <summary>
    /// Gets the dictionary mapping message types to their specific serializer implementations.
    /// </summary>
    /// <remarks>
    /// Key: The message type (e.g., typeof(OrderCreatedEvent))
    /// Value: The serializer type to use for that message (e.g., typeof(ProtobufSerializer))
    /// </remarks>
    public Dictionary<Type, Type> TypeSerializers { get; } = new();
}
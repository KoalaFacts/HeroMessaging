using HeroMessaging.Abstractions.Plugins;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Configuration; // For builder implementations
using HeroMessaging.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Abstractions.Configuration; // Matching target interface namespace

/// <summary>
/// Extension methods for IHeroMessagingBuilder that provide convenient configuration options
/// for storage, serialization, observability, and plugin discovery.
/// </summary>
/// <remarks>
/// These extensions provide a fluent API for configuring HeroMessaging components:
/// - Storage: SQL Server, PostgreSQL, in-memory
/// - Serialization: JSON, Protobuf, MessagePack
/// - Observability: Health checks, OpenTelemetry, metrics, tracing
/// - Plugins: Automatic discovery and registration
///
/// All methods return the builder for method chaining and follow the builder pattern.
/// </remarks>
public static class ExtensionsToIHeroMessagingBuilder
{
    /// <summary>
    /// Automatically discovers and registers all plugins in the current application domain.
    /// </summary>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// This method enables automatic plugin discovery which will:
    /// - Register the plugin infrastructure services (IPluginRegistry, IPluginDiscovery, IPluginLoader)
    /// - Scan for available plugins at startup
    /// - Automatically configure discovered plugins
    ///
    /// Use this for convention-based plugin configuration without manual registration.
    ///
    /// Example:
    /// <code>
    /// builder
    ///     .AddPluginsFromDiscovery()
    ///     .Build();
    /// </code>
    /// </remarks>
    public static IHeroMessagingBuilder AddPluginsFromDiscovery(this IHeroMessagingBuilder builder)
    {
        var services = builder.Build();

        // Register plugin infrastructure
        services.AddSingleton<IPluginRegistry, PluginRegistry>();
        services.AddSingleton<IPluginDiscovery, PluginDiscovery>();
        services.AddSingleton<IPluginLoader, PluginLoader>();

        // Register a hosted service that will discover and register plugins at startup
        services.AddSingleton<PluginDiscoveryService>();

        return builder;
    }

    /// <summary>
    /// Configures storage using a dedicated storage builder with fluent configuration options.
    /// </summary>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <param name="configure">Configuration action for the storage builder</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// The storage builder provides fine-grained control over storage configuration including:
    /// - Storage provider selection (in-memory, SQL Server, PostgreSQL, etc.)
    /// - Connection pooling settings
    /// - Retry policies
    /// - Circuit breaker configuration
    /// - Command timeouts
    ///
    /// Example:
    /// <code>
    /// builder.ConfigureStorage(storage =>
    /// {
    ///     storage
    ///         .UseInMemory()
    ///         .WithConnectionPooling(maxPoolSize: 100)
    ///         .WithRetry(maxRetries: 3, retryDelay: TimeSpan.FromSeconds(2))
    ///         .WithCircuitBreaker(failureThreshold: 5);
    /// });
    /// </code>
    /// </remarks>
    public static IHeroMessagingBuilder ConfigureStorage(
        this IHeroMessagingBuilder builder,
        Action<IStorageBuilder> configure)
    {
        var services = builder.Build();
        var storageBuilder = new StorageBuilder(services);
        configure(storageBuilder);
        return builder;
    }

    /// <summary>
    /// Configures serialization using a dedicated serialization builder with fluent configuration options.
    /// </summary>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <param name="configure">Configuration action for the serialization builder</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// The serialization builder provides control over message serialization including:
    /// - Serializer selection (JSON, Protobuf, MessagePack, custom)
    /// - Type-specific serializers
    /// - Compression settings
    /// - Maximum message size limits
    ///
    /// Example:
    /// <code>
    /// builder.ConfigureSerialization(serialization =>
    /// {
    ///     serialization
    ///         .UseJson(options =>
    ///         {
    ///             options.WriteIndented = false;
    ///             options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    ///         })
    ///         .WithCompression(CompressionLevel.Optimal)
    ///         .WithMaxMessageSize(1024 * 1024 * 10); // 10MB
    /// });
    /// </code>
    /// </remarks>
    public static IHeroMessagingBuilder ConfigureSerialization(
        this IHeroMessagingBuilder builder,
        Action<ISerializationBuilder> configure)
    {
        var services = builder.Build();
        var serializationBuilder = new SerializationBuilder(services);
        configure(serializationBuilder);
        return builder;
    }

    /// <summary>
    /// Configures observability using a dedicated observability builder with fluent configuration options.
    /// </summary>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <param name="configure">Configuration action for the observability builder</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// The observability builder provides configuration for monitoring and diagnostics including:
    /// - Health checks
    /// - OpenTelemetry (traces, metrics, logs)
    /// - Custom metrics collection
    /// - Distributed tracing
    /// - Performance counters
    ///
    /// Example:
    /// <code>
    /// builder.ConfigureObservability(observability =>
    /// {
    ///     observability
    ///         .AddHealthChecks()
    ///         .AddOpenTelemetry(options =>
    ///         {
    ///             options.ServiceName = "MyService";
    ///             options.EnableTracing = true;
    ///             options.EnableMetrics = true;
    ///         })
    ///         .AddMetrics()
    ///         .WithSamplingRate(0.1); // 10% sampling
    /// });
    /// </code>
    /// </remarks>
    public static IHeroMessagingBuilder ConfigureObservability(
        this IHeroMessagingBuilder builder,
        Action<IObservabilityBuilder> configure)
    {
        var services = builder.Build();
        var observabilityBuilder = new ObservabilityBuilder(services);
        configure(observabilityBuilder);
        return builder;
    }

    // Storage Extensions for manual configuration

    /// <summary>
    /// Configures HeroMessaging to use SQL Server for durable message storage.
    /// </summary>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <param name="connectionString">SQL Server connection string</param>
    /// <param name="configure">Optional configuration action for storage options</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// SQL Server storage provides:
    /// - Durable message persistence
    /// - Transactional consistency
    /// - Support for all messaging patterns (Outbox, Inbox, Queues)
    /// - Production-ready reliability
    ///
    /// Requirements:
    /// - HeroMessaging.Storage.SqlServer package must be installed
    /// - Database schema must be created (use migration scripts)
    ///
    /// Example:
    /// <code>
    /// builder.UseSqlServerStorage(
    ///     "Server=localhost;Database=Messaging;Trusted_Connection=True;",
    ///     options =>
    ///     {
    ///         options.CommandTimeout = TimeSpan.FromSeconds(30);
    ///     }
    /// );
    /// </code>
    /// </remarks>
    public static IHeroMessagingBuilder UseSqlServerStorage(
        this IHeroMessagingBuilder builder,
        string connectionString,
        Action<StorageOptions>? configure = null)
    {
        return builder.ConfigureStorage(storage =>
        {
            storage.UseSqlServer(connectionString, configure);
        });
    }

    /// <summary>
    /// Configures HeroMessaging to use PostgreSQL for durable message storage.
    /// </summary>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <param name="connectionString">PostgreSQL connection string</param>
    /// <param name="configure">Optional configuration action for storage options</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// PostgreSQL storage provides:
    /// - Durable message persistence
    /// - Transactional consistency with MVCC
    /// - Support for all messaging patterns (Outbox, Inbox, Queues)
    /// - Production-ready reliability
    /// - JSON/JSONB support for flexible schema
    ///
    /// Requirements:
    /// - HeroMessaging.Storage.PostgreSQL package must be installed
    /// - Database schema must be created (use migration scripts)
    ///
    /// Example:
    /// <code>
    /// builder.UsePostgreSqlStorage(
    ///     "Host=localhost;Database=messaging;Username=user;Password=pass",
    ///     options =>
    ///     {
    ///         options.CommandTimeout = TimeSpan.FromSeconds(30);
    ///     }
    /// );
    /// </code>
    /// </remarks>
    public static IHeroMessagingBuilder UsePostgreSqlStorage(
        this IHeroMessagingBuilder builder,
        string connectionString,
        Action<StorageOptions>? configure = null)
    {
        return builder.ConfigureStorage(storage =>
        {
            storage.UsePostgreSql(connectionString, configure);
        });
    }

    // Serialization Extensions for manual configuration

    /// <summary>
    /// Configures HeroMessaging to use JSON serialization for messages.
    /// </summary>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <param name="configure">Optional configuration action for JSON serialization options</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// JSON serialization provides:
    /// - Human-readable message format
    /// - Wide language and tool support
    /// - Flexible schema evolution
    /// - Good for debugging and development
    ///
    /// Trade-offs:
    /// - Larger message size compared to binary formats
    /// - Slower serialization/deserialization than binary formats
    ///
    /// Requirements:
    /// - HeroMessaging.Serialization.Json package must be installed
    ///
    /// Example:
    /// <code>
    /// builder.UseJsonSerialization(options =>
    /// {
    ///     options.WriteIndented = false;
    ///     options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    ///     options.IgnoreNullValues = true;
    /// });
    /// </code>
    /// </remarks>
    public static IHeroMessagingBuilder UseJsonSerialization(
        this IHeroMessagingBuilder builder,
        Action<Abstractions.Configuration.JsonSerializationOptions>? configure = null)
    {
        return builder.ConfigureSerialization(serialization =>
        {
            serialization.UseJson(configure);
        });
    }

    /// <summary>
    /// Configures HeroMessaging to use Protocol Buffers (Protobuf) serialization for messages.
    /// </summary>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <param name="configure">Optional configuration action for Protobuf serialization options</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// Protobuf serialization provides:
    /// - Compact binary format (smaller message size)
    /// - Fast serialization/deserialization
    /// - Strong schema enforcement
    /// - Good for high-throughput scenarios
    ///
    /// Trade-offs:
    /// - Requires .proto schema definitions
    /// - Less human-readable than JSON
    /// - Stricter schema evolution rules
    ///
    /// Requirements:
    /// - HeroMessaging.Serialization.Protobuf package must be installed
    /// - Message types must have Protobuf contracts
    ///
    /// Example:
    /// <code>
    /// builder.UseProtobufSerialization(options =>
    /// {
    ///     options.PreferLengthPrefix = true;
    ///     options.UseMemoryPool = true;
    /// });
    /// </code>
    /// </remarks>
    public static IHeroMessagingBuilder UseProtobufSerialization(
        this IHeroMessagingBuilder builder,
        Action<Abstractions.Configuration.ProtobufSerializationOptions>? configure = null)
    {
        return builder.ConfigureSerialization(serialization =>
        {
            serialization.UseProtobuf(configure);
        });
    }

    /// <summary>
    /// Configures HeroMessaging to use MessagePack serialization for messages.
    /// </summary>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <param name="configure">Optional configuration action for MessagePack serialization options</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// MessagePack serialization provides:
    /// - Compact binary format (smaller than JSON)
    /// - Very fast serialization/deserialization
    /// - No schema required
    /// - Good balance between size and speed
    ///
    /// Trade-offs:
    /// - Binary format (not human-readable)
    /// - Less widespread than JSON or Protobuf
    ///
    /// Requirements:
    /// - HeroMessaging.Serialization.MessagePack package must be installed
    ///
    /// Example:
    /// <code>
    /// builder.UseMessagePackSerialization(options =>
    /// {
    ///     options.Compression = MessagePackCompression.Lz4BlockArray;
    ///     options.OmitAssemblyVersion = true;
    /// });
    /// </code>
    /// </remarks>
    public static IHeroMessagingBuilder UseMessagePackSerialization(
        this IHeroMessagingBuilder builder,
        Action<Abstractions.Configuration.MessagePackSerializationOptions>? configure = null)
    {
        return builder.ConfigureSerialization(serialization =>
        {
            serialization.UseMessagePack(configure);
        });
    }

    // Observability Extensions for manual configuration

    /// <summary>
    /// Adds health checks for monitoring HeroMessaging component health.
    /// </summary>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <param name="configure">Optional configuration action for health check options</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// Health checks monitor:
    /// - Storage connectivity and availability
    /// - Message processor status
    /// - Queue depths and processing rates
    /// - Dead letter queue status
    ///
    /// Health checks integrate with ASP.NET Core health check middleware for
    /// exposing /health endpoints and integration with monitoring systems.
    ///
    /// Requirements:
    /// - HeroMessaging.Observability.HealthChecks package must be installed
    ///
    /// Example:
    /// <code>
    /// builder.AddHealthChecks(options =>
    /// {
    ///     options.Timeout = TimeSpan.FromSeconds(5);
    ///     options.FailureThreshold = 3;
    /// });
    /// </code>
    /// </remarks>
    public static IHeroMessagingBuilder AddHealthChecks(
        this IHeroMessagingBuilder builder,
        Action<object>? configure = null)
    {
        return builder.ConfigureObservability(observability =>
        {
            observability.AddHealthChecks(configure);
        });
    }

    /// <summary>
    /// Adds OpenTelemetry instrumentation for distributed tracing, metrics, and logging.
    /// </summary>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <param name="configure">Optional configuration action for OpenTelemetry options</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// OpenTelemetry provides:
    /// - Distributed tracing across service boundaries
    /// - Metrics collection (message counts, latencies, error rates)
    /// - Structured logging with trace context
    /// - Export to various backends (Jaeger, Zipkin, Prometheus, etc.)
    ///
    /// Automatically instruments:
    /// - Command and query processing
    /// - Event publishing and handling
    /// - Queue operations
    /// - Outbox and inbox processing
    ///
    /// Requirements:
    /// - HeroMessaging.Observability.OpenTelemetry package must be installed
    ///
    /// Example:
    /// <code>
    /// builder.AddOpenTelemetry(options =>
    /// {
    ///     options.ServiceName = "OrderService";
    ///     options.OtlpEndpoint = "http://collector:4317";
    ///     options.EnableTracing = true;
    ///     options.EnableMetrics = true;
    ///     options.EnableLogging = true;
    /// });
    /// </code>
    /// </remarks>
    public static IHeroMessagingBuilder AddOpenTelemetry(
        this IHeroMessagingBuilder builder,
        Action<Abstractions.Configuration.OpenTelemetryOptions>? configure = null)
    {
        return builder.ConfigureObservability(observability =>
        {
            observability.AddOpenTelemetry(configure);
        });
    }

    private static void RegisterPluginByCategory(IServiceCollection services, IPluginDescriptor plugin)
    {
        switch (plugin.Category)
        {
            case PluginCategory.Storage:
                RegisterStoragePlugin(services, plugin);
                break;
            case PluginCategory.Serialization:
                RegisterSerializationPlugin(services, plugin);
                break;
            case PluginCategory.Observability:
                RegisterObservabilityPlugin(services, plugin);
                break;
                // Add other categories as needed
        }
    }

    private static void RegisterStoragePlugin(IServiceCollection services, IPluginDescriptor plugin)
    {
        var interfaces = plugin.PluginType.GetInterfaces();

        if (interfaces.Contains(typeof(IMessageStorage)))
            services.AddSingleton(typeof(IMessageStorage), plugin.PluginType);
        if (interfaces.Contains(typeof(IOutboxStorage)))
            services.AddSingleton(typeof(IOutboxStorage), plugin.PluginType);
        if (interfaces.Contains(typeof(IInboxStorage)))
            services.AddSingleton(typeof(IInboxStorage), plugin.PluginType);
        if (interfaces.Contains(typeof(IQueueStorage)))
            services.AddSingleton(typeof(IQueueStorage), plugin.PluginType);
    }

    private static void RegisterSerializationPlugin(IServiceCollection services, IPluginDescriptor plugin)
    {
        var interfaces = plugin.PluginType.GetInterfaces();

        if (interfaces.Contains(typeof(IMessageSerializer)))
            services.AddSingleton(typeof(IMessageSerializer), plugin.PluginType);
    }

    private static void RegisterObservabilityPlugin(IServiceCollection services, IPluginDescriptor plugin)
    {
        // Register observability plugins based on their specific interfaces
        // This would be expanded based on actual observability interfaces
    }
}


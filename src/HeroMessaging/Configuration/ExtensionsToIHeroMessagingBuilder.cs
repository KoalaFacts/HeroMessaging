using System;
using System.Linq;
using System.Reflection;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Plugins;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Configuration; // For builder implementations
using HeroMessaging.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Abstractions.Configuration; // Matching target interface namespace

/// <summary>
/// Extension methods for HeroMessaging builder to support both automatic discovery and manual configuration
/// </summary>
public static class ExtensionsToIHeroMessagingBuilder
{
    /// <summary>
    /// Automatically discover and register all plugins in the current AppDomain
    /// </summary>
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
    /// Configure storage with a dedicated builder
    /// </summary>
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
    /// Configure serialization with a dedicated builder
    /// </summary>
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
    /// Configure observability with a dedicated builder
    /// </summary>
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
    /// Use SQL Server storage
    /// </summary>
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
    /// Use PostgreSQL storage
    /// </summary>
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
    /// Use JSON serialization
    /// </summary>
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
    /// Use Protobuf serialization
    /// </summary>
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
    /// Use MessagePack serialization
    /// </summary>
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
    /// Add health checks
    /// </summary>
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
    /// Add OpenTelemetry
    /// </summary>
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


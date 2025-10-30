using HeroMessaging.Abstractions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
#if !NETSTANDARD2_0
using Microsoft.Extensions.Hosting;
#endif

namespace HeroMessaging.Configuration;

/// <summary>
/// Extension methods for IServiceCollection to configure HeroMessaging services
/// in the dependency injection container.
/// </summary>
/// <remarks>
/// This class provides the main entry points for registering HeroMessaging in your application.
/// Use these methods in your Startup.cs or Program.cs to configure the messaging system.
/// </remarks>
public static class HeroMessagingServiceCollectionExtensions
{
    /// <summary>
    /// Adds HeroMessaging services to the dependency injection container with fluent configuration.
    /// </summary>
    /// <param name="services">The service collection to add HeroMessaging services to</param>
    /// <returns>The HeroMessaging builder for fluent configuration</returns>
    /// <remarks>
    /// This method returns a builder that allows you to configure HeroMessaging using a fluent API.
    /// You must call Build() on the builder when configuration is complete.
    ///
    /// Example usage:
    /// <code>
    /// var builder = services.AddHeroMessaging();
    /// builder
    ///     .UseInMemoryStorage()
    ///     .WithMediator()
    ///     .WithEventBus()
    ///     .ScanAssembly(typeof(Program).Assembly)
    ///     .Build();
    /// </code>
    ///
    /// For convenience methods that automatically call Build(), see the overload that accepts
    /// an Action&lt;IHeroMessagingBuilder&gt; or use the preset configuration methods like
    /// AddHeroMessagingDevelopment() or AddHeroMessagingProduction().
    /// </remarks>
    public static IHeroMessagingBuilder AddHeroMessaging(this IServiceCollection services)
    {
        return new HeroMessagingBuilder(services);
    }

    /// <summary>
    /// Adds HeroMessaging services to the dependency injection container with inline configuration.
    /// </summary>
    /// <param name="services">The service collection to add HeroMessaging services to</param>
    /// <param name="configure">Configuration action that configures the HeroMessaging builder</param>
    /// <returns>The service collection for method chaining</returns>
    /// <remarks>
    /// This is the recommended way to configure HeroMessaging as it handles builder initialization
    /// and Build() invocation automatically.
    ///
    /// Example usage:
    /// <code>
    /// services.AddHeroMessaging(builder =>
    /// {
    ///     builder
    ///         .UseInMemoryStorage()
    ///         .WithMediator()
    ///         .WithEventBus()
    ///         .WithErrorHandling()
    ///         .ScanAssembly(typeof(Program).Assembly);
    /// });
    /// </code>
    ///
    /// For production scenarios with database storage:
    /// <code>
    /// services.AddHeroMessaging(builder =>
    /// {
    ///     builder
    ///         .UseSqlServerStorage(connectionString)
    ///         .WithMediator()
    ///         .WithEventBus()
    ///         .WithQueues()
    ///         .WithOutbox()
    ///         .WithInbox()
    ///         .WithErrorHandling()
    ///         .ScanAssemblies(typeof(Program).Assembly, typeof(Domain.Order).Assembly);
    /// });
    /// </code>
    /// </remarks>
    public static IServiceCollection AddHeroMessaging(
        this IServiceCollection services,
        Action<IHeroMessagingBuilder> configure)
    {
        var builder = new HeroMessagingBuilder(services);
        configure(builder);
        builder.Build();
        return services;
    }

    /// <summary>
    /// Adds HeroMessaging with minimal configuration optimized for local development.
    /// </summary>
    /// <param name="services">The service collection to add HeroMessaging services to</param>
    /// <param name="assemblies">Assemblies to scan for message handlers (commands, queries, events)</param>
    /// <returns>The service collection for method chaining</returns>
    /// <remarks>
    /// This preset configuration includes:
    /// - In-memory storage (no database required)
    /// - Mediator pattern (commands and queries)
    /// - Event bus (event publishing)
    /// - Error handling with dead letter queue
    ///
    /// This is ideal for:
    /// - Local development
    /// - Unit and integration testing
    /// - Prototyping and demos
    /// - CI/CD environments without database access
    ///
    /// Example usage:
    /// <code>
    /// services.AddHeroMessagingDevelopment(
    ///     typeof(Program).Assembly,
    ///     typeof(CommandHandlers).Assembly
    /// );
    /// </code>
    ///
    /// Warning: In-memory storage is not durable. All messages are lost when the application stops.
    /// For production use, see AddHeroMessagingProduction() or AddHeroMessagingMicroservice().
    /// </remarks>
    public static IServiceCollection AddHeroMessagingDevelopment(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        return services.AddHeroMessaging(builder => builder
            .Development()
            .UseInMemoryStorage()
            .WithErrorHandling()
            .ScanAssemblies(assemblies)
        );
    }

    /// <summary>
    /// Adds HeroMessaging with production-ready configuration including durable storage and transactional patterns.
    /// </summary>
    /// <param name="services">The service collection to add HeroMessaging services to</param>
    /// <param name="connectionString">Database connection string for durable message storage</param>
    /// <param name="assemblies">Assemblies to scan for message handlers (commands, queries, events)</param>
    /// <returns>The service collection for method chaining</returns>
    /// <remarks>
    /// This preset configuration includes:
    /// - Durable storage (database-backed)
    /// - Mediator pattern (commands and queries)
    /// - Event bus (event publishing)
    /// - Queue processing (background jobs)
    /// - Outbox pattern (transactional publishing)
    /// - Inbox pattern (exactly-once delivery)
    /// - Error handling with dead letter queue
    ///
    /// This is ideal for:
    /// - Production monolithic applications
    /// - Applications requiring transactional consistency
    /// - Systems with background job processing
    /// - Applications needing guaranteed message delivery
    ///
    /// Example usage:
    /// <code>
    /// services.AddHeroMessagingProduction(
    ///     configuration.GetConnectionString("DefaultConnection"),
    ///     typeof(Program).Assembly,
    ///     typeof(CommandHandlers).Assembly
    /// );
    /// </code>
    ///
    /// Note: Requires a database provider plugin like HeroMessaging.Storage.SqlServer
    /// or HeroMessaging.Storage.PostgreSQL to be installed and configured.
    /// </remarks>
    public static IServiceCollection AddHeroMessagingProduction(
        this IServiceCollection services,
        string connectionString,
        params Assembly[] assemblies)
    {
        return services.AddHeroMessaging(builder => builder
            .Production(connectionString)
            .WithErrorHandling()
            .ScanAssemblies(assemblies)
        );
    }

    /// <summary>
    /// Adds HeroMessaging with configuration optimized for microservices and distributed systems.
    /// </summary>
    /// <param name="services">The service collection to add HeroMessaging services to</param>
    /// <param name="connectionString">Database connection string for durable message storage</param>
    /// <param name="assemblies">Assemblies to scan for message handlers (commands, queries, events)</param>
    /// <returns>The service collection for method chaining</returns>
    /// <remarks>
    /// This preset configuration includes:
    /// - Event bus (inter-service communication)
    /// - Outbox pattern (transactional publishing)
    /// - Inbox pattern (exactly-once delivery)
    /// - Parallel processing (optimized for throughput)
    /// - High concurrency (2x CPU cores)
    /// - Error handling with dead letter queue
    ///
    /// This configuration does NOT include:
    /// - Mediator pattern (services typically handle their own commands directly)
    /// - Queue processing (use dedicated queue services instead)
    ///
    /// This is ideal for:
    /// - Microservices architectures
    /// - Event-driven systems
    /// - Distributed applications with multiple services
    /// - High-throughput event processing
    ///
    /// Example usage:
    /// <code>
    /// services.AddHeroMessagingMicroservice(
    ///     configuration.GetConnectionString("ServiceDatabase"),
    ///     typeof(OrderService).Assembly
    /// );
    /// </code>
    ///
    /// Note: Configure your transport layer (RabbitMQ, Azure Service Bus, etc.) separately
    /// using the appropriate HeroMessaging.Transport.* plugin package.
    /// </remarks>
    public static IServiceCollection AddHeroMessagingMicroservice(
        this IServiceCollection services,
        string connectionString,
        params Assembly[] assemblies)
    {
        return services.AddHeroMessaging(builder => builder
            .Microservice(connectionString)
            .WithErrorHandling()
            .ScanAssemblies(assemblies)
        );
    }

    /// <summary>
    /// Adds HeroMessaging with custom configuration, automatically scanning specified assemblies for handlers.
    /// </summary>
    /// <param name="services">The service collection to add HeroMessaging services to</param>
    /// <param name="configure">Configuration action that customizes the HeroMessaging builder</param>
    /// <param name="assemblies">Assemblies to scan for message handlers (commands, queries, events)</param>
    /// <returns>The service collection for method chaining</returns>
    /// <remarks>
    /// This method is useful when you want to customize configuration beyond the preset scenarios
    /// while still benefiting from automatic handler registration.
    ///
    /// The specified assemblies are scanned automatically before your configuration action runs,
    /// so you can focus on configuring storage, serialization, and other components.
    ///
    /// Example usage:
    /// <code>
    /// services.AddHeroMessagingCustom(builder =>
    /// {
    ///     builder
    ///         .UsePostgreSqlStorage(connectionString)
    ///         .UseJsonSerialization()
    ///         .WithMediator()
    ///         .WithEventBus()
    ///         .ConfigureProcessing(options =>
    ///         {
    ///             options.MaxConcurrency = 10;
    ///             options.SequentialProcessing = false;
    ///         });
    /// },
    /// typeof(Program).Assembly,
    /// typeof(Domain.Events).Assembly);
    /// </code>
    /// </remarks>
    public static IServiceCollection AddHeroMessagingCustom(
        this IServiceCollection services,
        Action<IHeroMessagingBuilder> configure,
        params Assembly[] assemblies)
    {
        return services.AddHeroMessaging(builder =>
        {
            builder.ScanAssemblies(assemblies);
            configure(builder);
        });
    }
}

#if !NETSTANDARD2_0
/// <summary>
/// Extension methods for IHostBuilder to configure HeroMessaging services
/// in .NET applications using the Generic Host pattern.
/// </summary>
/// <remarks>
/// These extensions are only available for .NET 6.0 and later (not available in .NET Standard 2.0).
/// They provide a convenient way to configure HeroMessaging directly on the host builder.
/// </remarks>
public static class HeroMessagingHostBuilderExtensions
{
    /// <summary>
    /// Configures HeroMessaging services in the host builder with custom configuration.
    /// </summary>
    /// <param name="hostBuilder">The host builder to configure</param>
    /// <param name="configure">Configuration action that configures the HeroMessaging builder</param>
    /// <returns>The host builder for method chaining</returns>
    /// <remarks>
    /// This extension allows you to configure HeroMessaging as part of the host building process,
    /// which can be useful when you need access to the HostBuilderContext (e.g., for configuration values).
    ///
    /// Example usage in Program.cs:
    /// <code>
    /// var builder = Host.CreateDefaultBuilder(args)
    ///     .UseHeroMessaging(builder =>
    ///     {
    ///         builder
    ///             .UseInMemoryStorage()
    ///             .WithMediator()
    ///             .WithEventBus()
    ///             .ScanAssembly(typeof(Program).Assembly);
    ///     })
    ///     .ConfigureWebHostDefaults(webBuilder =>
    ///     {
    ///         webBuilder.UseStartup&lt;Startup&gt;();
    ///     });
    /// </code>
    /// </remarks>
    public static IHostBuilder UseHeroMessaging(
        this IHostBuilder hostBuilder,
        Action<IHeroMessagingBuilder> configure)
    {
        return hostBuilder.ConfigureServices((context, services) =>
        {
            services.AddHeroMessaging(configure);
        });
    }

    /// <summary>
    /// Configures HeroMessaging for local development with in-memory storage in the host builder.
    /// </summary>
    /// <param name="hostBuilder">The host builder to configure</param>
    /// <param name="assemblies">Assemblies to scan for message handlers (commands, queries, events)</param>
    /// <returns>The host builder for method chaining</returns>
    /// <remarks>
    /// This is equivalent to calling AddHeroMessagingDevelopment() in ConfigureServices.
    /// See <see cref="AddHeroMessagingDevelopment"/> for details on what's included.
    ///
    /// Example usage:
    /// <code>
    /// var builder = Host.CreateDefaultBuilder(args)
    ///     .UseHeroMessagingDevelopment(typeof(Program).Assembly)
    ///     .ConfigureWebHostDefaults(webBuilder =>
    ///     {
    ///         webBuilder.UseStartup&lt;Startup&gt;();
    ///     });
    /// </code>
    /// </remarks>
    public static IHostBuilder UseHeroMessagingDevelopment(
        this IHostBuilder hostBuilder,
        params Assembly[] assemblies)
    {
        return hostBuilder.ConfigureServices((context, services) =>
        {
            services.AddHeroMessagingDevelopment(assemblies);
        });
    }

    /// <summary>
    /// Configures HeroMessaging for production with durable storage and transactional patterns in the host builder.
    /// </summary>
    /// <param name="hostBuilder">The host builder to configure</param>
    /// <param name="connectionString">Database connection string for durable message storage</param>
    /// <param name="assemblies">Assemblies to scan for message handlers (commands, queries, events)</param>
    /// <returns>The host builder for method chaining</returns>
    /// <remarks>
    /// This is equivalent to calling AddHeroMessagingProduction() in ConfigureServices.
    /// See <see cref="AddHeroMessagingProduction"/> for details on what's included.
    ///
    /// Example usage:
    /// <code>
    /// var builder = Host.CreateDefaultBuilder(args)
    ///     .UseHeroMessagingProduction(
    ///         configuration.GetConnectionString("DefaultConnection"),
    ///         typeof(Program).Assembly
    ///     )
    ///     .ConfigureWebHostDefaults(webBuilder =>
    ///     {
    ///         webBuilder.UseStartup&lt;Startup&gt;();
    ///     });
    /// </code>
    /// </remarks>
    public static IHostBuilder UseHeroMessagingProduction(
        this IHostBuilder hostBuilder,
        string connectionString,
        params Assembly[] assemblies)
    {
        return hostBuilder.ConfigureServices((context, services) =>
        {
            services.AddHeroMessagingProduction(connectionString, assemblies);
        });
    }
}
#endif


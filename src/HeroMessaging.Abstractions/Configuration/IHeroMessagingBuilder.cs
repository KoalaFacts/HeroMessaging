using HeroMessaging.Abstractions.Plugins;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace HeroMessaging.Abstractions.Configuration;

/// <summary>
/// Provides a fluent API for configuring HeroMessaging services and components
/// </summary>
/// <remarks>
/// The builder pattern enables intuitive configuration of HeroMessaging features through method chaining.
/// Configure messaging patterns (mediator, event bus, queues), storage providers, error handling,
/// and plugins with a consistent, discoverable API.
///
/// Configuration workflow:
/// 1. Enable desired messaging patterns (WithMediator, WithEventBus, WithQueues, etc.)
/// 2. Configure storage providers (UseInMemoryStorage, UseStorage, etc.)
/// 3. Scan assemblies for handlers (ScanAssembly, ScanAssemblies)
/// 4. Add plugins for extended functionality (AddPlugin, DiscoverPlugins)
/// 5. Configure processing options (ConfigureProcessing)
/// 6. Call Build() to register all services with the DI container
///
/// Example basic configuration:
/// <code>
/// services.AddHeroMessaging(builder => builder
///     .WithMediator()
///     .WithEventBus()
///     .UseInMemoryStorage()
///     .ScanAssembly(typeof(Startup).Assembly)
///     .Build());
/// </code>
///
/// Example advanced configuration:
/// <code>
/// services.AddHeroMessaging(builder => builder
///     .WithMediator()
///     .WithEventBus()
///     .WithQueues()
///     .WithOutbox()
///     .WithInbox()
///     .WithErrorHandling()
///     .UseStorage&lt;PostgreSqlStorage&gt;()
///     .ScanAssemblies(Assembly.GetExecutingAssembly(), typeof(Handlers.CreateOrder).Assembly)
///     .ConfigureProcessing(options =>
///     {
///         options.MaxConcurrency = 10;
///         options.MaxRetries = 5;
///         options.EnableCircuitBreaker = true;
///     })
///     .AddPlugin&lt;OpenTelemetryPlugin&gt;()
///     .DiscoverPlugins()
///     .Build());
/// </code>
///
/// Preset configurations for common scenarios:
/// <code>
/// // Development: In-memory storage, no external dependencies
/// builder.Development();
///
/// // Production: Full featured with persistent storage
/// builder.Production("Host=localhost;Database=messaging");
///
/// // Microservice: Event bus, outbox, inbox for distributed systems
/// builder.Microservice("Host=localhost;Database=messaging");
/// </code>
/// </remarks>
public interface IHeroMessagingBuilder
{
    /// <summary>
    /// Enables the mediator pattern for in-process command and query handling
    /// </summary>
    IHeroMessagingBuilder WithMediator();

    /// <summary>
    /// Enables the event bus for publish/subscribe messaging patterns
    /// </summary>
    IHeroMessagingBuilder WithEventBus();

    /// <summary>
    /// Enables queue-based message processing with competing consumers
    /// </summary>
    IHeroMessagingBuilder WithQueues();

    /// <summary>
    /// Enables the outbox pattern for reliable message publishing with transactional guarantees
    /// </summary>
    IHeroMessagingBuilder WithOutbox();

    /// <summary>
    /// Enables the inbox pattern for idempotent message processing with deduplication
    /// </summary>
    IHeroMessagingBuilder WithInbox();

    /// <summary>
    /// Enables comprehensive error handling with retry policies and dead-letter queues
    /// </summary>
    IHeroMessagingBuilder WithErrorHandling();

    /// <summary>
    /// Configures in-memory storage for development and testing scenarios
    /// </summary>
    IHeroMessagingBuilder UseInMemoryStorage();

    /// <summary>
    /// Configures a custom storage provider implementation
    /// </summary>
    /// <typeparam name="TStorage">The storage implementation type to use</typeparam>
    IHeroMessagingBuilder UseStorage<TStorage>() where TStorage : class, IMessageStorage;

    /// <summary>
    /// Configures a custom storage provider instance
    /// </summary>
    /// <param name="storage">The storage instance to use</param>
    IHeroMessagingBuilder UseStorage(IMessageStorage storage);

    /// <summary>
    /// Scans an assembly for message handlers (commands, queries, events)
    /// </summary>
    /// <param name="assembly">The assembly to scan for handlers</param>
    IHeroMessagingBuilder ScanAssembly(Assembly assembly);

    /// <summary>
    /// Scans multiple assemblies for message handlers
    /// </summary>
    /// <param name="assemblies">The assemblies to scan for handlers</param>
    IHeroMessagingBuilder ScanAssemblies(params Assembly[] assemblies);

    /// <summary>
    /// Configures message processing options such as concurrency, retries, and circuit breaker settings
    /// </summary>
    /// <param name="configure">Action to configure processing options</param>
    IHeroMessagingBuilder ConfigureProcessing(Action<ProcessingOptions> configure);

    /// <summary>
    /// Adds a plugin to extend HeroMessaging functionality
    /// </summary>
    /// <typeparam name="TPlugin">The plugin type to add</typeparam>
    IHeroMessagingBuilder AddPlugin<TPlugin>() where TPlugin : class, IMessagingPlugin;

    /// <summary>
    /// Adds a plugin instance to extend HeroMessaging functionality
    /// </summary>
    /// <param name="plugin">The plugin instance to add</param>
    IHeroMessagingBuilder AddPlugin(IMessagingPlugin plugin);

    /// <summary>
    /// Adds and configures a plugin to extend HeroMessaging functionality
    /// </summary>
    /// <typeparam name="TPlugin">The plugin type to add</typeparam>
    /// <param name="configure">Action to configure the plugin</param>
    IHeroMessagingBuilder AddPlugin<TPlugin>(Action<TPlugin> configure) where TPlugin : class, IMessagingPlugin;

    /// <summary>
    /// Discovers and registers plugins from loaded assemblies
    /// </summary>
    IHeroMessagingBuilder DiscoverPlugins();

    /// <summary>
    /// Discovers and registers plugins from a directory
    /// </summary>
    /// <param name="directory">The directory to search for plugin assemblies</param>
    IHeroMessagingBuilder DiscoverPlugins(string directory);

    /// <summary>
    /// Discovers and registers plugins from a specific assembly
    /// </summary>
    /// <param name="assembly">The assembly to search for plugins</param>
    IHeroMessagingBuilder DiscoverPlugins(Assembly assembly);

    /// <summary>
    /// Configures HeroMessaging for development with in-memory storage and simplified error handling
    /// </summary>
    IHeroMessagingBuilder Development();

    /// <summary>
    /// Configures HeroMessaging for production with persistent storage, full error handling, and all features enabled
    /// </summary>
    /// <param name="connectionString">The database connection string for persistent storage</param>
    IHeroMessagingBuilder Production(string connectionString);

    /// <summary>
    /// Configures HeroMessaging for microservice architecture with event bus, outbox, and inbox patterns
    /// </summary>
    /// <param name="connectionString">The database connection string for persistent storage</param>
    IHeroMessagingBuilder Microservice(string connectionString);

    /// <summary>
    /// Builds and returns the configured service collection with all HeroMessaging services registered
    /// </summary>
    IServiceCollection Build();
}

/// <summary>
/// Configuration options for message processing behavior
/// </summary>
public class ProcessingOptions
{
    /// <summary>
    /// Maximum number of messages to process concurrently. Default is processor count.
    /// </summary>
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Whether to process messages sequentially (one at a time). Default is true for message ordering.
    /// </summary>
    public bool SequentialProcessing { get; set; } = true;

    /// <summary>
    /// Maximum time to wait for a message to be processed before timing out. Default is 5 minutes.
    /// </summary>
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum number of retry attempts for failed messages. Default is 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Initial delay between retry attempts. Default is 1 second.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether to enable circuit breaker pattern to prevent cascading failures. Default is true.
    /// </summary>
    public bool EnableCircuitBreaker { get; set; } = true;

    /// <summary>
    /// Number of consecutive failures before opening the circuit. Default is 5.
    /// </summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>
    /// How long to keep the circuit open before attempting to close it. Default is 1 minute.
    /// </summary>
    public TimeSpan CircuitBreakerTimeout { get; set; } = TimeSpan.FromMinutes(1);
}
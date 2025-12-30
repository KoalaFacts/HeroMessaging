using System.Reflection;
using HeroMessaging.Abstractions.Plugins;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Abstractions.Configuration;

/// <summary>
/// Fluent builder for configuring HeroMessaging services.
/// </summary>
public interface IHeroMessagingBuilder
{
    /// <summary>
    /// Gets the service collection for direct service registration.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Enables the mediator pattern for command and query processing.
    /// </summary>
    IHeroMessagingBuilder WithMediator();

    /// <summary>
    /// Enables the event bus for publish/subscribe messaging.
    /// </summary>
    IHeroMessagingBuilder WithEventBus();

    /// <summary>
    /// Enables queue-based message processing.
    /// </summary>
    IHeroMessagingBuilder WithQueues();

    /// <summary>
    /// Enables outbox pattern for reliable message publishing.
    /// </summary>
    IHeroMessagingBuilder WithOutbox();

    /// <summary>
    /// Enables inbox pattern for idempotent message processing.
    /// </summary>
    IHeroMessagingBuilder WithInbox();

    /// <summary>
    /// Configures error handling including retries and dead letter queues.
    /// </summary>
    IHeroMessagingBuilder WithErrorHandling();

    /// <summary>
    /// Uses in-memory storage for development and testing.
    /// </summary>
    IHeroMessagingBuilder UseInMemoryStorage();

    /// <summary>
    /// Uses a custom storage implementation.
    /// </summary>
    /// <typeparam name="TStorage">The storage implementation type</typeparam>
    IHeroMessagingBuilder UseStorage<TStorage>() where TStorage : class, IMessageStorage;

    /// <summary>
    /// Uses a specific storage instance.
    /// </summary>
    /// <param name="storage">The storage instance to use</param>
    IHeroMessagingBuilder UseStorage(IMessageStorage storage);

    /// <summary>
    /// Scans an assembly for message handlers and registers them.
    /// </summary>
    /// <param name="assembly">The assembly to scan</param>
    IHeroMessagingBuilder ScanAssembly(Assembly assembly);

    /// <summary>
    /// Scans multiple assemblies for message handlers and registers them.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan</param>
    IHeroMessagingBuilder ScanAssemblies(params IEnumerable<Assembly> assemblies);

    /// <summary>
    /// Configures message processing options.
    /// </summary>
    /// <param name="configure">Configuration action for processing options</param>
    IHeroMessagingBuilder ConfigureProcessing(Action<ProcessingOptions> configure);

    /// <summary>
    /// Adds a messaging plugin.
    /// </summary>
    /// <typeparam name="TPlugin">The plugin type</typeparam>
    IHeroMessagingBuilder AddPlugin<TPlugin>() where TPlugin : class, IMessagingPlugin;

    /// <summary>
    /// Adds a specific plugin instance.
    /// </summary>
    /// <param name="plugin">The plugin instance</param>
    IHeroMessagingBuilder AddPlugin(IMessagingPlugin plugin);

    /// <summary>
    /// Adds and configures a messaging plugin.
    /// </summary>
    /// <typeparam name="TPlugin">The plugin type</typeparam>
    /// <param name="configure">Configuration action for the plugin</param>
    IHeroMessagingBuilder AddPlugin<TPlugin>(Action<TPlugin> configure) where TPlugin : class, IMessagingPlugin;

    /// <summary>
    /// Discovers and loads plugins from the default location.
    /// </summary>
    IHeroMessagingBuilder DiscoverPlugins();

    /// <summary>
    /// Discovers and loads plugins from a specific directory.
    /// </summary>
    /// <param name="directory">The directory to search for plugins</param>
    IHeroMessagingBuilder DiscoverPlugins(string directory);

    /// <summary>
    /// Discovers and loads plugins from a specific assembly.
    /// </summary>
    /// <param name="assembly">The assembly to search for plugins</param>
    IHeroMessagingBuilder DiscoverPlugins(Assembly assembly);

    /// <summary>
    /// Configures for development environment with in-memory storage.
    /// </summary>
    IHeroMessagingBuilder Development();

    /// <summary>
    /// Configures for production environment with persistent storage.
    /// </summary>
    /// <param name="connectionString">The database connection string</param>
    IHeroMessagingBuilder Production(string connectionString);

    /// <summary>
    /// Configures for microservice environment with distributed messaging.
    /// </summary>
    /// <param name="connectionString">The message broker connection string</param>
    IHeroMessagingBuilder Microservice(string connectionString);

    /// <summary>
    /// Builds and returns the configured service collection.
    /// </summary>
    IServiceCollection Build();
}

/// <summary>
/// Configuration options for message processing.
/// </summary>
public class ProcessingOptions
{
    /// <summary>
    /// Maximum number of concurrent message processors. Default: CPU count.
    /// </summary>
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Whether to process messages sequentially. Default: true.
    /// </summary>
    public bool SequentialProcessing { get; set; } = true;

    /// <summary>
    /// Timeout for processing a single message. Default: 5 minutes.
    /// </summary>
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum number of retry attempts for failed messages. Default: 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts. Default: 1 second.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether to enable circuit breaker pattern. Default: true.
    /// </summary>
    public bool EnableCircuitBreaker { get; set; } = true;

    /// <summary>
    /// Number of failures before circuit breaker opens. Default: 5.
    /// </summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>
    /// Duration the circuit breaker stays open. Default: 1 minute.
    /// </summary>
    public TimeSpan CircuitBreakerTimeout { get; set; } = TimeSpan.FromMinutes(1);
}

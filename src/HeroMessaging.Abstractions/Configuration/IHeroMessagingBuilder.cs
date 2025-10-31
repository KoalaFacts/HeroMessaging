using HeroMessaging.Abstractions.Plugins;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace HeroMessaging.Abstractions.Configuration;

public interface IHeroMessagingBuilder
{
    IHeroMessagingBuilder WithMediator();

    IHeroMessagingBuilder WithEventBus();

    IHeroMessagingBuilder WithQueues();

    IHeroMessagingBuilder WithOutbox();

    IHeroMessagingBuilder WithInbox();

    IHeroMessagingBuilder WithErrorHandling();

    IHeroMessagingBuilder UseInMemoryStorage();

    IHeroMessagingBuilder UseStorage<TStorage>() where TStorage : class, IMessageStorage;

    IHeroMessagingBuilder UseStorage(IMessageStorage storage);

    IHeroMessagingBuilder ScanAssembly(Assembly assembly);

    IHeroMessagingBuilder ScanAssemblies(params Assembly[] assemblies);

    IHeroMessagingBuilder ConfigureProcessing(Action<ProcessingOptions> configure);

    IHeroMessagingBuilder AddPlugin<TPlugin>() where TPlugin : class, IMessagingPlugin;

    IHeroMessagingBuilder AddPlugin(IMessagingPlugin plugin);

    IHeroMessagingBuilder AddPlugin<TPlugin>(Action<TPlugin> configure) where TPlugin : class, IMessagingPlugin;

    IHeroMessagingBuilder DiscoverPlugins();

    IHeroMessagingBuilder DiscoverPlugins(string directory);

    IHeroMessagingBuilder DiscoverPlugins(Assembly assembly);

    IHeroMessagingBuilder Development();

    IHeroMessagingBuilder Production(string connectionString);

    IHeroMessagingBuilder Microservice(string connectionString);

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
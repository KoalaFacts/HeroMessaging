using System.Reflection;
using HeroMessaging.Abstractions.Plugins;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;

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

public class ProcessingOptions
{
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;
    public bool SequentialProcessing { get; set; } = true;
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public int MaxRetries { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public bool EnableCircuitBreaker { get; set; } = true;
    public int CircuitBreakerThreshold { get; set; } = 5;
    public TimeSpan CircuitBreakerTimeout { get; set; } = TimeSpan.FromMinutes(1);
}
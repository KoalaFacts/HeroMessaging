using System.Reflection;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Plugins;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.ErrorHandling;
using HeroMessaging.Processing;
using HeroMessaging.Storage;
using HeroMessaging.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Configuration;

public class HeroMessagingBuilder(IServiceCollection services) : IHeroMessagingBuilder
{
    public IServiceCollection Services { get; } = services;
    private readonly List<Assembly> _assemblies = [];
    private readonly List<IMessagingPlugin> _plugins = [];
    private readonly ProcessingOptions _processingOptions = new();

    private bool _withMediator;
    private bool _withEventBus;
    private bool _withQueues;
    private bool _withOutbox;
    private bool _withInbox;

    public IHeroMessagingBuilder WithMediator()
    {
        _withMediator = true;
        return this;
    }

    public IHeroMessagingBuilder WithEventBus()
    {
        _withEventBus = true;
        return this;
    }

    public IHeroMessagingBuilder WithQueues()
    {
        _withQueues = true;
        return this;
    }

    public IHeroMessagingBuilder WithOutbox()
    {
        _withOutbox = true;
        return this;
    }

    public IHeroMessagingBuilder WithInbox()
    {
        _withInbox = true;
        return this;
    }

    public IHeroMessagingBuilder UseInMemoryStorage()
    {
        Services.AddSingleton<IMessageStorage, InMemoryMessageStorage>();
        Services.AddSingleton<IOutboxStorage, InMemoryOutboxStorage>();
        Services.AddSingleton<IInboxStorage, InMemoryInboxStorage>();
        Services.AddSingleton<IQueueStorage, InMemoryQueueStorage>();
        return this;
    }

    public IHeroMessagingBuilder WithErrorHandling()
    {
        Services.AddSingleton<IDeadLetterQueue, InMemoryDeadLetterQueue>();
        Services.AddSingleton<IErrorHandler, DefaultErrorHandler>();
        return this;
    }

    public IHeroMessagingBuilder UseStorage<TStorage>() where TStorage : class, IMessageStorage
    {
        Services.AddSingleton<IMessageStorage, TStorage>();
        return this;
    }

    public IHeroMessagingBuilder UseStorage(IMessageStorage storage)
    {
        Services.AddSingleton(storage);
        return this;
    }

    public IHeroMessagingBuilder ScanAssembly(Assembly assembly)
    {
        _assemblies.Add(assembly);
        return this;
    }

    public IHeroMessagingBuilder ScanAssemblies(params IEnumerable<Assembly> assemblies)
    {
        _assemblies.AddRange(assemblies);
        return this;
    }

    public IHeroMessagingBuilder ConfigureProcessing(Action<ProcessingOptions> configure)
    {
        configure(_processingOptions);
        return this;
    }

    public IHeroMessagingBuilder AddPlugin<TPlugin>() where TPlugin : class, IMessagingPlugin
    {
        Services.AddSingleton<IMessagingPlugin, TPlugin>();
        return this;
    }

    public IHeroMessagingBuilder AddPlugin<TPlugin>(Action<TPlugin> configure) where TPlugin : class, IMessagingPlugin
    {
        var plugin = Activator.CreateInstance<TPlugin>();
        configure(plugin);
        Services.AddSingleton<IMessagingPlugin>(plugin);
        return this;
    }

    public IHeroMessagingBuilder AddPlugin(IMessagingPlugin plugin)
    {
        _plugins.Add(plugin);
        Services.AddSingleton(plugin);
        return this;
    }

    public IHeroMessagingBuilder Development()
    {
        UseInMemoryStorage();
        WithMediator();
        WithEventBus();
        return this;
    }

    public IHeroMessagingBuilder Production(string connectionString)
    {
        WithMediator();
        WithEventBus();
        WithQueues();
        WithOutbox();
        WithInbox();
        return this;
    }

    public IHeroMessagingBuilder Microservice(string connectionString)
    {
        WithEventBus();
        WithOutbox();
        WithInbox();
        ConfigureProcessing(options =>
        {
            options.SequentialProcessing = false;
            options.MaxConcurrency = Environment.ProcessorCount * 2;
        });
        return this;
    }

    public IHeroMessagingBuilder DiscoverPlugins()
    {
        // Discover plugins from the current app domain
        return DiscoverPlugins(AppDomain.CurrentDomain.BaseDirectory);
    }

    public IHeroMessagingBuilder DiscoverPlugins(string directory)
    {
        // This would be implemented with the plugin discovery system
        // For now, it's a placeholder
        return this;
    }

    public IHeroMessagingBuilder DiscoverPlugins(Assembly assembly)
    {
        // This would scan the assembly for plugins
        // For now, it's a placeholder
        return this;
    }

    public IServiceCollection Build()
    {
        // Register TimeProvider if not already registered
        if (!Services.Any(s => s.ServiceType == typeof(TimeProvider)))
        {
            Services.AddSingleton(TimeProvider.System);
        }

        // Register core utilities for JSON serialization and buffer pooling
        if (!Services.Any(s => s.ServiceType == typeof(DefaultBufferPoolManager)))
        {
            Services.AddSingleton<DefaultBufferPoolManager>();
        }

        if (!Services.Any(s => s.ServiceType == typeof(IJsonSerializer)))
        {
            Services.AddSingleton<IJsonSerializer>(sp =>
                new DefaultJsonSerializer(sp.GetRequiredService<DefaultBufferPoolManager>()));
        }

        Services.AddSingleton(_processingOptions);

        if (_withMediator)
        {
            Services.AddSingleton<ICommandProcessor, CommandProcessor>();
            Services.AddSingleton<IQueryProcessor, QueryProcessor>();
        }

        if (_withEventBus)
        {
            // Register the pipeline-based EventBus
            Services.AddSingleton<IEventBus, EventBus>();

            // Register pipeline services
            Services.AddMessageProcessingPipeline();
        }

        if (_withQueues)
        {
            Services.AddSingleton<IQueueProcessor, QueueProcessor>();
        }

        if (_withOutbox)
        {
            Services.AddSingleton<IOutboxProcessor, OutboxProcessor>();
        }

        if (_withInbox)
        {
            Services.AddSingleton<IInboxProcessor, InboxProcessor>();
        }

        Services.AddSingleton<IHeroMessaging, HeroMessagingService>();

        RegisterHandlers();

        foreach (var plugin in _plugins)
        {
            plugin.Configure(Services);
        }

        // Register configuration validator
        Services.AddSingleton<IConfigurationValidator>(sp =>
            new ConfigurationValidator(Services, sp.GetService<ILogger<ConfigurationValidator>>()));

        return Services;
    }

    private void RegisterHandlers()
    {
        foreach (var assembly in _assemblies)
        {
            var handlerTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract)
                .Where(t => t.GetInterfaces().Any(i =>
                    i.IsGenericType && (
                        i.GetGenericTypeDefinition() == typeof(ICommandHandler<>) ||
                        i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) ||
                        i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>) ||
                        i.GetGenericTypeDefinition() == typeof(IEventHandler<>)
                    )))
                .ToList();

            foreach (var handlerType in handlerTypes)
            {
                var interfaces = handlerType.GetInterfaces()
                    .Where(i => i.IsGenericType && (
                        i.GetGenericTypeDefinition() == typeof(ICommandHandler<>) ||
                        i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) ||
                        i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>) ||
                        i.GetGenericTypeDefinition() == typeof(IEventHandler<>)
                    ));

                foreach (var @interface in interfaces)
                {
                    Services.AddTransient(@interface, handlerType);
                }
            }
        }
    }
}

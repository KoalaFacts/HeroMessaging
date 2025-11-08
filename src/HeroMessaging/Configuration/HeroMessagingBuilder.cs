using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Plugins;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.ErrorHandling;
using HeroMessaging.Processing;
using HeroMessaging.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace HeroMessaging.Configuration;

public class HeroMessagingBuilder(IServiceCollection services) : IHeroMessagingBuilder
{
    private readonly IServiceCollection _services = services;

    public IServiceCollection Services => _services;
    private readonly List<Assembly> _assemblies = new();
    private readonly List<IMessagingPlugin> _plugins = new();
    private ProcessingOptions _processingOptions = new();

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
        _services.AddSingleton<IMessageStorage, InMemoryMessageStorage>();
        _services.AddSingleton<IOutboxStorage, InMemoryOutboxStorage>();
        _services.AddSingleton<IInboxStorage, InMemoryInboxStorage>();
        _services.AddSingleton<IQueueStorage, InMemoryQueueStorage>();
        return this;
    }

    public IHeroMessagingBuilder WithErrorHandling()
    {
        _services.AddSingleton<IDeadLetterQueue, InMemoryDeadLetterQueue>();
        _services.AddSingleton<IErrorHandler, DefaultErrorHandler>();
        return this;
    }

    public IHeroMessagingBuilder UseStorage<TStorage>() where TStorage : class, IMessageStorage
    {
        _services.AddSingleton<IMessageStorage, TStorage>();
        return this;
    }

    public IHeroMessagingBuilder UseStorage(IMessageStorage storage)
    {
        _services.AddSingleton(storage);
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
        _services.AddSingleton<IMessagingPlugin, TPlugin>();
        return this;
    }

    public IHeroMessagingBuilder AddPlugin<TPlugin>(Action<TPlugin> configure) where TPlugin : class, IMessagingPlugin
    {
        var plugin = Activator.CreateInstance<TPlugin>();
        configure(plugin);
        _services.AddSingleton<IMessagingPlugin>(plugin);
        return this;
    }

    public IHeroMessagingBuilder AddPlugin(IMessagingPlugin plugin)
    {
        _plugins.Add(plugin);
        _services.AddSingleton(plugin);
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
        if (!_services.Any(s => s.ServiceType == typeof(TimeProvider)))
        {
            _services.AddSingleton(TimeProvider.System);
        }

        _services.AddSingleton(_processingOptions);

        if (_withMediator)
        {
            _services.AddSingleton<ICommandProcessor, CommandProcessor>();
            _services.AddSingleton<IQueryProcessor, QueryProcessor>();
        }

        if (_withEventBus)
        {
            // Register the new pipeline-based EventBus
            _services.AddSingleton<IEventBus, EventBusV2>();

            // Register pipeline services
            _services.AddMessageProcessingPipeline();
        }

        if (_withQueues)
        {
            _services.AddSingleton<IQueueProcessor, QueueProcessor>();
        }

        if (_withOutbox)
        {
            _services.AddSingleton<IOutboxProcessor, OutboxProcessor>();
        }

        if (_withInbox)
        {
            _services.AddSingleton<IInboxProcessor, InboxProcessor>();
        }

        _services.AddSingleton<IHeroMessaging, HeroMessagingService>();

        RegisterHandlers();

        foreach (var plugin in _plugins)
        {
            plugin.Configure(_services);
        }

        // Register configuration validator
        _services.AddSingleton<IConfigurationValidator>(sp =>
            new ConfigurationValidator(_services, sp.GetService<ILogger<ConfigurationValidator>>()));

        return _services;
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
                    _services.AddTransient(@interface, handlerType);
                }
            }
        }
    }
}
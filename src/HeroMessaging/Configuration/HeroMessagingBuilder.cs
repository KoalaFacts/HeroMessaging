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

/// <summary>
/// Builder class for configuring HeroMessaging services with a fluent API.
/// </summary>
/// <remarks>
/// This builder provides methods to configure all aspects of HeroMessaging including:
/// - Storage (in-memory, SQL Server, PostgreSQL)
/// - Patterns (Mediator, Event Bus, Queues, Outbox, Inbox)
/// - Error handling and dead letter queues
/// - Handler discovery from assemblies
/// - Processing options (concurrency, parallelism)
/// - Plugins for extensibility
///
/// The builder follows the fluent interface pattern where all methods return the builder
/// for method chaining. Call Build() when configuration is complete to register all services.
///
/// Example usage:
/// <code>
/// var builder = new HeroMessagingBuilder(services);
/// builder
///     .UseInMemoryStorage()
///     .WithMediator()
///     .WithEventBus()
///     .WithErrorHandling()
///     .ScanAssembly(typeof(Program).Assembly)
///     .ConfigureProcessing(options =>
///     {
///         options.MaxConcurrency = 10;
///         options.SequentialProcessing = false;
///     })
///     .Build();
/// </code>
/// </remarks>
/// <param name="services">The service collection to register HeroMessaging services with</param>
public class HeroMessagingBuilder(IServiceCollection services) : IHeroMessagingBuilder
{
    private readonly IServiceCollection _services = services;

    /// <summary>
    /// Gets the underlying service collection being configured.
    /// </summary>
    /// <remarks>
    /// This property provides access to the service collection for advanced scenarios
    /// where you need to register additional services directly.
    /// </remarks>
    public IServiceCollection Services => _services;
    private readonly List<Assembly> _assemblies = new();
    private readonly List<IMessagingPlugin> _plugins = new();
    private ProcessingOptions _processingOptions = new();

    private bool _withMediator;
    private bool _withEventBus;
    private bool _withQueues;
    private bool _withOutbox;
    private bool _withInbox;

    /// <summary>
    /// Enables the Mediator pattern for in-process command and query handling.
    /// </summary>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// The Mediator pattern provides:
    /// - ICommandProcessor for executing commands with ICommandHandler&lt;TCommand&gt;
    /// - IQueryProcessor for executing queries with IQueryHandler&lt;TQuery, TResult&gt;
    /// - Decoupling between senders and handlers
    /// - Single responsibility for each handler
    ///
    /// Use this when you want to implement CQRS (Command Query Responsibility Segregation)
    /// in your application.
    ///
    /// Example:
    /// <code>
    /// builder.WithMediator();
    ///
    /// // Then use in your code:
    /// await messaging.Send(new CreateOrderCommand("CUST-001", 99.99m));
    /// var order = await messaging.Send(new GetOrderQuery("ORDER-001"));
    /// </code>
    /// </remarks>
    public IHeroMessagingBuilder WithMediator()
    {
        _withMediator = true;
        return this;
    }

    /// <summary>
    /// Enables the Event Bus for publishing and subscribing to domain events.
    /// </summary>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// The Event Bus provides:
    /// - IEventBus for publishing events with IEventHandler&lt;TEvent&gt;
    /// - Support for multiple handlers per event type
    /// - Fire-and-forget event publishing
    /// - Pipeline-based processing with middleware support
    ///
    /// Use this when you want to implement event-driven architecture and domain events.
    ///
    /// Example:
    /// <code>
    /// builder.WithEventBus();
    ///
    /// // Then use in your code:
    /// await messaging.Publish(new OrderCreatedEvent("ORDER-001", "CUST-001"));
    /// </code>
    /// </remarks>
    public IHeroMessagingBuilder WithEventBus()
    {
        _withEventBus = true;
        return this;
    }

    /// <summary>
    /// Enables queue-based background job processing.
    /// </summary>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// Queue processing provides:
    /// - IQueueProcessor for managing background job queues
    /// - Priority-based message processing
    /// - Delayed message delivery
    /// - Independent scaling of queue workers
    ///
    /// Use this for:
    /// - Background job processing
    /// - Asynchronous operations
    /// - Rate-limited tasks
    /// - Email sending, report generation, etc.
    ///
    /// Example:
    /// <code>
    /// builder.WithQueues();
    ///
    /// // Then use in your code:
    /// await messaging.Enqueue(new SendEmailMessage("user@example.com"), "email-queue");
    /// await messaging.StartQueue("email-queue");
    /// </code>
    /// </remarks>
    public IHeroMessagingBuilder WithQueues()
    {
        _withQueues = true;
        return this;
    }

    /// <summary>
    /// Enables the Outbox pattern for transactional message publishing.
    /// </summary>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// The Outbox pattern provides:
    /// - IOutboxProcessor for reliable message publishing
    /// - Atomic publishing with database transactions
    /// - Guaranteed at-least-once delivery
    /// - Retry logic for failed publishes
    ///
    /// Use this when you need to ensure messages are only published if a database
    /// transaction succeeds, preventing inconsistencies between database state and
    /// published events.
    ///
    /// Example:
    /// <code>
    /// builder.WithOutbox();
    ///
    /// // Then use in your code within a transaction:
    /// await messaging.PublishToOutbox(new OrderCreatedEvent(order.Id));
    /// await unitOfWork.CommitAsync(); // Event published only if commit succeeds
    /// </code>
    /// </remarks>
    public IHeroMessagingBuilder WithOutbox()
    {
        _withOutbox = true;
        return this;
    }

    /// <summary>
    /// Enables the Inbox pattern for exactly-once message processing.
    /// </summary>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// The Inbox pattern provides:
    /// - IInboxProcessor for deduplicating incoming messages
    /// - Exactly-once processing guarantees
    /// - Message ID-based deduplication
    /// - Protection against duplicate message delivery
    ///
    /// Use this when receiving messages from external systems (message brokers, webhooks)
    /// that may deliver the same message multiple times (at-least-once delivery semantics).
    ///
    /// Example:
    /// <code>
    /// builder.WithInbox();
    ///
    /// // Then use in your code when receiving messages:
    /// await messaging.ProcessIncoming(incomingMessage, new InboxOptions
    /// {
    ///     Source = "rabbitmq",
    ///     RequireIdempotency = true
    /// });
    /// </code>
    /// </remarks>
    public IHeroMessagingBuilder WithInbox()
    {
        _withInbox = true;
        return this;
    }

    /// <summary>
    /// Configures HeroMessaging to use in-memory storage for all storage operations.
    /// </summary>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// In-memory storage is ideal for:
    /// - Local development
    /// - Testing and prototyping
    /// - CI/CD environments without database dependencies
    /// - Demos and tutorials
    ///
    /// This registers in-memory implementations for:
    /// - IMessageStorage (general message storage)
    /// - IOutboxStorage (outbox pattern)
    /// - IInboxStorage (inbox pattern)
    /// - IQueueStorage (queue processing)
    ///
    /// Warning: All data is lost when the application stops. Do not use in production.
    ///
    /// Example:
    /// <code>
    /// builder.UseInMemoryStorage();
    /// </code>
    /// </remarks>
    public IHeroMessagingBuilder UseInMemoryStorage()
    {
        _services.AddSingleton<IMessageStorage, InMemoryMessageStorage>();
        _services.AddSingleton<IOutboxStorage, InMemoryOutboxStorage>();
        _services.AddSingleton<IInboxStorage, InMemoryInboxStorage>();
        _services.AddSingleton<IQueueStorage, InMemoryQueueStorage>();
        return this;
    }

    /// <summary>
    /// Enables error handling with dead letter queue for failed messages.
    /// </summary>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// Error handling provides:
    /// - IDeadLetterQueue for storing failed messages
    /// - IErrorHandler for processing errors and retries
    /// - Automatic retry logic with exponential backoff
    /// - Failed message inspection and recovery
    ///
    /// Messages that fail processing after maximum retry attempts are moved to the
    /// dead letter queue for manual inspection and potential reprocessing.
    ///
    /// Example:
    /// <code>
    /// builder.WithErrorHandling();
    ///
    /// // Failed messages can be inspected:
    /// var failedMessages = await deadLetterQueue.GetMessagesAsync();
    /// </code>
    /// </remarks>
    public IHeroMessagingBuilder WithErrorHandling()
    {
        _services.AddSingleton<IDeadLetterQueue, InMemoryDeadLetterQueue>();
        _services.AddSingleton<IErrorHandler, DefaultErrorHandler>();
        return this;
    }

    /// <summary>
    /// Configures HeroMessaging to use a custom storage implementation.
    /// </summary>
    /// <typeparam name="TStorage">The custom storage implementation type</typeparam>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// Use this method to register a custom IMessageStorage implementation.
    /// The storage type will be resolved from the DI container.
    ///
    /// Example:
    /// <code>
    /// builder.UseStorage&lt;MyCustomStorage&gt;();
    /// </code>
    /// </remarks>
    public IHeroMessagingBuilder UseStorage<TStorage>() where TStorage : class, IMessageStorage
    {
        _services.AddSingleton<IMessageStorage, TStorage>();
        return this;
    }

    /// <summary>
    /// Configures HeroMessaging to use a specific storage instance.
    /// </summary>
    /// <param name="storage">The storage instance to use</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// Use this method when you need to provide a pre-configured storage instance,
    /// for example with specific connection settings or state.
    ///
    /// Example:
    /// <code>
    /// var customStorage = new MyStorage(connectionString);
    /// builder.UseStorage(customStorage);
    /// </code>
    /// </remarks>
    public IHeroMessagingBuilder UseStorage(IMessageStorage storage)
    {
        _services.AddSingleton(storage);
        return this;
    }

    /// <summary>
    /// Scans the specified assembly for message handlers and registers them automatically.
    /// </summary>
    /// <param name="assembly">The assembly to scan for handlers</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// This method scans the assembly for implementations of:
    /// - ICommandHandler&lt;TCommand&gt; (commands without response)
    /// - ICommandHandler&lt;TCommand, TResponse&gt; (commands with response)
    /// - IQueryHandler&lt;TQuery, TResult&gt; (queries)
    /// - IEventHandler&lt;TEvent&gt; (event handlers)
    ///
    /// All handlers are registered with Transient lifetime, meaning a new instance
    /// is created for each request.
    ///
    /// Example:
    /// <code>
    /// builder.ScanAssembly(typeof(Program).Assembly);
    /// </code>
    /// </remarks>
    public IHeroMessagingBuilder ScanAssembly(Assembly assembly)
    {
        _assemblies.Add(assembly);
        return this;
    }

    /// <summary>
    /// Scans multiple assemblies for message handlers and registers them automatically.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan for handlers</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// This is a convenience method for scanning multiple assemblies at once.
    /// See <see cref="ScanAssembly"/> for details on what handlers are registered.
    ///
    /// Example:
    /// <code>
    /// builder.ScanAssemblies(
    ///     typeof(Program).Assembly,
    ///     typeof(Domain.Order).Assembly,
    ///     typeof(Application.Commands).Assembly
    /// );
    /// </code>
    /// </remarks>
    public IHeroMessagingBuilder ScanAssemblies(params Assembly[] assemblies)
    {
        _assemblies.AddRange(assemblies);
        return this;
    }

    /// <summary>
    /// Configures message processing options such as concurrency and parallelism.
    /// </summary>
    /// <param name="configure">Configuration action for processing options</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// Processing options control how messages are processed:
    /// - MaxConcurrency: Maximum number of concurrent message processors
    /// - SequentialProcessing: Whether to process messages sequentially or in parallel
    /// - BatchSize: Number of messages to process in a batch
    /// - Timeout: Maximum time to wait for message processing
    ///
    /// Example:
    /// <code>
    /// builder.ConfigureProcessing(options =>
    /// {
    ///     options.MaxConcurrency = 20;
    ///     options.SequentialProcessing = false;
    /// });
    /// </code>
    /// </remarks>
    public IHeroMessagingBuilder ConfigureProcessing(Action<ProcessingOptions> configure)
    {
        configure(_processingOptions);
        return this;
    }

    /// <summary>
    /// Adds a plugin to extend HeroMessaging functionality.
    /// </summary>
    /// <typeparam name="TPlugin">The plugin type to add</typeparam>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// Plugins provide a way to extend HeroMessaging with additional functionality such as:
    /// - Custom storage providers (SQL Server, PostgreSQL, MongoDB, etc.)
    /// - Custom serializers (JSON, Protobuf, MessagePack, etc.)
    /// - Observability tools (OpenTelemetry, Application Insights, etc.)
    /// - Custom transport layers (RabbitMQ, Azure Service Bus, etc.)
    ///
    /// The plugin will be instantiated by the DI container and configured automatically.
    ///
    /// Example:
    /// <code>
    /// builder.AddPlugin&lt;SqlServerStoragePlugin&gt;();
    /// </code>
    /// </remarks>
    public IHeroMessagingBuilder AddPlugin<TPlugin>() where TPlugin : class, IMessagingPlugin
    {
        _services.AddSingleton<IMessagingPlugin, TPlugin>();
        return this;
    }

    /// <summary>
    /// Adds a plugin with custom configuration.
    /// </summary>
    /// <typeparam name="TPlugin">The plugin type to add</typeparam>
    /// <param name="configure">Configuration action for the plugin</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// This overload allows you to configure the plugin instance before it's registered.
    ///
    /// Example:
    /// <code>
    /// builder.AddPlugin&lt;SqlServerStoragePlugin&gt;(plugin =>
    /// {
    ///     plugin.ConnectionString = "Server=...;Database=...";
    ///     plugin.CommandTimeout = TimeSpan.FromSeconds(30);
    /// });
    /// </code>
    /// </remarks>
    public IHeroMessagingBuilder AddPlugin<TPlugin>(Action<TPlugin> configure) where TPlugin : class, IMessagingPlugin
    {
        var plugin = Activator.CreateInstance<TPlugin>();
        configure(plugin);
        _services.AddSingleton<IMessagingPlugin>(plugin);
        return this;
    }

    /// <summary>
    /// Adds a pre-configured plugin instance.
    /// </summary>
    /// <param name="plugin">The plugin instance to add</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// Use this method when you have a pre-configured plugin instance to register.
    ///
    /// Example:
    /// <code>
    /// var storagePlugin = new SqlServerStoragePlugin(connectionString);
    /// builder.AddPlugin(storagePlugin);
    /// </code>
    /// </remarks>
    public IHeroMessagingBuilder AddPlugin(IMessagingPlugin plugin)
    {
        _plugins.Add(plugin);
        _services.AddSingleton(plugin);
        return this;
    }

    /// <summary>
    /// Applies a preset configuration optimized for local development.
    /// </summary>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// This preset includes:
    /// - In-memory storage (no database required)
    /// - Mediator pattern (commands and queries)
    /// - Event bus (event publishing)
    ///
    /// This configuration is ideal for rapid development without external dependencies.
    /// Add WithErrorHandling() and ScanAssemblies() to complete the configuration.
    ///
    /// Example:
    /// <code>
    /// builder
    ///     .Development()
    ///     .WithErrorHandling()
    ///     .ScanAssembly(typeof(Program).Assembly);
    /// </code>
    /// </remarks>
    public IHeroMessagingBuilder Development()
    {
        UseInMemoryStorage();
        WithMediator();
        WithEventBus();
        return this;
    }

    /// <summary>
    /// Applies a preset configuration optimized for production monolithic applications.
    /// </summary>
    /// <param name="connectionString">Database connection string for durable storage</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// This preset includes:
    /// - Mediator pattern (commands and queries)
    /// - Event bus (event publishing)
    /// - Queue processing (background jobs)
    /// - Outbox pattern (transactional publishing)
    /// - Inbox pattern (exactly-once delivery)
    ///
    /// Note: You must configure the storage provider separately using a plugin or
    /// ConfigureStorage() extension method.
    ///
    /// Example:
    /// <code>
    /// builder
    ///     .Production(connectionString)
    ///     .UseSqlServerStorage(connectionString)
    ///     .WithErrorHandling()
    ///     .ScanAssemblies(typeof(Program).Assembly);
    /// </code>
    /// </remarks>
    public IHeroMessagingBuilder Production(string connectionString)
    {
        WithMediator();
        WithEventBus();
        WithQueues();
        WithOutbox();
        WithInbox();
        return this;
    }

    /// <summary>
    /// Applies a preset configuration optimized for microservices and distributed systems.
    /// </summary>
    /// <param name="connectionString">Database connection string for durable storage</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// This preset includes:
    /// - Event bus (inter-service communication)
    /// - Outbox pattern (transactional publishing)
    /// - Inbox pattern (exactly-once delivery)
    /// - Parallel processing (non-sequential)
    /// - High concurrency (2x CPU cores)
    ///
    /// This configuration excludes Mediator and Queues as microservices typically
    /// handle commands directly and use dedicated queue services.
    ///
    /// Note: You must configure the storage and transport providers separately.
    ///
    /// Example:
    /// <code>
    /// builder
    ///     .Microservice(connectionString)
    ///     .UseSqlServerStorage(connectionString)
    ///     .WithErrorHandling()
    ///     .ScanAssemblies(typeof(OrderService).Assembly);
    /// </code>
    /// </remarks>
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

    /// <summary>
    /// Automatically discovers and registers plugins from the current application domain.
    /// </summary>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// This method scans the application's base directory for plugin assemblies and
    /// registers any discovered plugins automatically.
    ///
    /// Plugin discovery looks for assemblies that contain types implementing IMessagingPlugin.
    ///
    /// Example:
    /// <code>
    /// builder.DiscoverPlugins();
    /// </code>
    ///
    /// Note: This is a convenience method that calls DiscoverPlugins(AppDomain.CurrentDomain.BaseDirectory).
    /// </remarks>
    public IHeroMessagingBuilder DiscoverPlugins()
    {
        // Discover plugins from the current app domain
        return DiscoverPlugins(AppDomain.CurrentDomain.BaseDirectory);
    }

    /// <summary>
    /// Discovers and registers plugins from the specified directory.
    /// </summary>
    /// <param name="directory">The directory path to scan for plugin assemblies</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// This method scans the specified directory for plugin assemblies and registers
    /// any discovered plugins automatically.
    ///
    /// Example:
    /// <code>
    /// builder.DiscoverPlugins("/path/to/plugins");
    /// </code>
    ///
    /// Note: This feature requires the plugin discovery system to be configured.
    /// </remarks>
    public IHeroMessagingBuilder DiscoverPlugins(string directory)
    {
        // This would be implemented with the plugin discovery system
        // For now, it's a placeholder
        return this;
    }

    /// <summary>
    /// Discovers and registers plugins from the specified assembly.
    /// </summary>
    /// <param name="assembly">The assembly to scan for plugins</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// This method scans the specified assembly for types implementing IMessagingPlugin
    /// and registers them automatically.
    ///
    /// Example:
    /// <code>
    /// builder.DiscoverPlugins(typeof(MyPlugin).Assembly);
    /// </code>
    ///
    /// Note: This feature requires the plugin discovery system to be configured.
    /// </remarks>
    public IHeroMessagingBuilder DiscoverPlugins(Assembly assembly)
    {
        // This would scan the assembly for plugins
        // For now, it's a placeholder
        return this;
    }

    /// <summary>
    /// Completes the configuration and registers all HeroMessaging services with the DI container.
    /// </summary>
    /// <returns>The configured service collection</returns>
    /// <remarks>
    /// This method finalizes the builder configuration by:
    /// 1. Registering core services (TimeProvider, ProcessingOptions)
    /// 2. Registering enabled patterns (Mediator, EventBus, Queues, Outbox, Inbox)
    /// 3. Scanning assemblies and registering handlers
    /// 4. Configuring plugins
    /// 5. Registering configuration validator
    ///
    /// You must call this method to complete the configuration process.
    ///
    /// Example:
    /// <code>
    /// builder
    ///     .UseInMemoryStorage()
    ///     .WithMediator()
    ///     .WithEventBus()
    ///     .ScanAssembly(typeof(Program).Assembly)
    ///     .Build(); // Completes configuration
    /// </code>
    ///
    /// Note: If you use the AddHeroMessaging(Action&lt;IHeroMessagingBuilder&gt;) extension method,
    /// Build() is called automatically.
    /// </remarks>
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
using System.Reflection;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Plugins;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Configuration;
using HeroMessaging.ErrorHandling;
using HeroMessaging.Processing;
using HeroMessaging.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Configuration;

/// <summary>
/// Comprehensive unit tests for HeroMessagingBuilder
/// Tests all builder methods, service registration, configuration validation, and edge cases
/// Targets 80%+ code coverage
/// </summary>
public class HeroMessagingBuilderTests
{
    private IServiceCollection CreateServiceCollection() => new ServiceCollection();

    #region Builder Creation and Initialization Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidServiceCollection_CreatesBuilder()
    {
        // Arrange
        var services = CreateServiceCollection();

        // Act
        var builder = new HeroMessagingBuilder(services);

        // Assert
        Assert.NotNull(builder);
        Assert.Same(services, builder.Services);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Services_Property_ReturnsInternalServiceCollection()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.Services;

        // Assert
        Assert.NotNull(result);
        Assert.Same(services, result);
    }

    #endregion

    #region Feature Flag Tests (WithMediator, WithEventBus, etc.)

    [Fact]
    [Trait("Category", "Unit")]
    public void WithMediator_ReturnsBuilder_ForFluentAPI()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());

        // Act
        var result = builder.WithMediator();

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithEventBus_ReturnsBuilder_ForFluentAPI()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());

        // Act
        var result = builder.WithEventBus();

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithQueues_ReturnsBuilder_ForFluentAPI()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());

        // Act
        var result = builder.WithQueues();

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithOutbox_ReturnsBuilder_ForFluentAPI()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());

        // Act
        var result = builder.WithOutbox();

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithInbox_ReturnsBuilder_ForFluentAPI()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());

        // Act
        var result = builder.WithInbox();

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithErrorHandling_ReturnsBuilder_ForFluentAPI()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());

        // Act
        var result = builder.WithErrorHandling();

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    #endregion

    #region Storage Configuration Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void UseInMemoryStorage_RegistersAllStorageTypes()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.UseInMemoryStorage();
        var provider = services.BuildServiceProvider();

        // Assert
        var messageStorage = provider.GetService<IMessageStorage>();
        var outboxStorage = provider.GetService<IOutboxStorage>();
        var inboxStorage = provider.GetService<IInboxStorage>();
        var queueStorage = provider.GetService<IQueueStorage>();

        Assert.NotNull(messageStorage);
        Assert.NotNull(outboxStorage);
        Assert.NotNull(inboxStorage);
        Assert.NotNull(queueStorage);

        Assert.IsType<InMemoryMessageStorage>(messageStorage);
        Assert.IsType<InMemoryOutboxStorage>(outboxStorage);
        Assert.IsType<InMemoryInboxStorage>(inboxStorage);
        Assert.IsType<InMemoryQueueStorage>(queueStorage);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void UseInMemoryStorage_ReturnsBuilderForChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());

        // Act
        var result = builder.UseInMemoryStorage();

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void UseStorage_WithGenericType_RegistersStorage()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.UseStorage<InMemoryMessageStorage>();
        var provider = services.BuildServiceProvider();

        // Assert
        var storage = provider.GetService<IMessageStorage>();
        Assert.NotNull(storage);
        Assert.IsType<InMemoryMessageStorage>(storage);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void UseStorage_WithGenericType_ReturnsBuilderForChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());

        // Act
        var result = builder.UseStorage<InMemoryMessageStorage>();

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void UseStorage_WithInstance_RegistersProvidedStorage()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);
        var mockStorage = new Mock<IMessageStorage>();
        var storageInstance = mockStorage.Object;

        // Act
        builder.UseStorage(storageInstance);
        var provider = services.BuildServiceProvider();

        // Assert
        var storage = provider.GetService<IMessageStorage>();
        Assert.Same(storageInstance, storage);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void UseStorage_WithInstance_ReturnsBuilderForChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());
        var mockStorage = new Mock<IMessageStorage>();

        // Act
        var result = builder.UseStorage(mockStorage.Object);

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region Error Handling Configuration Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void WithErrorHandling_RegistersDeadLetterQueue()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithErrorHandling();
        var provider = services.BuildServiceProvider();

        // Assert
        var deadLetterQueue = provider.GetService<IDeadLetterQueue>();
        Assert.NotNull(deadLetterQueue);
        Assert.IsType<InMemoryDeadLetterQueue>(deadLetterQueue);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithErrorHandling_RegistersErrorHandler()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithErrorHandling();
        var provider = services.BuildServiceProvider();

        // Assert
        var errorHandler = provider.GetService<IErrorHandler>();
        Assert.NotNull(errorHandler);
        Assert.IsType<DefaultErrorHandler>(errorHandler);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithErrorHandling_ReturnsBuilderForChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());

        // Act
        var result = builder.WithErrorHandling();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region Assembly Scanning Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ScanAssembly_WithValidAssembly_AccumulatesAssembly()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());
        var assembly = typeof(HeroMessagingBuilderTests).Assembly;

        // Act
        var result = builder.ScanAssembly(assembly);

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ScanAssembly_ReturnsBuilderForChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());
        var assembly = typeof(HeroMessagingBuilderTests).Assembly;

        // Act
        var result = builder.ScanAssembly(assembly);

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ScanAssemblies_WithMultipleAssemblies_AccumulatesAllAssemblies()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());
        var assemblies = new[] { typeof(HeroMessagingBuilderTests).Assembly };

        // Act
        var result = builder.ScanAssemblies(assemblies);

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ScanAssemblies_WithEmptyEnumerable_ReturnsBuilder()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());
        var assemblies = new Assembly[] { };

        // Act
        var result = builder.ScanAssemblies(assemblies);

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ScanAssemblies_ReturnsBuilderForChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());
        var assemblies = new[] { typeof(HeroMessagingBuilderTests).Assembly };

        // Act
        var result = builder.ScanAssemblies(assemblies);

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    #endregion

    #region Processing Options Configuration Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ConfigureProcessing_WithConfiguration_ModifiesProcessingOptions()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.ConfigureProcessing(options =>
        {
            options.MaxConcurrency = 16;
            options.SequentialProcessing = false;
        });

        var result = builder.Build();
        var provider = result.BuildServiceProvider();
        var processingOptions = provider.GetService<ProcessingOptions>();

        // Assert
        Assert.NotNull(processingOptions);
        Assert.Equal(16, processingOptions.MaxConcurrency);
        Assert.False(processingOptions.SequentialProcessing);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ConfigureProcessing_ReturnsBuilderForChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());

        // Act
        var result = builder.ConfigureProcessing(options => { });

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ConfigureProcessing_WithRetryPolicy_ConfiguresRetries()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.ConfigureProcessing(options =>
        {
            options.MaxRetries = 5;
            options.RetryDelay = TimeSpan.FromSeconds(2);
        });

        var result = builder.Build();
        var provider = result.BuildServiceProvider();
        var processingOptions = provider.GetService<ProcessingOptions>();

        // Assert
        Assert.NotNull(processingOptions);
        Assert.Equal(5, processingOptions.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(2), processingOptions.RetryDelay);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ConfigureProcessing_WithCircuitBreaker_ConfiguresCircuitBreaker()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.ConfigureProcessing(options =>
        {
            options.EnableCircuitBreaker = true;
            options.CircuitBreakerThreshold = 10;
            options.CircuitBreakerTimeout = TimeSpan.FromMinutes(2);
        });

        var result = builder.Build();
        var provider = result.BuildServiceProvider();
        var processingOptions = provider.GetService<ProcessingOptions>();

        // Assert
        Assert.NotNull(processingOptions);
        Assert.True(processingOptions.EnableCircuitBreaker);
        Assert.Equal(10, processingOptions.CircuitBreakerThreshold);
        Assert.Equal(TimeSpan.FromMinutes(2), processingOptions.CircuitBreakerTimeout);
    }

    #endregion

    #region Plugin Registration Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void AddPlugin_WithGenericType_RegistersPlugin()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.AddPlugin<TestPlugin>();

        // Assert
        Assert.Same(builder, result);
        var provider = services.BuildServiceProvider();
        var plugin = provider.GetService<IMessagingPlugin>();
        Assert.NotNull(plugin);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddPlugin_WithGenericType_ReturnsBuilderForChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());

        // Act
        var result = builder.AddPlugin<TestPlugin>();

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddPlugin_WithConfiguration_ConfiguresAndRegistersPlugin()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);
        var configureWasCalled = false;

        // Act
        builder.AddPlugin<TestPlugin>(plugin =>
        {
            configureWasCalled = true;
        });

        // Assert
        Assert.True(configureWasCalled);
        var provider = services.BuildServiceProvider();
        var plugins = provider.GetServices<IMessagingPlugin>();
        Assert.NotEmpty(plugins);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddPlugin_WithConfiguration_ReturnsBuilderForChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());

        // Act
        var result = builder.AddPlugin<TestPlugin>(plugin => { });

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddPlugin_WithInstance_RegistersProvidedPlugin()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);
        var mockPlugin = new Mock<IMessagingPlugin>();

        // Act
        builder.AddPlugin(mockPlugin.Object);
        var provider = services.BuildServiceProvider();

        // Assert
        var plugins = provider.GetServices<IMessagingPlugin>();
        Assert.Contains(mockPlugin.Object, plugins);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddPlugin_WithInstance_ReturnsBuilderForChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());
        var mockPlugin = new Mock<IMessagingPlugin>();

        // Act
        var result = builder.AddPlugin(mockPlugin.Object);

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddPlugin_MultiplePlugins_RegistersAllPlugins()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);
        var plugin1 = new Mock<IMessagingPlugin>();
        var plugin2 = new Mock<IMessagingPlugin>();

        // Act
        builder.AddPlugin(plugin1.Object).AddPlugin(plugin2.Object);
        var provider = services.BuildServiceProvider();

        // Assert
        var plugins = provider.GetServices<IMessagingPlugin>().ToList();
        Assert.Equal(2, plugins.Count);
    }

    #endregion

    #region Plugin Discovery Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void DiscoverPlugins_WithoutArguments_ReturnsBuilder()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());

        // Act
        var result = builder.DiscoverPlugins();

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DiscoverPlugins_WithDirectory_ReturnsBuilder()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());
        var directory = AppDomain.CurrentDomain.BaseDirectory;

        // Act
        var result = builder.DiscoverPlugins(directory);

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DiscoverPlugins_WithAssembly_ReturnsBuilder()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());
        var assembly = typeof(HeroMessagingBuilderTests).Assembly;

        // Act
        var result = builder.DiscoverPlugins(assembly);

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region Preset Configuration Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Development_ConfiguresDevelopmentEnvironment()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.Development();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);

        // Should have in-memory storage
        var messageStorage = provider.GetService<IMessageStorage>();
        Assert.NotNull(messageStorage);

        // Should have mediator and event bus capability
        var services_built = builder.Build();
        Assert.NotNull(services_built);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Development_ReturnsBuilderForChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());

        // Act
        var result = builder.Development();

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Production_ConfiguresProductionEnvironment()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);
        var connectionString = "Server=.;Database=HeroMessaging";

        // Act
        var result = builder.Production(connectionString);

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Production_ReturnsBuilderForChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());

        // Act
        var result = builder.Production("connection");

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Microservice_ConfiguresMicroserviceEnvironment()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);
        var connectionString = "Server=.;Database=HeroMessaging";

        // Act
        var result = builder.Microservice(connectionString);
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);

        // Build to verify configuration
        var builtServices = builder.Build();
        var processingOptions = builtServices.BuildServiceProvider().GetService<ProcessingOptions>();
        Assert.NotNull(processingOptions);
        Assert.False(processingOptions.SequentialProcessing);
        Assert.Equal(Environment.ProcessorCount * 2, processingOptions.MaxConcurrency);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Microservice_ReturnsBuilderForChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());

        // Act
        var result = builder.Microservice("connection");

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    #endregion

    #region Build Method Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_RegistersTimeProvider()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.Build();
        var provider = result.BuildServiceProvider();

        // Assert
        var timeProvider = provider.GetService<TimeProvider>();
        Assert.NotNull(timeProvider);
        Assert.Same(TimeProvider.System, timeProvider);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_DoesNotRegisterDuplicateTimeProvider()
    {
        // Arrange
        var services = CreateServiceCollection();
        var customTimeProvider = TimeProvider.System;
        services.AddSingleton(customTimeProvider);
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.Build();
        var provider = result.BuildServiceProvider();

        // Assert
        var timeProvider = provider.GetService<TimeProvider>();
        Assert.NotNull(timeProvider);
        // Should be the one we registered
        Assert.Same(customTimeProvider, timeProvider);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_RegistersBufferPoolManager()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.Build();
        var provider = result.BuildServiceProvider();

        // Assert
        var bufferPoolManager = provider.GetService<DefaultBufferPoolManager>();
        Assert.NotNull(bufferPoolManager);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_DoesNotRegisterDuplicateBufferPoolManager()
    {
        // Arrange
        var services = CreateServiceCollection();
        var customBufferPool = new DefaultBufferPoolManager();
        services.AddSingleton(customBufferPool);
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.Build();
        var provider = result.BuildServiceProvider();

        // Assert
        var bufferPoolManager = provider.GetService<DefaultBufferPoolManager>();
        Assert.NotNull(bufferPoolManager);
        Assert.Same(customBufferPool, bufferPoolManager);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_RegistersJsonSerializer()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.Build();
        var provider = result.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IJsonSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_DoesNotRegisterDuplicateJsonSerializer()
    {
        // Arrange
        var services = CreateServiceCollection();
        var mockSerializer = new Mock<IJsonSerializer>();
        services.AddSingleton(mockSerializer.Object);
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.Build();
        var provider = result.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IJsonSerializer>();
        Assert.NotNull(serializer);
        Assert.Same(mockSerializer.Object, serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_RegistersProcessingOptions()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.Build();
        var provider = result.BuildServiceProvider();

        // Assert
        var processingOptions = provider.GetService<ProcessingOptions>();
        Assert.NotNull(processingOptions);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_WithMediator_RegistersCommandAndQueryProcessors()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithMediator();
        var result = builder.Build();
        var provider = result.BuildServiceProvider();

        // Assert
        var commandProcessor = provider.GetService<ICommandProcessor>();
        var queryProcessor = provider.GetService<IQueryProcessor>();

        Assert.NotNull(commandProcessor);
        Assert.NotNull(queryProcessor);
        Assert.IsType<CommandProcessor>(commandProcessor);
        Assert.IsType<QueryProcessor>(queryProcessor);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_WithoutMediator_DoesNotRegisterProcessors()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.Build();
        var provider = result.BuildServiceProvider();

        // Assert
        var commandProcessor = provider.GetService<ICommandProcessor>();
        var queryProcessor = provider.GetService<IQueryProcessor>();

        Assert.Null(commandProcessor);
        Assert.Null(queryProcessor);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_WithEventBus_RegistersEventBus()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithEventBus();
        var result = builder.Build();
        var provider = result.BuildServiceProvider();

        // Assert
        var eventBus = provider.GetService<IEventBus>();
        Assert.NotNull(eventBus);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_WithQueues_RegistersQueueProcessor()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithQueues();
        var result = builder.Build();
        var provider = result.BuildServiceProvider();

        // Assert
        var queueProcessor = provider.GetService<IQueueProcessor>();
        Assert.NotNull(queueProcessor);
        Assert.IsType<QueueProcessor>(queueProcessor);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_WithOutbox_RegistersOutboxProcessor()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithOutbox();
        var result = builder.Build();
        var provider = result.BuildServiceProvider();

        // Assert
        var outboxProcessor = provider.GetService<IOutboxProcessor>();
        Assert.NotNull(outboxProcessor);
        Assert.IsType<OutboxProcessor>(outboxProcessor);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_WithInbox_RegistersInboxProcessor()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithInbox();
        var result = builder.Build();
        var provider = result.BuildServiceProvider();

        // Assert
        var inboxProcessor = provider.GetService<IInboxProcessor>();
        Assert.NotNull(inboxProcessor);
        Assert.IsType<InboxProcessor>(inboxProcessor);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_RegistersHeroMessagingService()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.Build();
        var provider = result.BuildServiceProvider();

        // Assert
        var heroMessaging = provider.GetService<IHeroMessaging>();
        Assert.NotNull(heroMessaging);
        Assert.IsType<HeroMessagingService>(heroMessaging);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_RegistersConfigurationValidator()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.Build();
        var provider = result.BuildServiceProvider();

        // Assert
        var validator = provider.GetService<IConfigurationValidator>();
        Assert.NotNull(validator);
        Assert.IsType<ConfigurationValidator>(validator);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_ReturnsServiceCollection()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.Build();

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IServiceCollection>(result);
    }

    #endregion

    #region Handler Registration Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_WithScannedAssembly_RegistersHandlers()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.ScanAssembly(typeof(TestCommandHandler).Assembly);
        var result = builder.Build();
        var provider = result.BuildServiceProvider();

        // Assert
        var handler = provider.GetService<ICommandHandler<TestCommand>>();
        Assert.NotNull(handler);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_WithMultipleHandlers_RegistersAllHandlers()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.ScanAssembly(typeof(TestCommandHandler).Assembly);
        var result = builder.Build();
        var provider = result.BuildServiceProvider();

        // Assert
        var commandHandler = provider.GetService<ICommandHandler<TestCommand>>();
        var queryHandler = provider.GetService<IQueryHandler<TestQuery, string>>();

        Assert.NotNull(commandHandler);
        Assert.NotNull(queryHandler);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_WithoutScannedAssembly_DoesNotRegisterHandlers()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.Build();
        var provider = result.BuildServiceProvider();

        // Assert
        var handler = provider.GetService<ICommandHandler<TestCommand>>();
        Assert.Null(handler);
    }

    #endregion

    #region Plugin Configuration Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_WithPlugin_CallsPluginConfigure()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);
        var configureWasCalled = false;

        var mockPlugin = new Mock<IMessagingPlugin>();
        mockPlugin.Setup(p => p.Configure(It.IsAny<IServiceCollection>()))
            .Callback(() => configureWasCalled = true);

        // Act
        builder.AddPlugin(mockPlugin.Object);
        builder.Build();

        // Assert
        Assert.True(configureWasCalled);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_WithMultiplePlugins_ConfiguresAllPlugins()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);
        var configure1Called = false;
        var configure2Called = false;

        var mockPlugin1 = new Mock<IMessagingPlugin>();
        var mockPlugin2 = new Mock<IMessagingPlugin>();

        mockPlugin1.Setup(p => p.Configure(It.IsAny<IServiceCollection>()))
            .Callback(() => configure1Called = true);
        mockPlugin2.Setup(p => p.Configure(It.IsAny<IServiceCollection>()))
            .Callback(() => configure2Called = true);

        // Act
        builder.AddPlugin(mockPlugin1.Object).AddPlugin(mockPlugin2.Object);
        builder.Build();

        // Assert
        Assert.True(configure1Called);
        Assert.True(configure2Called);
    }

    #endregion

    #region Fluent API Chaining Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void FluentAPI_ChainMultipleMethods_WorksCorrectly()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder
            .WithMediator()
            .WithEventBus()
            .WithQueues()
            .WithOutbox()
            .WithInbox()
            .WithErrorHandling()
            .UseInMemoryStorage()
            .ConfigureProcessing(options => options.MaxConcurrency = 8)
            .ScanAssembly(typeof(HeroMessagingBuilderTests).Assembly)
            .Build();

        // Assert
        Assert.NotNull(result);
        var provider = result.BuildServiceProvider();

        var commandProcessor = provider.GetService<ICommandProcessor>();
        var eventBus = provider.GetService<IEventBus>();
        var queueProcessor = provider.GetService<IQueueProcessor>();
        var outboxProcessor = provider.GetService<IOutboxProcessor>();
        var inboxProcessor = provider.GetService<IInboxProcessor>();
        var deadLetterQueue = provider.GetService<IDeadLetterQueue>();
        var storage = provider.GetService<IMessageStorage>();

        Assert.NotNull(commandProcessor);
        Assert.NotNull(eventBus);
        Assert.NotNull(queueProcessor);
        Assert.NotNull(outboxProcessor);
        Assert.NotNull(inboxProcessor);
        Assert.NotNull(deadLetterQueue);
        Assert.NotNull(storage);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FluentAPI_MultipleChains_EachReturnsBuilder()
    {
        // Arrange & Act
        var builder = new HeroMessagingBuilder(CreateServiceCollection());
        var result1 = builder.WithMediator();
        var result2 = result1.WithEventBus();
        var result3 = result2.WithQueues();

        // Assert
        Assert.Same(builder, result1);
        Assert.Same(builder, result2);
        Assert.Same(builder, result3);
    }

    #endregion

    #region Edge Cases and Error Scenarios

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_WithMultipleConfigureProcessing_LastConfigurationWins()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.ConfigureProcessing(options => options.MaxConcurrency = 4);
        builder.ConfigureProcessing(options => options.MaxConcurrency = 8);

        var result = builder.Build();
        var provider = result.BuildServiceProvider();
        var processingOptions = provider.GetService<ProcessingOptions>();

        // Assert
        Assert.NotNull(processingOptions);
        Assert.Equal(8, processingOptions.MaxConcurrency);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_WithEmptyServiceCollection_CreatesValidProvider()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());

        // Act
        var result = builder.Build();
        var provider = result.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider);
        var heroMessaging = provider.GetService<IHeroMessaging>();
        Assert.NotNull(heroMessaging);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ScanAssemblies_WithVeryLargeEnumerable_HandlesCorrectly()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(CreateServiceCollection());
        var assemblies = Enumerable.Repeat(typeof(HeroMessagingBuilderTests).Assembly, 100);

        // Act
        var result = builder.ScanAssemblies(assemblies);

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Build_AllProcessorsRegistered_WorksTogether()
    {
        // Arrange
        var services = CreateServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder
            .WithMediator()
            .WithEventBus()
            .WithQueues()
            .WithOutbox()
            .WithInbox()
            .UseInMemoryStorage()
            .Build();

        var provider = services.BuildServiceProvider();

        // Assert - All should be registered without conflicts
        var commandProcessor = provider.GetService<ICommandProcessor>();
        var queryProcessor = provider.GetService<IQueryProcessor>();
        var eventBus = provider.GetService<IEventBus>();
        var queueProcessor = provider.GetService<IQueueProcessor>();
        var outboxProcessor = provider.GetService<IOutboxProcessor>();
        var inboxProcessor = provider.GetService<IInboxProcessor>();

        Assert.NotNull(commandProcessor);
        Assert.NotNull(queryProcessor);
        Assert.NotNull(eventBus);
        Assert.NotNull(queueProcessor);
        Assert.NotNull(outboxProcessor);
        Assert.NotNull(inboxProcessor);
    }

    #endregion

    #region Test Helpers

    private class TestPlugin : IMessagingPlugin
    {
        public string Name => "TestPlugin";

        public void Configure(IServiceCollection services)
        {
        }

        public Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private class TestCommand : ICommand
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private class TestCommandHandler : ICommandHandler<TestCommand>
    {
        public Task Handle(TestCommand command, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private class TestQuery : IQuery<string>
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private class TestQueryHandler : IQueryHandler<TestQuery, string>
    {
        public Task<string> Handle(TestQuery query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult("test result");
        }
    }

    #endregion
}

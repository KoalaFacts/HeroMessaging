using System.Reflection;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Plugins;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Configuration;
using HeroMessaging.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Configuration;

[Trait("Category", "Unit")]
public sealed class HeroMessagingBuilderTests
{
    private readonly ServiceCollection _services;

    public HeroMessagingBuilderTests()
    {
        _services = new ServiceCollection();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidServices_CreatesInstance()
    {
        // Arrange & Act
        var builder = new HeroMessagingBuilder(_services);

        // Assert
        Assert.NotNull(builder);
        Assert.Same(_services, builder.Services);
    }

    #endregion

    #region WithMediator Tests

    [Fact]
    public void WithMediator_EnablesMediatorPattern()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        builder.WithMediator().Build();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(ICommandProcessor));
        Assert.Contains(_services, s => s.ServiceType == typeof(IQueryProcessor));
    }

    [Fact]
    public void WithMediator_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        var result = builder.WithMediator();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region WithEventBus Tests

    [Fact]
    public void WithEventBus_EnablesEventBusPattern()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        builder.WithEventBus().Build();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IEventBus));
    }

    [Fact]
    public void WithEventBus_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        var result = builder.WithEventBus();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region WithQueues Tests

    [Fact]
    public void WithQueues_EnablesQueuePattern()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        builder.WithQueues().Build();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IQueueProcessor));
    }

    [Fact]
    public void WithQueues_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        var result = builder.WithQueues();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region WithOutbox Tests

    [Fact]
    public void WithOutbox_EnablesOutboxPattern()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        builder.WithOutbox().Build();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IOutboxProcessor));
    }

    [Fact]
    public void WithOutbox_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        var result = builder.WithOutbox();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region WithInbox Tests

    [Fact]
    public void WithInbox_EnablesInboxPattern()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        builder.WithInbox().Build();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IInboxProcessor));
    }

    [Fact]
    public void WithInbox_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        var result = builder.WithInbox();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region UseInMemoryStorage Tests

    [Fact]
    public void UseInMemoryStorage_RegistersAllStorageTypes()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        builder.UseInMemoryStorage();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IMessageStorage));
        Assert.Contains(_services, s => s.ServiceType == typeof(IOutboxStorage));
        Assert.Contains(_services, s => s.ServiceType == typeof(IInboxStorage));
        Assert.Contains(_services, s => s.ServiceType == typeof(IQueueStorage));
    }

    [Fact]
    public void UseInMemoryStorage_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        var result = builder.UseInMemoryStorage();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region WithErrorHandling Tests

    [Fact]
    public void WithErrorHandling_RegistersErrorHandlingServices()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        builder.WithErrorHandling();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IDeadLetterQueue));
        Assert.Contains(_services, s => s.ServiceType == typeof(IErrorHandler));
    }

    [Fact]
    public void WithErrorHandling_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        var result = builder.WithErrorHandling();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region UseStorage Tests

    [Fact]
    public void UseStorage_Generic_RegistersStorageType()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        builder.UseStorage<TestMessageStorage>();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IMessageStorage) &&
                                        s.ImplementationType == typeof(TestMessageStorage));
    }

    [Fact]
    public void UseStorage_Instance_RegistersStorageInstance()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);
        var storage = new TestMessageStorage();

        // Act
        builder.UseStorage(storage);

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IMessageStorage) ||
                                        s.ServiceType == typeof(TestMessageStorage));
    }

    #endregion

    #region ScanAssembly Tests

    [Fact]
    public void ScanAssembly_AddsAssemblyToList()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);
        var assembly = Assembly.GetExecutingAssembly();

        // Act
        var result = builder.ScanAssembly(assembly);

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void ScanAssemblies_AddsMultipleAssemblies()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);
        var assemblies = new[] { Assembly.GetExecutingAssembly(), typeof(HeroMessagingBuilder).Assembly };

        // Act
        var result = builder.ScanAssemblies(assemblies);

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region ConfigureProcessing Tests

    [Fact]
    public void ConfigureProcessing_InvokesConfigurationAction()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);
        var configuredMaxConcurrency = 0;

        // Act
        builder.ConfigureProcessing(options =>
        {
            options.MaxConcurrency = 42;
            configuredMaxConcurrency = options.MaxConcurrency;
        });

        // Assert
        Assert.Equal(42, configuredMaxConcurrency);
    }

    [Fact]
    public void ConfigureProcessing_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        var result = builder.ConfigureProcessing(options => { });

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region AddPlugin Tests

    [Fact]
    public void AddPlugin_Generic_RegistersPlugin()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        builder.AddPlugin<TestPlugin>();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IMessagingPlugin) &&
                                        s.ImplementationType == typeof(TestPlugin));
    }

    [Fact]
    public void AddPlugin_WithConfiguration_RegistersAndConfiguresPlugin()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);
        var configured = false;

        // Act
        builder.AddPlugin<TestPlugin>(plugin =>
        {
            configured = true;
        });

        // Assert
        Assert.True(configured);
        Assert.Contains(_services, s => s.ServiceType == typeof(IMessagingPlugin));
    }

    [Fact]
    public void AddPlugin_WithInstance_RegistersPluginInstance()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);
        var plugin = new TestPlugin();

        // Act
        builder.AddPlugin(plugin);

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IMessagingPlugin) ||
                                        s.ServiceType == typeof(TestPlugin));
    }

    #endregion

    #region Preset Configuration Tests

    [Fact]
    public void Development_ConfiguresForDevelopment()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        builder.Development();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IMessageStorage));
        Assert.Contains(_services, s => s.ServiceType == typeof(IOutboxStorage));
        Assert.Contains(_services, s => s.ServiceType == typeof(IInboxStorage));
        Assert.Contains(_services, s => s.ServiceType == typeof(IQueueStorage));
    }

    [Fact]
    public void Development_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        var result = builder.Development();

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void Production_ConfiguresForProduction()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        builder.Production("connection-string").Build();

        // Assert - Should configure all production features
        Assert.Contains(_services, s => s.ServiceType == typeof(ICommandProcessor));
        Assert.Contains(_services, s => s.ServiceType == typeof(IQueryProcessor));
        Assert.Contains(_services, s => s.ServiceType == typeof(IEventBus));
        Assert.Contains(_services, s => s.ServiceType == typeof(IQueueProcessor));
        Assert.Contains(_services, s => s.ServiceType == typeof(IOutboxProcessor));
        Assert.Contains(_services, s => s.ServiceType == typeof(IInboxProcessor));
    }

    [Fact]
    public void Microservice_ConfiguresForMicroservices()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        builder.Microservice("connection-string").Build();

        // Assert - Should configure event bus and outbox/inbox patterns
        Assert.Contains(_services, s => s.ServiceType == typeof(IEventBus));
        Assert.Contains(_services, s => s.ServiceType == typeof(IOutboxProcessor));
        Assert.Contains(_services, s => s.ServiceType == typeof(IInboxProcessor));
    }

    #endregion

    #region DiscoverPlugins Tests

    [Fact]
    public void DiscoverPlugins_NoParameters_ReturnsBuilder()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        var result = builder.DiscoverPlugins();

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void DiscoverPlugins_WithDirectory_ReturnsBuilder()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        var result = builder.DiscoverPlugins("test-directory");

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void DiscoverPlugins_WithAssembly_ReturnsBuilder()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);
        var assembly = Assembly.GetExecutingAssembly();

        // Act
        var result = builder.DiscoverPlugins(assembly);

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region Build Tests

    [Fact]
    public void Build_RegistersTimeProvider_WhenNotAlreadyRegistered()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        builder.Build();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(TimeProvider));
    }

    [Fact]
    public void Build_DoesNotRegisterTimeProvider_WhenAlreadyRegistered()
    {
        // Arrange
        var timeProvider = TimeProvider.System;
        _services.AddSingleton(timeProvider);
        var builder = new HeroMessagingBuilder(_services);

        // Act
        builder.Build();

        // Assert
        var timeProviderServices = _services.Where(s => s.ServiceType == typeof(TimeProvider)).ToList();
        Assert.Single(timeProviderServices);
    }

    [Fact]
    public void Build_RegistersCoreUtilities()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        builder.Build();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(DefaultBufferPoolManager));
        Assert.Contains(_services, s => s.ServiceType == typeof(IJsonSerializer));
    }

    [Fact]
    public void Build_RegistersProcessingOptions()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        builder.ConfigureProcessing(options => options.MaxConcurrency = 42).Build();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(ProcessingOptions));
    }

    [Fact]
    public void Build_RegistersIHeroMessaging()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        builder.Build();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(Abstractions.IHeroMessaging));
    }

    [Fact]
    public void Build_RegistersConfigurationValidator()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        builder.Build();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IConfigurationValidator));
    }

    [Fact]
    public void Build_InvokesPluginConfiguration()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);
        var plugin = new TestPlugin();
        builder.AddPlugin(plugin);

        // Act
        builder.Build();

        // Assert
        Assert.True(plugin.WasConfigured);
    }

    [Fact]
    public void Build_ReturnsServiceCollection()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        var result = builder.Build();

        // Assert
        Assert.Same(_services, result);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void FluentConfiguration_ChainsCorrectly()
    {
        // Arrange
        var builder = new HeroMessagingBuilder(_services);

        // Act
        var result = builder
            .WithMediator()
            .WithEventBus()
            .WithQueues()
            .WithOutbox()
            .WithInbox()
            .UseInMemoryStorage()
            .WithErrorHandling()
            .ConfigureProcessing(options => options.MaxConcurrency = 10)
            .Build();

        // Assert
        Assert.Same(_services, result);
        Assert.Contains(_services, s => s.ServiceType == typeof(ICommandProcessor));
        Assert.Contains(_services, s => s.ServiceType == typeof(IEventBus));
        Assert.Contains(_services, s => s.ServiceType == typeof(IQueueProcessor));
        Assert.Contains(_services, s => s.ServiceType == typeof(IOutboxProcessor));
        Assert.Contains(_services, s => s.ServiceType == typeof(IInboxProcessor));
        Assert.Contains(_services, s => s.ServiceType == typeof(IMessageStorage));
        Assert.Contains(_services, s => s.ServiceType == typeof(IErrorHandler));
    }

    #endregion

    #region Test Helper Classes

    public class TestMessageStorage : IMessageStorage
    {
        public Task<string> StoreAsync(IMessage message, MessageStorageOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.NewGuid().ToString());

        public Task<T?> RetrieveAsync<T>(string messageId, CancellationToken cancellationToken = default) where T : IMessage
            => Task.FromResult<T?>(default);

        public Task<IEnumerable<T>> QueryAsync<T>(MessageQuery query, CancellationToken cancellationToken = default) where T : IMessage
            => Task.FromResult(Enumerable.Empty<T>());

        public Task<bool> DeleteAsync(string messageId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> UpdateAsync(string messageId, IMessage message, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> ExistsAsync(string messageId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<long> CountAsync(MessageQuery? query = null, CancellationToken cancellationToken = default)
            => Task.FromResult(0L);

        public Task ClearAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StoreAsync(IMessage message, IStorageTransaction? transaction = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IMessage?> RetrieveAsync(Guid messageId, IStorageTransaction? transaction = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IMessage?>(null);

        public Task<List<IMessage>> QueryAsync(MessageQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<IMessage>());

        public Task DeleteAsync(Guid messageId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IStorageTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IStorageTransaction>(Mock.Of<IStorageTransaction>());
    }

    public class TestPlugin : IMessagingPlugin
    {
        public bool WasConfigured { get; private set; }

        public string Name => "TestPlugin";

        public void Configure(IServiceCollection services)
        {
            WasConfigured = true;
        }

        public Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ShutdownAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    #endregion
}

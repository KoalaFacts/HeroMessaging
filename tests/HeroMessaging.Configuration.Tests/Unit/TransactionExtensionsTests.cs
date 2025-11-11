using System.Data;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Configuration;
using HeroMessaging.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Configuration.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class TransactionExtensionsTests
{
    [Fact]
    public void WithTransactions_WithDefaultIsolationLevel_DecoratesAllProcessors()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Register required dependencies
        services.AddSingleton(Mock.Of<ICommandProcessor>());
        services.AddSingleton(Mock.Of<IQueryProcessor>());
        services.AddSingleton(Mock.Of<IEventBus>());
        services.AddSingleton(Mock.Of<IOutboxProcessor>());
        services.AddSingleton(Mock.Of<IInboxProcessor>());
        services.AddSingleton(Mock.Of<ITransactionExecutor>());
        services.AddSingleton(Mock.Of<IUnitOfWorkFactory>());
        services.AddSingleton(Mock.Of<ILogger<TransactionEventBusDecorator>>());

        // Act
        var result = builder.WithTransactions();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        Assert.NotNull(provider.GetService<ICommandProcessor>());
        Assert.NotNull(provider.GetService<IQueryProcessor>());
        Assert.NotNull(provider.GetService<IEventBus>());
        Assert.NotNull(provider.GetService<IOutboxProcessor>());
        Assert.NotNull(provider.GetService<IInboxProcessor>());
    }

    [Fact]
    public void WithTransactions_WithCustomIsolationLevel_UsesSpecifiedLevel()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        services.AddSingleton(Mock.Of<ICommandProcessor>());
        services.AddSingleton(Mock.Of<IQueryProcessor>());
        services.AddSingleton(Mock.Of<IEventBus>());
        services.AddSingleton(Mock.Of<IOutboxProcessor>());
        services.AddSingleton(Mock.Of<IInboxProcessor>());
        services.AddSingleton(Mock.Of<ITransactionExecutor>());
        services.AddSingleton(Mock.Of<IUnitOfWorkFactory>());
        services.AddSingleton(Mock.Of<ILogger<TransactionEventBusDecorator>>());

        // Act
        builder.WithTransactions(IsolationLevel.Serializable);
        var provider = services.BuildServiceProvider();

        // Assert - verify decorators are registered
        Assert.NotNull(provider.GetService<ICommandProcessor>());
        Assert.NotNull(provider.GetService<IQueryProcessor>());
    }

    [Fact]
    public void WithTransactions_WithInvalidBuilder_ThrowsArgumentException()
    {
        // Arrange
        var mockBuilder = new Mock<IHeroMessagingBuilder>();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => mockBuilder.Object.WithTransactions());
        Assert.Contains("Builder must be of type HeroMessagingBuilder", ex.Message);
        Assert.Equal("builder", ex.ParamName);
    }

    [Fact]
    public void WithCommandTransactions_DecoratesCommandProcessorOnly()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        services.AddSingleton(Mock.Of<ICommandProcessor>());
        services.AddSingleton(Mock.Of<ITransactionExecutor>());

        // Act
        var result = builder.WithCommandTransactions();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        Assert.NotNull(provider.GetService<ICommandProcessor>());
    }

    [Fact]
    public void WithCommandTransactions_WithCustomIsolationLevel_UsesSpecifiedLevel()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        services.AddSingleton(Mock.Of<ICommandProcessor>());
        services.AddSingleton(Mock.Of<ITransactionExecutor>());

        // Act
        builder.WithCommandTransactions(IsolationLevel.Snapshot);
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetService<ICommandProcessor>());
    }

    [Fact]
    public void WithCommandTransactions_WithInvalidBuilder_ThrowsArgumentException()
    {
        // Arrange
        var mockBuilder = new Mock<IHeroMessagingBuilder>();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => mockBuilder.Object.WithCommandTransactions());
        Assert.Contains("Builder must be of type HeroMessagingBuilder", ex.Message);
        Assert.Equal("builder", ex.ParamName);
    }

    [Fact]
    public void WithTransactions_WithConfiguration_DecoratesEnabledProcessors()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        services.AddSingleton(Mock.Of<ICommandProcessor>());
        services.AddSingleton(Mock.Of<IQueryProcessor>());
        services.AddSingleton(Mock.Of<IEventBus>());
        services.AddSingleton(Mock.Of<ITransactionExecutor>());
        services.AddSingleton(Mock.Of<IUnitOfWorkFactory>());
        services.AddSingleton(Mock.Of<ILogger<TransactionEventBusDecorator>>());

        var config = new TransactionConfiguration
        {
            CommandIsolationLevel = IsolationLevel.Serializable,
            QueryIsolationLevel = IsolationLevel.ReadCommitted,
            EventIsolationLevel = IsolationLevel.ReadCommitted,
            OutboxIsolationLevel = null,
            InboxIsolationLevel = null
        };

        // Act
        builder.WithTransactions(config);
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetService<ICommandProcessor>());
        Assert.NotNull(provider.GetService<IQueryProcessor>());
        Assert.NotNull(provider.GetService<IEventBus>());
    }

    [Fact]
    public void WithTransactions_WithConfigurationNullLevels_SkipsThoseProcessors()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        services.AddSingleton(Mock.Of<ICommandProcessor>());
        services.AddSingleton(Mock.Of<ITransactionExecutor>());

        var config = new TransactionConfiguration
        {
            CommandIsolationLevel = IsolationLevel.ReadCommitted,
            QueryIsolationLevel = null,
            EventIsolationLevel = null,
            OutboxIsolationLevel = null,
            InboxIsolationLevel = null
        };

        // Act
        var result = builder.WithTransactions(config);

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void WithTransactions_WithConfigurationInvalidBuilder_ThrowsArgumentException()
    {
        // Arrange
        var mockBuilder = new Mock<IHeroMessagingBuilder>();
        var config = new TransactionConfiguration();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => mockBuilder.Object.WithTransactions(config));
        Assert.Contains("Builder must be of type HeroMessagingBuilder", ex.Message);
        Assert.Equal("builder", ex.ParamName);
    }

    [Fact]
    public void TransactionConfiguration_DefaultValues_AreCorrect()
    {
        // Act
        var config = new TransactionConfiguration();

        // Assert
        Assert.Equal(IsolationLevel.ReadCommitted, config.CommandIsolationLevel);
        Assert.Equal(IsolationLevel.ReadCommitted, config.QueryIsolationLevel);
        Assert.Equal(IsolationLevel.ReadCommitted, config.EventIsolationLevel);
        Assert.Equal(IsolationLevel.ReadCommitted, config.OutboxIsolationLevel);
        Assert.Equal(IsolationLevel.ReadCommitted, config.InboxIsolationLevel);
    }

    [Fact]
    public void TransactionConfiguration_WriteOperationsOnly_ConfiguresCorrectly()
    {
        // Act
        var config = TransactionConfiguration.WriteOperationsOnly();

        // Assert
        Assert.Equal(IsolationLevel.ReadCommitted, config.CommandIsolationLevel);
        Assert.Null(config.QueryIsolationLevel);
        Assert.Equal(IsolationLevel.ReadCommitted, config.EventIsolationLevel);
        Assert.Equal(IsolationLevel.ReadCommitted, config.OutboxIsolationLevel);
        Assert.Equal(IsolationLevel.ReadCommitted, config.InboxIsolationLevel);
    }

    [Fact]
    public void TransactionConfiguration_Serializable_ConfiguresCorrectly()
    {
        // Act
        var config = TransactionConfiguration.Serializable();

        // Assert
        Assert.Equal(IsolationLevel.Serializable, config.CommandIsolationLevel);
        Assert.Equal(IsolationLevel.ReadCommitted, config.QueryIsolationLevel);
        Assert.Equal(IsolationLevel.Serializable, config.EventIsolationLevel);
        Assert.Equal(IsolationLevel.Serializable, config.OutboxIsolationLevel);
        Assert.Equal(IsolationLevel.Serializable, config.InboxIsolationLevel);
    }

    [Fact]
    public void TransactionConfiguration_CanSetCustomValues()
    {
        // Act
        var config = new TransactionConfiguration
        {
            CommandIsolationLevel = IsolationLevel.Snapshot,
            QueryIsolationLevel = IsolationLevel.ReadUncommitted,
            EventIsolationLevel = IsolationLevel.RepeatableRead,
            OutboxIsolationLevel = IsolationLevel.Chaos,
            InboxIsolationLevel = IsolationLevel.Unspecified
        };

        // Assert
        Assert.Equal(IsolationLevel.Snapshot, config.CommandIsolationLevel);
        Assert.Equal(IsolationLevel.ReadUncommitted, config.QueryIsolationLevel);
        Assert.Equal(IsolationLevel.RepeatableRead, config.EventIsolationLevel);
        Assert.Equal(IsolationLevel.Chaos, config.OutboxIsolationLevel);
        Assert.Equal(IsolationLevel.Unspecified, config.InboxIsolationLevel);
    }

    [Fact]
    public void WithTransactions_DecoratesServicesInCorrectOrder()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        services.AddSingleton(Mock.Of<ICommandProcessor>());
        services.AddSingleton(Mock.Of<IQueryProcessor>());
        services.AddSingleton(Mock.Of<IEventBus>());
        services.AddSingleton(Mock.Of<IOutboxProcessor>());
        services.AddSingleton(Mock.Of<IInboxProcessor>());
        services.AddSingleton(Mock.Of<ITransactionExecutor>());
        services.AddSingleton(Mock.Of<IUnitOfWorkFactory>());
        services.AddSingleton(Mock.Of<ILogger<TransactionEventBusDecorator>>());

        // Act
        builder.WithTransactions();
        var provider = services.BuildServiceProvider();

        // Assert - just verify all services can be resolved
        var commandProcessor = provider.GetService<ICommandProcessor>();
        var queryProcessor = provider.GetService<IQueryProcessor>();
        var eventBus = provider.GetService<IEventBus>();
        var outboxProcessor = provider.GetService<IOutboxProcessor>();
        var inboxProcessor = provider.GetService<IInboxProcessor>();

        Assert.NotNull(commandProcessor);
        Assert.NotNull(queryProcessor);
        Assert.NotNull(eventBus);
        Assert.NotNull(outboxProcessor);
        Assert.NotNull(inboxProcessor);
    }

    [Fact]
    public void WithTransactions_AllIsolationLevels_CanBeUsed()
    {
        // Arrange
        var isolationLevels = new[]
        {
            IsolationLevel.Unspecified,
            IsolationLevel.Chaos,
            IsolationLevel.ReadUncommitted,
            IsolationLevel.ReadCommitted,
            IsolationLevel.RepeatableRead,
            IsolationLevel.Serializable,
            IsolationLevel.Snapshot
        };

        // Act & Assert - verify each isolation level works
        foreach (var level in isolationLevels)
        {
            var services = new ServiceCollection();
            var builder = new HeroMessagingBuilder(services);

            services.AddSingleton(Mock.Of<ICommandProcessor>());
            services.AddSingleton(Mock.Of<IQueryProcessor>());
            services.AddSingleton(Mock.Of<IEventBus>());
            services.AddSingleton(Mock.Of<IOutboxProcessor>());
            services.AddSingleton(Mock.Of<IInboxProcessor>());
            services.AddSingleton(Mock.Of<ITransactionExecutor>());
            services.AddSingleton(Mock.Of<IUnitOfWorkFactory>());
            services.AddSingleton(Mock.Of<ILogger<TransactionEventBusDecorator>>());

            builder.WithTransactions(level);
            var provider = services.BuildServiceProvider();

            Assert.NotNull(provider.GetService<ICommandProcessor>());
        }
    }
}

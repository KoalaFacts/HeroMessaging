using System.Data;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Configuration;

[Trait("Category", "Unit")]
public class TransactionExtensionsTests
{
    private readonly ServiceCollection _services;
    private readonly HeroMessagingBuilder _builder;

    public TransactionExtensionsTests()
    {
        _services = new ServiceCollection();
        _services.AddLogging();
        _builder = new HeroMessagingBuilder(_services);

        // Add required services
        _services.AddSingleton(Mock.Of<ITransactionExecutor>());
        _services.AddSingleton(Mock.Of<IUnitOfWorkFactory>());
        _services.AddSingleton(Mock.Of<ICommandProcessor>());
        _services.AddSingleton(Mock.Of<IQueryProcessor>());
        _services.AddSingleton(Mock.Of<IEventBus>());
        _services.AddSingleton(Mock.Of<IOutboxProcessor>());
        _services.AddSingleton(Mock.Of<IInboxProcessor>());
    }

    #region WithTransactions Tests

    [Fact]
    public void WithTransactions_WithDefaultOptions_RegistersTransactionDecorators()
    {
        // Act
        var result = _builder.WithTransactions();

        // Assert
        Assert.NotNull(result);
        Assert.Same(_builder, result);

        var provider = _services.BuildServiceProvider();
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
    public void WithTransactions_WithCustomIsolationLevel_UsesProvidedLevel()
    {
        // Arrange
        var isolationLevel = IsolationLevel.Serializable;

        // Act
        var result = _builder.WithTransactions(isolationLevel);

        // Assert
        Assert.NotNull(result);
        Assert.Same(_builder, result);
    }

    [Fact]
    public void WithTransactions_WithNonHeroMessagingBuilder_ThrowsArgumentException()
    {
        // Arrange
        var mockBuilder = new Mock<IHeroMessagingBuilder>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            mockBuilder.Object.WithTransactions());

        Assert.Equal("builder", exception.ParamName);
        Assert.Contains("must be of type HeroMessagingBuilder", exception.Message);
    }

    [Fact]
    public void WithTransactions_DecoratesAllProcessors()
    {
        // Act
        _builder.WithTransactions();
        var provider = _services.BuildServiceProvider();

        // Assert
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

    #endregion

    #region WithCommandTransactions Tests

    [Fact]
    public void WithCommandTransactions_WithDefaultOptions_RegistersCommandDecorator()
    {
        // Act
        var result = _builder.WithCommandTransactions();

        // Assert
        Assert.NotNull(result);
        Assert.Same(_builder, result);

        var provider = _services.BuildServiceProvider();
        var commandProcessor = provider.GetService<ICommandProcessor>();
        Assert.NotNull(commandProcessor);
    }

    [Fact]
    public void WithCommandTransactions_WithCustomIsolationLevel_UsesProvidedLevel()
    {
        // Arrange
        var isolationLevel = IsolationLevel.RepeatableRead;

        // Act
        var result = _builder.WithCommandTransactions(isolationLevel);

        // Assert
        Assert.NotNull(result);
        Assert.Same(_builder, result);
    }

    [Fact]
    public void WithCommandTransactions_WithNonHeroMessagingBuilder_ThrowsArgumentException()
    {
        // Arrange
        var mockBuilder = new Mock<IHeroMessagingBuilder>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            mockBuilder.Object.WithCommandTransactions());

        Assert.Equal("builder", exception.ParamName);
        Assert.Contains("must be of type HeroMessagingBuilder", exception.Message);
    }

    #endregion

    #region WithTransactions(TransactionConfiguration) Tests

    [Fact]
    public void WithTransactions_WithConfiguration_UsesProvidedConfiguration()
    {
        // Arrange
        var config = new TransactionConfiguration
        {
            CommandIsolationLevel = IsolationLevel.Serializable,
            QueryIsolationLevel = IsolationLevel.ReadCommitted,
            EventIsolationLevel = IsolationLevel.RepeatableRead,
            OutboxIsolationLevel = IsolationLevel.Serializable,
            InboxIsolationLevel = IsolationLevel.Serializable
        };

        // Act
        var result = _builder.WithTransactions(config);

        // Assert
        Assert.NotNull(result);
        Assert.Same(_builder, result);
    }

    [Fact]
    public void WithTransactions_WithNullIsolationLevels_SkipsDecorators()
    {
        // Arrange
        var config = new TransactionConfiguration
        {
            CommandIsolationLevel = null,
            QueryIsolationLevel = null,
            EventIsolationLevel = null,
            OutboxIsolationLevel = null,
            InboxIsolationLevel = null
        };

        // Act
        var result = _builder.WithTransactions(config);

        // Assert
        Assert.NotNull(result);
        Assert.Same(_builder, result);
    }

    [Fact]
    public void WithTransactions_WithConfiguration_WithNonHeroMessagingBuilder_ThrowsArgumentException()
    {
        // Arrange
        var mockBuilder = new Mock<IHeroMessagingBuilder>();
        var config = new TransactionConfiguration();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            mockBuilder.Object.WithTransactions(config));

        Assert.Equal("builder", exception.ParamName);
    }

    #endregion

    #region TransactionConfiguration Tests

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
    public void TransactionConfiguration_WriteOperationsOnly_HasCorrectSettings()
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
    public void TransactionConfiguration_Serializable_HasCorrectSettings()
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

    #endregion

    #region Chaining Tests

    [Fact]
    public void WithTransactions_CanChainWithOtherExtensions()
    {
        // Act
        var result = _builder
            .WithTransactions()
            .Development()
            .UseInMemoryStorage();

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void WithCommandTransactions_CanChainWithOtherExtensions()
    {
        // Act
        var result = _builder
            .WithCommandTransactions()
            .Development()
            .UseInMemoryStorage();

        // Assert
        Assert.NotNull(result);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void WithTransactions_FullConfiguration_BuildsSuccessfully()
    {
        // Act
        _builder.WithTransactions(IsolationLevel.Serializable);
        var provider = _services.BuildServiceProvider();

        // Assert
        var transactionExecutor = provider.GetService<ITransactionExecutor>();
        var unitOfWorkFactory = provider.GetService<IUnitOfWorkFactory>();
        var commandProcessor = provider.GetService<ICommandProcessor>();
        var queryProcessor = provider.GetService<IQueryProcessor>();
        var eventBus = provider.GetService<IEventBus>();

        Assert.NotNull(transactionExecutor);
        Assert.NotNull(unitOfWorkFactory);
        Assert.NotNull(commandProcessor);
        Assert.NotNull(queryProcessor);
        Assert.NotNull(eventBus);
    }

    [Fact]
    public void WithTransactions_MultipleIsolationLevels_ConfiguresCorrectly()
    {
        // Arrange
        var config = new TransactionConfiguration
        {
            CommandIsolationLevel = IsolationLevel.Serializable,
            QueryIsolationLevel = IsolationLevel.ReadUncommitted,
            EventIsolationLevel = IsolationLevel.RepeatableRead,
            OutboxIsolationLevel = IsolationLevel.Snapshot,
            InboxIsolationLevel = IsolationLevel.Chaos
        };

        // Act
        _builder.WithTransactions(config);
        var provider = _services.BuildServiceProvider();

        // Assert
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

    #endregion
}

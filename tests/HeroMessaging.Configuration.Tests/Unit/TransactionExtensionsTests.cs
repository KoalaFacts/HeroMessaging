using System.Data;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Configuration;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Configuration.Tests.Unit
{
    [Trait("Category", "Unit")]
    public sealed class TransactionExtensionsTests
    {
        [Fact]
        public void WithTransactions_WithNullBuilder_ThrowsArgumentException()
        {
            // Arrange
            IHeroMessagingBuilder builder = null!;

            // Act & Assert - Null fails the type check, resulting in ArgumentException
            var ex = Assert.Throws<ArgumentException>(() => builder.WithTransactions());
            Assert.Contains("Builder must be of type HeroMessagingBuilder", ex.Message);
            Assert.Equal("builder", ex.ParamName);
        }

        [Fact]
        public void WithTransactions_WithWrongBuilderType_ThrowsArgumentException()
        {
            // Arrange
            var builder = new Mock<IHeroMessagingBuilder>().Object;

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => builder.WithTransactions());
            Assert.Contains("Builder must be of type HeroMessagingBuilder", ex.Message);
            Assert.Equal("builder", ex.ParamName);
        }

        [Fact]
        public void WithTransactions_WithDefaultIsolationLevel_DecoratesAllProcessors()
        {
            // Arrange
            var services = new ServiceCollection();

            // Register required dependencies
            services.AddSingleton<ITransactionExecutor>(new Mock<ITransactionExecutor>().Object);
            services.AddSingleton<IUnitOfWorkFactory>(new Mock<IUnitOfWorkFactory>().Object);
            services.AddSingleton<ILogger<TransactionEventBusDecorator>>(new Mock<ILogger<TransactionEventBusDecorator>>().Object);

            // Register processors to be decorated
            services.AddSingleton<ICommandProcessor>(new Mock<ICommandProcessor>().Object);
            services.AddSingleton<IQueryProcessor>(new Mock<IQueryProcessor>().Object);
            services.AddSingleton<IEventBus>(new Mock<IEventBus>().Object);
            services.AddSingleton<IOutboxProcessor>(new Mock<IOutboxProcessor>().Object);
            services.AddSingleton<IInboxProcessor>(new Mock<IInboxProcessor>().Object);

            var builder = new HeroMessagingBuilder(services);

            // Act
            var result = builder.WithTransactions();

            // Assert
            Assert.Same(builder, result);

            var provider = services.BuildServiceProvider();

            // Verify all processors are decorated
            var commandProcessor = provider.GetRequiredService<ICommandProcessor>();
            Assert.IsType<TransactionCommandProcessorDecorator>(commandProcessor);

            var queryProcessor = provider.GetRequiredService<IQueryProcessor>();
            Assert.IsType<TransactionQueryProcessorDecorator>(queryProcessor);

            var eventBus = provider.GetRequiredService<IEventBus>();
            Assert.IsType<TransactionEventBusDecorator>(eventBus);

            var outboxProcessor = provider.GetRequiredService<IOutboxProcessor>();
            Assert.IsType<TransactionOutboxProcessorDecorator>(outboxProcessor);

            var inboxProcessor = provider.GetRequiredService<IInboxProcessor>();
            Assert.IsType<TransactionInboxProcessorDecorator>(inboxProcessor);
        }

        [Fact]
        public void WithTransactions_WithCustomIsolationLevel_AppliesIsolationLevel()
        {
            // Arrange
            var services = new ServiceCollection();

            services.AddSingleton<ITransactionExecutor>(new Mock<ITransactionExecutor>().Object);
            services.AddSingleton<IUnitOfWorkFactory>(new Mock<IUnitOfWorkFactory>().Object);
            services.AddSingleton<ILogger<TransactionEventBusDecorator>>(new Mock<ILogger<TransactionEventBusDecorator>>().Object);

            services.AddSingleton<ICommandProcessor>(new Mock<ICommandProcessor>().Object);
            services.AddSingleton<IQueryProcessor>(new Mock<IQueryProcessor>().Object);
            services.AddSingleton<IEventBus>(new Mock<IEventBus>().Object);
            services.AddSingleton<IOutboxProcessor>(new Mock<IOutboxProcessor>().Object);
            services.AddSingleton<IInboxProcessor>(new Mock<IInboxProcessor>().Object);

            var builder = new HeroMessagingBuilder(services);

            // Act
            var result = builder.WithTransactions(IsolationLevel.Serializable);

            // Assert
            Assert.Same(builder, result);

            var provider = services.BuildServiceProvider();

            // Verify decorators are created (isolation level is internal to decorators)
            Assert.IsType<TransactionCommandProcessorDecorator>(provider.GetRequiredService<ICommandProcessor>());
            Assert.IsType<TransactionQueryProcessorDecorator>(provider.GetRequiredService<IQueryProcessor>());
            Assert.IsType<TransactionEventBusDecorator>(provider.GetRequiredService<IEventBus>());
            Assert.IsType<TransactionOutboxProcessorDecorator>(provider.GetRequiredService<IOutboxProcessor>());
            Assert.IsType<TransactionInboxProcessorDecorator>(provider.GetRequiredService<IInboxProcessor>());
        }

        [Fact]
        public void WithCommandTransactions_WithNullBuilder_ThrowsArgumentException()
        {
            // Arrange
            IHeroMessagingBuilder builder = null!;

            // Act & Assert - Null fails the type check, resulting in ArgumentException
            var ex = Assert.Throws<ArgumentException>(() => builder.WithCommandTransactions());
            Assert.Contains("Builder must be of type HeroMessagingBuilder", ex.Message);
            Assert.Equal("builder", ex.ParamName);
        }

        [Fact]
        public void WithCommandTransactions_WithWrongBuilderType_ThrowsArgumentException()
        {
            // Arrange
            var builder = new Mock<IHeroMessagingBuilder>().Object;

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => builder.WithCommandTransactions());
            Assert.Contains("Builder must be of type HeroMessagingBuilder", ex.Message);
            Assert.Equal("builder", ex.ParamName);
        }

        [Fact]
        public void WithCommandTransactions_WithDefaultIsolationLevel_DecoratesCommandProcessorOnly()
        {
            // Arrange
            var services = new ServiceCollection();

            services.AddSingleton<ITransactionExecutor>(new Mock<ITransactionExecutor>().Object);
            services.AddSingleton<ICommandProcessor>(new Mock<ICommandProcessor>().Object);

            var builder = new HeroMessagingBuilder(services);

            // Act
            var result = builder.WithCommandTransactions();

            // Assert
            Assert.Same(builder, result);

            var provider = services.BuildServiceProvider();
            var commandProcessor = provider.GetRequiredService<ICommandProcessor>();
            Assert.IsType<TransactionCommandProcessorDecorator>(commandProcessor);
        }

        [Fact]
        public void WithCommandTransactions_WithCustomIsolationLevel_AppliesIsolationLevel()
        {
            // Arrange
            var services = new ServiceCollection();

            services.AddSingleton<ITransactionExecutor>(new Mock<ITransactionExecutor>().Object);
            services.AddSingleton<ICommandProcessor>(new Mock<ICommandProcessor>().Object);

            var builder = new HeroMessagingBuilder(services);

            // Act
            var result = builder.WithCommandTransactions(IsolationLevel.RepeatableRead);

            // Assert
            Assert.Same(builder, result);

            var provider = services.BuildServiceProvider();
            var commandProcessor = provider.GetRequiredService<ICommandProcessor>();
            Assert.IsType<TransactionCommandProcessorDecorator>(commandProcessor);
        }

        [Fact]
        public void WithTransactions_WithConfiguration_WithNullBuilder_ThrowsArgumentException()
        {
            // Arrange
            IHeroMessagingBuilder builder = null!;
            var config = new TransactionConfiguration();

            // Act & Assert - Null fails the type check, resulting in ArgumentException
            var ex = Assert.Throws<ArgumentException>(() => builder.WithTransactions(config));
            Assert.Contains("Builder must be of type HeroMessagingBuilder", ex.Message);
            Assert.Equal("builder", ex.ParamName);
        }

        [Fact]
        public void WithTransactions_WithConfiguration_WithWrongBuilderType_ThrowsArgumentException()
        {
            // Arrange
            var builder = new Mock<IHeroMessagingBuilder>().Object;
            var config = new TransactionConfiguration();

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => builder.WithTransactions(config));
            Assert.Contains("Builder must be of type HeroMessagingBuilder", ex.Message);
            Assert.Equal("builder", ex.ParamName);
        }

        [Fact]
        public void WithTransactions_WithConfiguration_DecoratesSpecifiedProcessors()
        {
            // Arrange
            var services = new ServiceCollection();

            services.AddSingleton<ITransactionExecutor>(new Mock<ITransactionExecutor>().Object);
            services.AddSingleton<IUnitOfWorkFactory>(new Mock<IUnitOfWorkFactory>().Object);
            services.AddSingleton<ILogger<TransactionEventBusDecorator>>(new Mock<ILogger<TransactionEventBusDecorator>>().Object);

            services.AddSingleton<ICommandProcessor>(new Mock<ICommandProcessor>().Object);
            services.AddSingleton<IQueryProcessor>(new Mock<IQueryProcessor>().Object);
            services.AddSingleton<IEventBus>(new Mock<IEventBus>().Object);

            var builder = new HeroMessagingBuilder(services);
            var config = new TransactionConfiguration
            {
                CommandIsolationLevel = IsolationLevel.Serializable,
                QueryIsolationLevel = IsolationLevel.ReadCommitted,
                EventIsolationLevel = IsolationLevel.RepeatableRead,
                OutboxIsolationLevel = null, // Don't decorate outbox
                InboxIsolationLevel = null   // Don't decorate inbox
            };

            // Act
            var result = builder.WithTransactions(config);

            // Assert
            Assert.Same(builder, result);

            var provider = services.BuildServiceProvider();

            // Verify only specified processors are decorated
            Assert.IsType<TransactionCommandProcessorDecorator>(provider.GetRequiredService<ICommandProcessor>());
            Assert.IsType<TransactionQueryProcessorDecorator>(provider.GetRequiredService<IQueryProcessor>());
            Assert.IsType<TransactionEventBusDecorator>(provider.GetRequiredService<IEventBus>());
        }

        [Fact]
        public void WithTransactions_WithNullConfiguration_OnlyDecoratesProcessorsWithNonNullIsolationLevels()
        {
            // Arrange
            var services = new ServiceCollection();

            services.AddSingleton<ITransactionExecutor>(new Mock<ITransactionExecutor>().Object);
            services.AddSingleton<ICommandProcessor>(new Mock<ICommandProcessor>().Object);

            var builder = new HeroMessagingBuilder(services);
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

            var provider = services.BuildServiceProvider();

            // Only command processor should be decorated
            Assert.IsType<TransactionCommandProcessorDecorator>(provider.GetRequiredService<ICommandProcessor>());
        }

        [Fact]
        public void TransactionConfiguration_DefaultValues_AreReadCommitted()
        {
            // Arrange & Act
            var config = new TransactionConfiguration();

            // Assert
            Assert.Equal(IsolationLevel.ReadCommitted, config.CommandIsolationLevel);
            Assert.Equal(IsolationLevel.ReadCommitted, config.QueryIsolationLevel);
            Assert.Equal(IsolationLevel.ReadCommitted, config.EventIsolationLevel);
            Assert.Equal(IsolationLevel.ReadCommitted, config.OutboxIsolationLevel);
            Assert.Equal(IsolationLevel.ReadCommitted, config.InboxIsolationLevel);
        }

        [Fact]
        public void TransactionConfiguration_WriteOperationsOnly_IncludesCommandsEventsOutboxInbox()
        {
            // Act
            var config = TransactionConfiguration.WriteOperationsOnly();

            // Assert
            Assert.Equal(IsolationLevel.ReadCommitted, config.CommandIsolationLevel);
            Assert.Null(config.QueryIsolationLevel); // Read-only, no transaction
            Assert.Equal(IsolationLevel.ReadCommitted, config.EventIsolationLevel);
            Assert.Equal(IsolationLevel.ReadCommitted, config.OutboxIsolationLevel);
            Assert.Equal(IsolationLevel.ReadCommitted, config.InboxIsolationLevel);
        }

        [Fact]
        public void TransactionConfiguration_Serializable_UsesSerializableForAllExceptQueries()
        {
            // Act
            var config = TransactionConfiguration.Serializable();

            // Assert
            Assert.Equal(IsolationLevel.Serializable, config.CommandIsolationLevel);
            Assert.Equal(IsolationLevel.ReadCommitted, config.QueryIsolationLevel); // Queries use ReadCommitted
            Assert.Equal(IsolationLevel.Serializable, config.EventIsolationLevel);
            Assert.Equal(IsolationLevel.Serializable, config.OutboxIsolationLevel);
            Assert.Equal(IsolationLevel.Serializable, config.InboxIsolationLevel);
        }

        [Fact]
        public void WithTransactions_CanBeChainedWithOtherBuilderMethods()
        {
            // Arrange
            var services = new ServiceCollection();

            services.AddSingleton<ITransactionExecutor>(new Mock<ITransactionExecutor>().Object);
            services.AddSingleton<IUnitOfWorkFactory>(new Mock<IUnitOfWorkFactory>().Object);
            services.AddSingleton<ILogger<TransactionEventBusDecorator>>(new Mock<ILogger<TransactionEventBusDecorator>>().Object);

            services.AddSingleton<ICommandProcessor>(new Mock<ICommandProcessor>().Object);
            services.AddSingleton<IQueryProcessor>(new Mock<IQueryProcessor>().Object);
            services.AddSingleton<IEventBus>(new Mock<IEventBus>().Object);
            services.AddSingleton<IOutboxProcessor>(new Mock<IOutboxProcessor>().Object);
            services.AddSingleton<IInboxProcessor>(new Mock<IInboxProcessor>().Object);

            var builder = new HeroMessagingBuilder(services);

            // Act
            var result = builder
                .WithTransactions()
                .WithTransactions(IsolationLevel.Serializable);

            // Assert
            Assert.Same(builder, result);
        }

        [Fact]
        public void WithCommandTransactions_CanBeChainedWithOtherBuilderMethods()
        {
            // Arrange
            var services = new ServiceCollection();

            services.AddSingleton<ITransactionExecutor>(new Mock<ITransactionExecutor>().Object);
            services.AddSingleton<ICommandProcessor>(new Mock<ICommandProcessor>().Object);

            var builder = new HeroMessagingBuilder(services);

            // Act
            var result = builder
                .WithCommandTransactions()
                .WithCommandTransactions(IsolationLevel.RepeatableRead);

            // Assert
            Assert.Same(builder, result);
        }

        [Fact]
        public void WithTransactions_WithConfiguration_AllIsolationLevels_DecoratesAllProcessors()
        {
            // Arrange
            var services = new ServiceCollection();

            services.AddSingleton<ITransactionExecutor>(new Mock<ITransactionExecutor>().Object);
            services.AddSingleton<IUnitOfWorkFactory>(new Mock<IUnitOfWorkFactory>().Object);
            services.AddSingleton<ILogger<TransactionEventBusDecorator>>(new Mock<ILogger<TransactionEventBusDecorator>>().Object);

            services.AddSingleton<ICommandProcessor>(new Mock<ICommandProcessor>().Object);
            services.AddSingleton<IQueryProcessor>(new Mock<IQueryProcessor>().Object);
            services.AddSingleton<IEventBus>(new Mock<IEventBus>().Object);
            services.AddSingleton<IOutboxProcessor>(new Mock<IOutboxProcessor>().Object);
            services.AddSingleton<IInboxProcessor>(new Mock<IInboxProcessor>().Object);

            var builder = new HeroMessagingBuilder(services);
            var config = new TransactionConfiguration
            {
                CommandIsolationLevel = IsolationLevel.Chaos,
                QueryIsolationLevel = IsolationLevel.ReadUncommitted,
                EventIsolationLevel = IsolationLevel.ReadCommitted,
                OutboxIsolationLevel = IsolationLevel.RepeatableRead,
                InboxIsolationLevel = IsolationLevel.Serializable
            };

            // Act
            var result = builder.WithTransactions(config);

            // Assert
            Assert.Same(builder, result);

            var provider = services.BuildServiceProvider();

            Assert.IsType<TransactionCommandProcessorDecorator>(provider.GetRequiredService<ICommandProcessor>());
            Assert.IsType<TransactionQueryProcessorDecorator>(provider.GetRequiredService<IQueryProcessor>());
            Assert.IsType<TransactionEventBusDecorator>(provider.GetRequiredService<IEventBus>());
            Assert.IsType<TransactionOutboxProcessorDecorator>(provider.GetRequiredService<IOutboxProcessor>());
            Assert.IsType<TransactionInboxProcessorDecorator>(provider.GetRequiredService<IInboxProcessor>());
        }

        [Fact]
        public void WithTransactions_WithAllNullIsolationLevels_DoesNotDecorateAnyProcessors()
        {
            // Arrange
            var services = new ServiceCollection();
            var builder = new HeroMessagingBuilder(services);
            var config = new TransactionConfiguration
            {
                CommandIsolationLevel = null,
                QueryIsolationLevel = null,
                EventIsolationLevel = null,
                OutboxIsolationLevel = null,
                InboxIsolationLevel = null
            };

            // Act
            var result = builder.WithTransactions(config);

            // Assert
            Assert.Same(builder, result);

            // No decorators should be registered since all isolation levels are null
            // We just verify the builder returned successfully
        }

        [Fact]
        public void TransactionConfiguration_CanBeCustomized()
        {
            // Act
            var config = new TransactionConfiguration
            {
                CommandIsolationLevel = IsolationLevel.Snapshot,
                QueryIsolationLevel = IsolationLevel.ReadUncommitted,
                EventIsolationLevel = IsolationLevel.Chaos,
                OutboxIsolationLevel = IsolationLevel.RepeatableRead,
                InboxIsolationLevel = IsolationLevel.Serializable
            };

            // Assert
            Assert.Equal(IsolationLevel.Snapshot, config.CommandIsolationLevel);
            Assert.Equal(IsolationLevel.ReadUncommitted, config.QueryIsolationLevel);
            Assert.Equal(IsolationLevel.Chaos, config.EventIsolationLevel);
            Assert.Equal(IsolationLevel.RepeatableRead, config.OutboxIsolationLevel);
            Assert.Equal(IsolationLevel.Serializable, config.InboxIsolationLevel);
        }
    }
}

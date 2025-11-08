using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Configuration;
using HeroMessaging.Idempotency;
using HeroMessaging.Idempotency.Decorators;
using HeroMessaging.Idempotency.KeyGeneration;
using HeroMessaging.Idempotency.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Tests.Unit.Idempotency;

/// <summary>
/// Unit tests for the idempotency configuration API and builder.
/// </summary>
[Trait("Category", "Unit")]
public sealed class IdempotencyBuilderTests
{
    [Fact]
    public void WithIdempotency_WithNullBuilder_ThrowsArgumentNullException()
    {
        // Arrange
        IHeroMessagingBuilder? builder = null;

        // Act & Assert
#pragma warning disable CS8604 // Possible null reference argument - intentional for test
        var exception = Assert.Throws<ArgumentNullException>(() =>
            builder.WithIdempotency());
#pragma warning restore CS8604
        Assert.Equal("builder", exception.ParamName);
    }

    [Fact]
    public void WithIdempotency_WithDefaultConfiguration_RegistersDefaultServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = new TestHeroMessagingBuilder(services);

        // Act
        builder.WithIdempotency();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(serviceProvider.GetService<IIdempotencyStore>());
        Assert.NotNull(serviceProvider.GetService<IIdempotencyKeyGenerator>());
        Assert.NotNull(serviceProvider.GetService<IIdempotencyPolicy>());
        Assert.NotNull(serviceProvider.GetService<TimeProvider>());
        Assert.NotNull(serviceProvider.GetService<Func<Abstractions.Processing.IMessageProcessor, IdempotencyDecorator>>());

        // Verify default types
        Assert.IsType<InMemoryIdempotencyStore>(serviceProvider.GetService<IIdempotencyStore>());
        Assert.IsType<MessageIdKeyGenerator>(serviceProvider.GetService<IIdempotencyKeyGenerator>());
        Assert.IsType<DefaultIdempotencyPolicy>(serviceProvider.GetService<IIdempotencyPolicy>());
    }

    [Fact]
    public void WithIdempotency_WithSuccessTtl_ConfiguresPolicy()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new TestHeroMessagingBuilder(services);
        var expectedTtl = TimeSpan.FromDays(7);

        // Act
        builder.WithIdempotency(config =>
        {
            config.WithSuccessTtl(expectedTtl);
        });
        var serviceProvider = services.BuildServiceProvider();
        var policy = serviceProvider.GetRequiredService<IIdempotencyPolicy>();

        // Assert
        Assert.Equal(expectedTtl, policy.SuccessTtl);
    }

    [Fact]
    public void WithIdempotency_WithFailureTtl_ConfiguresPolicy()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new TestHeroMessagingBuilder(services);
        var expectedTtl = TimeSpan.FromHours(2);

        // Act
        builder.WithIdempotency(config =>
        {
            config.WithFailureTtl(expectedTtl);
        });
        var serviceProvider = services.BuildServiceProvider();
        var policy = serviceProvider.GetRequiredService<IIdempotencyPolicy>();

        // Assert
        Assert.Equal(expectedTtl, policy.FailureTtl);
    }

    [Fact]
    public void WithIdempotency_WithFailureCaching_ConfiguresPolicy()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new TestHeroMessagingBuilder(services);

        // Act
        builder.WithIdempotency(config =>
        {
            config.WithFailureCaching(false);
        });
        var serviceProvider = services.BuildServiceProvider();
        var policy = serviceProvider.GetRequiredService<IIdempotencyPolicy>();

        // Assert
        Assert.False(policy.CacheFailures);
    }

    [Fact]
    public void WithIdempotency_WithMessageIdKeyGenerator_RegistersCorrectType()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new TestHeroMessagingBuilder(services);

        // Act
        builder.WithIdempotency(config =>
        {
            config.UseMessageIdKeyGenerator();
        });
        var serviceProvider = services.BuildServiceProvider();
        var keyGenerator = serviceProvider.GetRequiredService<IIdempotencyKeyGenerator>();

        // Assert
        Assert.IsType<MessageIdKeyGenerator>(keyGenerator);
    }

    [Fact]
    public void WithIdempotency_WithCustomKeyGenerator_RegistersCorrectType()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new TestHeroMessagingBuilder(services);

        // Act
        builder.WithIdempotency(config =>
        {
            config.UseKeyGenerator<CustomKeyGenerator>();
        });
        var serviceProvider = services.BuildServiceProvider();
        var keyGenerator = serviceProvider.GetRequiredService<IIdempotencyKeyGenerator>();

        // Assert
        Assert.IsType<CustomKeyGenerator>(keyGenerator);
    }

    [Fact]
    public void WithIdempotency_WithInMemoryStore_RegistersCorrectType()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new TestHeroMessagingBuilder(services);

        // Act
        builder.WithIdempotency(config =>
        {
            config.UseInMemoryStore();
        });
        var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<IIdempotencyStore>();

        // Assert
        Assert.IsType<InMemoryIdempotencyStore>(store);
    }

    [Fact]
    public void WithIdempotency_WithCustomStore_RegistersCorrectType()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new TestHeroMessagingBuilder(services);

        // Act
        builder.WithIdempotency(config =>
        {
            config.UseStore<CustomIdempotencyStore>();
        });
        var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<IIdempotencyStore>();

        // Assert
        Assert.IsType<CustomIdempotencyStore>(store);
    }

    [Fact]
    public void WithIdempotency_WithInvalidSuccessTtl_ThrowsArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new TestHeroMessagingBuilder(services);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
        {
            builder.WithIdempotency(config =>
            {
                config.WithSuccessTtl(TimeSpan.Zero);
            });
        });
        Assert.Contains("Success TTL must be greater than zero", exception.Message);
    }

    [Fact]
    public void WithIdempotency_WithNegativeSuccessTtl_ThrowsArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new TestHeroMessagingBuilder(services);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
        {
            builder.WithIdempotency(config =>
            {
                config.WithSuccessTtl(TimeSpan.FromSeconds(-1));
            });
        });
        Assert.Contains("Success TTL must be greater than zero", exception.Message);
    }

    [Fact]
    public void WithIdempotency_WithInvalidFailureTtl_ThrowsArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new TestHeroMessagingBuilder(services);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
        {
            builder.WithIdempotency(config =>
            {
                config.WithFailureTtl(TimeSpan.Zero);
            });
        });
        Assert.Contains("Failure TTL must be greater than zero", exception.Message);
    }

    [Fact]
    public void WithIdempotency_WithCompleteConfiguration_RegistersAllServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new TestHeroMessagingBuilder(services);

        // Act
        builder.WithIdempotency(config =>
        {
            config
                .UseInMemoryStore()
                .UseMessageIdKeyGenerator()
                .WithSuccessTtl(TimeSpan.FromDays(7))
                .WithFailureTtl(TimeSpan.FromHours(2))
                .WithFailureCaching(true);
        });
        var serviceProvider = services.BuildServiceProvider();

        // Assert - all services registered
        var store = serviceProvider.GetRequiredService<IIdempotencyStore>();
        var keyGenerator = serviceProvider.GetRequiredService<IIdempotencyKeyGenerator>();
        var policy = serviceProvider.GetRequiredService<IIdempotencyPolicy>();
        var timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
        var decoratorFactory = serviceProvider.GetRequiredService<Func<Abstractions.Processing.IMessageProcessor, IdempotencyDecorator>>();

        Assert.NotNull(store);
        Assert.NotNull(keyGenerator);
        Assert.NotNull(policy);
        Assert.NotNull(timeProvider);
        Assert.NotNull(decoratorFactory);

        // Assert - correct types
        Assert.IsType<InMemoryIdempotencyStore>(store);
        Assert.IsType<MessageIdKeyGenerator>(keyGenerator);
        Assert.IsType<DefaultIdempotencyPolicy>(policy);

        // Assert - correct configuration
        Assert.Equal(TimeSpan.FromDays(7), policy.SuccessTtl);
        Assert.Equal(TimeSpan.FromHours(2), policy.FailureTtl);
        Assert.True(policy.CacheFailures);
    }

    [Fact]
    public void WithIdempotency_CalledMultipleTimes_KeepsFirstRegistration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new TestHeroMessagingBuilder(services);

        // Act
        builder.WithIdempotency(config =>
        {
            config.WithSuccessTtl(TimeSpan.FromDays(7));
        });

        builder.WithIdempotency(config =>
        {
            config.WithSuccessTtl(TimeSpan.FromDays(30));
        });

        var serviceProvider = services.BuildServiceProvider();
        var policy = serviceProvider.GetRequiredService<IIdempotencyPolicy>();

        // Assert - first registration wins (TryAddSingleton behavior)
        Assert.Equal(TimeSpan.FromDays(7), policy.SuccessTtl);
    }

    [Fact]
    public void WithIdempotency_DecoratorFactory_CreatesValidDecorator()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new TestHeroMessagingBuilder(services);

        builder.WithIdempotency();
        var serviceProvider = services.BuildServiceProvider();

        var decoratorFactory = serviceProvider.GetRequiredService<Func<Abstractions.Processing.IMessageProcessor, IdempotencyDecorator>>();
        var mockProcessor = new TestMessageProcessor();

        // Act
        var decorator = decoratorFactory(mockProcessor);

        // Assert
        Assert.NotNull(decorator);
        Assert.IsType<IdempotencyDecorator>(decorator);
    }

    // Test helper classes

    private sealed class TestHeroMessagingBuilder : IHeroMessagingBuilder
    {
        private readonly IServiceCollection _services;

        public TestHeroMessagingBuilder(IServiceCollection services)
        {
            _services = services;
        }

        public IServiceCollection Services => _services;

        public IServiceCollection Build() => _services;

        public IHeroMessagingBuilder WithMediator() => this;
        public IHeroMessagingBuilder WithEventBus() => this;
        public IHeroMessagingBuilder WithQueues() => this;
        public IHeroMessagingBuilder WithOutbox() => this;
        public IHeroMessagingBuilder WithInbox() => this;
        public IHeroMessagingBuilder WithErrorHandling() => this;
        public IHeroMessagingBuilder UseInMemoryStorage() => this;
        public IHeroMessagingBuilder UseStorage<TStorage>() where TStorage : class, Abstractions.Storage.IMessageStorage => this;
        public IHeroMessagingBuilder UseStorage(Abstractions.Storage.IMessageStorage storage) => this;
        public IHeroMessagingBuilder ScanAssembly(System.Reflection.Assembly assembly) => this;
        public IHeroMessagingBuilder ScanAssemblies(params System.Reflection.Assembly[] assemblies) => this;
        public IHeroMessagingBuilder ScanAssemblies(params IEnumerable<System.Reflection.Assembly> assemblies) => this;
        public IHeroMessagingBuilder ConfigureProcessing(Action<ProcessingOptions> configure) => this;
        public IHeroMessagingBuilder AddPlugin<TPlugin>() where TPlugin : class, Abstractions.Plugins.IMessagingPlugin => this;
        public IHeroMessagingBuilder AddPlugin(Abstractions.Plugins.IMessagingPlugin plugin) => this;
        public IHeroMessagingBuilder AddPlugin<TPlugin>(Action<TPlugin> configure) where TPlugin : class, Abstractions.Plugins.IMessagingPlugin => this;
        public IHeroMessagingBuilder DiscoverPlugins() => this;
        public IHeroMessagingBuilder DiscoverPlugins(string directory) => this;
        public IHeroMessagingBuilder DiscoverPlugins(System.Reflection.Assembly assembly) => this;
        public IHeroMessagingBuilder Development() => this;
        public IHeroMessagingBuilder Production(string connectionString) => this;
        public IHeroMessagingBuilder Microservice(string connectionString) => this;
    }

    private sealed class CustomKeyGenerator : IIdempotencyKeyGenerator
    {
        public string GenerateKey(Abstractions.Messages.IMessage message, Abstractions.Processing.ProcessingContext context)
        {
            return $"custom:{message.MessageId}";
        }
    }

    private sealed class CustomIdempotencyStore : IIdempotencyStore
    {
        public ValueTask<IdempotencyResponse?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IdempotencyResponse?>(null);
        }

        public ValueTask StoreSuccessAsync(string key, object? result, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask StoreFailureAsync(string key, Exception exception, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(false);
        }

        public ValueTask<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(0);
        }
    }

    private sealed class TestMessageProcessor : Abstractions.Processing.IMessageProcessor
    {
        public ValueTask<Abstractions.Processing.ProcessingResult> ProcessAsync(
            Abstractions.Messages.IMessage message,
            Abstractions.Processing.ProcessingContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Abstractions.Processing.ProcessingResult.Successful());
        }
    }
}

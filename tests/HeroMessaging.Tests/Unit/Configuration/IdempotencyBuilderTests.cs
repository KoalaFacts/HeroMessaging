using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Configuration;
using HeroMessaging.Idempotency;
using HeroMessaging.Idempotency.Decorators;
using HeroMessaging.Idempotency.KeyGeneration;
using HeroMessaging.Idempotency.Storage;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Configuration;

/// <summary>
/// Unit tests for IdempotencyBuilder and related extensions.
/// </summary>
[Trait("Category", "Unit")]
public class IdempotencyBuilderTests
{
    private readonly ServiceCollection _services;
    private readonly HeroMessagingBuilder _builder;

    public IdempotencyBuilderTests()
    {
        _services = new ServiceCollection();
        _services.AddLogging();
        _builder = new HeroMessagingBuilder(_services);
    }

    #region WithIdempotency Extension Tests

    [Fact]
    public void WithIdempotency_WithNullBuilder_ThrowsArgumentNullException()
    {
        // Arrange
        IHeroMessagingBuilder? builder = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => builder!.WithIdempotency());
        Assert.Equal("builder", exception.ParamName);
    }

    [Fact]
    public void WithIdempotency_WithDefaultConfiguration_RegistersDefaultServices()
    {
        // Act
        _builder.WithIdempotency();
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var store = provider.GetService<IIdempotencyStore>();
        var keyGenerator = provider.GetService<IIdempotencyKeyGenerator>();
        var policy = provider.GetService<IIdempotencyPolicy>();

        Assert.NotNull(store);
        Assert.NotNull(keyGenerator);
        Assert.NotNull(policy);
        Assert.IsType<InMemoryIdempotencyStore>(store);
        Assert.IsType<MessageIdKeyGenerator>(keyGenerator);
        Assert.IsType<DefaultIdempotencyPolicy>(policy);
    }

    [Fact]
    public void WithIdempotency_RegistersTimeProvider()
    {
        // Act
        _builder.WithIdempotency();
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var timeProvider = provider.GetService<TimeProvider>();
        Assert.NotNull(timeProvider);
    }

    [Fact]
    public void WithIdempotency_RegistersIdempotencyDecoratorFactory()
    {
        // Act
        _builder.WithIdempotency();
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var factory = provider.GetService<Func<IMessageProcessor, IdempotencyDecorator>>();
        Assert.NotNull(factory);
    }

    [Fact]
    public void WithIdempotency_ReturnsSameBuilder_ForMethodChaining()
    {
        // Act
        var result = _builder.WithIdempotency();

        // Assert
        Assert.Same(_builder, result);
    }

    #endregion

    #region WithSuccessTtl Tests

    [Fact]
    public void WithSuccessTtl_WithValidValue_ConfiguresPolicy()
    {
        // Act
        _builder.WithIdempotency(idempotency =>
            idempotency.WithSuccessTtl(TimeSpan.FromDays(7)));
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var policy = provider.GetRequiredService<IIdempotencyPolicy>();
        Assert.NotNull(policy);
        Assert.IsType<DefaultIdempotencyPolicy>(policy);
    }

    [Fact]
    public void WithSuccessTtl_WithZero_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _builder.WithIdempotency(idempotency => idempotency.WithSuccessTtl(TimeSpan.Zero)));
        Assert.Contains("Success TTL must be greater than zero", exception.Message);
    }

    [Fact]
    public void WithSuccessTtl_WithNegativeValue_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _builder.WithIdempotency(idempotency => idempotency.WithSuccessTtl(TimeSpan.FromHours(-1))));
        Assert.Contains("Success TTL must be greater than zero", exception.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(168)]
    public void WithSuccessTtl_WithVariousValidValues_DoesNotThrow(int hours)
    {
        // Act & Assert - Should not throw
        _builder.WithIdempotency(idempotency =>
            idempotency.WithSuccessTtl(TimeSpan.FromHours(hours)));
    }

    #endregion

    #region WithFailureTtl Tests

    [Fact]
    public void WithFailureTtl_WithValidValue_ConfiguresPolicy()
    {
        // Act
        _builder.WithIdempotency(idempotency =>
            idempotency.WithFailureTtl(TimeSpan.FromHours(2)));
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var policy = provider.GetRequiredService<IIdempotencyPolicy>();
        Assert.NotNull(policy);
    }

    [Fact]
    public void WithFailureTtl_WithZero_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _builder.WithIdempotency(idempotency => idempotency.WithFailureTtl(TimeSpan.Zero)));
        Assert.Contains("Failure TTL must be greater than zero", exception.Message);
    }

    [Fact]
    public void WithFailureTtl_WithNegativeValue_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _builder.WithIdempotency(idempotency => idempotency.WithFailureTtl(TimeSpan.FromMinutes(-30))));
        Assert.Contains("Failure TTL must be greater than zero", exception.Message);
    }

    #endregion

    #region WithFailureCaching Tests

    [Fact]
    public void WithFailureCaching_WithTrue_EnablesFailureCaching()
    {
        // Act
        _builder.WithIdempotency(idempotency =>
            idempotency.WithFailureCaching(true));
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var policy = provider.GetRequiredService<IIdempotencyPolicy>();
        Assert.NotNull(policy);
    }

    [Fact]
    public void WithFailureCaching_WithFalse_DisablesFailureCaching()
    {
        // Act
        _builder.WithIdempotency(idempotency =>
            idempotency.WithFailureCaching(false));
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var policy = provider.GetRequiredService<IIdempotencyPolicy>();
        Assert.NotNull(policy);
    }

    #endregion

    #region UseMessageIdKeyGenerator Tests

    [Fact]
    public void UseMessageIdKeyGenerator_RegistersMessageIdKeyGenerator()
    {
        // Act
        _builder.WithIdempotency(idempotency =>
            idempotency.UseMessageIdKeyGenerator());
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var keyGenerator = provider.GetRequiredService<IIdempotencyKeyGenerator>();
        Assert.IsType<MessageIdKeyGenerator>(keyGenerator);
    }

    #endregion

    #region UseKeyGenerator Tests

    [Fact]
    public void UseKeyGenerator_WithCustomType_RegistersCustomKeyGenerator()
    {
        // Act
        _builder.WithIdempotency(idempotency =>
            idempotency.UseKeyGenerator<TestKeyGenerator>());
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var keyGenerator = provider.GetRequiredService<IIdempotencyKeyGenerator>();
        Assert.IsType<TestKeyGenerator>(keyGenerator);
    }

    #endregion

    #region UseInMemoryStore Tests

    [Fact]
    public void UseInMemoryStore_RegistersInMemoryIdempotencyStore()
    {
        // Act
        _builder.WithIdempotency(idempotency =>
            idempotency.UseInMemoryStore());
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var store = provider.GetRequiredService<IIdempotencyStore>();
        Assert.IsType<InMemoryIdempotencyStore>(store);
    }

    #endregion

    #region UseStore Tests

    [Fact]
    public void UseStore_WithCustomType_RegistersCustomStore()
    {
        // Act
        _builder.WithIdempotency(idempotency =>
            idempotency.UseStore<TestIdempotencyStore>());
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var store = provider.GetRequiredService<IIdempotencyStore>();
        Assert.IsType<TestIdempotencyStore>(store);
    }

    #endregion

    #region Method Chaining Tests

    [Fact]
    public void AllMethods_SupportFluentChaining()
    {
        // Act & Assert - Should not throw
        _builder.WithIdempotency(idempotency =>
        {
            idempotency
                .WithSuccessTtl(TimeSpan.FromDays(7))
                .WithFailureTtl(TimeSpan.FromHours(1))
                .WithFailureCaching(true)
                .UseMessageIdKeyGenerator()
                .UseInMemoryStore();
        });
    }

    [Fact]
    public void CustomTypeMethods_SupportFluentChaining()
    {
        // Act & Assert - Should not throw
        _builder.WithIdempotency(idempotency =>
        {
            idempotency
                .WithSuccessTtl(TimeSpan.FromDays(7))
                .UseKeyGenerator<TestKeyGenerator>()
                .UseStore<TestIdempotencyStore>();
        });
    }

    #endregion

    #region IdempotencyDecorator Factory Tests

    [Fact]
    public void IdempotencyDecoratorFactory_CreatesDecoratorWithConfiguredServices()
    {
        // Arrange
        var innerProcessor = new Mock<IMessageProcessor>().Object;

        // Act
        _builder.WithIdempotency(idempotency =>
        {
            idempotency
                .WithSuccessTtl(TimeSpan.FromDays(1))
                .UseInMemoryStore();
        });
        _builder.Build();
        var provider = _services.BuildServiceProvider();
        var factory = provider.GetRequiredService<Func<IMessageProcessor, IdempotencyDecorator>>();
        var decorator = factory(innerProcessor);

        // Assert
        Assert.NotNull(decorator);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void WithIdempotency_WithFullConfiguration_RegistersAllServices()
    {
        // Act
        _builder.WithIdempotency(idempotency =>
        {
            idempotency
                .WithSuccessTtl(TimeSpan.FromDays(7))
                .WithFailureTtl(TimeSpan.FromHours(2))
                .WithFailureCaching(true)
                .UseInMemoryStore()
                .UseMessageIdKeyGenerator();
        });
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert - All services should be registered and resolvable
        Assert.NotNull(provider.GetRequiredService<IIdempotencyStore>());
        Assert.NotNull(provider.GetRequiredService<IIdempotencyKeyGenerator>());
        Assert.NotNull(provider.GetRequiredService<IIdempotencyPolicy>());
        Assert.NotNull(provider.GetRequiredService<TimeProvider>());
        Assert.NotNull(provider.GetRequiredService<Func<IMessageProcessor, IdempotencyDecorator>>());
    }

    #endregion

    #region Test Doubles

    private sealed class TestKeyGenerator : IIdempotencyKeyGenerator
    {
        public string GenerateKey(IMessage message, ProcessingContext context)
        {
            return $"test-key-{message.MessageId}";
        }
    }

    private sealed class TestIdempotencyStore : IIdempotencyStore
    {
        public ValueTask<IdempotencyResponse?> GetAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IdempotencyResponse?>(null);
        }

        public ValueTask StoreSuccessAsync(string idempotencyKey, object? result, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask StoreFailureAsync(string idempotencyKey, Exception exception, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> ExistsAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(false);
        }

        public ValueTask<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(0);
        }
    }

    #endregion
}

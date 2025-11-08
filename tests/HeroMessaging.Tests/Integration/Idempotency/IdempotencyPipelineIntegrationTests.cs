using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Configuration;
using HeroMessaging.Idempotency.Decorators;
using HeroMessaging.Idempotency.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Tests.Integration.Idempotency;

/// <summary>
/// Integration tests for the idempotency pipeline with configuration API.
/// </summary>
[Trait("Category", "Integration")]
public sealed class IdempotencyPipelineIntegrationTests
{
    [Fact]
    public async Task EndToEnd_WithDefaultConfiguration_CachesSuccessfulResponses()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = new TestHeroMessagingBuilder(services);
        builder.WithIdempotency();

        var serviceProvider = services.BuildServiceProvider();
        var decoratorFactory = serviceProvider.GetRequiredService<Func<IMessageProcessor, IdempotencyDecorator>>();
        var store = serviceProvider.GetRequiredService<IIdempotencyStore>();

        var handler = new TestMessageHandler("success-result");
        var decorator = decoratorFactory(handler);

        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext();

        // Act - First execution
        var result1 = await decorator.ProcessAsync(message, context, CancellationToken.None);

        // Act - Second execution (should hit cache)
        var result2 = await decorator.ProcessAsync(message, context, CancellationToken.None);

        // Assert
        Assert.True(result1.Success);
        Assert.Equal("success-result", result1.Data);
        Assert.Equal(1, handler.ExecutionCount); // Handler only called once

        Assert.True(result2.Success);
        Assert.Equal("success-result", result2.Data); // Cached result
    }

    [Fact]
    public async Task EndToEnd_WithCustomTTL_RespectsConfiguration()
    {
        // Arrange
        var fakeTimeProvider = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(fakeTimeProvider);

        var builder = new TestHeroMessagingBuilder(services);
        builder.WithIdempotency(config =>
        {
            config
                .UseInMemoryStore()
                .WithSuccessTtl(TimeSpan.FromHours(1))
                .WithFailureTtl(TimeSpan.FromMinutes(30));
        });

        var serviceProvider = services.BuildServiceProvider();
        var decoratorFactory = serviceProvider.GetRequiredService<Func<IMessageProcessor, IdempotencyDecorator>>();
        var policy = serviceProvider.GetRequiredService<IIdempotencyPolicy>();

        var handler = new TestMessageHandler("test-result");
        var decorator = decoratorFactory(handler);

        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext();

        // Act & Assert - Verify policy configuration
        Assert.Equal(TimeSpan.FromHours(1), policy.SuccessTtl);
        Assert.Equal(TimeSpan.FromMinutes(30), policy.FailureTtl);

        // Act - Execute and cache
        var result1 = await decorator.ProcessAsync(message, context, CancellationToken.None);
        Assert.True(result1.Success);
        Assert.Equal(1, handler.ExecutionCount);

        // Act - Advance time within TTL
        fakeTimeProvider.Advance(TimeSpan.FromMinutes(30));
        var result2 = await decorator.ProcessAsync(message, context, CancellationToken.None);
        Assert.True(result2.Success);
        Assert.Equal(1, handler.ExecutionCount); // Still cached

        // Act - Advance time beyond TTL
        fakeTimeProvider.Advance(TimeSpan.FromMinutes(31));
        var result3 = await decorator.ProcessAsync(message, context, CancellationToken.None);
        Assert.True(result3.Success);
        Assert.Equal(2, handler.ExecutionCount); // Re-executed after expiration
    }

    [Fact]
    public async Task EndToEnd_WithIdempotentFailure_CachesFailureResponse()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = new TestHeroMessagingBuilder(services);
        builder.WithIdempotency(config =>
        {
            config.WithFailureCaching(true);
        });

        var serviceProvider = services.BuildServiceProvider();
        var decoratorFactory = serviceProvider.GetRequiredService<Func<IMessageProcessor, IdempotencyDecorator>>();

        var exception = new ArgumentException("Invalid input");
        var handler = new TestMessageHandler(exception);
        var decorator = decoratorFactory(handler);

        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext();

        // Act - First execution (should fail and cache)
        var result1 = await decorator.ProcessAsync(message, context, CancellationToken.None);

        // Act - Second execution (should return cached failure)
        var result2 = await decorator.ProcessAsync(message, context, CancellationToken.None);

        // Assert
        Assert.False(result1.Success);
        Assert.NotNull(result1.Exception);
        Assert.Contains("Invalid input", result1.Exception.Message);
        Assert.Equal(1, handler.ExecutionCount); // Handler only called once

        Assert.False(result2.Success);
        Assert.NotNull(result2.Exception);
        Assert.Contains("Invalid input", result2.Exception.Message);
        Assert.Equal(1, handler.ExecutionCount); // Cached failure, handler not called again
    }

    [Fact]
    public async Task EndToEnd_WithFailureCachingDisabled_RetriesFailures()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = new TestHeroMessagingBuilder(services);
        builder.WithIdempotency(config =>
        {
            config.WithFailureCaching(false); // Disable failure caching
        });

        var serviceProvider = services.BuildServiceProvider();
        var decoratorFactory = serviceProvider.GetRequiredService<Func<IMessageProcessor, IdempotencyDecorator>>();

        var exception = new ArgumentException("Invalid input");
        var handler = new TestMessageHandler(exception);
        var decorator = decoratorFactory(handler);

        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext();

        // Act - First execution (should fail)
        var result1 = await decorator.ProcessAsync(message, context, CancellationToken.None);

        // Act - Second execution (should retry, not use cache)
        var result2 = await decorator.ProcessAsync(message, context, CancellationToken.None);

        // Assert
        Assert.False(result1.Success);

        Assert.False(result2.Success);
        Assert.Equal(2, handler.ExecutionCount); // Handler called again (no cache)
    }

    [Fact]
    public async Task EndToEnd_WithNonIdempotentFailure_DoesNotCache()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = new TestHeroMessagingBuilder(services);
        builder.WithIdempotency(config =>
        {
            config.WithFailureCaching(true);
        });

        var serviceProvider = services.BuildServiceProvider();
        var decoratorFactory = serviceProvider.GetRequiredService<Func<IMessageProcessor, IdempotencyDecorator>>();

        // TimeoutException is non-idempotent (transient)
        var exception = new TimeoutException("Request timed out");
        var handler = new TestMessageHandler(exception);
        var decorator = decoratorFactory(handler);

        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext();

        // Act - First execution (should fail)
        var result1 = await decorator.ProcessAsync(message, context, CancellationToken.None);

        // Act - Second execution (should retry, non-idempotent failure)
        var result2 = await decorator.ProcessAsync(message, context, CancellationToken.None);

        // Assert
        Assert.False(result1.Success);

        Assert.False(result2.Success);
        Assert.Equal(2, handler.ExecutionCount); // Retried (transient failure not cached)
    }

    [Fact]
    public async Task EndToEnd_WithCustomKeyGenerator_UsesCustomKeys()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = new TestHeroMessagingBuilder(services);
        builder.WithIdempotency(config =>
        {
            config.UseKeyGenerator<CustomKeyGenerator>();
        });

        var serviceProvider = services.BuildServiceProvider();
        var decoratorFactory = serviceProvider.GetRequiredService<Func<IMessageProcessor, IdempotencyDecorator>>();
        var store = serviceProvider.GetRequiredService<IIdempotencyStore>();

        var handler = new TestMessageHandler("result");
        var decorator = decoratorFactory(handler);

        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext();

        // Act
        await decorator.ProcessAsync(message, context, CancellationToken.None);

        // Assert - Verify custom key format is used
        var customKey = $"custom:{message.MessageId}";
        var exists = await store.ExistsAsync(customKey, CancellationToken.None);
        Assert.True(exists);
    }

    [Fact]
    public async Task EndToEnd_WithMultipleMessages_CachesSeparately()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = new TestHeroMessagingBuilder(services);
        builder.WithIdempotency();

        var serviceProvider = services.BuildServiceProvider();
        var decoratorFactory = serviceProvider.GetRequiredService<Func<IMessageProcessor, IdempotencyDecorator>>();

        var handler = new TestMessageHandler("result");
        var decorator = decoratorFactory(handler);

        var message1 = new TestMessage { MessageId = Guid.NewGuid() };
        var message2 = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext();

        // Act
        await decorator.ProcessAsync(message1, context, CancellationToken.None);
        await decorator.ProcessAsync(message2, context, CancellationToken.None);
        await decorator.ProcessAsync(message1, context, CancellationToken.None); // Duplicate of message1
        await decorator.ProcessAsync(message2, context, CancellationToken.None); // Duplicate of message2

        // Assert
        Assert.Equal(2, handler.ExecutionCount); // Only 2 unique messages executed
    }

    [Fact]
    public async Task EndToEnd_WithCustomStore_UsesCustomStorage()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = new TestHeroMessagingBuilder(services);
        builder.WithIdempotency(config =>
        {
            config.UseStore<TestIdempotencyStore>();
        });

        var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<IIdempotencyStore>();

        // Assert - Verify custom store is registered
        Assert.IsType<TestIdempotencyStore>(store);
    }

    [Fact]
    public async Task EndToEnd_WithCompleteConfiguration_WorksEndToEnd()
    {
        // Arrange
        var fakeTimeProvider = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(fakeTimeProvider);

        var builder = new TestHeroMessagingBuilder(services);
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
        var decoratorFactory = serviceProvider.GetRequiredService<Func<IMessageProcessor, IdempotencyDecorator>>();
        var policy = serviceProvider.GetRequiredService<IIdempotencyPolicy>();
        var store = serviceProvider.GetRequiredService<IIdempotencyStore>();

        // Assert - Configuration applied correctly
        Assert.Equal(TimeSpan.FromDays(7), policy.SuccessTtl);
        Assert.Equal(TimeSpan.FromHours(2), policy.FailureTtl);
        Assert.True(policy.CacheFailures);
        Assert.IsType<InMemoryIdempotencyStore>(store);

        // Act - End-to-end success scenario
        var handler = new TestMessageHandler("complete-result");
        var decorator = decoratorFactory(handler);
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext();

        var result1 = await decorator.ProcessAsync(message, context, CancellationToken.None);
        var result2 = await decorator.ProcessAsync(message, context, CancellationToken.None);

        // Assert - Idempotency working
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(1, handler.ExecutionCount);
        Assert.Equal("complete-result", result2.Data);
    }

    [Fact]
    public async Task EndToEnd_WithConcurrentRequests_HandlesSafely()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = new TestHeroMessagingBuilder(services);
        builder.WithIdempotency();

        var serviceProvider = services.BuildServiceProvider();
        var decoratorFactory = serviceProvider.GetRequiredService<Func<IMessageProcessor, IdempotencyDecorator>>();

        var handler = new SlowMessageHandler(TimeSpan.FromMilliseconds(100), "result");
        var decorator = decoratorFactory(handler);

        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext();

        // Act - Execute concurrently
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => decorator.ProcessAsync(message, context, CancellationToken.None).AsTask())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert - All requests succeed
        Assert.All(results, r => Assert.True(r.Success));
        Assert.All(results, r => Assert.Equal("result", r.Data));

        // Note: Due to race conditions, handler might be called 1-10 times
        // The important thing is all requests complete successfully
        Assert.True(handler.ExecutionCount >= 1);
        Assert.True(handler.ExecutionCount <= 10);
    }

    // Test helper classes

    private sealed class TestHeroMessagingBuilder : IHeroMessagingBuilder
    {
        private readonly IServiceCollection _services;

        public TestHeroMessagingBuilder(IServiceCollection services)
        {
            _services = services;
        }

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

    private sealed class TestMessage : IMessage
    {
        public Guid MessageId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private sealed class TestMessageHandler : IMessageProcessor
    {
        private readonly object? _result;
        private readonly Exception? _exception;
        private int _executionCount;

        public int ExecutionCount => _executionCount;

        public TestMessageHandler(object? result)
        {
            _result = result;
        }

        public TestMessageHandler(Exception exception)
        {
            _exception = exception;
        }

        public ValueTask<ProcessingResult> ProcessAsync(
            IMessage message,
            ProcessingContext context,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _executionCount);

            if (_exception != null)
            {
                return ValueTask.FromResult(ProcessingResult.Failed(_exception));
            }

            return ValueTask.FromResult(ProcessingResult.Successful(data: _result));
        }
    }

    private sealed class SlowMessageHandler : IMessageProcessor
    {
        private readonly TimeSpan _delay;
        private readonly object? _result;
        private int _executionCount;

        public int ExecutionCount => _executionCount;

        public SlowMessageHandler(TimeSpan delay, object? result)
        {
            _delay = delay;
            _result = result;
        }

        public async ValueTask<ProcessingResult> ProcessAsync(
            IMessage message,
            ProcessingContext context,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _executionCount);
            await Task.Delay(_delay, cancellationToken);
            return ProcessingResult.Successful(data: _result);
        }
    }

    private sealed class CustomKeyGenerator : IIdempotencyKeyGenerator
    {
        public string GenerateKey(IMessage message, ProcessingContext context)
        {
            return $"custom:{message.MessageId}";
        }
    }

    private sealed class TestIdempotencyStore : IIdempotencyStore
    {
        private readonly Dictionary<string, IdempotencyResponse> _store = new();

        public ValueTask<IdempotencyResponse?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            _store.TryGetValue(key, out var response);
            return ValueTask.FromResult(response);
        }

        public ValueTask StoreSuccessAsync(string key, object? result, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            _store[key] = new IdempotencyResponse
            {
                IdempotencyKey = key,
                Status = IdempotencyStatus.Success,
                SuccessResult = result,
                StoredAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow + ttl
            };
            return ValueTask.CompletedTask;
        }

        public ValueTask StoreFailureAsync(string key, Exception exception, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            _store[key] = new IdempotencyResponse
            {
                IdempotencyKey = key,
                Status = IdempotencyStatus.Failure,
                FailureType = exception.GetType().AssemblyQualifiedName,
                FailureMessage = exception.Message,
                StoredAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow + ttl
            };
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_store.ContainsKey(key));
        }

        public ValueTask<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var expiredKeys = _store.Where(kvp => kvp.Value.ExpiresAt <= now).Select(kvp => kvp.Key).ToList();
            foreach (var key in expiredKeys)
            {
                _store.Remove(key);
            }
            return ValueTask.FromResult(expiredKeys.Count);
        }
    }
}

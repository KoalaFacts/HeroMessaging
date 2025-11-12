using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Configuration;
using HeroMessaging.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Configuration.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ResilienceExtensionsTests
{
    [Fact]
    public void WithConnectionResilience_WithNullBuilder_ThrowsArgumentException()
    {
        // Arrange
        IHeroMessagingBuilder builder = null!;

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => builder.WithConnectionResilience());
        Assert.Equal("builder", ex.ParamName);
    }

    [Fact]
    public void WithConnectionResilience_WithInvalidBuilderType_ThrowsArgumentException()
    {
        // Arrange
        var mockBuilder = new Mock<IHeroMessagingBuilder>();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => mockBuilder.Object.WithConnectionResilience());
        Assert.Equal("builder", ex.ParamName);
        Assert.Contains("Builder must be of type HeroMessagingBuilder", ex.Message);
    }

    [Fact]
    public void WithConnectionResilience_WithDefaultOptions_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.WithConnectionResilience();

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(IConnectionResiliencePolicy));
    }

    [Fact]
    public void WithConnectionResilience_WithCustomOptions_AppliesOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        var builder = new HeroMessagingBuilder(services);

        var options = new ConnectionResilienceOptions
        {
            MaxRetries = 5,
            BaseRetryDelay = TimeSpan.FromSeconds(2),
            MaxRetryDelay = TimeSpan.FromMinutes(1)
        };

        // Act
        var result = builder.WithConnectionResilience(options);

        // Assert
        Assert.Same(builder, result);
        var provider = services.BuildServiceProvider();
        var policy = provider.GetService<IConnectionResiliencePolicy>();
        Assert.NotNull(policy);
    }

    [Fact]
    public void WithConnectionResilience_WithConfigurationAction_AppliesConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithConnectionResilience(options =>
        {
            options.MaxRetries = 10;
            options.BaseRetryDelay = TimeSpan.FromMilliseconds(500);
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var policy = provider.GetService<IConnectionResiliencePolicy>();
        Assert.NotNull(policy);
    }

    [Fact]
    public void WithHighAvailabilityResilience_RegistersAggressiveOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.WithHighAvailabilityResilience();

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(IConnectionResiliencePolicy));
    }

    [Fact]
    public void WithDevelopmentResilience_RegistersConservativeOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.WithDevelopmentResilience();

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(IConnectionResiliencePolicy));
    }

    [Fact]
    public void WithWriteOnlyResilience_WithNullBuilder_ThrowsArgumentException()
    {
        // Arrange
        IHeroMessagingBuilder builder = null!;

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => builder.WithWriteOnlyResilience());
        Assert.Equal("builder", ex.ParamName);
    }

    [Fact]
    public void WithWriteOnlyResilience_WithInvalidBuilderType_ThrowsArgumentException()
    {
        // Arrange
        var mockBuilder = new Mock<IHeroMessagingBuilder>();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => mockBuilder.Object.WithWriteOnlyResilience());
        Assert.Equal("builder", ex.ParamName);
        Assert.Contains("Builder must be of type HeroMessagingBuilder", ex.Message);
    }

    [Fact]
    public void WithWriteOnlyResilience_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.WithWriteOnlyResilience();

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(IConnectionResiliencePolicy));
    }

    [Fact]
    public void WithConnectionResilience_WithCustomPolicyType_RegistersCustomPolicy()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.WithConnectionResilience<TestResiliencePolicy>();

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(IConnectionResiliencePolicy) &&
                                        sd.ImplementationType == typeof(TestResiliencePolicy));
    }

    [Fact]
    public void WithConnectionResilience_WithCustomPolicyType_WithInvalidBuilder_ThrowsArgumentException()
    {
        // Arrange
        var mockBuilder = new Mock<IHeroMessagingBuilder>();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            mockBuilder.Object.WithConnectionResilience<TestResiliencePolicy>());
        Assert.Equal("builder", ex.ParamName);
    }

    [Fact]
    public void ResilienceProfiles_Cloud_HasCorrectConfiguration()
    {
        // Act
        var options = ResilienceProfiles.Cloud;

        // Assert
        Assert.NotNull(options);
        Assert.Equal(5, options.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(2), options.BaseRetryDelay);
        Assert.Equal(TimeSpan.FromMinutes(2), options.MaxRetryDelay);
        Assert.NotNull(options.CircuitBreakerOptions);
        Assert.Equal(8, options.CircuitBreakerOptions.FailureThreshold);
        Assert.Equal(TimeSpan.FromMinutes(3), options.CircuitBreakerOptions.BreakDuration);
    }

    [Fact]
    public void ResilienceProfiles_OnPremises_HasCorrectConfiguration()
    {
        // Act
        var options = ResilienceProfiles.OnPremises;

        // Assert
        Assert.NotNull(options);
        Assert.Equal(3, options.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.BaseRetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), options.MaxRetryDelay);
        Assert.NotNull(options.CircuitBreakerOptions);
        Assert.Equal(5, options.CircuitBreakerOptions.FailureThreshold);
        Assert.Equal(TimeSpan.FromMinutes(1), options.CircuitBreakerOptions.BreakDuration);
    }

    [Fact]
    public void ResilienceProfiles_Microservices_HasCorrectConfiguration()
    {
        // Act
        var options = ResilienceProfiles.Microservices;

        // Assert
        Assert.NotNull(options);
        Assert.Equal(4, options.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), options.BaseRetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(45), options.MaxRetryDelay);
        Assert.NotNull(options.CircuitBreakerOptions);
        Assert.Equal(6, options.CircuitBreakerOptions.FailureThreshold);
        Assert.Equal(TimeSpan.FromMinutes(1.5), options.CircuitBreakerOptions.BreakDuration);
    }

    [Fact]
    public void ResilienceProfiles_BatchProcessing_HasCorrectConfiguration()
    {
        // Act
        var options = ResilienceProfiles.BatchProcessing;

        // Assert
        Assert.NotNull(options);
        Assert.Equal(7, options.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(3), options.BaseRetryDelay);
        Assert.Equal(TimeSpan.FromMinutes(5), options.MaxRetryDelay);
        Assert.NotNull(options.CircuitBreakerOptions);
        Assert.Equal(12, options.CircuitBreakerOptions.FailureThreshold);
        Assert.Equal(TimeSpan.FromMinutes(5), options.CircuitBreakerOptions.BreakDuration);
    }

    [Fact]
    public void WithConnectionResilience_DecoratesMessageStorage()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddSingleton<IMessageStorage, TestMessageStorage>();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithConnectionResilience();
        var provider = services.BuildServiceProvider();

        // Assert
        var storage = provider.GetService<IMessageStorage>();
        Assert.NotNull(storage);
        Assert.IsType<ResilientMessageStorageDecorator>(storage);
    }

    [Fact]
    public void WithConnectionResilience_DecoratesOutboxStorage()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddSingleton<IOutboxStorage, TestOutboxStorage>();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithConnectionResilience();
        var provider = services.BuildServiceProvider();

        // Assert
        var storage = provider.GetService<IOutboxStorage>();
        Assert.NotNull(storage);
        Assert.IsType<ResilientOutboxStorageDecorator>(storage);
    }

    [Fact]
    public void WithConnectionResilience_DecoratesInboxStorage()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddSingleton<IInboxStorage, TestInboxStorage>();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithConnectionResilience();
        var provider = services.BuildServiceProvider();

        // Assert
        var storage = provider.GetService<IInboxStorage>();
        Assert.NotNull(storage);
        Assert.IsType<ResilientInboxStorageDecorator>(storage);
    }

    [Fact]
    public void WithConnectionResilience_DecoratesQueueStorage()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddSingleton<IQueueStorage, TestQueueStorage>();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithConnectionResilience();
        var provider = services.BuildServiceProvider();

        // Assert
        var storage = provider.GetService<IQueueStorage>();
        Assert.NotNull(storage);
        Assert.IsType<ResilientQueueStorageDecorator>(storage);
    }

    [Fact]
    public void WithWriteOnlyResilience_DoesNotDecorateMessageStorage()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddSingleton<IMessageStorage, TestMessageStorage>();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithWriteOnlyResilience();
        var provider = services.BuildServiceProvider();

        // Assert
        var storage = provider.GetService<IMessageStorage>();
        Assert.NotNull(storage);
        Assert.IsType<TestMessageStorage>(storage);
    }

    [Fact]
    public void FluentConfiguration_CanChainMultipleMethods()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder
            .WithConnectionResilience(o => o.MaxRetries = 5)
            .WithDevelopmentResilience();

        // Assert
        Assert.Same(builder, result);
    }

    // Test helper classes
    private sealed class TestResiliencePolicy : IConnectionResiliencePolicy
    {
        public Task ExecuteAsync(Func<Task> operation, string operationName, CancellationToken cancellationToken = default)
        {
            return operation();
        }

        public Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName, CancellationToken cancellationToken = default)
        {
            return operation();
        }
    }

    private sealed class TestMessageStorage : IMessageStorage
    {
        public Task<string> StoreAsync(IMessage message, MessageStorageOptions? options = null, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task<T?> RetrieveAsync<T>(string messageId, CancellationToken cancellationToken = default) where T : IMessage => Task.FromResult<T?>(default);
        public Task<IEnumerable<T>> QueryAsync<T>(MessageQuery query, CancellationToken cancellationToken = default) where T : IMessage => Task.FromResult(Enumerable.Empty<T>());
        public Task<bool> DeleteAsync(string messageId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> UpdateAsync(string messageId, IMessage message, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExistsAsync(string messageId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<long> CountAsync(MessageQuery? query = null, CancellationToken cancellationToken = default) => Task.FromResult(0L);
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        Task IMessageStorage.StoreAsync(IMessage message, IStorageTransaction? transaction, CancellationToken cancellationToken) => Task.CompletedTask;
        Task<IMessage?> IMessageStorage.RetrieveAsync(Guid messageId, IStorageTransaction? transaction, CancellationToken cancellationToken) => Task.FromResult<IMessage?>(null);
        Task<List<IMessage>> IMessageStorage.QueryAsync(MessageQuery query, CancellationToken cancellationToken) => Task.FromResult(new List<IMessage>());
        Task IMessageStorage.DeleteAsync(Guid messageId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IStorageTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.FromResult<IStorageTransaction>(null!);
    }

    private sealed class TestOutboxStorage : IOutboxStorage
    {
        public Task<OutboxEntry> AddAsync(IMessage message, Abstractions.OutboxOptions options, CancellationToken cancellationToken = default) => Task.FromResult(new OutboxEntry());
        public Task<IEnumerable<OutboxEntry>> GetPendingAsync(OutboxQuery query, CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<OutboxEntry>());
        public Task<IEnumerable<OutboxEntry>> GetPendingAsync(int limit = 100, CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<OutboxEntry>());
        public Task<bool> MarkProcessedAsync(string entryId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> MarkFailedAsync(string entryId, string error, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> UpdateRetryCountAsync(string entryId, int retryCount, DateTimeOffset? nextRetry = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0L);
        public Task<IEnumerable<OutboxEntry>> GetFailedAsync(int limit = 100, CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<OutboxEntry>());
    }

    private sealed class TestInboxStorage : IInboxStorage
    {
        public Task<InboxEntry?> AddAsync(IMessage message, InboxOptions options, CancellationToken cancellationToken = default) => Task.FromResult<InboxEntry?>(new InboxEntry());
        public Task<bool> IsDuplicateAsync(string messageId, TimeSpan? window = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<InboxEntry?> GetAsync(string messageId, CancellationToken cancellationToken = default) => Task.FromResult<InboxEntry?>(null);
        public Task<bool> MarkProcessedAsync(string messageId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> MarkFailedAsync(string messageId, string error, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IEnumerable<InboxEntry>> GetPendingAsync(InboxQuery query, CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<InboxEntry>());
        public Task<IEnumerable<InboxEntry>> GetUnprocessedAsync(int limit = 100, CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<InboxEntry>());
        public Task<long> GetUnprocessedCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0L);
        public Task CleanupOldEntriesAsync(TimeSpan olderThan, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestQueueStorage : IQueueStorage
    {
        public Task<QueueEntry> EnqueueAsync(string queueName, IMessage message, EnqueueOptions? options = null, CancellationToken cancellationToken = default) => Task.FromResult(new QueueEntry());
        public Task<QueueEntry?> DequeueAsync(string queueName, CancellationToken cancellationToken = default) => Task.FromResult<QueueEntry?>(null);
        public Task<IEnumerable<QueueEntry>> PeekAsync(string queueName, int count = 1, CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<QueueEntry>());
        public Task<bool> AcknowledgeAsync(string queueName, string entryId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> RejectAsync(string queueName, string entryId, bool requeue = false, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<long> GetQueueDepthAsync(string queueName, CancellationToken cancellationToken = default) => Task.FromResult(0L);
        public Task<bool> CreateQueueAsync(string queueName, QueueOptions? options = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> DeleteQueueAsync(string queueName, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IEnumerable<string>> GetQueuesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<string>());
        public Task<bool> QueueExistsAsync(string queueName, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}

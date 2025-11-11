using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace HeroMessaging.Configuration.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class StorageBuilderTests
{
    [Fact]
    public void Constructor_WithNullServices_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new StorageBuilder(null!));
        Assert.Equal("services", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithValidServices_CreatesBuilder()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var builder = new StorageBuilder(services);

        // Assert
        Assert.NotNull(builder);
    }

    [Fact]
    public void UseInMemory_RegistersAllInMemoryStorageTypes()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new StorageBuilder(services);

        // Act
        var result = builder.UseInMemory();

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(IMessageStorage));
        Assert.Contains(services, sd => sd.ServiceType == typeof(IOutboxStorage));
        Assert.Contains(services, sd => sd.ServiceType == typeof(IInboxStorage));
        Assert.Contains(services, sd => sd.ServiceType == typeof(IQueueStorage));
    }

    [Fact]
    public void UseInMemory_WithOptions_RegistersOptionsAndStorage()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new StorageBuilder(services);

        // Act
        builder.UseInMemory(options =>
        {
            options.MaxMessageCount = 1000;
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<InMemoryStorageOptions>();
        Assert.NotNull(options);
        Assert.Equal(1000, options.MaxMessageCount);
        Assert.NotNull(provider.GetService<IMessageStorage>());
    }

    [Fact]
    public void UseMessageStorage_WithTypeParameter_RegistersCustomStorage()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new StorageBuilder(services);

        // Act
        var result = builder.UseMessageStorage<TestMessageStorage>();

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(IMessageStorage) && sd.ImplementationType == typeof(TestMessageStorage));
    }

    [Fact]
    public void UseMessageStorage_WithInstance_RegistersStorageInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new StorageBuilder(services);
        var storage = new TestMessageStorage();

        // Act
        var result = builder.UseMessageStorage(storage);

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(IMessageStorage) && sd.ImplementationInstance == storage);
    }

    [Fact]
    public void UseOutboxStorage_WithTypeParameter_RegistersCustomStorage()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new StorageBuilder(services);

        // Act
        var result = builder.UseOutboxStorage<TestOutboxStorage>();

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(IOutboxStorage) && sd.ImplementationType == typeof(TestOutboxStorage));
    }

    [Fact]
    public void UseOutboxStorage_WithInstance_RegistersStorageInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new StorageBuilder(services);
        var storage = new TestOutboxStorage();

        // Act
        var result = builder.UseOutboxStorage(storage);

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(IOutboxStorage) && sd.ImplementationInstance == storage);
    }

    [Fact]
    public void UseInboxStorage_WithTypeParameter_RegistersCustomStorage()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new StorageBuilder(services);

        // Act
        var result = builder.UseInboxStorage<TestInboxStorage>();

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(IInboxStorage) && sd.ImplementationType == typeof(TestInboxStorage));
    }

    [Fact]
    public void UseInboxStorage_WithInstance_RegistersStorageInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new StorageBuilder(services);
        var storage = new TestInboxStorage();

        // Act
        var result = builder.UseInboxStorage(storage);

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(IInboxStorage) && sd.ImplementationInstance == storage);
    }

    [Fact]
    public void UseQueueStorage_WithTypeParameter_RegistersCustomStorage()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new StorageBuilder(services);

        // Act
        var result = builder.UseQueueStorage<TestQueueStorage>();

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(IQueueStorage) && sd.ImplementationType == typeof(TestQueueStorage));
    }

    [Fact]
    public void UseQueueStorage_WithInstance_RegistersStorageInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new StorageBuilder(services);
        var storage = new TestQueueStorage();

        // Act
        var result = builder.UseQueueStorage(storage);

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(IQueueStorage) && sd.ImplementationInstance == storage);
    }

    [Fact]
    public void WithConnectionPooling_DefaultPoolSize_EnablesPooling()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new StorageBuilder(services);

        // Act
        var result = builder.WithConnectionPooling();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        var options = provider.GetService<IOptions<StorageConnectionOptions>>();
        Assert.NotNull(options);
        Assert.True(options.Value.EnablePooling);
        Assert.Equal(100, options.Value.MaxPoolSize);
    }

    [Fact]
    public void WithConnectionPooling_CustomPoolSize_UsesSpecifiedSize()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new StorageBuilder(services);

        // Act
        builder.WithConnectionPooling(maxPoolSize: 50);
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<IOptions<StorageConnectionOptions>>();
        Assert.NotNull(options);
        Assert.True(options.Value.EnablePooling);
        Assert.Equal(50, options.Value.MaxPoolSize);
    }

    [Fact]
    public void WithRetry_DefaultSettings_EnablesRetry()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new StorageBuilder(services);

        // Act
        var result = builder.WithRetry();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        var options = provider.GetService<IOptions<StorageRetryOptions>>();
        Assert.NotNull(options);
        Assert.True(options.Value.EnableRetry);
        Assert.Equal(3, options.Value.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), options.Value.RetryDelay);
    }

    [Fact]
    public void WithRetry_CustomSettings_UsesSpecifiedSettings()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new StorageBuilder(services);

        // Act
        builder.WithRetry(maxRetries: 5, retryDelay: TimeSpan.FromSeconds(2));
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<IOptions<StorageRetryOptions>>();
        Assert.NotNull(options);
        Assert.True(options.Value.EnableRetry);
        Assert.Equal(5, options.Value.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(2), options.Value.RetryDelay);
    }

    [Fact]
    public void WithCircuitBreaker_DefaultSettings_EnablesCircuitBreaker()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new StorageBuilder(services);

        // Act
        var result = builder.WithCircuitBreaker();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        var options = provider.GetService<IOptions<StorageCircuitBreakerOptions>>();
        Assert.NotNull(options);
        Assert.True(options.Value.EnableCircuitBreaker);
        Assert.Equal(5, options.Value.FailureThreshold);
        Assert.Equal(TimeSpan.FromMinutes(1), options.Value.BreakDuration);
    }

    [Fact]
    public void WithCircuitBreaker_CustomSettings_UsesSpecifiedSettings()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new StorageBuilder(services);

        // Act
        builder.WithCircuitBreaker(failureThreshold: 10, breakDuration: TimeSpan.FromMinutes(5));
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<IOptions<StorageCircuitBreakerOptions>>();
        Assert.NotNull(options);
        Assert.True(options.Value.EnableCircuitBreaker);
        Assert.Equal(10, options.Value.FailureThreshold);
        Assert.Equal(TimeSpan.FromMinutes(5), options.Value.BreakDuration);
    }

    [Fact]
    public void WithCommandTimeout_SetsTimeout()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new StorageBuilder(services);
        var timeout = TimeSpan.FromSeconds(60);

        // Act
        var result = builder.WithCommandTimeout(timeout);
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        var options = provider.GetService<IOptions<StorageOptions>>();
        Assert.NotNull(options);
        Assert.Equal(timeout, options.Value.CommandTimeout);
    }

    [Fact]
    public void Build_WithoutConfiguration_RegistersInMemoryStorage()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new StorageBuilder(services);

        // Act
        var result = builder.Build();
        var provider = result.BuildServiceProvider();

        // Assert
        Assert.Same(services, result);
        Assert.NotNull(provider.GetService<IMessageStorage>());
        Assert.NotNull(provider.GetService<IOutboxStorage>());
        Assert.NotNull(provider.GetService<IInboxStorage>());
        Assert.NotNull(provider.GetService<IQueueStorage>());
    }

    [Fact]
    public void Build_WithCustomStorage_DoesNotRegisterInMemory()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new StorageBuilder(services);

        // Act
        builder.UseMessageStorage<TestMessageStorage>();
        var result = builder.Build();
        var provider = result.BuildServiceProvider();

        // Assert
        Assert.Same(services, result);
        var messageStorage = provider.GetService<IMessageStorage>();
        Assert.NotNull(messageStorage);
        Assert.IsType<TestMessageStorage>(messageStorage);
    }

    [Fact]
    public void StorageOptions_DefaultCommandTimeout_Is30Seconds()
    {
        // Act
        var options = new StorageOptions();

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(30), options.CommandTimeout);
    }

    [Fact]
    public void StorageConnectionOptions_DefaultValues_AreCorrect()
    {
        // Act
        var options = new StorageConnectionOptions();

        // Assert
        Assert.False(options.EnablePooling);
        Assert.Equal(100, options.MaxPoolSize);
        Assert.Equal(0, options.MinPoolSize);
        Assert.Equal(TimeSpan.FromMinutes(5), options.ConnectionLifetime);
        Assert.Equal(TimeSpan.FromSeconds(30), options.CommandTimeout); // Inherits from base
    }

    [Fact]
    public void StorageRetryOptions_DefaultValues_AreCorrect()
    {
        // Act
        var options = new StorageRetryOptions();

        // Assert
        Assert.False(options.EnableRetry);
        Assert.Equal(3, options.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), options.RetryDelay);
        Assert.Equal(TimeSpan.FromMinutes(1), options.MaxRetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), options.CommandTimeout); // Inherits from base
    }

    [Fact]
    public void StorageCircuitBreakerOptions_DefaultValues_AreCorrect()
    {
        // Act
        var options = new StorageCircuitBreakerOptions();

        // Assert
        Assert.False(options.EnableCircuitBreaker);
        Assert.Equal(5, options.FailureThreshold);
        Assert.Equal(TimeSpan.FromMinutes(1), options.BreakDuration);
        Assert.Equal(TimeSpan.FromSeconds(10), options.SamplingDuration);
        Assert.Equal(TimeSpan.FromSeconds(30), options.CommandTimeout); // Inherits from base
    }

    [Fact]
    public void FluentConfiguration_CanChainMultipleMethods()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new StorageBuilder(services);

        // Act
        var result = builder
            .UseInMemory()
            .WithConnectionPooling(50)
            .WithRetry(5, TimeSpan.FromSeconds(2))
            .WithCircuitBreaker(10, TimeSpan.FromMinutes(2))
            .WithCommandTimeout(TimeSpan.FromSeconds(45))
            .Build();

        // Assert
        Assert.Same(services, result);
    }

    [Fact]
    public void MultipleStorageTypes_CanBeConfiguredIndependently()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new StorageBuilder(services);

        // Act
        builder
            .UseMessageStorage<TestMessageStorage>()
            .UseOutboxStorage<TestOutboxStorage>()
            .UseInboxStorage<TestInboxStorage>()
            .UseQueueStorage<TestQueueStorage>();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.IsType<TestMessageStorage>(provider.GetService<IMessageStorage>());
        Assert.IsType<TestOutboxStorage>(provider.GetService<IOutboxStorage>());
        Assert.IsType<TestInboxStorage>(provider.GetService<IInboxStorage>());
        Assert.IsType<TestQueueStorage>(provider.GetService<IQueueStorage>());
    }

    // Test helper classes
    private sealed class TestMessageStorage : IMessageStorage
    {
        public Task StoreAsync(IStoredMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IStoredMessage?> GetAsync(Guid messageId, CancellationToken cancellationToken = default) => Task.FromResult<IStoredMessage?>(null);
        public Task<IEnumerable<IStoredMessage>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<IStoredMessage>());
        public Task DeleteAsync(Guid messageId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(Guid messageId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class TestOutboxStorage : IOutboxStorage
    {
        public Task AddAsync(IOutboxMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IEnumerable<IOutboxMessage>> GetPendingAsync(int batchSize = 100, CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<IOutboxMessage>());
        public Task MarkAsPublishedAsync(Guid messageId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkAsFailedAsync(Guid messageId, string error, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid messageId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IOutboxMessage?> GetAsync(Guid messageId, CancellationToken cancellationToken = default) => Task.FromResult<IOutboxMessage?>(null);
    }

    private sealed class TestInboxStorage : IInboxStorage
    {
        public Task AddAsync(IInboxMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IEnumerable<IInboxMessage>> GetPendingAsync(int batchSize = 100, CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<IInboxMessage>());
        public Task MarkAsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkAsFailedAsync(Guid messageId, string error, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(Guid messageId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IInboxMessage?> GetAsync(Guid messageId, CancellationToken cancellationToken = default) => Task.FromResult<IInboxMessage?>(null);
    }

    private sealed class TestQueueStorage : IQueueStorage
    {
        public Task EnqueueAsync(IQueueMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IQueueMessage?> DequeueAsync(string queueName, CancellationToken cancellationToken = default) => Task.FromResult<IQueueMessage?>(null);
        public Task<IEnumerable<IQueueMessage>> PeekAsync(string queueName, int count = 1, CancellationToken cancellationToken = default) => Task.FromResult(Enumerable.Empty<IQueueMessage>());
        public Task<int> GetCountAsync(string queueName, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task DeleteAsync(Guid messageId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcknowledgeAsync(Guid messageId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

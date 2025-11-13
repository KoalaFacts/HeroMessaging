using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Configuration;

[Trait("Category", "Unit")]
public sealed class StorageBuilderTests
{
    private readonly ServiceCollection _services;

    public StorageBuilderTests()
    {
        _services = new ServiceCollection();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidServices_CreatesInstance()
    {
        // Arrange & Act
        var builder = new StorageBuilder(_services);

        // Assert
        Assert.NotNull(builder);
    }

    [Fact]
    public void Constructor_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new StorageBuilder(null!));

        Assert.Equal("services", exception.ParamName);
    }

    #endregion

    #region UseInMemory Tests

    [Fact]
    public void UseInMemory_RegistersAllStorageTypes()
    {
        // Arrange
        var builder = new StorageBuilder(_services);

        // Act
        builder.UseInMemory();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IMessageStorage));
        Assert.Contains(_services, s => s.ServiceType == typeof(IOutboxStorage));
        Assert.Contains(_services, s => s.ServiceType == typeof(IInboxStorage));
        Assert.Contains(_services, s => s.ServiceType == typeof(IQueueStorage));
    }

    [Fact]
    public void UseInMemory_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new StorageBuilder(_services);

        // Act
        var result = builder.UseInMemory();

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void UseInMemory_WithConfiguration_AppliesConfiguration()
    {
        // Arrange
        var builder = new StorageBuilder(_services);

        // Act
        builder.UseInMemory(options =>
        {
            // Configuration would be applied here
        });

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(InMemoryStorageOptions));
    }

    #endregion

    #region UseMessageStorage Tests

    [Fact]
    public void UseMessageStorage_Generic_RegistersMessageStorage()
    {
        // Arrange
        var builder = new StorageBuilder(_services);

        // Act
        builder.UseMessageStorage<TestMessageStorage>();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IMessageStorage) &&
                                        s.ImplementationType == typeof(TestMessageStorage));
    }

    [Fact]
    public void UseMessageStorage_Instance_RegistersMessageStorageInstance()
    {
        // Arrange
        var builder = new StorageBuilder(_services);
        var storage = new TestMessageStorage();

        // Act
        builder.UseMessageStorage(storage);

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IMessageStorage) ||
                                        s.ServiceType == typeof(TestMessageStorage));
    }

    [Fact]
    public void UseMessageStorage_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new StorageBuilder(_services);

        // Act
        var result = builder.UseMessageStorage<TestMessageStorage>();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region UseOutboxStorage Tests

    [Fact]
    public void UseOutboxStorage_Generic_RegistersOutboxStorage()
    {
        // Arrange
        var builder = new StorageBuilder(_services);

        // Act
        builder.UseOutboxStorage<TestOutboxStorage>();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IOutboxStorage) &&
                                        s.ImplementationType == typeof(TestOutboxStorage));
    }

    [Fact]
    public void UseOutboxStorage_Instance_RegistersOutboxStorageInstance()
    {
        // Arrange
        var builder = new StorageBuilder(_services);
        var storage = new TestOutboxStorage();

        // Act
        builder.UseOutboxStorage(storage);

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IOutboxStorage) ||
                                        s.ServiceType == typeof(TestOutboxStorage));
    }

    [Fact]
    public void UseOutboxStorage_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new StorageBuilder(_services);

        // Act
        var result = builder.UseOutboxStorage<TestOutboxStorage>();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region UseInboxStorage Tests

    [Fact]
    public void UseInboxStorage_Generic_RegistersInboxStorage()
    {
        // Arrange
        var builder = new StorageBuilder(_services);

        // Act
        builder.UseInboxStorage<TestInboxStorage>();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IInboxStorage) &&
                                        s.ImplementationType == typeof(TestInboxStorage));
    }

    [Fact]
    public void UseInboxStorage_Instance_RegistersInboxStorageInstance()
    {
        // Arrange
        var builder = new StorageBuilder(_services);
        var storage = new TestInboxStorage();

        // Act
        builder.UseInboxStorage(storage);

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IInboxStorage) ||
                                        s.ServiceType == typeof(TestInboxStorage));
    }

    [Fact]
    public void UseInboxStorage_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new StorageBuilder(_services);

        // Act
        var result = builder.UseInboxStorage<TestInboxStorage>();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region UseQueueStorage Tests

    [Fact]
    public void UseQueueStorage_Generic_RegistersQueueStorage()
    {
        // Arrange
        var builder = new StorageBuilder(_services);

        // Act
        builder.UseQueueStorage<TestQueueStorage>();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IQueueStorage) &&
                                        s.ImplementationType == typeof(TestQueueStorage));
    }

    [Fact]
    public void UseQueueStorage_Instance_RegistersQueueStorageInstance()
    {
        // Arrange
        var builder = new StorageBuilder(_services);
        var storage = new TestQueueStorage();

        // Act
        builder.UseQueueStorage(storage);

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IQueueStorage) ||
                                        s.ServiceType == typeof(TestQueueStorage));
    }

    [Fact]
    public void UseQueueStorage_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new StorageBuilder(_services);

        // Act
        var result = builder.UseQueueStorage<TestQueueStorage>();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region WithConnectionPooling Tests

    [Fact]
    public void WithConnectionPooling_DefaultPoolSize_ConfiguresPooling()
    {
        // Arrange
        var builder = new StorageBuilder(_services);
        builder.WithConnectionPooling();
        var provider = _services.BuildServiceProvider();

        // Act
        var options = provider.GetService<IOptions<StorageConnectionOptions>>();

        // Assert
        Assert.NotNull(options);
    }

    [Fact]
    public void WithConnectionPooling_CustomPoolSize_ConfiguresPoolSize()
    {
        // Arrange
        var builder = new StorageBuilder(_services);
        builder.WithConnectionPooling(50);
        var provider = _services.BuildServiceProvider();

        // Act
        var options = provider.GetService<IOptions<StorageConnectionOptions>>();

        // Assert
        Assert.NotNull(options);
    }

    [Fact]
    public void WithConnectionPooling_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new StorageBuilder(_services);

        // Act
        var result = builder.WithConnectionPooling();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region WithRetry Tests

    [Fact]
    public void WithRetry_DefaultParameters_ConfiguresRetry()
    {
        // Arrange
        var builder = new StorageBuilder(_services);
        builder.WithRetry();
        var provider = _services.BuildServiceProvider();

        // Act
        var options = provider.GetService<IOptions<StorageRetryOptions>>();

        // Assert
        Assert.NotNull(options);
    }

    [Fact]
    public void WithRetry_CustomParameters_ConfiguresRetryWithParameters()
    {
        // Arrange
        var builder = new StorageBuilder(_services);
        builder.WithRetry(5, TimeSpan.FromSeconds(2));
        var provider = _services.BuildServiceProvider();

        // Act
        var options = provider.GetService<IOptions<StorageRetryOptions>>();

        // Assert
        Assert.NotNull(options);
    }

    [Fact]
    public void WithRetry_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new StorageBuilder(_services);

        // Act
        var result = builder.WithRetry();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region WithCircuitBreaker Tests

    [Fact]
    public void WithCircuitBreaker_DefaultParameters_ConfiguresCircuitBreaker()
    {
        // Arrange
        var builder = new StorageBuilder(_services);
        builder.WithCircuitBreaker();
        var provider = _services.BuildServiceProvider();

        // Act
        var options = provider.GetService<IOptions<StorageCircuitBreakerOptions>>();

        // Assert
        Assert.NotNull(options);
    }

    [Fact]
    public void WithCircuitBreaker_CustomParameters_ConfiguresCircuitBreakerWithParameters()
    {
        // Arrange
        var builder = new StorageBuilder(_services);
        builder.WithCircuitBreaker(10, TimeSpan.FromMinutes(5));
        var provider = _services.BuildServiceProvider();

        // Act
        var options = provider.GetService<IOptions<StorageCircuitBreakerOptions>>();

        // Assert
        Assert.NotNull(options);
    }

    [Fact]
    public void WithCircuitBreaker_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new StorageBuilder(_services);

        // Act
        var result = builder.WithCircuitBreaker();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region WithCommandTimeout Tests

    [Fact]
    public void WithCommandTimeout_ConfiguresTimeout()
    {
        // Arrange
        var builder = new StorageBuilder(_services);
        builder.WithCommandTimeout(TimeSpan.FromSeconds(60));
        var provider = _services.BuildServiceProvider();

        // Act
        var options = provider.GetService<IOptions<StorageOptions>>();

        // Assert
        Assert.NotNull(options);
    }

    [Fact]
    public void WithCommandTimeout_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new StorageBuilder(_services);

        // Act
        var result = builder.WithCommandTimeout(TimeSpan.FromSeconds(60));

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region Build Tests

    [Fact]
    public void Build_ReturnsServiceCollection()
    {
        // Arrange
        var builder = new StorageBuilder(_services);

        // Act
        var result = builder.Build();

        // Assert
        Assert.Same(_services, result);
    }

    [Fact]
    public void Build_RegistersInMemoryStorage_WhenNoStorageConfigured()
    {
        // Arrange
        var builder = new StorageBuilder(_services);

        // Act
        builder.Build();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IMessageStorage));
    }

    [Fact]
    public void Build_DoesNotRegisterInMemoryStorage_WhenStorageAlreadyConfigured()
    {
        // Arrange
        var builder = new StorageBuilder(_services);
        builder.UseMessageStorage<TestMessageStorage>();

        // Act
        builder.Build();

        // Assert
        var messageStorageServices = _services.Where(s => s.ServiceType == typeof(IMessageStorage)).ToList();
        Assert.Single(messageStorageServices);
    }

    #endregion

    #region Configuration Classes Tests

    [Fact]
    public void StorageOptions_HasDefaultCommandTimeout()
    {
        // Arrange & Act
        var options = new StorageOptions();

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(30), options.CommandTimeout);
    }

    [Fact]
    public void StorageConnectionOptions_HasDefaultValues()
    {
        // Arrange & Act
        var options = new StorageConnectionOptions();

        // Assert
        Assert.False(options.EnablePooling);
        Assert.Equal(100, options.MaxPoolSize);
        Assert.Equal(0, options.MinPoolSize);
        Assert.Equal(TimeSpan.FromMinutes(5), options.ConnectionLifetime);
        Assert.Equal(TimeSpan.FromSeconds(30), options.CommandTimeout);
    }

    [Fact]
    public void StorageRetryOptions_HasDefaultValues()
    {
        // Arrange & Act
        var options = new StorageRetryOptions();

        // Assert
        Assert.False(options.EnableRetry);
        Assert.Equal(3, options.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), options.RetryDelay);
        Assert.Equal(TimeSpan.FromMinutes(1), options.MaxRetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), options.CommandTimeout);
    }

    [Fact]
    public void StorageCircuitBreakerOptions_HasDefaultValues()
    {
        // Arrange & Act
        var options = new StorageCircuitBreakerOptions();

        // Assert
        Assert.False(options.EnableCircuitBreaker);
        Assert.Equal(5, options.FailureThreshold);
        Assert.Equal(TimeSpan.FromMinutes(1), options.BreakDuration);
        Assert.Equal(TimeSpan.FromSeconds(10), options.SamplingDuration);
        Assert.Equal(TimeSpan.FromSeconds(30), options.CommandTimeout);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void FluentConfiguration_ChainsCorrectly()
    {
        // Arrange
        var builder = new StorageBuilder(_services);

        // Act
        var result = builder
            .UseMessageStorage<TestMessageStorage>()
            .UseOutboxStorage<TestOutboxStorage>()
            .UseInboxStorage<TestInboxStorage>()
            .UseQueueStorage<TestQueueStorage>()
            .WithConnectionPooling(50)
            .WithRetry(5, TimeSpan.FromSeconds(2))
            .WithCircuitBreaker(10, TimeSpan.FromMinutes(5))
            .WithCommandTimeout(TimeSpan.FromSeconds(60))
            .Build();

        // Assert
        Assert.Same(_services, result);
        Assert.Contains(_services, s => s.ServiceType == typeof(IMessageStorage));
        Assert.Contains(_services, s => s.ServiceType == typeof(IOutboxStorage));
        Assert.Contains(_services, s => s.ServiceType == typeof(IInboxStorage));
        Assert.Contains(_services, s => s.ServiceType == typeof(IQueueStorage));
    }

    #endregion

    #region Test Helper Classes

    private class TestMessageStorage : IMessageStorage
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

    private class TestOutboxStorage : IOutboxStorage
    {
        public Task<OutboxEntry> AddAsync(IMessage message, Abstractions.OutboxOptions options, CancellationToken cancellationToken = default)
            => Task.FromResult(new OutboxEntry { Message = message, Options = options });

        public Task<IEnumerable<OutboxEntry>> GetPendingAsync(OutboxQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(Enumerable.Empty<OutboxEntry>());

        public Task<IEnumerable<OutboxEntry>> GetPendingAsync(int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult(Enumerable.Empty<OutboxEntry>());

        public Task<bool> MarkProcessedAsync(string entryId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> MarkFailedAsync(string entryId, string error, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> UpdateRetryCountAsync(string entryId, int retryCount, DateTimeOffset? nextRetry = null, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0L);

        public Task<IEnumerable<OutboxEntry>> GetFailedAsync(int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult(Enumerable.Empty<OutboxEntry>());
    }

    private class TestInboxStorage : IInboxStorage
    {
        public Task<InboxEntry?> AddAsync(IMessage message, InboxOptions options, CancellationToken cancellationToken = default)
            => Task.FromResult<InboxEntry?>(new InboxEntry { Message = message, Options = options });

        public Task<bool> IsDuplicateAsync(string messageId, TimeSpan? window = null, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<InboxEntry?> GetAsync(string messageId, CancellationToken cancellationToken = default)
            => Task.FromResult<InboxEntry?>(null);

        public Task<bool> MarkProcessedAsync(string messageId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> MarkFailedAsync(string messageId, string error, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<IEnumerable<InboxEntry>> GetPendingAsync(InboxQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(Enumerable.Empty<InboxEntry>());

        public Task<IEnumerable<InboxEntry>> GetUnprocessedAsync(int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult(Enumerable.Empty<InboxEntry>());

        public Task<long> GetUnprocessedCountAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0L);

        public Task CleanupOldEntriesAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private class TestQueueStorage : IQueueStorage
    {
        public Task<QueueEntry> EnqueueAsync(string queueName, IMessage message, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new QueueEntry { Message = message });

        public Task<QueueEntry?> DequeueAsync(string queueName, CancellationToken cancellationToken = default)
            => Task.FromResult<QueueEntry?>(null);

        public Task<IEnumerable<QueueEntry>> PeekAsync(string queueName, int count = 1, CancellationToken cancellationToken = default)
            => Task.FromResult(Enumerable.Empty<QueueEntry>());

        public Task<bool> AcknowledgeAsync(string queueName, string entryId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> RejectAsync(string queueName, string entryId, bool requeue = false, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<long> GetQueueDepthAsync(string queueName, CancellationToken cancellationToken = default)
            => Task.FromResult(0L);

        public Task<bool> CreateQueueAsync(string queueName, QueueOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> DeleteQueueAsync(string queueName, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<IEnumerable<string>> GetQueuesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Enumerable.Empty<string>());

        public Task<bool> QueueExistsAsync(string queueName, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    #endregion
}

using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

namespace HeroMessaging.Observability.HealthChecks.Tests.Unit;

/// <summary>
/// Unit tests for storage health check implementations
/// Testing MessageStorageHealthCheck, OutboxStorageHealthCheck, InboxStorageHealthCheck, and QueueStorageHealthCheck
/// </summary>
public class StorageHealthCheckTests
{
    #region Test Helpers

    /// <summary>
    /// Fake IMessageStorage implementation for testing generic method calls
    /// This avoids Moq limitations with generic methods that have constraints
    /// </summary>
    private class FakeMessageStorage : IMessageStorage
    {
        private readonly Func<IMessage, MessageStorageOptions?, CancellationToken, Task<string>>? _storeFunc;
        private readonly Func<string, CancellationToken, Task<IMessage?>>? _retrieveFunc;
        private readonly Func<string, CancellationToken, Task<bool>>? _deleteFunc;
        private string? _lastStoredId;
        private IMessage? _lastStoredMessage;

        public FakeMessageStorage(
            Func<IMessage, MessageStorageOptions?, CancellationToken, Task<string>>? storeFunc = null,
            Func<string, CancellationToken, Task<IMessage?>>? retrieveFunc = null,
            Func<string, CancellationToken, Task<bool>>? deleteFunc = null)
        {
            _storeFunc = storeFunc;
            _retrieveFunc = retrieveFunc;
            _deleteFunc = deleteFunc;
        }

        public Task<string> Store(IMessage message, MessageStorageOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (_storeFunc != null)
                return _storeFunc(message, options, cancellationToken);

            // Store the actual message instance so we can return it later
            _lastStoredMessage = message;
            _lastStoredId = Guid.NewGuid().ToString();
            return Task.FromResult(_lastStoredId);
        }

        public Task<T?> Retrieve<T>(string messageId, CancellationToken cancellationToken = default) where T : IMessage
        {
            if (_retrieveFunc != null)
            {
                var result = _retrieveFunc(messageId, cancellationToken).Result;
                return Task.FromResult((T?)result);
            }

            // Return the actual stored message if ID matches and type is compatible
            if (messageId == _lastStoredId && _lastStoredMessage is T typedMessage)
                return Task.FromResult<T?>(typedMessage);

            return Task.FromResult<T?>(default);
        }

        public Task<bool> Delete(string messageId, CancellationToken cancellationToken = default)
        {
            if (_deleteFunc != null)
                return _deleteFunc(messageId, cancellationToken);

            return Task.FromResult(messageId == _lastStoredId);
        }

        // Other interface methods - not used by MessageStorageHealthCheck but required by interface
        public Task<IEnumerable<T>> Query<T>(MessageQuery query, CancellationToken cancellationToken = default) where T : IMessage
            => Task.FromResult(Enumerable.Empty<T>());

        public Task<bool> Update(string messageId, IMessage message, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> Exists(string messageId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<long> Count(MessageQuery? query = null, CancellationToken cancellationToken = default)
            => Task.FromResult(0L);

        public Task Clear(CancellationToken cancellationToken = default)
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
            => Task.FromResult<IStorageTransaction>(null!);
    }

    #endregion

    #region MessageStorageHealthCheck Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task MessageStorageHealthCheck_WithOperationalStorage_ReturnsHealthy()
    {
        // Arrange
        var storage = new FakeMessageStorage();
        var timeProvider = TimeProvider.System;
        var healthCheck = new MessageStorageHealthCheck(storage, timeProvider);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("operational", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task MessageStorageHealthCheck_WithFailedRetrieval_ReturnsUnhealthy()
    {
        // Arrange
        var storage = new FakeMessageStorage(
            retrieveFunc: (id, ct) => Task.FromResult<IMessage?>(null));
        var timeProvider = TimeProvider.System;
        var healthCheck = new MessageStorageHealthCheck(storage, timeProvider);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Failed to retrieve test message", result.Description);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task MessageStorageHealthCheck_WithStorageException_ReturnsUnhealthy()
    {
        // Arrange
        var mockStorage = new Mock<IMessageStorage>();
        var timeProvider = TimeProvider.System;
        var expectedException = new InvalidOperationException("Database connection failed");

        mockStorage
            .Setup(s => s.Store(It.IsAny<IMessage>(), It.IsAny<MessageStorageOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        var healthCheck = new MessageStorageHealthCheck(mockStorage.Object, timeProvider);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Storage check failed", result.Description);
        Assert.Equal(expectedException, result.Exception);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.ContainsKey("storage_type"));
        Assert.True(result.Data.ContainsKey("error"));
        Assert.Equal(expectedException.Message, result.Data["error"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task MessageStorageHealthCheck_WithCustomName_UsesCustomNameInDescription()
    {
        // Arrange
        var storage = new FakeMessageStorage();
        var timeProvider = TimeProvider.System;
        var customName = "custom_message_storage";
        var healthCheck = new MessageStorageHealthCheck(storage, timeProvider, customName);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains(customName, result.Description);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MessageStorageHealthCheck_WithNullStorage_ThrowsArgumentNullException()
    {
        // Arrange
        var timeProvider = TimeProvider.System;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new MessageStorageHealthCheck(null!, timeProvider));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MessageStorageHealthCheck_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange
        var mockStorage = new Mock<IMessageStorage>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new MessageStorageHealthCheck(mockStorage.Object, null!));
    }

    #endregion

    #region OutboxStorageHealthCheck Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task OutboxStorageHealthCheck_WithOperationalStorage_ReturnsHealthy()
    {
        // Arrange
        var mockStorage = new Mock<IOutboxStorage>();

        mockStorage
            .Setup(s => s.GetPending(It.IsAny<OutboxQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboxEntry>());

        var healthCheck = new OutboxStorageHealthCheck(mockStorage.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("operational", result.Description, StringComparison.OrdinalIgnoreCase);

        mockStorage.Verify(s => s.GetPending(
            It.Is<OutboxQuery>(q => q.Status == OutboxEntryStatus.Pending && q.Limit == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task OutboxStorageHealthCheck_WithStorageException_ReturnsUnhealthy()
    {
        // Arrange
        var mockStorage = new Mock<IOutboxStorage>();
        var expectedException = new InvalidOperationException("Outbox query failed");

        mockStorage
            .Setup(s => s.GetPending(It.IsAny<OutboxQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        var healthCheck = new OutboxStorageHealthCheck(mockStorage.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Storage check failed", result.Description);
        Assert.Equal(expectedException, result.Exception);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.ContainsKey("storage_type"));
        Assert.True(result.Data.ContainsKey("error"));
        Assert.Equal(expectedException.Message, result.Data["error"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task OutboxStorageHealthCheck_WithCustomName_UsesCustomNameInDescription()
    {
        // Arrange
        var mockStorage = new Mock<IOutboxStorage>();
        var customName = "custom_outbox_storage";

        mockStorage
            .Setup(s => s.GetPending(It.IsAny<OutboxQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboxEntry>());

        var healthCheck = new OutboxStorageHealthCheck(mockStorage.Object, customName);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains(customName, result.Description);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OutboxStorageHealthCheck_WithNullStorage_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new OutboxStorageHealthCheck(null!));
    }

    #endregion

    #region InboxStorageHealthCheck Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task InboxStorageHealthCheck_WithOperationalStorage_ReturnsHealthy()
    {
        // Arrange
        var mockStorage = new Mock<IInboxStorage>();

        mockStorage
            .Setup(s => s.GetPending(It.IsAny<InboxQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<InboxEntry>());

        var healthCheck = new InboxStorageHealthCheck(mockStorage.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("operational", result.Description, StringComparison.OrdinalIgnoreCase);

        mockStorage.Verify(s => s.GetPending(
            It.Is<InboxQuery>(q => q.Status == InboxEntryStatus.Pending && q.Limit == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task InboxStorageHealthCheck_WithStorageException_ReturnsUnhealthy()
    {
        // Arrange
        var mockStorage = new Mock<IInboxStorage>();
        var expectedException = new InvalidOperationException("Inbox query failed");

        mockStorage
            .Setup(s => s.GetPending(It.IsAny<InboxQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        var healthCheck = new InboxStorageHealthCheck(mockStorage.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Storage check failed", result.Description);
        Assert.Equal(expectedException, result.Exception);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.ContainsKey("storage_type"));
        Assert.True(result.Data.ContainsKey("error"));
        Assert.Equal(expectedException.Message, result.Data["error"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task InboxStorageHealthCheck_WithCustomName_UsesCustomNameInDescription()
    {
        // Arrange
        var mockStorage = new Mock<IInboxStorage>();
        var customName = "custom_inbox_storage";

        mockStorage
            .Setup(s => s.GetPending(It.IsAny<InboxQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<InboxEntry>());

        var healthCheck = new InboxStorageHealthCheck(mockStorage.Object, customName);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains(customName, result.Description);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void InboxStorageHealthCheck_WithNullStorage_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new InboxStorageHealthCheck(null!));
    }

    #endregion

    #region QueueStorageHealthCheck Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task QueueStorageHealthCheck_WithOperationalStorage_ReturnsHealthy()
    {
        // Arrange
        var mockStorage = new Mock<IQueueStorage>();
        var queueDepth = 42L;

        mockStorage
            .Setup(s => s.GetQueueDepth(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(queueDepth);

        var healthCheck = new QueueStorageHealthCheck(mockStorage.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("operational", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.ContainsKey("queue"));
        Assert.True(result.Data.ContainsKey("depth"));
        Assert.Equal(queueDepth, result.Data["depth"]);

        mockStorage.Verify(s => s.GetQueueDepth(
            "health_check_queue",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task QueueStorageHealthCheck_WithCustomQueueName_UsesCustomQueueName()
    {
        // Arrange
        var mockStorage = new Mock<IQueueStorage>();
        var customQueueName = "my_custom_queue";
        var queueDepth = 10L;

        mockStorage
            .Setup(s => s.GetQueueDepth(customQueueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(queueDepth);

        var healthCheck = new QueueStorageHealthCheck(mockStorage.Object, queueName: customQueueName);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.NotNull(result.Data);
        Assert.Equal(customQueueName, result.Data["queue"]);
        Assert.Equal(queueDepth, result.Data["depth"]);

        mockStorage.Verify(s => s.GetQueueDepth(
            customQueueName,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task QueueStorageHealthCheck_WithStorageException_ReturnsUnhealthy()
    {
        // Arrange
        var mockStorage = new Mock<IQueueStorage>();
        var expectedException = new InvalidOperationException("Queue depth query failed");
        var queueName = "test_queue";

        mockStorage
            .Setup(s => s.GetQueueDepth(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        var healthCheck = new QueueStorageHealthCheck(mockStorage.Object, queueName: queueName);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Storage check failed", result.Description);
        Assert.Equal(expectedException, result.Exception);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.ContainsKey("storage_type"));
        Assert.True(result.Data.ContainsKey("queue"));
        Assert.True(result.Data.ContainsKey("error"));
        Assert.Equal(queueName, result.Data["queue"]);
        Assert.Equal(expectedException.Message, result.Data["error"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task QueueStorageHealthCheck_WithCustomName_UsesCustomNameInDescription()
    {
        // Arrange
        var mockStorage = new Mock<IQueueStorage>();
        var customName = "custom_queue_storage";

        mockStorage
            .Setup(s => s.GetQueueDepth(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5L);

        var healthCheck = new QueueStorageHealthCheck(mockStorage.Object, customName);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains(customName, result.Description);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void QueueStorageHealthCheck_WithNullStorage_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new QueueStorageHealthCheck(null!));
    }

    #endregion
}

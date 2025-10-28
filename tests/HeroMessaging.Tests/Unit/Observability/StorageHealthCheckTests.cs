using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Observability.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Observability;

/// <summary>
/// Unit tests for storage health check implementations
/// Testing MessageStorageHealthCheck, OutboxStorageHealthCheck, InboxStorageHealthCheck, and QueueStorageHealthCheck
/// </summary>
public class StorageHealthCheckTests
{
    #region MessageStorageHealthCheck Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task MessageStorageHealthCheck_WithOperationalStorage_ReturnsHealthy()
    {
        // Arrange
        var mockStorage = new Mock<IMessageStorage>();
        var timeProvider = TimeProvider.System;

        mockStorage
            .Setup(s => s.Store(It.IsAny<IMessage>(), It.IsAny<MessageStorageOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());

        mockStorage
            .Setup(s => s.Retrieve<IMessage>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IMessage>());

        mockStorage
            .Setup(s => s.Delete(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var healthCheck = new MessageStorageHealthCheck(mockStorage.Object, timeProvider);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("operational", result.Description, StringComparison.OrdinalIgnoreCase);

        mockStorage.Verify(s => s.Store(It.IsAny<IMessage>(), It.IsAny<MessageStorageOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        mockStorage.Verify(s => s.Retrieve<IMessage>(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        mockStorage.Verify(s => s.Delete(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task MessageStorageHealthCheck_WithFailedRetrieval_ReturnsUnhealthy()
    {
        // Arrange
        var mockStorage = new Mock<IMessageStorage>();
        var timeProvider = TimeProvider.System;

        mockStorage
            .Setup(s => s.Store(It.IsAny<IMessage>(), It.IsAny<MessageStorageOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());

        mockStorage
            .Setup(s => s.Retrieve<IMessage>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IMessage?)null);

        var healthCheck = new MessageStorageHealthCheck(mockStorage.Object, timeProvider);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Failed to retrieve test message", result.Description);

        mockStorage.Verify(s => s.Store(It.IsAny<IMessage>(), It.IsAny<MessageStorageOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        mockStorage.Verify(s => s.Retrieve<IMessage>(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
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
        var mockStorage = new Mock<IMessageStorage>();
        var timeProvider = TimeProvider.System;
        var customName = "custom_message_storage";

        mockStorage
            .Setup(s => s.Store(It.IsAny<IMessage>(), It.IsAny<MessageStorageOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());

        mockStorage
            .Setup(s => s.Retrieve<IMessage>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IMessage>());

        var healthCheck = new MessageStorageHealthCheck(mockStorage.Object, timeProvider, customName);

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

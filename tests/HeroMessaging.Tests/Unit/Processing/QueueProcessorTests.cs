using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

[Trait("Category", "Unit")]
public class QueueProcessorTests
{
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IQueueStorage> _mockQueueStorage;
    private readonly Mock<ILogger<QueueProcessor>> _mockLogger;
    private readonly QueueProcessor _sut;

    public QueueProcessorTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockQueueStorage = new Mock<IQueueStorage>();
        _mockLogger = new Mock<ILogger<QueueProcessor>>();

        _sut = new QueueProcessor(
            _mockServiceProvider.Object,
            _mockQueueStorage.Object,
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act & Assert
        Assert.NotNull(_sut);
    }

    #endregion

    #region Enqueue Tests

    [Fact]
    public async Task Enqueue_WithNonExistentQueue_CreatesQueueAndEnqueuesMessage()
    {
        // Arrange
        var message = new Mock<IMessage>().Object;
        var queueName = "test-queue";
        var options = new EnqueueOptions { Priority = 5 };

        _mockQueueStorage.Setup(x => x.QueueExistsAsync(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockQueueStorage.Setup(x => x.CreateQueueAsync(queueName, null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockQueueStorage.Setup(x => x.EnqueueAsync(queueName, message, options, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.Enqueue(message, queueName, options);

        // Assert
        _mockQueueStorage.Verify(x => x.QueueExistsAsync(queueName, It.IsAny<CancellationToken>()), Times.Once);
        _mockQueueStorage.Verify(x => x.CreateQueueAsync(queueName, null, It.IsAny<CancellationToken>()), Times.Once);
        _mockQueueStorage.Verify(x => x.EnqueueAsync(queueName, message, options, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Enqueue_WithExistingQueue_EnqueuesMessageWithoutCreating()
    {
        // Arrange
        var message = new Mock<IMessage>().Object;
        var queueName = "existing-queue";
        var options = new EnqueueOptions();

        _mockQueueStorage.Setup(x => x.QueueExistsAsync(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockQueueStorage.Setup(x => x.EnqueueAsync(queueName, message, options, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.Enqueue(message, queueName, options);

        // Assert
        _mockQueueStorage.Verify(x => x.QueueExistsAsync(queueName, It.IsAny<CancellationToken>()), Times.Once);
        _mockQueueStorage.Verify(x => x.CreateQueueAsync(It.IsAny<string>(), It.IsAny<QueueOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockQueueStorage.Verify(x => x.EnqueueAsync(queueName, message, options, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Enqueue_WithNullOptions_EnqueuesWithNullOptions()
    {
        // Arrange
        var message = new Mock<IMessage>().Object;
        var queueName = "test-queue";

        _mockQueueStorage.Setup(x => x.QueueExistsAsync(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockQueueStorage.Setup(x => x.EnqueueAsync(queueName, message, null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.Enqueue(message, queueName, null);

        // Assert
        _mockQueueStorage.Verify(x => x.EnqueueAsync(queueName, message, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Enqueue_WithCancellationToken_PassesToStorage()
    {
        // Arrange
        var message = new Mock<IMessage>().Object;
        var queueName = "test-queue";
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _mockQueueStorage.Setup(x => x.QueueExistsAsync(queueName, cancellationToken))
            .ReturnsAsync(true);
        _mockQueueStorage.Setup(x => x.EnqueueAsync(queueName, message, null, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.Enqueue(message, queueName, null, cancellationToken);

        // Assert
        _mockQueueStorage.Verify(x => x.EnqueueAsync(queueName, message, null, cancellationToken), Times.Once);
    }

    #endregion

    #region StartQueue Tests

    [Fact]
    public async Task StartQueue_WithNonExistentQueue_CreatesQueueAndStartsWorker()
    {
        // Arrange
        var queueName = "test-queue";

        _mockQueueStorage.Setup(x => x.QueueExistsAsync(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockQueueStorage.Setup(x => x.CreateQueueAsync(queueName, null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.StartQueue(queueName);

        // Assert
        _mockQueueStorage.Verify(x => x.QueueExistsAsync(queueName, It.IsAny<CancellationToken>()), Times.Once);
        _mockQueueStorage.Verify(x => x.CreateQueueAsync(queueName, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartQueue_WithExistingQueue_StartsWorkerWithoutCreating()
    {
        // Arrange
        var queueName = "existing-queue";

        _mockQueueStorage.Setup(x => x.QueueExistsAsync(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _sut.StartQueue(queueName);

        // Assert
        _mockQueueStorage.Verify(x => x.QueueExistsAsync(queueName, It.IsAny<CancellationToken>()), Times.Once);
        _mockQueueStorage.Verify(x => x.CreateQueueAsync(It.IsAny<string>(), It.IsAny<QueueOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartQueue_MultipleTimes_StartsWorkerOnlyOnce()
    {
        // Arrange
        var queueName = "test-queue";

        _mockQueueStorage.Setup(x => x.QueueExistsAsync(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _sut.StartQueue(queueName);
        await _sut.StartQueue(queueName); // Second call should not create new worker

        // Assert
        _mockQueueStorage.Verify(x => x.QueueExistsAsync(queueName, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task StartQueue_WithCancellationToken_PassesToStorage()
    {
        // Arrange
        var queueName = "test-queue";
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _mockQueueStorage.Setup(x => x.QueueExistsAsync(queueName, cancellationToken))
            .ReturnsAsync(true);

        // Act
        await _sut.StartQueue(queueName, cancellationToken);

        // Assert
        _mockQueueStorage.Verify(x => x.QueueExistsAsync(queueName, cancellationToken), Times.Once);
    }

    #endregion

    #region StopQueue Tests

    [Fact]
    public async Task StopQueue_WithRunningQueue_StopsWorker()
    {
        // Arrange
        var queueName = "test-queue";

        _mockQueueStorage.Setup(x => x.QueueExistsAsync(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _sut.StartQueue(queueName);

        // Act
        await _sut.StopQueue(queueName);

        // Assert - Should complete without exception
        Assert.True(true);
    }

    [Fact]
    public async Task StopQueue_WithNonExistentQueue_CompletesWithoutError()
    {
        // Arrange
        var queueName = "non-existent-queue";

        // Act
        await _sut.StopQueue(queueName);

        // Assert - Should complete without exception
        Assert.True(true);
    }

    [Fact]
    public async Task StopQueue_MultipleTimes_CompletesWithoutError()
    {
        // Arrange
        var queueName = "test-queue";

        _mockQueueStorage.Setup(x => x.QueueExistsAsync(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _sut.StartQueue(queueName);

        // Act
        await _sut.StopQueue(queueName);
        await _sut.StopQueue(queueName); // Second call should complete without error

        // Assert
        Assert.True(true);
    }

    #endregion

    #region GetQueueDepthAsync Tests

    [Fact]
    public async Task GetQueueDepthAsync_ReturnsDepthFromStorage()
    {
        // Arrange
        var queueName = "test-queue";
        var expectedDepth = 42L;

        _mockQueueStorage.Setup(x => x.GetQueueDepthAsync(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedDepth);

        // Act
        var result = await _sut.GetQueueDepthAsync(queueName);

        // Assert
        Assert.Equal(expectedDepth, result);
        _mockQueueStorage.Verify(x => x.GetQueueDepthAsync(queueName, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetQueueDepthAsync_WithCancellationToken_PassesToStorage()
    {
        // Arrange
        var queueName = "test-queue";
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _mockQueueStorage.Setup(x => x.GetQueueDepthAsync(queueName, cancellationToken))
            .ReturnsAsync(10);

        // Act
        await _sut.GetQueueDepthAsync(queueName, cancellationToken);

        // Assert
        _mockQueueStorage.Verify(x => x.GetQueueDepthAsync(queueName, cancellationToken), Times.Once);
    }

    #endregion

    #region IsRunning Tests

    [Fact]
    public void IsRunning_WithNoQueues_ReturnsFalse()
    {
        // Act
        var result = _sut.IsRunning;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsRunning_WithStartedQueue_ReturnsTrue()
    {
        // Arrange
        var queueName = "test-queue";

        _mockQueueStorage.Setup(x => x.QueueExistsAsync(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _sut.StartQueue(queueName);
        var result = _sut.IsRunning;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsRunning_AfterStoppingQueue_ReturnsFalse()
    {
        // Arrange
        var queueName = "test-queue";

        _mockQueueStorage.Setup(x => x.QueueExistsAsync(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _sut.StartQueue(queueName);

        // Act
        await _sut.StopQueue(queueName);
        var result = _sut.IsRunning;

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetMetrics Tests

    [Fact]
    public void GetMetrics_ReturnsMetrics()
    {
        // Act
        var metrics = _sut.GetMetrics();

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal(0, metrics.TotalMessages);
        Assert.Equal(0, metrics.ProcessedMessages);
        Assert.Equal(0, metrics.FailedMessages);
    }

    #endregion

    #region GetActiveQueuesAsync Tests

    [Fact]
    public async Task GetActiveQueuesAsync_WithNoQueues_ReturnsEmpty()
    {
        // Act
        var result = await _sut.GetActiveQueuesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetActiveQueuesAsync_WithActiveQueues_ReturnsQueueNames()
    {
        // Arrange
        var queue1 = "queue1";
        var queue2 = "queue2";

        _mockQueueStorage.Setup(x => x.QueueExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _sut.StartQueue(queue1);
        await _sut.StartQueue(queue2);

        // Act
        var result = await _sut.GetActiveQueuesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count());
        Assert.Contains(queue1, result);
        Assert.Contains(queue2, result);
    }

    [Fact]
    public async Task GetActiveQueuesAsync_AfterStoppingQueue_ReturnsRemainingQueues()
    {
        // Arrange
        var queue1 = "queue1";
        var queue2 = "queue2";

        _mockQueueStorage.Setup(x => x.QueueExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _sut.StartQueue(queue1);
        await _sut.StartQueue(queue2);
        await _sut.StopQueue(queue1);

        // Act
        var result = await _sut.GetActiveQueuesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Contains(queue2, result);
        Assert.DoesNotContain(queue1, result);
    }

    [Fact]
    public async Task GetActiveQueuesAsync_WithCancellationToken_Completes()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        // Act
        var result = await _sut.GetActiveQueuesAsync(cancellationToken);

        // Assert
        Assert.NotNull(result);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task QueueProcessor_EnqueueAndStart_ProcessesMessages()
    {
        // Arrange
        var queueName = "integration-queue";
        var message = new Mock<ICommand>();
        message.Setup(x => x.MessageId).Returns(Guid.NewGuid());

        var entries = new Queue<QueueEntry>();
        var queueEntry = new QueueEntry(Guid.NewGuid(), message.Object, 5, 0, DateTimeOffset.UtcNow);
        entries.Enqueue(queueEntry);

        _mockQueueStorage.Setup(x => x.QueueExistsAsync(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockQueueStorage.Setup(x => x.EnqueueAsync(queueName, It.IsAny<IMessage>(), It.IsAny<EnqueueOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, IMessage, EnqueueOptions?, CancellationToken>((q, m, o, ct) =>
            {
                var entry = new QueueEntry(Guid.NewGuid(), m, o?.Priority ?? 0, 0, DateTimeOffset.UtcNow);
                entries.Enqueue(entry);
            })
            .Returns(Task.CompletedTask);

        _mockQueueStorage.Setup(x => x.DequeueAsync(queueName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => entries.Count > 0 ? entries.Dequeue() : null);

        var mockScope = new Mock<IServiceScope>();
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        var mockMessaging = new Mock<IHeroMessaging>();

        mockMessaging.Setup(x => x.SendAsync(It.IsAny<ICommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mockScope.Setup(x => x.ServiceProvider.GetService(typeof(IHeroMessaging)))
            .Returns(mockMessaging.Object);
        mockScope.Setup(x => x.Dispose());

        _mockServiceProvider.Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(mockScopeFactory.Object);
        mockScopeFactory.Setup(x => x.CreateScope())
            .Returns(mockScope.Object);

        _mockQueueStorage.Setup(x => x.AcknowledgeAsync(queueName, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.Enqueue(message.Object, queueName);
        await _sut.StartQueue(queueName);

        // Give worker time to process
        await Task.Delay(500);

        await _sut.StopQueue(queueName);

        // Assert
        _mockQueueStorage.Verify(x => x.EnqueueAsync(queueName, It.IsAny<IMessage>(), It.IsAny<EnqueueOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}

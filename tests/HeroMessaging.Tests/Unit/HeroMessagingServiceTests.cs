using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Queries;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit;

[Trait("Category", "Unit")]
public class HeroMessagingServiceTests
{
    private readonly Mock<ICommandProcessor> _mockCommandProcessor;
    private readonly Mock<IQueryProcessor> _mockQueryProcessor;
    private readonly Mock<IEventBus> _mockEventBus;
    private readonly Mock<IQueueProcessor> _mockQueueProcessor;
    private readonly Mock<IOutboxProcessor> _mockOutboxProcessor;
    private readonly Mock<IInboxProcessor> _mockInboxProcessor;
    private readonly FakeTimeProvider _timeProvider;
    private readonly HeroMessagingService _sut;

    public HeroMessagingServiceTests()
    {
        _mockCommandProcessor = new Mock<ICommandProcessor>();
        _mockQueryProcessor = new Mock<IQueryProcessor>();
        _mockEventBus = new Mock<IEventBus>();
        _mockQueueProcessor = new Mock<IQueueProcessor>();
        _mockOutboxProcessor = new Mock<IOutboxProcessor>();
        _mockInboxProcessor = new Mock<IInboxProcessor>();
        _timeProvider = new FakeTimeProvider();

        _sut = new HeroMessagingService(
            _mockCommandProcessor.Object,
            _mockQueryProcessor.Object,
            _mockEventBus.Object,
            _timeProvider,
            logger: null,
            _mockQueueProcessor.Object,
            _mockOutboxProcessor.Object,
            _mockInboxProcessor.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new HeroMessagingService(
                _mockCommandProcessor.Object,
                _mockQueryProcessor.Object,
                _mockEventBus.Object,
                null!,
                logger: null,
                _mockQueueProcessor.Object,
                _mockOutboxProcessor.Object,
                _mockInboxProcessor.Object));

        Assert.Equal("timeProvider", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithOptionalNullProcessors_DoesNotThrow()
    {
        // Act
        var service = new HeroMessagingService(
            _mockCommandProcessor.Object,
            _mockQueryProcessor.Object,
            _mockEventBus.Object,
            _timeProvider,
            null,
            null,
            null);

        // Assert
        Assert.NotNull(service);
    }

    #endregion

    #region SendAsync Command Tests

    [Fact]
    public async Task SendAsync_WithVoidCommand_CallsCommandProcessorAndIncrementsMetrics()
    {
        // Arrange
        var command = new Mock<ICommand>().Object;
        _mockCommandProcessor.Setup(x => x.SendAsync(command, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.SendAsync(command, TestContext.Current.CancellationToken);

        // Assert
        _mockCommandProcessor.Verify(x => x.SendAsync(command, It.IsAny<CancellationToken>()), Times.Once);
        var metrics = _sut.GetMetrics();
        Assert.Equal(1, metrics.CommandsSent);
    }

    [Fact]
    public async Task SendAsync_WithCommandResponse_CallsCommandProcessorAndReturnsResponse()
    {
        // Arrange
        var command = new Mock<ICommand<string>>().Object;
        var expectedResponse = "test-response";
        _mockCommandProcessor.Setup(x => x.SendAsync(command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _sut.SendAsync(command, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedResponse, result);
        _mockCommandProcessor.Verify(x => x.SendAsync(command, It.IsAny<CancellationToken>()), Times.Once);
        var metrics = _sut.GetMetrics();
        Assert.Equal(1, metrics.CommandsSent);
    }

    [Fact]
    public async Task SendAsync_WithCancellationToken_PassesToCommandProcessor()
    {
        // Arrange
        var command = new Mock<ICommand>().Object;
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _mockCommandProcessor.Setup(x => x.SendAsync(command, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.SendAsync(command, cancellationToken);

        // Assert
        _mockCommandProcessor.Verify(x => x.SendAsync(command, cancellationToken), Times.Once);
    }

    #endregion

    #region SendAsync Query Tests

    [Fact]
    public async Task SendAsync_WithQuery_CallsQueryProcessorAndReturnsResponse()
    {
        // Arrange
        var query = new Mock<IQuery<int>>().Object;
        var expectedResponse = 42;
        _mockQueryProcessor.Setup(x => x.SendAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _sut.SendAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedResponse, result);
        _mockQueryProcessor.Verify(x => x.SendAsync(query, It.IsAny<CancellationToken>()), Times.Once);
        var metrics = _sut.GetMetrics();
        Assert.Equal(1, metrics.QueriesSent);
    }

    [Fact]
    public async Task SendAsync_WithQueryAndCancellationToken_PassesToQueryProcessor()
    {
        // Arrange
        var query = new Mock<IQuery<string>>().Object;
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _mockQueryProcessor.Setup(x => x.SendAsync(query, cancellationToken))
            .ReturnsAsync("result");

        // Act
        await _sut.SendAsync(query, cancellationToken);

        // Assert
        _mockQueryProcessor.Verify(x => x.SendAsync(query, cancellationToken), Times.Once);
    }

    #endregion

    #region PublishAsync Tests

    [Fact]
    public async Task PublishAsync_WithEvent_CallsEventBusAndIncrementsMetrics()
    {
        // Arrange
        var @event = new Mock<IEvent>().Object;
        _mockEventBus.Setup(x => x.PublishAsync(@event, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.PublishAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        _mockEventBus.Verify(x => x.PublishAsync(@event, It.IsAny<CancellationToken>()), Times.Once);
        var metrics = _sut.GetMetrics();
        Assert.Equal(1, metrics.EventsPublished);
    }

    [Fact]
    public async Task PublishAsync_WithCancellationToken_PassesToEventBus()
    {
        // Arrange
        var @event = new Mock<IEvent>().Object;
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _mockEventBus.Setup(x => x.PublishAsync(@event, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.PublishAsync(@event, cancellationToken);

        // Assert
        _mockEventBus.Verify(x => x.PublishAsync(@event, cancellationToken), Times.Once);
    }

    #endregion

    #region SendBatchAsync Command Tests

    [Fact]
    public async Task SendBatchAsync_WithNullCommands_ReturnsEmptyArray()
    {
        // Act
        var result = await _sut.SendBatchAsync(null!, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task SendBatchAsync_WithEmptyCommands_ReturnsEmptyArray()
    {
        // Arrange
        var commands = Array.Empty<ICommand>();

        // Act
        var result = await _sut.SendBatchAsync(commands, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task SendBatchAsync_WithValidCommands_ProcessesAllAndReturnsTrue()
    {
        // Arrange
        var commands = new List<ICommand>
        {
            new Mock<ICommand>().Object,
            new Mock<ICommand>().Object,
            new Mock<ICommand>().Object
        };

        _mockCommandProcessor.Setup(x => x.SendAsync(It.IsAny<ICommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var results = await _sut.SendBatchAsync(commands, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, Assert.True);
        _mockCommandProcessor.Verify(x => x.SendAsync(It.IsAny<ICommand>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        var metrics = _sut.GetMetrics();
        Assert.Equal(3, metrics.CommandsSent);
    }

    [Fact]
    public async Task SendBatchAsync_WithFailingCommand_ContinuesAndMarksFailure()
    {
        // Arrange
        var commands = new List<ICommand>
        {
            new Mock<ICommand>().Object,
            new Mock<ICommand>().Object,
            new Mock<ICommand>().Object
        };

        _mockCommandProcessor.SetupSequence(x => x.SendAsync(It.IsAny<ICommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .ThrowsAsync(new InvalidOperationException("Command failed"))
            .Returns(Task.CompletedTask);

        // Act - batch operations should NOT throw, they should continue processing
        var results = await _sut.SendBatchAsync(commands, TestContext.Current.CancellationToken);

        // Assert - first succeeded, second failed, third succeeded
        Assert.Equal(3, results.Count);
        Assert.True(results[0]);
        Assert.False(results[1]);
        Assert.True(results[2]);
    }

    [Fact]
    public async Task SendBatchAsync_WithTypedCommands_ProcessesAllAndReturnsResponses()
    {
        // Arrange
        var commands = new List<ICommand<string>>
        {
            new Mock<ICommand<string>>().Object,
            new Mock<ICommand<string>>().Object
        };

        _mockCommandProcessor.SetupSequence(x => x.SendAsync(It.IsAny<ICommand<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("result1")
            .ReturnsAsync("result2");

        // Act
        var results = await _sut.SendBatchAsync(commands, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal("result1", results[0]);
        Assert.Equal("result2", results[1]);
        var metrics = _sut.GetMetrics();
        Assert.Equal(2, metrics.CommandsSent);
    }

    [Fact]
    public async Task SendBatchAsync_WithNullTypedCommands_ReturnsEmptyArray()
    {
        // Act
        var result = await _sut.SendBatchAsync<string>(null!);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region PublishBatchAsync Tests

    [Fact]
    public async Task PublishBatchAsync_WithNullEvents_ReturnsEmptyArray()
    {
        // Act
        var result = await _sut.PublishBatchAsync(null!, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task PublishBatchAsync_WithEmptyEvents_ReturnsEmptyArray()
    {
        // Arrange
        var events = Array.Empty<IEvent>();

        // Act
        var result = await _sut.PublishBatchAsync(events, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task PublishBatchAsync_WithValidEvents_PublishesAllAndReturnsTrue()
    {
        // Arrange
        var events = new List<IEvent>
        {
            new Mock<IEvent>().Object,
            new Mock<IEvent>().Object
        };

        _mockEventBus.Setup(x => x.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var results = await _sut.PublishBatchAsync(events, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, Assert.True);
        _mockEventBus.Verify(x => x.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        var metrics = _sut.GetMetrics();
        Assert.Equal(2, metrics.EventsPublished);
    }

    [Fact]
    public async Task PublishBatchAsync_WithFailingEvent_ContinuesAndMarksFailure()
    {
        // Arrange
        var events = new List<IEvent>
        {
            new Mock<IEvent>().Object,
            new Mock<IEvent>().Object,
            new Mock<IEvent>().Object
        };

        _mockEventBus.SetupSequence(x => x.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .ThrowsAsync(new InvalidOperationException("Event failed"))
            .Returns(Task.CompletedTask);

        // Act - batch operations should NOT throw, they should continue processing
        var results = await _sut.PublishBatchAsync(events, TestContext.Current.CancellationToken);

        // Assert - first succeeded, second failed, third succeeded
        Assert.Equal(3, results.Count);
        Assert.True(results[0]);
        Assert.False(results[1]);
        Assert.True(results[2]);
    }

    #endregion

    #region EnqueueAsync Tests

    [Fact]
    public async Task EnqueueAsync_WithQueueProcessor_CallsQueueProcessorAndIncrementsMetrics()
    {
        // Arrange
        var message = new Mock<IMessage>().Object;
        var queueName = "test-queue";
        var options = new EnqueueOptions();

        _mockQueueProcessor.Setup(x => x.EnqueueAsync(message, queueName, options, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.EnqueueAsync(message, queueName, options, TestContext.Current.CancellationToken);

        // Assert
        _mockQueueProcessor.Verify(x => x.EnqueueAsync(message, queueName, options, It.IsAny<CancellationToken>()), Times.Once);
        var metrics = _sut.GetMetrics();
        Assert.Equal(1, metrics.MessagesQueued);
    }

    [Fact]
    public async Task EnqueueAsync_WithoutQueueProcessor_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = new HeroMessagingService(
            _mockCommandProcessor.Object,
            _mockQueryProcessor.Object,
            _mockEventBus.Object,
            _timeProvider,
            null,
            null,
            null);

        var message = new Mock<IMessage>().Object;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.EnqueueAsync(message, "queue", TestContext.Current.CancellationToken));

        Assert.Contains("Queue functionality is not enabled", exception.Message);
        Assert.Contains("WithQueues()", exception.Message);
    }

    #endregion

    #region StartQueueAsync Tests

    [Fact]
    public async Task StartQueueAsync_WithQueueProcessor_CallsQueueProcessor()
    {
        // Arrange
        var queueName = "test-queue";
        _mockQueueProcessor.Setup(x => x.StartQueueAsync(queueName, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.StartQueueAsync(queueName, TestContext.Current.CancellationToken);

        // Assert
        _mockQueueProcessor.Verify(x => x.StartQueueAsync(queueName, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartQueueAsync_WithoutQueueProcessor_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = new HeroMessagingService(
            _mockCommandProcessor.Object,
            _mockQueryProcessor.Object,
            _mockEventBus.Object,
            _timeProvider,
            null,
            null,
            null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.StartQueueAsync("queue", TestContext.Current.CancellationToken));

        Assert.Contains("Queue functionality is not enabled", exception.Message);
    }

    #endregion

    #region StopQueueAsync Tests

    [Fact]
    public async Task StopQueueAsync_WithQueueProcessor_CallsQueueProcessor()
    {
        // Arrange
        var queueName = "test-queue";
        _mockQueueProcessor.Setup(x => x.StopQueueAsync(queueName, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.StopQueueAsync(queueName, TestContext.Current.CancellationToken);

        // Assert
        _mockQueueProcessor.Verify(x => x.StopQueueAsync(queueName, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopQueueAsync_WithoutQueueProcessor_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = new HeroMessagingService(
            _mockCommandProcessor.Object,
            _mockQueryProcessor.Object,
            _mockEventBus.Object,
            _timeProvider,
            null,
            null,
            null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.StopQueueAsync("queue", TestContext.Current.CancellationToken));

        Assert.Contains("Queue functionality is not enabled", exception.Message);
    }

    #endregion

    #region PublishToOutboxAsync Tests

    [Fact]
    public async Task PublishToOutboxAsync_WithOutboxProcessor_CallsOutboxProcessorAndIncrementsMetrics()
    {
        // Arrange
        var message = new Mock<IMessage>().Object;
        var options = new OutboxOptions();

        _mockOutboxProcessor.Setup(x => x.PublishToOutboxAsync(message, options, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.PublishToOutboxAsync(message, options, TestContext.Current.CancellationToken);

        // Assert
        _mockOutboxProcessor.Verify(x => x.PublishToOutboxAsync(message, options, It.IsAny<CancellationToken>()), Times.Once);
        var metrics = _sut.GetMetrics();
        Assert.Equal(1, metrics.OutboxMessages);
    }

    [Fact]
    public async Task PublishToOutboxAsync_WithoutOutboxProcessor_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = new HeroMessagingService(
            _mockCommandProcessor.Object,
            _mockQueryProcessor.Object,
            _mockEventBus.Object,
            _timeProvider,
            null,
            null,
            null);

        var message = new Mock<IMessage>().Object;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.PublishToOutboxAsync(message, TestContext.Current.CancellationToken));

        Assert.Contains("Outbox functionality is not enabled", exception.Message);
        Assert.Contains("WithOutbox()", exception.Message);
    }

    #endregion

    #region ProcessIncomingAsync Tests

    [Fact]
    public async Task ProcessIncomingAsync_WithInboxProcessor_CallsInboxProcessorAndIncrementsMetrics()
    {
        // Arrange
        var message = new Mock<IMessage>().Object;
        var options = new InboxOptions();

        _mockInboxProcessor.Setup(x => x.ProcessIncomingAsync(message, options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _sut.ProcessIncomingAsync(message, options, TestContext.Current.CancellationToken);

        // Assert
        _mockInboxProcessor.Verify(x => x.ProcessIncomingAsync(message, options, It.IsAny<CancellationToken>()), Times.Once);
        var metrics = _sut.GetMetrics();
        Assert.Equal(1, metrics.InboxMessages);
    }

    [Fact]
    public async Task ProcessIncomingAsync_WithoutInboxProcessor_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = new HeroMessagingService(
            _mockCommandProcessor.Object,
            _mockQueryProcessor.Object,
            _mockEventBus.Object,
            _timeProvider,
            null,
            null,
            null);

        var message = new Mock<IMessage>().Object;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.ProcessIncomingAsync(message, TestContext.Current.CancellationToken));

        Assert.Contains("Inbox functionality is not enabled", exception.Message);
        Assert.Contains("WithInbox()", exception.Message);
    }

    #endregion

    #region GetMetrics Tests

    [Fact]
    public void GetMetrics_WithNoActivity_ReturnsZeroMetrics()
    {
        // Act
        var metrics = _sut.GetMetrics();

        // Assert
        Assert.Equal(0, metrics.CommandsSent);
        Assert.Equal(0, metrics.QueriesSent);
        Assert.Equal(0, metrics.EventsPublished);
        Assert.Equal(0, metrics.MessagesQueued);
        Assert.Equal(0, metrics.OutboxMessages);
        Assert.Equal(0, metrics.InboxMessages);
    }

    [Fact]
    public async Task GetMetrics_AfterMultipleOperations_ReturnsAccumulatedMetrics()
    {
        // Arrange
        _mockCommandProcessor.Setup(x => x.SendAsync(It.IsAny<ICommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockQueryProcessor.Setup(x => x.SendAsync(It.IsAny<IQuery<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockEventBus.Setup(x => x.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.SendAsync(new Mock<ICommand>().Object);
        await _sut.SendAsync(new Mock<ICommand>().Object);
        await _sut.SendAsync(new Mock<IQuery<int>>().Object);
        await _sut.PublishAsync(new Mock<IEvent>().Object);

        var metrics = _sut.GetMetrics();

        // Assert
        Assert.Equal(2, metrics.CommandsSent);
        Assert.Equal(1, metrics.QueriesSent);
        Assert.Equal(1, metrics.EventsPublished);
    }

    #endregion

    #region GetHealth Tests

    [Fact]
    public void GetHealth_WithAllProcessors_ReturnsHealthyComponents()
    {
        // Act
        var health = _sut.GetHealth();

        // Assert
        Assert.True(health.IsHealthy);
        Assert.Equal(6, health.Components.Count);

        Assert.True(health.Components["CommandProcessor"].IsHealthy);
        Assert.Equal("Operational", health.Components["CommandProcessor"].Status);

        Assert.True(health.Components["QueryProcessor"].IsHealthy);
        Assert.Equal("Operational", health.Components["QueryProcessor"].Status);

        Assert.True(health.Components["EventBus"].IsHealthy);
        Assert.Equal("Operational", health.Components["EventBus"].Status);

        Assert.True(health.Components["QueueProcessor"].IsHealthy);
        Assert.Equal("Operational", health.Components["QueueProcessor"].Status);

        Assert.True(health.Components["OutboxProcessor"].IsHealthy);
        Assert.Equal("Operational", health.Components["OutboxProcessor"].Status);

        Assert.True(health.Components["InboxProcessor"].IsHealthy);
        Assert.Equal("Operational", health.Components["InboxProcessor"].Status);
    }

    [Fact]
    public void GetHealth_WithoutOptionalProcessors_ReturnsNotConfiguredStatus()
    {
        // Arrange
        var service = new HeroMessagingService(
            _mockCommandProcessor.Object,
            _mockQueryProcessor.Object,
            _mockEventBus.Object,
            _timeProvider,
            null,
            null,
            null);

        // Act
        var health = service.GetHealth();

        // Assert
        Assert.True(health.IsHealthy);

        Assert.True(health.Components["CommandProcessor"].IsHealthy);
        Assert.True(health.Components["QueryProcessor"].IsHealthy);
        Assert.True(health.Components["EventBus"].IsHealthy);

        Assert.False(health.Components["QueueProcessor"].IsHealthy);
        Assert.Equal("Not Configured", health.Components["QueueProcessor"].Status);

        Assert.False(health.Components["OutboxProcessor"].IsHealthy);
        Assert.Equal("Not Configured", health.Components["OutboxProcessor"].Status);

        Assert.False(health.Components["InboxProcessor"].IsHealthy);
        Assert.Equal("Not Configured", health.Components["InboxProcessor"].Status);
    }

    [Fact]
    public void GetHealth_UsesTimeProviderForLastChecked()
    {
        // Arrange
        var expectedTime = new DateTimeOffset(2025, 11, 13, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(expectedTime);

        // Act
        var health = _sut.GetHealth();

        // Assert
        Assert.All(health.Components.Values, component =>
            Assert.Equal(expectedTime, component.LastChecked));
    }

    #endregion
}

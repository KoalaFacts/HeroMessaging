using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Processing;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit;

/// <summary>
/// Unit tests for HeroMessagingService
/// Tests the facade service coordinating commands, queries, events, queues, outbox, and inbox
/// </summary>
[Trait("Category", "Unit")]
public sealed class HeroMessagingServiceTests
{
    private readonly Mock<ICommandProcessor> _commandProcessorMock;
    private readonly Mock<IQueryProcessor> _queryProcessorMock;
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly Mock<IQueueProcessor> _queueProcessorMock;
    private readonly Mock<IOutboxProcessor> _outboxProcessorMock;
    private readonly Mock<IInboxProcessor> _inboxProcessorMock;
    private readonly FakeTimeProvider _timeProvider;
    private readonly HeroMessagingService _service;

    public HeroMessagingServiceTests()
    {
        _commandProcessorMock = new Mock<ICommandProcessor>();
        _queryProcessorMock = new Mock<IQueryProcessor>();
        _eventBusMock = new Mock<IEventBus>();
        _queueProcessorMock = new Mock<IQueueProcessor>();
        _outboxProcessorMock = new Mock<IOutboxProcessor>();
        _inboxProcessorMock = new Mock<IInboxProcessor>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));

        _service = new HeroMessagingService(
            _commandProcessorMock.Object,
            _queryProcessorMock.Object,
            _eventBusMock.Object,
            _timeProvider,
            _queueProcessorMock.Object,
            _outboxProcessorMock.Object,
            _inboxProcessorMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new HeroMessagingService(
                _commandProcessorMock.Object,
                _queryProcessorMock.Object,
                _eventBusMock.Object,
                null!));

        Assert.Equal("timeProvider", exception.ParamName);
    }

    #endregion

    #region Command Tests

    [Fact]
    public async Task SendAsync_WithCommand_DelegatesToCommandProcessor()
    {
        // Arrange
        var command = new TestCommand();

        // Act
        await _service.SendAsync(command);

        // Assert
        _commandProcessorMock.Verify(x => x.Send(command, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_WithCommand_IncrementsMetrics()
    {
        // Arrange
        var command = new TestCommand();

        // Act
        await _service.SendAsync(command);

        // Assert
        var metrics = _service.GetMetrics();
        Assert.Equal(1, metrics.CommandsSent);
    }

    [Fact]
    public async Task SendAsync_WithCommandWithResponse_ReturnsResponse()
    {
        // Arrange
        var command = new TestCommandWithResponse { Value = 42 };
        var expectedResponse = new TestResponse { Result = "Success" };
        _commandProcessorMock.Setup(x => x.Send(command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var response = await _service.SendAsync<TestResponse>(command);

        // Assert
        Assert.Equal(expectedResponse, response);
        _commandProcessorMock.Verify(x => x.Send(command, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_WithCommandWithResponse_IncrementsMetrics()
    {
        // Arrange
        var command = new TestCommandWithResponse { Value = 42 };
        _commandProcessorMock.Setup(x => x.Send(command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TestResponse());

        // Act
        await _service.SendAsync<TestResponse>(command);

        // Assert
        var metrics = _service.GetMetrics();
        Assert.Equal(1, metrics.CommandsSent);
    }

    #endregion

    #region Query Tests

    [Fact]
    public async Task SendAsync_WithQuery_ReturnsQueryResponse()
    {
        // Arrange
        var query = new TestQuery { Id = 123 };
        var expectedResponse = new TestQueryResponse { Data = "Test Data" };
        _queryProcessorMock.Setup(x => x.Send(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var response = await _service.SendAsync<TestQueryResponse>(query);

        // Assert
        Assert.Equal(expectedResponse, response);
        _queryProcessorMock.Verify(x => x.Send(query, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_WithQuery_IncrementsMetrics()
    {
        // Arrange
        var query = new TestQuery { Id = 123 };
        _queryProcessorMock.Setup(x => x.Send(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TestQueryResponse());

        // Act
        await _service.SendAsync<TestQueryResponse>(query);

        // Assert
        var metrics = _service.GetMetrics();
        Assert.Equal(1, metrics.QueriesSent);
    }

    #endregion

    #region Event Tests

    [Fact]
    public async Task PublishAsync_WithEvent_DelegatesToEventBus()
    {
        // Arrange
        var @event = new TestEvent { MessageId = Guid.NewGuid() };

        // Act
        await _service.PublishAsync(@event);

        // Assert
        _eventBusMock.Verify(x => x.Publish(@event, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_WithEvent_IncrementsMetrics()
    {
        // Arrange
        var @event = new TestEvent { MessageId = Guid.NewGuid() };

        // Act
        await _service.PublishAsync(@event);

        // Assert
        var metrics = _service.GetMetrics();
        Assert.Equal(1, metrics.EventsPublished);
    }

    #endregion

    #region Batch Command Tests

    [Fact]
    public async Task SendBatchAsync_WithNullCommands_ReturnsEmptyList()
    {
        // Act
        var results = await _service.SendBatchAsync(null!);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SendBatchAsync_WithEmptyCommands_ReturnsEmptyList()
    {
        // Act
        var results = await _service.SendBatchAsync(Array.Empty<ICommand>());

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SendBatchAsync_WithMultipleCommands_ProcessesAll()
    {
        // Arrange
        var commands = new List<ICommand>
        {
            new TestCommand(),
            new TestCommand(),
            new TestCommand()
        };

        // Act
        var results = await _service.SendBatchAsync(commands);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r));
        _commandProcessorMock.Verify(x => x.Send(It.IsAny<ICommand>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task SendBatchAsync_WithMultipleCommands_IncrementsMetrics()
    {
        // Arrange
        var commands = new List<ICommand> { new TestCommand(), new TestCommand(), new TestCommand() };

        // Act
        await _service.SendBatchAsync(commands);

        // Assert
        var metrics = _service.GetMetrics();
        Assert.Equal(3, metrics.CommandsSent);
    }

    [Fact]
    public async Task SendBatchAsync_WhenCommandFails_RethrowsException()
    {
        // Arrange
        var commands = new List<ICommand> { new TestCommand(), new TestCommand() };
        _commandProcessorMock.SetupSequence(x => x.Send(It.IsAny<ICommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Throws(new InvalidOperationException("Command failed"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SendBatchAsync(commands));
    }

    [Fact]
    public async Task SendBatchAsync_WithResponseCommands_ReturnsAllResponses()
    {
        // Arrange
        var commands = new List<ICommand<TestResponse>>
        {
            new TestCommandWithResponse { Value = 1 },
            new TestCommandWithResponse { Value = 2 },
            new TestCommandWithResponse { Value = 3 }
        };
        _commandProcessorMock.SetupSequence(x => x.Send(It.IsAny<ICommand<TestResponse>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TestResponse { Result = "R1" })
            .ReturnsAsync(new TestResponse { Result = "R2" })
            .ReturnsAsync(new TestResponse { Result = "R3" });

        // Act
        var results = await _service.SendBatchAsync(commands);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Equal("R1", results[0].Result);
        Assert.Equal("R2", results[1].Result);
        Assert.Equal("R3", results[2].Result);
    }

    [Fact]
    public async Task SendBatchAsync_WithResponseCommandsNull_ReturnsEmptyList()
    {
        // Act
        var results = await _service.SendBatchAsync<TestResponse>(null!);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SendBatchAsync_WithResponseCommandsEmpty_ReturnsEmptyList()
    {
        // Act
        var results = await _service.SendBatchAsync(Array.Empty<ICommand<TestResponse>>());

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region Batch Event Tests

    [Fact]
    public async Task PublishBatchAsync_WithNullEvents_ReturnsEmptyList()
    {
        // Act
        var results = await _service.PublishBatchAsync(null!);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task PublishBatchAsync_WithEmptyEvents_ReturnsEmptyList()
    {
        // Act
        var results = await _service.PublishBatchAsync(Array.Empty<IEvent>());

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task PublishBatchAsync_WithMultipleEvents_ProcessesAll()
    {
        // Arrange
        var events = new List<IEvent>
        {
            new TestEvent { MessageId = Guid.NewGuid() },
            new TestEvent { MessageId = Guid.NewGuid() },
            new TestEvent { MessageId = Guid.NewGuid() }
        };

        // Act
        var results = await _service.PublishBatchAsync(events);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r));
        _eventBusMock.Verify(x => x.Publish(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task PublishBatchAsync_WithMultipleEvents_IncrementsMetrics()
    {
        // Arrange
        var events = new List<IEvent>
        {
            new TestEvent { MessageId = Guid.NewGuid() },
            new TestEvent { MessageId = Guid.NewGuid() }
        };

        // Act
        await _service.PublishBatchAsync(events);

        // Assert
        var metrics = _service.GetMetrics();
        Assert.Equal(2, metrics.EventsPublished);
    }

    [Fact]
    public async Task PublishBatchAsync_WhenEventFails_RethrowsException()
    {
        // Arrange
        var events = new List<IEvent>
        {
            new TestEvent { MessageId = Guid.NewGuid() },
            new TestEvent { MessageId = Guid.NewGuid() }
        };
        _eventBusMock.SetupSequence(x => x.Publish(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Throws(new InvalidOperationException("Event failed"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.PublishBatchAsync(events));
    }

    #endregion

    #region Queue Tests

    [Fact]
    public async Task EnqueueAsync_WithoutQueueProcessor_ThrowsInvalidOperationException()
    {
        // Arrange
        var serviceWithoutQueue = new HeroMessagingService(
            _commandProcessorMock.Object,
            _queryProcessorMock.Object,
            _eventBusMock.Object,
            _timeProvider);

        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            serviceWithoutQueue.EnqueueAsync(message, "test-queue"));

        Assert.Contains("Queue functionality is not enabled", exception.Message);
    }

    [Fact]
    public async Task EnqueueAsync_WithQueueProcessor_DelegatesToProcessor()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var options = new EnqueueOptions();

        // Act
        await _service.EnqueueAsync(message, "test-queue", options);

        // Assert
        _queueProcessorMock.Verify(x => x.Enqueue(message, "test-queue", options, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnqueueAsync_WithQueueProcessor_IncrementsMetrics()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act
        await _service.EnqueueAsync(message, "test-queue");

        // Assert
        var metrics = _service.GetMetrics();
        Assert.Equal(1, metrics.MessagesQueued);
    }

    [Fact]
    public async Task StartQueueAsync_WithoutQueueProcessor_ThrowsInvalidOperationException()
    {
        // Arrange
        var serviceWithoutQueue = new HeroMessagingService(
            _commandProcessorMock.Object,
            _queryProcessorMock.Object,
            _eventBusMock.Object,
            _timeProvider);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            serviceWithoutQueue.StartQueueAsync("test-queue"));

        Assert.Contains("Queue functionality is not enabled", exception.Message);
    }

    [Fact]
    public async Task StartQueueAsync_WithQueueProcessor_DelegatesToProcessor()
    {
        // Act
        await _service.StartQueueAsync("test-queue");

        // Assert
        _queueProcessorMock.Verify(x => x.StartQueue("test-queue", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopQueueAsync_WithoutQueueProcessor_ThrowsInvalidOperationException()
    {
        // Arrange
        var serviceWithoutQueue = new HeroMessagingService(
            _commandProcessorMock.Object,
            _queryProcessorMock.Object,
            _eventBusMock.Object,
            _timeProvider);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            serviceWithoutQueue.StopQueueAsync("test-queue"));

        Assert.Contains("Queue functionality is not enabled", exception.Message);
    }

    [Fact]
    public async Task StopQueueAsync_WithQueueProcessor_DelegatesToProcessor()
    {
        // Act
        await _service.StopQueueAsync("test-queue");

        // Assert
        _queueProcessorMock.Verify(x => x.StopQueue("test-queue", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Outbox Tests

    [Fact]
    public async Task PublishToOutboxAsync_WithoutOutboxProcessor_ThrowsInvalidOperationException()
    {
        // Arrange
        var serviceWithoutOutbox = new HeroMessagingService(
            _commandProcessorMock.Object,
            _queryProcessorMock.Object,
            _eventBusMock.Object,
            _timeProvider);

        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            serviceWithoutOutbox.PublishToOutboxAsync(message));

        Assert.Contains("Outbox functionality is not enabled", exception.Message);
    }

    [Fact]
    public async Task PublishToOutboxAsync_WithOutboxProcessor_DelegatesToProcessor()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var options = new OutboxOptions();

        // Act
        await _service.PublishToOutboxAsync(message, options);

        // Assert
        _outboxProcessorMock.Verify(x => x.PublishToOutbox(message, options, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishToOutboxAsync_WithOutboxProcessor_IncrementsMetrics()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act
        await _service.PublishToOutboxAsync(message);

        // Assert
        var metrics = _service.GetMetrics();
        Assert.Equal(1, metrics.OutboxMessages);
    }

    #endregion

    #region Inbox Tests

    [Fact]
    public async Task ProcessIncomingAsync_WithoutInboxProcessor_ThrowsInvalidOperationException()
    {
        // Arrange
        var serviceWithoutInbox = new HeroMessagingService(
            _commandProcessorMock.Object,
            _queryProcessorMock.Object,
            _eventBusMock.Object,
            _timeProvider);

        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            serviceWithoutInbox.ProcessIncomingAsync(message));

        Assert.Contains("Inbox functionality is not enabled", exception.Message);
    }

    [Fact]
    public async Task ProcessIncomingAsync_WithInboxProcessor_DelegatesToProcessor()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var options = new InboxOptions();

        // Act
        await _service.ProcessIncomingAsync(message, options);

        // Assert
        _inboxProcessorMock.Verify(x => x.ProcessIncoming(message, options, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessIncomingAsync_WithInboxProcessor_IncrementsMetrics()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act
        await _service.ProcessIncomingAsync(message);

        // Assert
        var metrics = _service.GetMetrics();
        Assert.Equal(1, metrics.InboxMessages);
    }

    #endregion

    #region Metrics Tests

    [Fact]
    public void GetMetrics_ReturnsCurrentMetrics()
    {
        // Act
        var metrics = _service.GetMetrics();

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal(0, metrics.CommandsSent);
        Assert.Equal(0, metrics.QueriesSent);
        Assert.Equal(0, metrics.EventsPublished);
        Assert.Equal(0, metrics.MessagesQueued);
        Assert.Equal(0, metrics.OutboxMessages);
        Assert.Equal(0, metrics.InboxMessages);
    }

    #endregion

    #region Health Tests

    [Fact]
    public void GetHealth_WithAllProcessors_ReturnsHealthyStatus()
    {
        // Act
        var health = _service.GetHealth();

        // Assert
        Assert.NotNull(health);
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
            _commandProcessorMock.Object,
            _queryProcessorMock.Object,
            _eventBusMock.Object,
            _timeProvider);

        // Act
        var health = service.GetHealth();

        // Assert
        Assert.NotNull(health);
        Assert.True(health.IsHealthy);

        Assert.False(health.Components["QueueProcessor"].IsHealthy);
        Assert.Equal("Not Configured", health.Components["QueueProcessor"].Status);

        Assert.False(health.Components["OutboxProcessor"].IsHealthy);
        Assert.Equal("Not Configured", health.Components["OutboxProcessor"].Status);

        Assert.False(health.Components["InboxProcessor"].IsHealthy);
        Assert.Equal("Not Configured", health.Components["InboxProcessor"].Status);
    }

    [Fact]
    public void GetHealth_UsesCurrentTime()
    {
        // Arrange
        var expectedTime = _timeProvider.GetUtcNow().DateTime;

        // Act
        var health = _service.GetHealth();

        // Assert
        Assert.All(health.Components.Values, c => Assert.Equal(expectedTime, c.LastChecked));
    }

    #endregion

    #region Test Classes

    private class TestCommand : ICommand
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private class TestCommandWithResponse : ICommand<TestResponse>
    {
        public int Value { get; set; }
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private class TestResponse
    {
        public string Result { get; set; } = string.Empty;
    }

    private class TestQuery : IQuery<TestQueryResponse>
    {
        public int Id { get; set; }
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private class TestQueryResponse
    {
        public string Data { get; set; } = string.Empty;
    }

    private class TestEvent : IEvent
    {
        public Guid MessageId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    #endregion
}

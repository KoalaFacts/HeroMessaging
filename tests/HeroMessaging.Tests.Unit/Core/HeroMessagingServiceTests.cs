using System;
using System.Threading;
using System.Threading.Tasks;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Core;
using HeroMessaging.Core.Processing;
using HeroMessaging.Tests.Unit.TestDoubles;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Core;

public class HeroMessagingServiceTests
{
    private readonly Mock<ICommandProcessor> _commandProcessorMock;
    private readonly Mock<IQueryProcessor> _queryProcessorMock;
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly Mock<IQueueProcessor> _queueProcessorMock;
    private readonly Mock<IOutboxProcessor> _outboxProcessorMock;
    private readonly Mock<IInboxProcessor> _inboxProcessorMock;
    private readonly Mock<ILogger<HeroMessagingService>> _loggerMock;
    private readonly HeroMessagingService _sut;

    public HeroMessagingServiceTests()
    {
        _commandProcessorMock = new Mock<ICommandProcessor>();
        _queryProcessorMock = new Mock<IQueryProcessor>();
        _eventBusMock = new Mock<IEventBus>();
        _queueProcessorMock = new Mock<IQueueProcessor>();
        _outboxProcessorMock = new Mock<IOutboxProcessor>();
        _inboxProcessorMock = new Mock<IInboxProcessor>();
        _loggerMock = new Mock<ILogger<HeroMessagingService>>();

        _sut = new HeroMessagingService(
            _commandProcessorMock.Object,
            _queryProcessorMock.Object,
            _eventBusMock.Object,
            _loggerMock.Object,
            _queueProcessorMock.Object,
            _outboxProcessorMock.Object,
            _inboxProcessorMock.Object);
    }

    [Fact]
    public async Task Send_Command_Should_Delegate_To_CommandProcessor()
    {
        // Arrange
        var command = new TestCommand();
        var cancellationToken = CancellationToken.None;

        // Act
        await _sut.Send(command, cancellationToken);

        // Assert
        _commandProcessorMock.Verify(x => x.Send(command, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task Send_CommandWithResponse_Should_Delegate_To_CommandProcessor()
    {
        // Arrange
        var command = new TestCommandWithResponse();
        var expectedResponse = "test-response";
        var cancellationToken = CancellationToken.None;
        
        _commandProcessorMock
            .Setup(x => x.Send(command, cancellationToken))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _sut.Send<string>(command, cancellationToken);

        // Assert
        Assert.Equal(expectedResponse, result);
        _commandProcessorMock.Verify(x => x.Send(command, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task Send_Query_Should_Delegate_To_QueryProcessor()
    {
        // Arrange
        var query = new TestQuery();
        var expectedResponse = "query-result";
        var cancellationToken = CancellationToken.None;
        
        _queryProcessorMock
            .Setup(x => x.Send<string>(query, cancellationToken))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _sut.Send<string>(query, cancellationToken);

        // Assert
        Assert.Equal(expectedResponse, result);
        _queryProcessorMock.Verify(x => x.Send<string>(query, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task Publish_Event_Should_Delegate_To_EventBus()
    {
        // Arrange
        var @event = new TestEvent();
        var cancellationToken = CancellationToken.None;

        // Act
        await _sut.Publish(@event, cancellationToken);

        // Assert
        _eventBusMock.Verify(x => x.Publish(@event, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task Enqueue_Should_Delegate_To_QueueProcessor()
    {
        // Arrange
        var message = new TestMessage();
        var queueName = "test-queue";
        var options = new EnqueueOptions { Priority = 5 };
        var cancellationToken = CancellationToken.None;

        // Act
        await _sut.Enqueue(message, queueName, options, cancellationToken);

        // Assert
        _queueProcessorMock.Verify(x => x.Enqueue(message, queueName, options, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task Enqueue_Should_Throw_When_QueueProcessor_Not_Configured()
    {
        // Arrange
        var service = new HeroMessagingService(
            _commandProcessorMock.Object,
            _queryProcessorMock.Object,
            _eventBusMock.Object,
            _loggerMock.Object);
        
        var message = new TestMessage();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.Enqueue(message, "queue", null, CancellationToken.None));
        Assert.Equal("Queue functionality is not enabled. Call WithQueues() during configuration.", exception.Message);
    }

    [Fact]
    public async Task PublishToOutbox_Should_Delegate_To_OutboxProcessor()
    {
        // Arrange
        var message = new TestMessage();
        var options = new OutboxOptions { Destination = "external-system" };
        var cancellationToken = CancellationToken.None;

        // Act
        await _sut.PublishToOutbox(message, options, cancellationToken);

        // Assert
        _outboxProcessorMock.Verify(x => x.PublishToOutbox(message, options, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task PublishToOutbox_Should_Throw_When_OutboxProcessor_Not_Configured()
    {
        // Arrange
        var service = new HeroMessagingService(
            _commandProcessorMock.Object,
            _queryProcessorMock.Object,
            _eventBusMock.Object,
            _loggerMock.Object);
        
        var message = new TestMessage();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PublishToOutbox(message, null, CancellationToken.None));
        Assert.Equal("Outbox functionality is not enabled. Call WithOutbox() during configuration.", exception.Message);
    }

    [Fact]
    public async Task ProcessIncoming_Should_Delegate_To_InboxProcessor()
    {
        // Arrange
        var message = new TestMessage();
        var options = new InboxOptions { RequireIdempotency = true };
        var cancellationToken = CancellationToken.None;

        // Act
        await _sut.ProcessIncoming(message, options, cancellationToken);

        // Assert
        _inboxProcessorMock.Verify(x => x.ProcessIncoming(message, options, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task ProcessIncoming_Should_Throw_When_InboxProcessor_Not_Configured()
    {
        // Arrange
        var service = new HeroMessagingService(
            _commandProcessorMock.Object,
            _queryProcessorMock.Object,
            _eventBusMock.Object,
            _loggerMock.Object);
        
        var message = new TestMessage();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProcessIncoming(message, null, CancellationToken.None));
        Assert.Equal("Inbox functionality is not enabled. Call WithInbox() during configuration.", exception.Message);
    }

    [Fact]
    public void GetMetrics_Should_Return_Current_Metrics()
    {
        // Act
        var metrics = _sut.GetMetrics();

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal(0, metrics.CommandsSent);
        Assert.Equal(0, metrics.QueriesSent);
        Assert.Equal(0, metrics.EventsPublished);
    }

    [Fact]
    public async Task GetMetrics_Should_Track_Operations()
    {
        // Arrange
        var command = new TestCommand();
        var @event = new TestEvent();

        // Act
        await _sut.Send(command);
        await _sut.Send(command);
        await _sut.Publish(@event);
        
        var metrics = _sut.GetMetrics();

        // Assert
        Assert.Equal(2, metrics.CommandsSent);
        Assert.Equal(1, metrics.EventsPublished);
    }

    [Fact]
    public void GetHealth_Should_Return_Health_Status()
    {
        // Act
        var health = _sut.GetHealth();

        // Assert
        Assert.NotNull(health);
        Assert.True(health.IsHealthy);
        Assert.True(health.Components.ContainsKey("CommandProcessor"));
        Assert.True(health.Components.ContainsKey("QueryProcessor"));
        Assert.True(health.Components.ContainsKey("EventBus"));
        Assert.True(health.Components.ContainsKey("QueueProcessor"));
        Assert.True(health.Components.ContainsKey("OutboxProcessor"));
        Assert.True(health.Components.ContainsKey("InboxProcessor"));
    }

    [Fact]
    public void GetHealth_Should_Show_NotConfigured_For_Optional_Components()
    {
        // Arrange
        var service = new HeroMessagingService(
            _commandProcessorMock.Object,
            _queryProcessorMock.Object,
            _eventBusMock.Object,
            _loggerMock.Object);

        // Act
        var health = service.GetHealth();

        // Assert
        Assert.False(health.Components["QueueProcessor"].IsHealthy);
        Assert.Equal("Not Configured", health.Components["QueueProcessor"].Status);
        Assert.False(health.Components["OutboxProcessor"].IsHealthy);
        Assert.False(health.Components["InboxProcessor"].IsHealthy);
    }
}
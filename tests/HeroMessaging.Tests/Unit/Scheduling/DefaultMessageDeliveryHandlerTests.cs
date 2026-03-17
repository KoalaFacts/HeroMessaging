using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Abstractions.Scheduling;
using HeroMessaging.Scheduling;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Scheduling;

/// <summary>
/// Unit tests for DefaultMessageDeliveryHandler
/// Target: 100% coverage for public APIs
/// </summary>
[Trait("Category", "Unit")]
public sealed class DefaultMessageDeliveryHandlerTests
{
    private readonly Mock<IHeroMessaging> _messagingMock;
    private readonly Mock<ILogger<DefaultMessageDeliveryHandler>> _loggerMock;
    private readonly DefaultMessageDeliveryHandler _handler;

    public DefaultMessageDeliveryHandlerTests()
    {
        _messagingMock = new Mock<IHeroMessaging>();
        _loggerMock = new Mock<ILogger<DefaultMessageDeliveryHandler>>();
        _handler = new DefaultMessageDeliveryHandler(_messagingMock.Object, _loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullMessaging_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new DefaultMessageDeliveryHandler(null!, _loggerMock.Object));
        Assert.Equal("messaging", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new DefaultMessageDeliveryHandler(_messagingMock.Object, null!));
        Assert.Equal("logger", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act
        var handler = new DefaultMessageDeliveryHandler(_messagingMock.Object, _loggerMock.Object);

        // Assert
        Assert.NotNull(handler);
    }

    #endregion

    #region DeliverAsync - Command Tests

    [Fact]
    public async Task DeliverAsync_WithNullScheduledMessage_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _handler.DeliverAsync(null!));
    }

    [Fact]
    public async Task DeliverAsync_WithCommandWithoutResponse_SendsCommand()
    {
        // Arrange
        var command = new TestCommand();
        var scheduledMessage = CreateScheduledMessage(command);

        _messagingMock
            .Setup(m => m.SendAsync(command, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.DeliverAsync(scheduledMessage);

        // Assert
        _messagingMock.Verify(
            m => m.SendAsync(command, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeliverAsync_WithCommandWithResponse_LogsWarningAndDoesNotDeliver()
    {
        // Arrange
        var command = new TestCommandWithResponse();
        var scheduledMessage = CreateScheduledMessage(command);

        // Act
        await _handler.DeliverAsync(scheduledMessage);

        // Assert
        _messagingMock.Verify(
            m => m.SendAsync(It.IsAny<ICommand>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Verify warning was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Cannot deliver scheduled command")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region DeliverAsync - Event Tests

    [Fact]
    public async Task DeliverAsync_WithEvent_PublishesEvent()
    {
        // Arrange
        var @event = new TestEvent();
        var scheduledMessage = CreateScheduledMessage(@event);

        _messagingMock
            .Setup(m => m.PublishAsync(@event, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.DeliverAsync(scheduledMessage);

        // Assert
        _messagingMock.Verify(
            m => m.PublishAsync(@event, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeliverAsync_WithEventAndDestination_EnqueuesToDestination()
    {
        // Arrange
        var @event = new TestEvent();
        var destination = "test-queue";
        var scheduledMessage = CreateScheduledMessage(@event, destination);

        _messagingMock
            .Setup(m => m.EnqueueAsync(@event, destination, null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.DeliverAsync(scheduledMessage);

        // Assert
        _messagingMock.Verify(
            m => m.EnqueueAsync(@event, destination, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region DeliverAsync - Query Tests

    [Fact]
    public async Task DeliverAsync_WithQuery_LogsWarningAndDoesNotDeliver()
    {
        // Arrange
        var query = new TestQuery();
        var scheduledMessage = CreateScheduledMessage(query);

        // Act
        await _handler.DeliverAsync(scheduledMessage);

        // Assert
        _messagingMock.Verify(
            m => m.SendAsync(It.IsAny<IQuery<object>>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Verify warning was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Cannot deliver scheduled query")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region DeliverAsync - Generic Message Tests

    [Fact]
    public async Task DeliverAsync_WithGenericMessageAndNoDestination_EnqueuesTDefaultQueue()
    {
        // Arrange
        var message = new TestMessage();
        var scheduledMessage = CreateScheduledMessage(message);

        _messagingMock
            .Setup(m => m.EnqueueAsync(message, "scheduled-messages", null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.DeliverAsync(scheduledMessage);

        // Assert
        _messagingMock.Verify(
            m => m.EnqueueAsync(message, "scheduled-messages", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeliverAsync_WithGenericMessageAndDestination_EnqueuesToDestination()
    {
        // Arrange
        var message = new TestMessage();
        var destination = "custom-queue";
        var scheduledMessage = CreateScheduledMessage(message, destination);

        _messagingMock
            .Setup(m => m.EnqueueAsync(message, destination, null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.DeliverAsync(scheduledMessage);

        // Assert
        _messagingMock.Verify(
            m => m.EnqueueAsync(message, destination, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region DeliverAsync - Error Handling Tests

    [Fact]
    public async Task DeliverAsync_WhenDeliveryFails_ThrowsException()
    {
        // Arrange
        var @event = new TestEvent();
        var scheduledMessage = CreateScheduledMessage(@event);

        _messagingMock
            .Setup(m => m.PublishAsync(@event, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Delivery failed"));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _handler.DeliverAsync(scheduledMessage));

        Assert.Equal("Delivery failed", ex.Message);

        // Verify error was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to deliver scheduled message")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task DeliverAsync_WithCancellationToken_PassesTokenToMessaging()
    {
        // Arrange
        var @event = new TestEvent();
        var scheduledMessage = CreateScheduledMessage(@event);
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _messagingMock
            .Setup(m => m.PublishAsync(@event, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.DeliverAsync(scheduledMessage, cancellationToken);

        // Assert
        _messagingMock.Verify(
            m => m.PublishAsync(@event, cancellationToken),
            Times.Once);
    }

    #endregion

    #region HandleDeliveryFailureAsync Tests

    [Fact]
    public async Task HandleDeliveryFailureAsync_WithValidParameters_LogsError()
    {
        // Arrange
        var scheduleId = Guid.NewGuid();
        var exception = new InvalidOperationException("Test error");

        // Act
        await _handler.HandleDeliveryFailureAsync(scheduleId, exception);

        // Assert - Verify error was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("delivery failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleDeliveryFailureAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var scheduleId = Guid.NewGuid();
        var exception = new InvalidOperationException("Test error");
        using var cts = new CancellationTokenSource();

        // Act
        await _handler.HandleDeliveryFailureAsync(scheduleId, exception, cts.Token);

        // Assert - Should complete without throwing
        Assert.True(true);
    }

    #endregion

    #region Helper Methods

    private static ScheduledMessage CreateScheduledMessage(IMessage message, string? destination = null)
    {
        return new ScheduledMessage
        {
            ScheduleId = Guid.NewGuid(),
            Message = message,
            DeliverAt = DateTimeOffset.UtcNow.AddMinutes(5),
            ScheduledAt = DateTimeOffset.UtcNow,
            Options = new SchedulingOptions
            {
                Destination = destination
            }
        };
    }

    #endregion

    #region Test Message Classes

    public sealed class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    public sealed class TestCommand : ICommand
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    public sealed class TestCommandWithResponse : ICommand<string>
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    public sealed class TestEvent : IEvent
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    public sealed class TestQuery : IQuery<string>
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    #endregion
}

using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Abstractions.Scheduling;
using HeroMessaging.Scheduling;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Scheduling.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class DefaultMessageDeliveryHandlerTests : IDisposable
{
    private readonly Mock<IHeroMessaging> _messagingMock;
    private readonly Mock<ILogger<DefaultMessageDeliveryHandler>> _loggerMock;
    private DefaultMessageDeliveryHandler? _handler;

    public DefaultMessageDeliveryHandlerTests()
    {
        _messagingMock = new Mock<IHeroMessaging>();
        _loggerMock = new Mock<ILogger<DefaultMessageDeliveryHandler>>();
    }

    public void Dispose()
    {
        // No async disposal needed for this handler
    }

    private DefaultMessageDeliveryHandler CreateHandler()
    {
        return new DefaultMessageDeliveryHandler(
            _messagingMock.Object,
            _loggerMock.Object);
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
    public void Constructor_WithValidArguments_CreatesInstance()
    {
        // Act
        var handler = CreateHandler();

        // Assert
        Assert.NotNull(handler);
    }

    #endregion

    #region DeliverAsync Tests - Command without Response

    [Fact]
    public async Task DeliverAsync_WithCommandWithoutResponse_CallsSendAsync()
    {
        // Arrange
        _handler = CreateHandler();
        var command = new TestCommand();
        var scheduledMessage = new ScheduledMessage
        {
            ScheduleId = Guid.NewGuid(),
            Message = command,
            DeliverAt = DateTimeOffset.UtcNow,
            ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Options = new SchedulingOptions()
        };

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
        _handler = CreateHandler();
        var command = new TestCommandWithResponse();
        var scheduledMessage = new ScheduledMessage
        {
            ScheduleId = Guid.NewGuid(),
            Message = command,
            DeliverAt = DateTimeOffset.UtcNow,
            ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Options = new SchedulingOptions()
        };

        // Act
        await _handler.DeliverAsync(scheduledMessage);

        // Assert
        _messagingMock.Verify(
            m => m.SendAsync(It.IsAny<ICommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region DeliverAsync Tests - Event

    [Fact]
    public async Task DeliverAsync_WithEventAndNoDestination_CallsPublishAsync()
    {
        // Arrange
        _handler = CreateHandler();
        var @event = new TestEvent();
        var scheduledMessage = new ScheduledMessage
        {
            ScheduleId = Guid.NewGuid(),
            Message = @event,
            DeliverAt = DateTimeOffset.UtcNow,
            ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Options = new SchedulingOptions()
        };

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
    public async Task DeliverAsync_WithEventAndDestination_CallsEnqueueAsync()
    {
        // Arrange
        _handler = CreateHandler();
        var @event = new TestEvent();
        var destination = "test-queue";
        var scheduledMessage = new ScheduledMessage
        {
            ScheduleId = Guid.NewGuid(),
            Message = @event,
            DeliverAt = DateTimeOffset.UtcNow,
            ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Options = new SchedulingOptions { Destination = destination }
        };

        _messagingMock
            .Setup(m => m.EnqueueAsync(@event, destination, It.IsAny<EnqueueOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.DeliverAsync(scheduledMessage);

        // Assert
        _messagingMock.Verify(
            m => m.EnqueueAsync(@event, destination, It.IsAny<EnqueueOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region DeliverAsync Tests - Query

    [Fact]
    public async Task DeliverAsync_WithQuery_LogsWarningAndDoesNotDeliver()
    {
        // Arrange
        _handler = CreateHandler();
        var query = new TestQuery();
        var scheduledMessage = new ScheduledMessage
        {
            ScheduleId = Guid.NewGuid(),
            Message = query,
            DeliverAt = DateTimeOffset.UtcNow,
            ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Options = new SchedulingOptions()
        };

        // Act
        await _handler.DeliverAsync(scheduledMessage);

        // Assert
        _messagingMock.Verify(
            m => m.SendAsync(It.IsAny<ICommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _messagingMock.Verify(
            m => m.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _messagingMock.Verify(
            m => m.EnqueueAsync(It.IsAny<IMessage>(), It.IsAny<string>(), It.IsAny<EnqueueOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region DeliverAsync Tests - Generic Message

    [Fact]
    public async Task DeliverAsync_WithGenericMessageAndNoDestination_EnqueuesToDefaultQueue()
    {
        // Arrange
        _handler = CreateHandler();
        var message = new TestMessage();
        var scheduledMessage = new ScheduledMessage
        {
            ScheduleId = Guid.NewGuid(),
            Message = message,
            DeliverAt = DateTimeOffset.UtcNow,
            ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Options = new SchedulingOptions()
        };

        _messagingMock
            .Setup(m => m.EnqueueAsync(message, "scheduled-messages", It.IsAny<EnqueueOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.DeliverAsync(scheduledMessage);

        // Assert
        _messagingMock.Verify(
            m => m.EnqueueAsync(message, "scheduled-messages", It.IsAny<EnqueueOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeliverAsync_WithGenericMessageAndDestination_EnqueuesToSpecifiedQueue()
    {
        // Arrange
        _handler = CreateHandler();
        var message = new TestMessage();
        var destination = "custom-queue";
        var scheduledMessage = new ScheduledMessage
        {
            ScheduleId = Guid.NewGuid(),
            Message = message,
            DeliverAt = DateTimeOffset.UtcNow,
            ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Options = new SchedulingOptions { Destination = destination }
        };

        _messagingMock
            .Setup(m => m.EnqueueAsync(message, destination, It.IsAny<EnqueueOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.DeliverAsync(scheduledMessage);

        // Assert
        _messagingMock.Verify(
            m => m.EnqueueAsync(message, destination, It.IsAny<EnqueueOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region DeliverAsync Tests - Validation

    [Fact]
    public async Task DeliverAsync_WithNullScheduledMessage_ThrowsArgumentNullException()
    {
        // Arrange
        _handler = CreateHandler();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _handler.DeliverAsync(null!));

        Assert.Equal("scheduledMessage", ex.ParamName);
    }

    [Fact]
    public async Task DeliverAsync_WithCancellationToken_PassesCancellationToken()
    {
        // Arrange
        _handler = CreateHandler();
        var @event = new TestEvent();
        var scheduledMessage = new ScheduledMessage
        {
            ScheduleId = Guid.NewGuid(),
            Message = @event,
            DeliverAt = DateTimeOffset.UtcNow,
            ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Options = new SchedulingOptions()
        };

        var cts = new CancellationTokenSource();
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

    #region DeliverAsync Tests - Error Handling

    [Fact]
    public async Task DeliverAsync_WhenPublishAsyncThrows_RethrowsException()
    {
        // Arrange
        _handler = CreateHandler();
        var @event = new TestEvent();
        var scheduledMessage = new ScheduledMessage
        {
            ScheduleId = Guid.NewGuid(),
            Message = @event,
            DeliverAt = DateTimeOffset.UtcNow,
            ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Options = new SchedulingOptions()
        };

        var expectedException = new InvalidOperationException("Publish failed");
        _messagingMock
            .Setup(m => m.PublishAsync(@event, It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.DeliverAsync(scheduledMessage));

        Assert.Same(expectedException, ex);
    }

    [Fact]
    public async Task DeliverAsync_WhenSendAsyncThrows_RethrowsException()
    {
        // Arrange
        _handler = CreateHandler();
        var command = new TestCommand();
        var scheduledMessage = new ScheduledMessage
        {
            ScheduleId = Guid.NewGuid(),
            Message = command,
            DeliverAt = DateTimeOffset.UtcNow,
            ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Options = new SchedulingOptions()
        };

        var expectedException = new InvalidOperationException("Send failed");
        _messagingMock
            .Setup(m => m.SendAsync(command, It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.DeliverAsync(scheduledMessage));

        Assert.Same(expectedException, ex);
    }

    [Fact]
    public async Task DeliverAsync_WhenEnqueueAsyncThrows_RethrowsException()
    {
        // Arrange
        _handler = CreateHandler();
        var message = new TestMessage();
        var scheduledMessage = new ScheduledMessage
        {
            ScheduleId = Guid.NewGuid(),
            Message = message,
            DeliverAt = DateTimeOffset.UtcNow,
            ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Options = new SchedulingOptions()
        };

        var expectedException = new InvalidOperationException("Enqueue failed");
        _messagingMock
            .Setup(m => m.EnqueueAsync(message, "scheduled-messages", It.IsAny<EnqueueOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.DeliverAsync(scheduledMessage));

        Assert.Same(expectedException, ex);
    }

    #endregion

    #region HandleDeliveryFailureAsync Tests

    [Fact]
    public async Task HandleDeliveryFailureAsync_WithValidArguments_LogsErrorAndCompletes()
    {
        // Arrange
        _handler = CreateHandler();
        var scheduleId = Guid.NewGuid();
        var exception = new InvalidOperationException("Test failure");

        // Act
        await _handler.HandleDeliveryFailureAsync(scheduleId, exception);

        // Assert - Should complete without throwing
        // Verify logging would happen (currently just logs, no other action)
    }

    [Fact]
    public async Task HandleDeliveryFailureAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        _handler = CreateHandler();
        var scheduleId = Guid.NewGuid();
        var exception = new InvalidOperationException("Test failure");
        var cts = new CancellationTokenSource();

        // Act
        await _handler.HandleDeliveryFailureAsync(scheduleId, exception, cts.Token);

        // Assert - Should complete without throwing
    }

    #endregion

    #region Test Helper Classes

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private class TestCommand : ICommand, IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private class TestCommandWithResponse : ICommand<int>, IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private class TestEvent : IEvent, IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private class TestQuery : IQuery<string>, IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    #endregion
}

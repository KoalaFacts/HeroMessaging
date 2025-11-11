using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

/// <summary>
/// Unit tests for LoggingDecorator
/// Tests logging during message processing
/// </summary>
[Trait("Category", "Unit")]
public sealed class LoggingDecoratorTests
{
    private readonly Mock<IMessageProcessor> _innerProcessorMock;
    private readonly Mock<ILogger<LoggingDecorator>> _loggerMock;

    public LoggingDecoratorTests()
    {
        _innerProcessorMock = new Mock<IMessageProcessor>();
        _loggerMock = new Mock<ILogger<LoggingDecorator>>();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithDefaultParameters_CreatesDecorator()
    {
        // Act
        var decorator = new LoggingDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Assert
        Assert.NotNull(decorator);
    }

    [Fact]
    public void Constructor_WithCustomLogLevel_UsesCustomLevel()
    {
        // Act
        var decorator = new LoggingDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            LogLevel.Information);

        // Assert
        Assert.NotNull(decorator);
    }

    [Fact]
    public void Constructor_WithLogPayloadEnabled_CreatesDecorator()
    {
        // Act
        var decorator = new LoggingDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            LogLevel.Debug,
            logPayload: true);

        // Assert
        Assert.NotNull(decorator);
    }

    #endregion

    #region Success Logging Tests

    [Fact]
    public async Task ProcessAsync_OnSuccess_LogsAtDebugAndSuccessLevel()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext { Component = "TestComponent" };

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new LoggingDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            LogLevel.Information);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert - Should log at Debug (processing start) and Information (success)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Processing")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Successfully processed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_OnSuccess_IncludesElapsedTime()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new LoggingDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            LogLevel.Information);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert - Success log should contain elapsed time
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("ms")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Failure Logging Tests

    [Fact]
    public async Task ProcessAsync_OnFailure_LogsWarning()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var exception = new InvalidOperationException("Test failure");

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(exception, "Processing failed"));

        var decorator = new LoggingDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert - Should log warning on failure
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to process")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_OnFailure_IncludesReasonAndElapsedTime()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var errorMessage = "Validation failed";

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception(), errorMessage));

        var decorator = new LoggingDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert - Warning log should contain reason and elapsed time
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains(errorMessage) &&
                    v.ToString()!.Contains("ms")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Exception Logging Tests

    [Fact]
    public async Task ProcessAsync_WhenExceptionThrown_LogsErrorAndRethrows()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var exception = new InvalidOperationException("Critical error");

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var decorator = new LoggingDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            decorator.ProcessAsync(message, context).AsTask());

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Exception processing")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenExceptionThrown_IncludesElapsedTime()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var exception = new TimeoutException("Timeout");

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var decorator = new LoggingDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(() =>
            decorator.ProcessAsync(message, context).AsTask());

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("ms")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Payload Logging Tests

    [Fact]
    public async Task ProcessAsync_WithLogPayloadEnabled_LogsPayloadAtTrace()
    {
        // Arrange
        var message = new TestMessage { Content = "test payload" };
        var context = new ProcessingContext();

        // Set up logger to be enabled for Trace level
        _loggerMock.Setup(l => l.IsEnabled(LogLevel.Trace)).Returns(true);

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new LoggingDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            LogLevel.Debug,
            logPayload: true);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert - Should check if Trace is enabled and log payload
        _loggerMock.Verify(l => l.IsEnabled(LogLevel.Trace), Times.Once);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Trace,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Message payload")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithLogPayloadEnabledButTraceLevelDisabled_DoesNotLogPayload()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        // Set up logger to NOT be enabled for Trace level
        _loggerMock.Setup(l => l.IsEnabled(LogLevel.Trace)).Returns(false);

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new LoggingDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            LogLevel.Debug,
            logPayload: true);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert - Should check if Trace is enabled but not log
        _loggerMock.Verify(l => l.IsEnabled(LogLevel.Trace), Times.Once);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Trace,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithLogPayloadDisabled_DoesNotCheckTraceLevel()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new LoggingDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            LogLevel.Debug,
            logPayload: false);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert - Should not check Trace level when payload logging disabled
        _loggerMock.Verify(l => l.IsEnabled(LogLevel.Trace), Times.Never);
    }

    #endregion

    #region Context Information Tests

    [Fact]
    public async Task ProcessAsync_LogsMessageTypeAndId()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message = new TestMessage { Content = "test" };
        typeof(TestMessage).GetProperty(nameof(IMessage.MessageId))!
            .SetValue(message, messageId);

        var context = new ProcessingContext();

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new LoggingDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert - All logs should include message type and ID
        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains("TestMessage") &&
                    v.ToString()!.Contains(messageId.ToString())),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessAsync_LogsComponentFromContext()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var componentName = "OrderProcessingService";
        var context = new ProcessingContext { Component = componentName };

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new LoggingDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert - Debug log should include component name
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(componentName)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task ProcessAsync_WithCancellationToken_PropagatesToken()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var cts = new CancellationTokenSource();

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, cts.Token))
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new LoggingDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Act
        await decorator.ProcessAsync(message, context, cts.Token);

        // Assert
        _innerProcessorMock.Verify(p => p.ProcessAsync(message, context, cts.Token), Times.Once);
    }

    #endregion

    #region Test Message Class

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    #endregion
}

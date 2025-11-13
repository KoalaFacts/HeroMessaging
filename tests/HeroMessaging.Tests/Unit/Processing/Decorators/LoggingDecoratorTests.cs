using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing.Decorators;

[Trait("Category", "Unit")]
public sealed class LoggingDecoratorTests
{
    private readonly Mock<IMessageProcessor> _innerMock;
    private readonly Mock<ILogger<LoggingDecorator>> _loggerMock;

    public LoggingDecoratorTests()
    {
        _innerMock = new Mock<IMessageProcessor>();
        _loggerMock = new Mock<ILogger<LoggingDecorator>>();
    }

    private LoggingDecorator CreateDecorator(LogLevel successLogLevel = LogLevel.Debug, bool logPayload = false)
    {
        return new LoggingDecorator(_innerMock.Object, _loggerMock.Object, successLogLevel, logPayload);
    }

    #region ProcessAsync - Success Cases

    [Fact]
    public async Task ProcessAsync_WithSuccessfulResult_LogsDebugOnStart()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext("TestComponent");

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessAsync_WithSuccessfulResult_LogsSuccessAtSpecifiedLevel()
    {
        // Arrange
        var decorator = CreateDecorator(successLogLevel: LogLevel.Information);
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithSuccessfulResult_CallsInnerProcessor()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        _innerMock.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithSuccessfulResult_ReturnsResultFromInner()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var expectedMessage = "Processing completed";

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful(expectedMessage));

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(expectedMessage, result.Message);
    }

    #endregion

    #region ProcessAsync - Failure Cases

    [Fact]
    public async Task ProcessAsync_WithFailedResult_LogsWarning()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new InvalidOperationException("Test error");

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(testException, "Processing failed"));

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                testException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithFailedResult_ReturnsFailureResult()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new InvalidOperationException("Test error");

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(testException, "Processing failed"));

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(testException, result.Exception);
    }

    #endregion

    #region ProcessAsync - Exception Cases

    [Fact]
    public async Task ProcessAsync_WithException_LogsError()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new InvalidOperationException("Test exception");

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ThrowsAsync(testException);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await decorator.ProcessAsync(message, context));

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                testException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithException_RethrowsException()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new InvalidOperationException("Test exception");

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ThrowsAsync(testException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await decorator.ProcessAsync(message, context));

        Assert.Equal("Test exception", exception.Message);
    }

    #endregion

    #region ProcessAsync - Payload Logging

    [Fact]
    public async Task ProcessAsync_WithLogPayloadEnabled_LogsPayloadAtTrace()
    {
        // Arrange
        _loggerMock
            .Setup(l => l.IsEnabled(LogLevel.Trace))
            .Returns(true);

        var decorator = CreateDecorator(logPayload: true);
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Trace,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithLogPayloadDisabled_DoesNotLogPayload()
    {
        // Arrange
        _loggerMock
            .Setup(l => l.IsEnabled(LogLevel.Trace))
            .Returns(true);

        var decorator = CreateDecorator(logPayload: false);
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Trace,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithLogPayloadEnabledButTraceDisabled_DoesNotLogPayload()
    {
        // Arrange
        _loggerMock
            .Setup(l => l.IsEnabled(LogLevel.Trace))
            .Returns(false);

        var decorator = CreateDecorator(logPayload: true);
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Trace,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    #endregion

    #region ProcessAsync - Log Level Configuration

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    public async Task ProcessAsync_WithSuccessLogLevel_LogsAtSpecifiedLevel(LogLevel logLevel)
    {
        // Arrange
        var decorator = CreateDecorator(successLogLevel: logLevel);
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                logLevel,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region ProcessAsync - Timing

    [Fact]
    public async Task ProcessAsync_WithSuccessfulResult_LogsElapsedTime()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(10); // Simulate some processing time
                return ProcessingResult.Successful();
            });

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert - Verify logging includes elapsed time (should be > 0ms)
        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(2)); // Debug on start + success log
    }

    [Fact]
    public async Task ProcessAsync_WithFailedResult_LogsElapsedTime()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(10); // Simulate some processing time
                return ProcessingResult.Failed(new Exception("Test error"));
            });

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(2)); // Debug on start + warning on failure
    }

    [Fact]
    public async Task ProcessAsync_WithException_LogsElapsedTime()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(10); // Simulate some processing time
                throw new InvalidOperationException("Test exception");
            });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await decorator.ProcessAsync(message, context));

        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(2)); // Debug on start + error on exception
    }

    #endregion

    #region ProcessAsync - Message Type Logging

    [Fact]
    public async Task ProcessAsync_LogsMessageTypeName()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert - Logs should reference TestMessage
        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(2));
    }

    #endregion

    #region ProcessAsync - Cancellation

    [Fact]
    public async Task ProcessAsync_PassesCancellationTokenToInner()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, cancellationToken))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context, cancellationToken);

        // Assert
        _innerMock.Verify(p => p.ProcessAsync(message, context, cancellationToken), Times.Once);
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

    #endregion
}

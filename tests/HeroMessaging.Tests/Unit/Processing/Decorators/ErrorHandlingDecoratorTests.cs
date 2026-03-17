using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing.Decorators;

[Trait("Category", "Unit")]
public sealed class ErrorHandlingDecoratorTests
{
    private readonly Mock<IMessageProcessor> _innerMock;
    private readonly Mock<IErrorHandler> _errorHandlerMock;
    private readonly Mock<ILogger<ErrorHandlingDecorator>> _loggerMock;
    private readonly FakeTimeProvider _timeProvider;

    public ErrorHandlingDecoratorTests()
    {
        _innerMock = new Mock<IMessageProcessor>();
        _errorHandlerMock = new Mock<IErrorHandler>();
        _loggerMock = new Mock<ILogger<ErrorHandlingDecorator>>();
        _timeProvider = new FakeTimeProvider();
    }

    private ErrorHandlingDecorator CreateDecorator(int maxRetries = 3)
    {
        return new ErrorHandlingDecorator(
            _innerMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider,
            maxRetries);
    }

    #region ProcessAsync - Success Cases

    [Fact]
    public async Task ProcessAsync_WithSuccessfulResult_ReturnsSuccessWithoutRetry()
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
        _errorHandlerMock.Verify(
            e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithSuccessAfterRetries_LogsInformation()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var attemptCount = 0;

        _innerMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .Returns<IMessage, ProcessingContext, CancellationToken>((m, c, ct) =>
            {
                attemptCount++;
                if (attemptCount <= 2)
                {
                    return ValueTask.FromResult(ProcessingResult.Failed(new TimeoutException("Timeout")));
                }
                return ValueTask.FromResult(ProcessingResult.Successful());
            });

        _errorHandlerMock
            .Setup(e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.Zero));

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region ProcessAsync - Failure with Exception

    [Fact]
    public async Task ProcessAsync_WithException_CallsErrorHandler()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new InvalidOperationException("Test error");

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ThrowsAsync(testException);

        // Use It.IsAny<> matchers for the error handler mock to handle any matching exception
        _errorHandlerMock
            .Setup(e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.SendToDeadLetter("Permanent failure"));

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        _errorHandlerMock.Verify(
            e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithFailedResult_CallsErrorHandler()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new InvalidOperationException("Test error");

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(testException));

        // Use It.IsAny<> matchers for the error handler mock to handle any matching exception
        _errorHandlerMock
            .Setup(e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.SendToDeadLetter("Permanent failure"));

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        _errorHandlerMock.Verify(
            e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithFailedResultButNoException_ReturnsPermanentFailure()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(null!, "Permanent failure"));

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        _errorHandlerMock.Verify(
            e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region ProcessAsync - Retry Action

    [Fact]
    public async Task ProcessAsync_WithRetryAction_RetriesProcessing()
    {
        // Arrange
        var decorator = CreateDecorator(maxRetries: 2);
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new TimeoutException("Timeout");

        _innerMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(testException));

        _errorHandlerMock
            .Setup(e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.Zero));

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        _innerMock.Verify(
            p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3)); // Initial + 2 retries
    }

    [Fact]
    public async Task ProcessAsync_WithRetryAction_UpdatesContextWithRetryCount()
    {
        // Arrange
        var decorator = CreateDecorator(maxRetries: 1);
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new TimeoutException("Timeout");
        ProcessingContext? capturedContext = null;

        _innerMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .Returns<IMessage, ProcessingContext, CancellationToken>((m, c, ct) =>
            {
                capturedContext = c;
                return ValueTask.FromResult(ProcessingResult.Failed(testException));
            });

        _errorHandlerMock
            .Setup(e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.Zero));

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert - Last context should have RetryCount = 1
        Assert.NotNull(capturedContext);
        Assert.Equal(1, capturedContext.Value.RetryCount);
    }

    [Fact]
    public async Task ProcessAsync_WithRetryDelay_WaitsBeforeRetrying()
    {
        // Arrange
        var decorator = CreateDecorator(maxRetries: 1);
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new TimeoutException("Timeout");
        var retryDelay = TimeSpan.FromMilliseconds(100);

        _innerMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(testException));

        _errorHandlerMock
            .Setup(e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(retryDelay));

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert - With maxRetries=1, there will be 2 delay logs: one for initial retry, one for second retry
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(2)); // Initial + 1 retry = 2 debug logs about waiting
    }

    #endregion

    #region ProcessAsync - SendToDeadLetter Action

    [Fact]
    public async Task ProcessAsync_WithSendToDeadLetterAction_ReturnsFailureWithReason()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new InvalidOperationException("Test error");
        var dlqReason = "Invalid message format";

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(testException));

        _errorHandlerMock
            .Setup(e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.SendToDeadLetter(dlqReason));

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Sent to DLQ", result.Message);
        Assert.Contains(dlqReason, result.Message);
    }

    [Fact]
    public async Task ProcessAsync_WithSendToDeadLetterAction_LogsWarning()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new InvalidOperationException("Test error");

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(testException));

        _errorHandlerMock
            .Setup(e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.SendToDeadLetter("Test reason"));

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region ProcessAsync - Discard Action

    [Fact]
    public async Task ProcessAsync_WithDiscardAction_ReturnsFailureWithReason()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new InvalidOperationException("Test error");
        var discardReason = "Message is duplicate";

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(testException));

        _errorHandlerMock
            .Setup(e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Discard(discardReason));

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Discarded", result.Message);
        Assert.Contains(discardReason, result.Message);
    }

    [Fact]
    public async Task ProcessAsync_WithDiscardAction_LogsWarning()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new InvalidOperationException("Test error");

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(testException));

        _errorHandlerMock
            .Setup(e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Discard("Test reason"));

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region ProcessAsync - Escalate Action

    [Fact]
    public async Task ProcessAsync_WithEscalateAction_RethrowsException()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new InvalidOperationException("Test error");

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(testException));

        _errorHandlerMock
            .Setup(e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Escalate());

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await decorator.ProcessAsync(message, context));

        Assert.Equal("Test error", exception.Message);
    }

    [Fact]
    public async Task ProcessAsync_WithEscalateAction_LogsCritical()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new InvalidOperationException("Test error");

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(testException));

        _errorHandlerMock
            .Setup(e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Escalate());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await decorator.ProcessAsync(message, context));

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region ProcessAsync - Max Retries Exceeded

    [Fact]
    public async Task ProcessAsync_WhenMaxRetriesExceeded_ReturnsFailure()
    {
        // Arrange
        var decorator = CreateDecorator(maxRetries: 2);
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new TimeoutException("Timeout");

        _innerMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(testException));

        _errorHandlerMock
            .Setup(e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.Zero));

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed after 2 retries", result.Message);
        _innerMock.Verify(
            p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3)); // Initial + 2 retries
    }

    [Fact]
    public async Task ProcessAsync_WhenMaxRetriesExceeded_LogsError()
    {
        // Arrange
        var decorator = CreateDecorator(maxRetries: 1);
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new TimeoutException("Timeout");

        _innerMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(testException));

        _errorHandlerMock
            .Setup(e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.Zero));

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(2)); // One for each error attempt
    }

    #endregion

    #region ProcessAsync - Error Context

    [Fact]
    public async Task ProcessAsync_PassesCorrectErrorContextToHandler()
    {
        // Arrange
        var decorator = CreateDecorator(maxRetries: 3);
        var message = new TestMessage();
        var componentName = "TestComponent";
        var context = new ProcessingContext(componentName);
        var testException = new TimeoutException("Timeout");
        ErrorContext? capturedContext = null;

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(testException));

        _errorHandlerMock
            .Setup(e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), It.IsAny<CancellationToken>()))
            .Callback<IMessage, Exception, ErrorContext, CancellationToken>((m, ex, ctx, ct) =>
            {
                capturedContext = ctx;
            })
            .ReturnsAsync(ErrorHandlingResult.SendToDeadLetter("Max retries"));

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal(0, capturedContext.RetryCount);
        Assert.Equal(3, capturedContext.MaxRetries);
        Assert.Equal(componentName, capturedContext.Component);
        Assert.NotNull(capturedContext.FirstFailureTime);
        Assert.NotNull(capturedContext.LastFailureTime);
    }

    [Fact]
    public async Task ProcessAsync_OnRetry_UpdatesErrorContext()
    {
        // Arrange
        var decorator = CreateDecorator(maxRetries: 2);
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new TimeoutException("Timeout");
        var capturedContexts = new List<ErrorContext>();

        _innerMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(testException));

        _errorHandlerMock
            .Setup(e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), It.IsAny<CancellationToken>()))
            .Callback<IMessage, Exception, ErrorContext, CancellationToken>((m, ex, ctx, ct) =>
            {
                capturedContexts.Add(ctx);
            })
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.Zero));

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert - Should have 3 error contexts (initial + 2 retries)
        Assert.Equal(3, capturedContexts.Count);
        Assert.Equal(0, capturedContexts[0].RetryCount);
        Assert.Equal(1, capturedContexts[1].RetryCount);
        Assert.Equal(2, capturedContexts[2].RetryCount);

        // All should share the same FirstFailureTime
        Assert.Equal(capturedContexts[0].FirstFailureTime, capturedContexts[1].FirstFailureTime);
        Assert.Equal(capturedContexts[0].FirstFailureTime, capturedContexts[2].FirstFailureTime);
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

    [Fact]
    public async Task ProcessAsync_PassesCancellationTokenToErrorHandler()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        var testException = new TimeoutException("Timeout");

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, cancellationToken))
            .ReturnsAsync(ProcessingResult.Failed(testException));

        // Use It.IsAny<> matchers for the error handler mock to handle any matching call
        _errorHandlerMock
            .Setup(e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.SendToDeadLetter("Test"));

        // Act
        await decorator.ProcessAsync(message, context, cancellationToken);

        // Assert
        _errorHandlerMock.Verify(
            e => e.HandleErrorAsync(It.IsAny<IMessage>(), It.IsAny<Exception>(), It.IsAny<ErrorContext>(), cancellationToken),
            Times.Once);
    }

    #endregion

    #region Test Helper Classes

    public class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    #endregion
}

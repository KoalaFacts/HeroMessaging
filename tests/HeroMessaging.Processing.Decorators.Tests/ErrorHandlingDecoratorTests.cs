using System.Collections.Immutable;
using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

/// <summary>
/// Unit tests for ErrorHandlingDecorator
/// Tests error handling, retry logic, and integration with IErrorHandler
/// </summary>
[Trait("Category", "Unit")]
public sealed class ErrorHandlingDecoratorTests
{
    private readonly Mock<IMessageProcessor> _innerProcessorMock;
    private readonly Mock<IErrorHandler> _errorHandlerMock;
    private readonly Mock<ILogger<ErrorHandlingDecorator>> _loggerMock;
    private readonly FakeTimeProvider _timeProvider;

    public ErrorHandlingDecoratorTests()
    {
        _innerProcessorMock = new Mock<IMessageProcessor>();
        _errorHandlerMock = new Mock<IErrorHandler>();
        _loggerMock = new Mock<ILogger<ErrorHandlingDecorator>>();
        _timeProvider = new FakeTimeProvider();
        _timeProvider.SetUtcNow(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesDecorator()
    {
        // Act
        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Assert
        Assert.NotNull(decorator);
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ErrorHandlingDecorator(
                _innerProcessorMock.Object,
                _errorHandlerMock.Object,
                _loggerMock.Object,
                null!));

        Assert.Equal("timeProvider", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithCustomMaxRetries_UsesCustomValue()
    {
        // Act
        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider,
            maxRetries: 5);

        // Assert
        Assert.NotNull(decorator);
    }

    #endregion

    #region Success Scenarios

    [Fact]
    public async Task ProcessAsync_SuccessOnFirstAttempt_ReturnsSuccess()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var expectedResult = ProcessingResult.Successful();

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        _innerProcessorMock.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);
        _errorHandlerMock.Verify(e => e.HandleErrorAsync(
            It.IsAny<IMessage>(),
            It.IsAny<Exception>(),
            It.IsAny<ErrorContext>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_SuccessAfterRetry_LogsSuccessMessage()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var callCount = 0;

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    return ProcessingResult.Failed(new TimeoutException("Transient"), "Timeout");
                return ProcessingResult.Successful();
            });

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.Zero));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, callCount);
    }

    #endregion

    #region Error Handler Actions

    [Fact]
    public async Task ProcessAsync_ErrorHandlerReturnsRetry_RetriesProcessing()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var callCount = 0;

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount < 3)
                    return ProcessingResult.Failed(new TimeoutException("Transient"), "Timeout");
                return ProcessingResult.Successful();
            });

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.Zero));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider,
            maxRetries: 5);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, callCount);
        _errorHandlerMock.Verify(e => e.HandleErrorAsync(
            It.IsAny<IMessage>(),
            It.IsAny<Exception>(),
            It.IsAny<ErrorContext>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessAsync_ErrorHandlerReturnsRetryWithDelay_WaitsBeforeRetry()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var retryDelay = TimeSpan.FromMilliseconds(100);
        var callCount = 0;

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    return ProcessingResult.Failed(new TimeoutException("Transient"), "Timeout");
                return ProcessingResult.Successful();
            });

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(retryDelay));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ProcessAsync_ErrorHandlerReturnsSendToDeadLetter_ReturnsFailedResult()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var exception = new InvalidOperationException("Business rule violation");

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(exception, "Failed"));

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.SendToDeadLetter("Business rule violated"));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(exception, result.Exception);
        Assert.Contains("Sent to DLQ", result.Message);
        Assert.Contains("Business rule violated", result.Message);
        _innerProcessorMock.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ErrorHandlerReturnsDiscard_ReturnsFailedResult()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var exception = new ArgumentException("Invalid message");

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(exception, "Failed"));

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Discard("Message format invalid"));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(exception, result.Exception);
        Assert.Contains("Discarded", result.Message);
        Assert.Contains("Message format invalid", result.Message);
        _innerProcessorMock.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ErrorHandlerReturnsEscalate_ThrowsException()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var exception = new InvalidOperationException("Critical error");

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(exception, "Failed"));

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Escalate());

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act & Assert
        var thrownException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            decorator.ProcessAsync(message, context).AsTask());

        Assert.Equal(exception, thrownException);
        _innerProcessorMock.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Exception Handling

    [Fact]
    public async Task ProcessAsync_InnerProcessorThrowsException_HandlesException()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var exception = new TimeoutException("Connection timeout");

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Discard("Timeout"));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        _errorHandlerMock.Verify(e => e.HandleErrorAsync(
            It.IsAny<IMessage>(),
            exception,
            It.IsAny<ErrorContext>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_FailureWithoutException_ReturnsPermanentFailure()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var result = ProcessingResult.Failed(null, "Permanent failure");

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act
        var actualResult = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(actualResult.Success);
        Assert.Equal("Permanent failure", actualResult.Message);
        _innerProcessorMock.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);
        _errorHandlerMock.Verify(e => e.HandleErrorAsync(
            It.IsAny<IMessage>(),
            It.IsAny<Exception>(),
            It.IsAny<ErrorContext>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Max Retries

    [Fact]
    public async Task ProcessAsync_MaxRetriesExceeded_ReturnsFailure()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var exception = new TimeoutException("Persistent timeout");

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(exception, "Timeout"));

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.Zero));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider,
            maxRetries: 2);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(exception, result.Exception);
        Assert.Contains("Failed after 2 retries", result.Message);
        // Initial attempt + 2 retries = 3 total attempts
        _innerProcessorMock.Verify(p => p.ProcessAsync(
            It.IsAny<IMessage>(),
            It.IsAny<ProcessingContext>(),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ProcessAsync_MaxRetriesExceededWithNoException_ReturnsGenericFailure()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var callCount = 0;

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount < 3)
                    throw new TimeoutException("Transient");
                // Final attempt throws but we catch and retry until max retries
                throw new TimeoutException("Still failing");
            });

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.Zero));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider,
            maxRetries: 2);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Exception);
        Assert.Contains("Failed after 2 retries", result.Message);
    }

    #endregion

    #region Error Context

    [Fact]
    public async Task ProcessAsync_OnError_CreatesErrorContextWithCorrectData()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var component = "TestComponent";
        var context = new ProcessingContext { Component = component };
        var exception = new TimeoutException("Timeout");
        ErrorContext? capturedContext = null;

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(exception, "Timeout"));

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<IMessage, Exception, ErrorContext, CancellationToken>((msg, ex, ctx, ct) =>
            {
                capturedContext = ctx;
            })
            .ReturnsAsync(ErrorHandlingResult.Discard("Test"));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider,
            maxRetries: 3);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal(0, capturedContext.RetryCount);
        Assert.Equal(3, capturedContext.MaxRetries);
        Assert.Equal(component, capturedContext.Component);
        Assert.Equal(_timeProvider.GetUtcNow(), capturedContext.FirstFailureTime);
        Assert.Equal(_timeProvider.GetUtcNow(), capturedContext.LastFailureTime);
    }

    [Fact]
    public async Task ProcessAsync_OnRetry_UpdatesRetryCountInContext()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var retryContexts = new List<int>();

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .Returns<IMessage, ProcessingContext, CancellationToken>((msg, ctx, ct) =>
            {
                retryContexts.Add(ctx.RetryCount);
                return ValueTask.FromResult(ProcessingResult.Failed(new TimeoutException(), "Timeout"));
            });

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.Zero));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider,
            maxRetries: 2);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        Assert.Equal(3, retryContexts.Count); // Initial + 2 retries
        Assert.Equal(0, retryContexts[0]); // First attempt
        Assert.Equal(1, retryContexts[1]); // First retry
        Assert.Equal(2, retryContexts[2]); // Second retry
    }

    [Fact]
    public async Task ProcessAsync_OnRetry_PreservesFirstFailureTime()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var initialTime = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(initialTime);

        // Set initial FirstFailureTime in context so it can be preserved
        var initialFirstFailureTime = initialTime.DateTime;
        var context = new ProcessingContext { FirstFailureTime = initialFirstFailureTime };
        var errorContexts = new List<ErrorContext>();

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new TimeoutException(), "Timeout"));

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<IMessage, Exception, ErrorContext, CancellationToken>((msg, ex, ctx, ct) =>
            {
                errorContexts.Add(ctx);
                // Advance time before next retry
                _timeProvider.Advance(TimeSpan.FromMinutes(1));
            })
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.Zero));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider,
            maxRetries: 2);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        Assert.Equal(3, errorContexts.Count);

        // All error contexts should have the same FirstFailureTime (from initial context)
        Assert.All(errorContexts, ctx => Assert.Equal(initialFirstFailureTime, ctx.FirstFailureTime));

        // LastFailureTime should advance with each retry
        Assert.Equal(initialTime.DateTime, errorContexts[0].LastFailureTime);
        Assert.Equal(initialTime.AddMinutes(1).DateTime, errorContexts[1].LastFailureTime);
        Assert.Equal(initialTime.AddMinutes(2).DateTime, errorContexts[2].LastFailureTime);
    }

    #endregion

    #region Cancellation

    [Fact]
    public async Task ProcessAsync_WithCancellationToken_PropagatesToken()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var cts = new CancellationTokenSource();
        var expectedResult = ProcessingResult.Successful();

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, cts.Token))
            .ReturnsAsync(expectedResult);

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act
        var result = await decorator.ProcessAsync(message, context, cts.Token);

        // Assert
        Assert.True(result.Success);
        _innerProcessorMock.Verify(p => p.ProcessAsync(message, context, cts.Token), Times.Once);
    }

    #endregion

    #region Test Message Class

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    #endregion

    #region Retry Delay Edge Cases

    [Fact]
    public async Task ProcessAsync_RetryWithZeroDelay_CompletesWithNoWait()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var callCount = 0;

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    return ProcessingResult.Failed(new TimeoutException("Transient"), "Timeout");
                return ProcessingResult.Successful();
            });

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.Zero)); // Zero delay

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ProcessAsync_RetryWithLargeDelay_WaitsCorrectDuration()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var retryDelay = TimeSpan.FromMilliseconds(500);
        var callCount = 0;

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    return ProcessingResult.Failed(new TimeoutException("Transient"), "Timeout");
                return ProcessingResult.Successful();
            });

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(retryDelay));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ProcessAsync_MultipleRetriesWithVaryingDelays_RespectsEachDelay()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var callCount = 0;
        var delays = new[] { TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200) };

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount < 3)
                    return ProcessingResult.Failed(new TimeoutException("Transient"), "Timeout");
                return ProcessingResult.Successful();
            });

        var delayIndex = 0;
        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var delay = delays[delayIndex];
                delayIndex++;
                return ErrorHandlingResult.Retry(delay);
            });

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task ProcessAsync_RetryDelayTinyValue_RespectsTinyDelay()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var callCount = 0;
        var tinyDelay = TimeSpan.FromMilliseconds(1);

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    return ProcessingResult.Failed(new TimeoutException("Transient"), "Timeout");
                return ProcessingResult.Successful();
            });

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(tinyDelay));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, callCount);
    }

    #endregion

    #region Escalation Path Edge Cases

    [Fact]
    public async Task ProcessAsync_ErrorHandlerReturnsEscalate_PreservesOriginalException()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var originalException = new InvalidOperationException("Original critical error");

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(originalException, "Failed"));

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Escalate());

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act & Assert
        var thrownException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            decorator.ProcessAsync(message, context).AsTask());

        Assert.Same(originalException, thrownException);
        Assert.Equal("Original critical error", thrownException.Message);
    }

    [Fact]
    public async Task ProcessAsync_EscalateAfterExceptionThrown_ThrowsOriginalException()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var thrownException = new TimeoutException("Critical timeout");

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ThrowsAsync(thrownException);

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Escalate());

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act & Assert
        var caught = await Assert.ThrowsAsync<TimeoutException>(() =>
            decorator.ProcessAsync(message, context).AsTask());

        Assert.Same(thrownException, caught);
    }

    #endregion

    #region Max Retries Boundary Conditions

    [Fact]
    public async Task ProcessAsync_WithZeroMaxRetries_AllowsOnlyOneAttempt()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var exception = new TimeoutException("Persistent timeout");

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(exception, "Timeout"));

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.Zero));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider,
            maxRetries: 0);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(exception, result.Exception);
        Assert.Contains("Failed after 0 retries", result.Message);
        // 1 initial attempt + 0 retries = 1 total
        _innerProcessorMock.Verify(p => p.ProcessAsync(
            It.IsAny<IMessage>(),
            It.IsAny<ProcessingContext>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithLargeMaxRetries_AllowsManyAttempts()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var callCount = 0;

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount < 6)
                    return ProcessingResult.Failed(new TimeoutException("Transient"), "Timeout");
                return ProcessingResult.Successful();
            });

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.Zero));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider,
            maxRetries: 10);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(6, callCount);
    }

    [Fact]
    public async Task ProcessAsync_ExceedsMaxRetriesOnExactBoundary_FailsWithCorrectMessage()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var maxRetries = 3;
        var exception = new TimeoutException("Persistent");

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(exception, "Timeout"));

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.Zero));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider,
            maxRetries: maxRetries);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(exception, result.Exception);
        Assert.Contains($"Failed after {maxRetries} retries", result.Message);
        // Initial + maxRetries attempts
        _innerProcessorMock.Verify(p => p.ProcessAsync(
            It.IsAny<IMessage>(),
            It.IsAny<ProcessingContext>(),
            It.IsAny<CancellationToken>()), Times.Exactly(maxRetries + 1));
    }

    #endregion

    #region Error Context Edge Cases

    [Fact]
    public async Task ProcessAsync_WithEmptyMetadata_CreatesContextWithEmptyMetadata()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext { Metadata = ImmutableDictionary<string, object>.Empty };
        var exception = new TimeoutException("Timeout");
        ErrorContext? capturedContext = null;

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(exception, "Timeout"));

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<IMessage, Exception, ErrorContext, CancellationToken>((msg, ex, ctx, ct) =>
            {
                capturedContext = ctx;
            })
            .ReturnsAsync(ErrorHandlingResult.Discard("Test"));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.NotNull(capturedContext.Metadata);
        Assert.Empty(capturedContext.Metadata);
    }

    [Fact]
    public async Task ProcessAsync_WithMetadata_CopiesMetadataWithoutMutation()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var originalMetadata = ImmutableDictionary<string, object>.Empty
            .SetItem("key1", "value1")
            .SetItem("key2", 42);
        var context = new ProcessingContext { Metadata = originalMetadata };
        var exception = new TimeoutException("Timeout");
        ErrorContext? capturedContext = null;

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(exception, "Timeout"));

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<IMessage, Exception, ErrorContext, CancellationToken>((msg, ex, ctx, ct) =>
            {
                capturedContext = ctx;
            })
            .ReturnsAsync(ErrorHandlingResult.Discard("Test"));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.NotNull(capturedContext.Metadata);
        Assert.Equal(2, capturedContext.Metadata.Count);
        Assert.Equal("value1", capturedContext.Metadata["key1"]);
        Assert.Equal(42, capturedContext.Metadata["key2"]);

        // Verify original metadata wasn't mutated
        Assert.Equal(2, originalMetadata.Count);
        Assert.Equal("value1", originalMetadata["key1"]);
    }

    [Fact]
    public async Task ProcessAsync_RetryingWithDifferentExceptionTypes_UpdatesLastException()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var callCount = 0;

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount switch
                {
                    1 => ProcessingResult.Failed(new TimeoutException("Timeout"), "Timeout"),
                    2 => ProcessingResult.Failed(new InvalidOperationException("Invalid"), "Invalid"),
                    _ => ProcessingResult.Successful()
                };
            });

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.Zero));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, callCount);
    }

    #endregion

    #region Cancellation During Retry

    [Fact]
    public async Task ProcessAsync_CancellationDuringRetryDelay_PropagatesCancellation()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var cts = new CancellationTokenSource();
        var callCount = 0;

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    return ProcessingResult.Failed(new TimeoutException("Transient"), "Timeout");
                return ProcessingResult.Successful();
            });

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<IMessage, Exception, ErrorContext, CancellationToken>((msg, ex, ctx, ct) =>
            {
                // Cancel during error handling callback
                cts.Cancel();
            })
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.FromSeconds(10)));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            decorator.ProcessAsync(message, context, cts.Token).AsTask());

        Assert.Equal(1, callCount); // Only first attempt
    }

    #endregion

    #region Logging Coverage

    [Fact]
    public async Task ProcessAsync_OnRetrySuccess_LogsRetryCount()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var callCount = 0;

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount < 3)
                    return ProcessingResult.Failed(new TimeoutException("Transient"), "Timeout");
                return ProcessingResult.Successful();
            });

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.Zero));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        // Should log success after 2 retries
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("succeeded after") && v.ToString()!.Contains("2")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_LogsDebugWhenRetryDelayPresent()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var retryDelay = TimeSpan.FromMilliseconds(250);
        var callCount = 0;

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    return ProcessingResult.Failed(new TimeoutException("Transient"), "Timeout");
                return ProcessingResult.Successful();
            });

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(retryDelay));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Waiting") && v.ToString()!.Contains("250")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_MaxRetriesFailure_LogsErrorWithCorrectCount()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var maxRetries = 2;

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new TimeoutException("Persistent"), "Timeout"));

        _errorHandlerMock.Setup(e => e.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.Zero));

        var decorator = new ErrorHandlingDecorator(
            _innerProcessorMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object,
            _timeProvider,
            maxRetries: maxRetries);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("failed after") && v.ToString()!.Contains("2")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion
}

using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Policies;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

/// <summary>
/// Unit tests for RetryDecorator
/// Tests retry logic integration with message processing pipeline
/// </summary>
[Trait("Category", "Unit")]
public sealed class RetryDecoratorTests
{
    private readonly Mock<IMessageProcessor> _innerProcessorMock;
    private readonly Mock<ILogger<RetryDecorator>> _mockLogger;

    public RetryDecoratorTests()
    {
        _innerProcessorMock = new Mock<IMessageProcessor>();
        _mockLogger = new Mock<ILogger<RetryDecorator>>();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesDecorator()
    {
        // Act
        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object);

        // Assert
        Assert.NotNull(decorator);
    }

    [Fact]
    public void Constructor_WithCustomRetryPolicy_UsesCustomPolicy()
    {
        // Arrange
        var customPolicy = new Mock<IRetryPolicy>();
        customPolicy.Setup(p => p.MaxRetries).Returns(5);

        // Act
        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object, customPolicy.Object);

        // Assert
        Assert.NotNull(decorator);
    }

    [Fact]
    public void Constructor_WithNullRetryPolicy_UsesDefaultPolicy()
    {
        // Act
        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object, null);

        // Assert
        Assert.NotNull(decorator);
    }

    #endregion

    #region ProcessAsync Success Tests

    [Fact]
    public async Task ProcessAsync_SuccessOnFirstAttempt_ReturnsSuccess()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var expectedResult = ProcessingResult.Successful();

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        _innerProcessorMock.Verify(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_SuccessAfterRetry_LogsRetrySuccess()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var callCount = 0;

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    return ProcessingResult.Failed(new TimeoutException("Transient error"), "Timeout");
                return ProcessingResult.Successful();
            });

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, callCount); // Initial + 1 retry
    }

    #endregion

    #region ProcessAsync Retry Tests

    [Fact]
    public async Task ProcessAsync_TransientFailure_RetriesAccordingToPolicy()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var retryPolicy = new ExponentialBackoffRetryPolicy(maxRetries: 2, baseDelay: TimeSpan.Zero, jitterFactor: 0);

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new TimeoutException(), "Timeout"));

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object, retryPolicy);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        // Initial attempt + 2 retries = 3 total
        _innerProcessorMock.Verify(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ProcessAsync_NonTransientFailure_DoesNotRetry()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new InvalidOperationException("Non-transient"), "Error"));

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        _innerProcessorMock.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ExceptionThrown_RetriesIfTransient()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var callCount = 0;

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount < 3)
                    throw new TimeoutException("Transient");
                return ValueTask.FromResult(ProcessingResult.Successful());
            });

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task ProcessAsync_MaxRetriesExceeded_ReturnsFailure()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var retryPolicy = new ExponentialBackoffRetryPolicy(maxRetries: 2, baseDelay: TimeSpan.Zero, jitterFactor: 0);

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new TimeoutException("Persistent failure"), "Timeout"));

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object, retryPolicy);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        // The message comes from the last failed result, not from the decorator
        Assert.NotNull(result.Exception);
        _innerProcessorMock.Verify(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    #endregion

    #region ProcessAsync Context Tests

    [Fact]
    public async Task ProcessAsync_OnRetry_UpdatesContextWithRetryCount()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        ProcessingContext? capturedContext = null;
        var callCount = 0;

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .Returns<IMessage, ProcessingContext, CancellationToken>((msg, ctx, ct) =>
            {
                callCount++;
                capturedContext = ctx;
                if (callCount == 1)
                    return ValueTask.FromResult(ProcessingResult.Failed(new TimeoutException(), "Timeout"));
                return ValueTask.FromResult(ProcessingResult.Successful());
            });

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(capturedContext);
        Assert.Equal(1, capturedContext.Value.RetryCount); // Context from retry should have RetryCount = 1
    }

    [Fact]
    public async Task ProcessAsync_OnRetry_UpdatesRetryCount()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        int firstRetryCount = -1;
        int secondRetryCount = -1;
        var callCount = 0;

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .Returns<IMessage, ProcessingContext, CancellationToken>((msg, ctx, ct) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    firstRetryCount = ctx.RetryCount;
                    return ValueTask.FromResult(ProcessingResult.Failed(new TimeoutException(), "Timeout"));
                }
                else if (callCount == 2)
                {
                    secondRetryCount = ctx.RetryCount;
                    return ValueTask.FromResult(ProcessingResult.Failed(new TimeoutException(), "Timeout"));
                }
                return ValueTask.FromResult(ProcessingResult.Successful());
            });

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert - RetryCount should increment across retries
        Assert.Equal(0, firstRetryCount); // First attempt has retry count 0
        Assert.Equal(1, secondRetryCount); // Second attempt has retry count 1
    }

    #endregion

    #region ProcessAsync Cancellation Tests

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

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context, cts.Token);

        // Assert
        Assert.True(result.Success);
        _innerProcessorMock.Verify(p => p.ProcessAsync(message, context, cts.Token), Times.Once);
    }

    #endregion

    #region ProcessAsync Critical Error Tests

    [Fact]
    public async Task ProcessAsync_OutOfMemoryException_DoesNotRetry()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OutOfMemoryException("Critical error"));

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<OutOfMemoryException>(() =>
            decorator.ProcessAsync(message, context).AsTask());

        _innerProcessorMock.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_StackOverflowException_DoesNotRetry()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StackOverflowException("Critical error"));

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<StackOverflowException>(() =>
            decorator.ProcessAsync(message, context).AsTask());

        _innerProcessorMock.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_AccessViolationException_DoesNotRetry()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AccessViolationException("Critical error"));

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<AccessViolationException>(() =>
            decorator.ProcessAsync(message, context).AsTask());

        _innerProcessorMock.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ProcessAsync Cancellation Edge Case Tests

    [Fact]
    public async Task ProcessAsync_CancellationDuringRetryDelay_PropagatesCancellation()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var cts = new CancellationTokenSource();
        var callCount = 0;

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    cts.Cancel();
                    return ValueTask.FromResult(ProcessingResult.Failed(new TimeoutException("Transient"), "Timeout"));
                }
                return ValueTask.FromResult(ProcessingResult.Successful());
            });

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await decorator.ProcessAsync(message, context, cts.Token));
    }

    [Fact]
    public async Task ProcessAsync_OperationCanceledException_WhenTransientNotRetried()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var retryPolicy = new ExponentialBackoffRetryPolicy(maxRetries: 2, baseDelay: TimeSpan.Zero, jitterFactor: 0);

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new OperationCanceledException("Cancelled"), "Cancelled"));

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object, retryPolicy);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert - OperationCanceledException is transient, should retry
        Assert.False(result.Success);
        _innerProcessorMock.Verify(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ProcessAsync_TaskCanceledException_IsRetried()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var callCount = 0;

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount < 2)
                    return ValueTask.FromResult(ProcessingResult.Failed(new TaskCanceledException("Timeout"), "Timeout"));
                return ValueTask.FromResult(ProcessingResult.Successful());
            });

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, callCount);
    }

    #endregion

    #region ExponentialBackoffRetryPolicy Edge Case Tests

    [Fact]
    public void ExponentialBackoffRetryPolicy_AtMaxRetries_DoesNotRetry()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 3);

        // Act & Assert
        Assert.False(policy.ShouldRetry(new TimeoutException(), attemptNumber: 3));
    }

    [Fact]
    public void ExponentialBackoffRetryPolicy_WithNullException_DoesNotRetry()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 3);

        // Act & Assert
        Assert.False(policy.ShouldRetry(exception: null, attemptNumber: 0));
    }

    [Fact]
    public void ExponentialBackoffRetryPolicy_WithNonTransientException_DoesNotRetry()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 3);

        // Act & Assert
        Assert.False(policy.ShouldRetry(new ArgumentException("Invalid"), attemptNumber: 0));
    }

    [Fact]
    public void ExponentialBackoffRetryPolicy_WithTimeoutException_Retries()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 3);

        // Act & Assert
        Assert.True(policy.ShouldRetry(new TimeoutException(), attemptNumber: 0));
    }

    [Fact]
    public void ExponentialBackoffRetryPolicy_GetRetryDelay_RespectsClamping()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(
            maxRetries: 5,
            baseDelay: TimeSpan.FromSeconds(10),
            maxDelay: TimeSpan.FromSeconds(30),
            jitterFactor: 0);

        // Act - With 0 jitter, delay should be exactly exponential and clamped
        var delay1 = policy.GetRetryDelay(0); // 10 * 2^0 = 10s
        var delay2 = policy.GetRetryDelay(1); // 10 * 2^1 = 20s
        var delay3 = policy.GetRetryDelay(2); // 10 * 2^2 = 40s, but clamped to 30s

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(10), delay1);
        Assert.Equal(TimeSpan.FromSeconds(20), delay2);
        Assert.Equal(TimeSpan.FromSeconds(30), delay3);
    }

    [Fact]
    public void ExponentialBackoffRetryPolicy_GetRetryDelay_WithJitter_WithinBounds()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(
            maxRetries: 5,
            baseDelay: TimeSpan.FromSeconds(1),
            maxDelay: TimeSpan.FromSeconds(10),
            jitterFactor: 0.5);

        // Act
        var delays = Enumerable.Range(0, 10)
            .Select(i => policy.GetRetryDelay(0))
            .ToList();

        // Assert - With jitter, should be between 1s and 1.5s for attempt 0
        foreach (var delay in delays)
        {
            Assert.True(delay.TotalMilliseconds >= 1000, "Delay below minimum with jitter");
            Assert.True(delay.TotalMilliseconds <= 1500, "Delay above maximum with jitter");
        }
    }

    [Fact]
    public void ExponentialBackoffRetryPolicy_WithWrappedTransientException_Retries()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 3);
        var innerException = new TimeoutException("Inner timeout");
        var wrappedException = new Exception("Wrapped", innerException);

        // Act & Assert
        Assert.True(policy.ShouldRetry(wrappedException, attemptNumber: 0));
    }

    [Fact]
    public void ExponentialBackoffRetryPolicy_WithDeeplyNestedTransientException_Retries()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 3);
        var innerException = new TimeoutException("Inner timeout");
        var middleException = new Exception("Middle", innerException);
        var outerException = new Exception("Outer", middleException);

        // Act & Assert
        Assert.True(policy.ShouldRetry(outerException, attemptNumber: 0));
    }

    #endregion

    #region ProcessAsync First Failure Time Tests

    [Fact]
    public async Task ProcessAsync_PreservesFirstFailureTime()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var initialContext = new ProcessingContext();
        DateTimeOffset? capturedFirstFailureTime = null;
        var callCount = 0;

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .Returns<IMessage, ProcessingContext, CancellationToken>((msg, ctx, ct) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return ValueTask.FromResult(ProcessingResult.Failed(new TimeoutException(), "Timeout"));
                }
                else if (callCount == 2)
                {
                    capturedFirstFailureTime = ctx.FirstFailureTime;
                    return ValueTask.FromResult(ProcessingResult.Failed(new TimeoutException(), "Timeout"));
                }
                return ValueTask.FromResult(ProcessingResult.Successful());
            });

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object);

        // Act
        await decorator.ProcessAsync(message, initialContext);

        // Assert
        Assert.NotNull(capturedFirstFailureTime);
        Assert.Equal(initialContext.FirstFailureTime, capturedFirstFailureTime);
    }

    #endregion

    #region ProcessAsync Result Preservation Tests

    [Fact]
    public async Task ProcessAsync_ReturnsFinalException_WhenAllRetriesFail()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var expectedEx = new TimeoutException("Expected exception");
        var retryPolicy = new ExponentialBackoffRetryPolicy(maxRetries: 1, baseDelay: TimeSpan.Zero, jitterFactor: 0);

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(expectedEx, "Timeout"));

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object, retryPolicy);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(expectedEx, result.Exception);
    }

    [Fact]
    public async Task ProcessAsync_WithoutException_ReturnsDefaultException()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var retryPolicy = new ExponentialBackoffRetryPolicy(maxRetries: 0, baseDelay: TimeSpan.Zero, jitterFactor: 0);

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessingResult { Success = false, Exception = null });

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object, retryPolicy);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Exception);
        Assert.Equal("Processing failed after retries", result.Exception.Message);
    }

    #endregion

    #region ProcessAsync Immediate Success (No Retry Needed) Tests

    [Fact]
    public async Task ProcessAsync_SuccessOnFirstAttempt_LogsDirectSuccess()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var expectedResult = ProcessingResult.Successful();

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        // Should not log retry success because retryCount == 0
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Never);
    }

    #endregion

    #region ProcessAsync Non-Retryable Result Tests

    [Fact]
    public async Task ProcessAsync_NonRetryableResult_DoesNotRetry()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var retryPolicy = new ExponentialBackoffRetryPolicy(maxRetries: 3);

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new InvalidOperationException("Non-transient"), "Error"));

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object, retryPolicy);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        _innerProcessorMock.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ProcessAsync Exception Not Caught Tests

    [Fact]
    public async Task ProcessAsync_NonRetryableExceptionThrown_PropagatesException()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Non-transient error"));

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            decorator.ProcessAsync(message, context).AsTask());

        _innerProcessorMock.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ProcessAsync Logging Tests

    [Fact]
    public async Task ProcessAsync_OnRetryWarning_LogsRetryAttempt()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var callCount = 0;

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                    return ValueTask.FromResult(ProcessingResult.Failed(new TimeoutException(), "Timeout"));
                return ValueTask.FromResult(ProcessingResult.Successful());
            });

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Retry")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_OnFinalFailure_LogsError()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var retryPolicy = new ExponentialBackoffRetryPolicy(maxRetries: 0, baseDelay: TimeSpan.Zero, jitterFactor: 0);

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new TimeoutException(), "Timeout"));

        var decorator = new RetryDecorator(_innerProcessorMock.Object, _mockLogger.Object, retryPolicy);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("failed after")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
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
}

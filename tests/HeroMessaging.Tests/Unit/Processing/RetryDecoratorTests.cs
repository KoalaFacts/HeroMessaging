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

    #region Test Message Class

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    #endregion
}

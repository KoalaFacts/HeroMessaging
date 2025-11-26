using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Policies;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing.Decorators;

[Trait("Category", "Unit")]
public sealed class RetryDecoratorTests
{
    private readonly Mock<IMessageProcessor> _innerMock;
    private readonly Mock<ILogger<RetryDecorator>> _loggerMock;
    private readonly Mock<IRetryPolicy> _retryPolicyMock;

    public RetryDecoratorTests()
    {
        _innerMock = new Mock<IMessageProcessor>();
        _loggerMock = new Mock<ILogger<RetryDecorator>>();
        _retryPolicyMock = new Mock<IRetryPolicy>();
    }

    private RetryDecorator CreateDecorator(IRetryPolicy? retryPolicy = null)
    {
        return new RetryDecorator(_innerMock.Object, _loggerMock.Object, retryPolicy);
    }

    #region ProcessAsync - Success Cases

    [Fact]
    public async Task ProcessAsync_WithSuccessfulResult_ReturnsSuccessWithoutRetry()
    {
        // Arrange
        var decorator = CreateDecorator(_retryPolicyMock.Object);
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
        _retryPolicyMock.Verify(p => p.ShouldRetry(It.IsAny<Exception>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithSuccessAfterRetries_LogsInformation()
    {
        // Arrange
        _retryPolicyMock.Setup(p => p.MaxRetries).Returns(3);
        _retryPolicyMock.Setup(p => p.ShouldRetry(It.IsAny<Exception>(), It.IsAny<int>())).Returns(true);
        _retryPolicyMock.Setup(p => p.GetRetryDelay(It.IsAny<int>())).Returns(TimeSpan.Zero);

        var decorator = CreateDecorator(_retryPolicyMock.Object);
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

    #region ProcessAsync - Retry on Failure

    [Fact]
    public async Task ProcessAsync_WithRetryableFailure_RetriesProcessing()
    {
        // Arrange
        _retryPolicyMock.Setup(p => p.MaxRetries).Returns(2);
        _retryPolicyMock.Setup(p => p.ShouldRetry(It.IsAny<Exception>(), It.IsAny<int>()))
            .Returns((Exception ex, int attempt) => attempt < 2); // Only retry if attempt < MaxRetries
        _retryPolicyMock.Setup(p => p.GetRetryDelay(It.IsAny<int>())).Returns(TimeSpan.Zero);

        var decorator = CreateDecorator(_retryPolicyMock.Object);
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new TimeoutException("Timeout");

        _innerMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(testException));

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        _innerMock.Verify(
            p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3)); // Initial + 2 retries
    }

    [Fact]
    public async Task ProcessAsync_WithRetryableException_RetriesProcessing()
    {
        // Arrange
        _retryPolicyMock.Setup(p => p.MaxRetries).Returns(2);
        _retryPolicyMock.Setup(p => p.ShouldRetry(It.IsAny<Exception>(), It.IsAny<int>()))
            .Returns((Exception ex, int attempt) => attempt < 2); // Only retry if attempt < MaxRetries
        _retryPolicyMock.Setup(p => p.GetRetryDelay(It.IsAny<int>())).Returns(TimeSpan.Zero);

        var decorator = CreateDecorator(_retryPolicyMock.Object);
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new TimeoutException("Timeout");

        _innerMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(testException);

        // Act & Assert - Exception should be rethrown after retries exhausted
        await Assert.ThrowsAsync<TimeoutException>(
            async () => await decorator.ProcessAsync(message, context));

        _innerMock.Verify(
            p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3)); // Initial + 2 retries
    }

    [Fact]
    public async Task ProcessAsync_WithNonRetryableException_DoesNotRetry()
    {
        // Arrange
        _retryPolicyMock.Setup(p => p.MaxRetries).Returns(2);
        _retryPolicyMock.Setup(p => p.ShouldRetry(It.IsAny<Exception>(), It.IsAny<int>())).Returns(false);

        var decorator = CreateDecorator(_retryPolicyMock.Object);
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new InvalidOperationException("Non-retryable");

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ThrowsAsync(testException);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await decorator.ProcessAsync(message, context));

        _innerMock.Verify(
            p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()),
            Times.Once); // No retries
    }

    #endregion

    #region ProcessAsync - Retry Delay

    [Fact]
    public async Task ProcessAsync_WithRetryDelay_WaitsBeforeRetrying()
    {
        // Arrange
        _retryPolicyMock.Setup(p => p.MaxRetries).Returns(1);
        _retryPolicyMock.Setup(p => p.ShouldRetry(It.IsAny<Exception>(), It.IsAny<int>())).Returns(true);
        _retryPolicyMock.Setup(p => p.GetRetryDelay(0)).Returns(TimeSpan.FromMilliseconds(100));

        var decorator = CreateDecorator(_retryPolicyMock.Object);
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new TimeoutException("Timeout")));

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await decorator.ProcessAsync(message, context);
        stopwatch.Stop();

        // Assert - Should have waited at least for the delay
        Assert.True(stopwatch.ElapsedMilliseconds >= 100,
            $"Expected at least 100ms delay, but was {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task ProcessAsync_OnRetry_LogsWarningWithDelay()
    {
        // Arrange
        _retryPolicyMock.Setup(p => p.MaxRetries).Returns(1);
        _retryPolicyMock.Setup(p => p.ShouldRetry(It.IsAny<Exception>(), It.IsAny<int>())).Returns(true);
        _retryPolicyMock.Setup(p => p.GetRetryDelay(It.IsAny<int>())).Returns(TimeSpan.FromMilliseconds(50));

        var decorator = CreateDecorator(_retryPolicyMock.Object);
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new TimeoutException("Timeout")));

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

    #region ProcessAsync - Context Updates

    [Fact]
    public async Task ProcessAsync_OnRetry_UpdatesContextWithRetryCount()
    {
        // Arrange
        _retryPolicyMock.Setup(p => p.MaxRetries).Returns(2);
        _retryPolicyMock.Setup(p => p.ShouldRetry(It.IsAny<Exception>(), It.IsAny<int>())).Returns(true);
        _retryPolicyMock.Setup(p => p.GetRetryDelay(It.IsAny<int>())).Returns(TimeSpan.Zero);

        var decorator = CreateDecorator(_retryPolicyMock.Object);
        var message = new TestMessage();
        var context = new ProcessingContext();
        var capturedContexts = new List<ProcessingContext>();

        _innerMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .Returns<IMessage, ProcessingContext, CancellationToken>((m, c, ct) =>
            {
                capturedContexts.Add(c);
                return ValueTask.FromResult(ProcessingResult.Failed(new TimeoutException("Timeout")));
            });

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        Assert.Equal(3, capturedContexts.Count); // Initial + 2 retries
        Assert.Equal(0, capturedContexts[0].RetryCount);
        Assert.Equal(1, capturedContexts[1].RetryCount);
        Assert.Equal(2, capturedContexts[2].RetryCount);
    }

    [Fact]
    public async Task ProcessAsync_OnRetry_PreservesFirstFailureTime()
    {
        // Arrange
        _retryPolicyMock.Setup(p => p.MaxRetries).Returns(2);
        _retryPolicyMock.Setup(p => p.ShouldRetry(It.IsAny<Exception>(), It.IsAny<int>())).Returns(true);
        _retryPolicyMock.Setup(p => p.GetRetryDelay(It.IsAny<int>())).Returns(TimeSpan.Zero);

        var decorator = CreateDecorator(_retryPolicyMock.Object);
        var message = new TestMessage();
        var context = new ProcessingContext();
        var capturedContexts = new List<ProcessingContext>();

        _innerMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .Returns<IMessage, ProcessingContext, CancellationToken>((m, c, ct) =>
            {
                capturedContexts.Add(c);
                return ValueTask.FromResult(ProcessingResult.Failed(new TimeoutException("Timeout")));
            });

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert - FirstFailureTime should be the same for all retries
        Assert.True(capturedContexts.All(c => c.FirstFailureTime == capturedContexts[0].FirstFailureTime));
    }

    #endregion

    #region ProcessAsync - Max Retries

    [Fact]
    public async Task ProcessAsync_WhenMaxRetriesExceeded_ReturnsFailure()
    {
        // Arrange
        _retryPolicyMock.Setup(p => p.MaxRetries).Returns(2);
        _retryPolicyMock.Setup(p => p.ShouldRetry(It.IsAny<Exception>(), It.IsAny<int>())).Returns(true);
        _retryPolicyMock.Setup(p => p.GetRetryDelay(It.IsAny<int>())).Returns(TimeSpan.Zero);

        var decorator = CreateDecorator(_retryPolicyMock.Object);
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new TimeoutException("Timeout");

        _innerMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(testException));

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed after 2 retries", result.Message);
    }

    [Fact]
    public async Task ProcessAsync_WhenMaxRetriesExceeded_LogsError()
    {
        // Arrange
        _retryPolicyMock.Setup(p => p.MaxRetries).Returns(1);
        _retryPolicyMock.Setup(p => p.ShouldRetry(It.IsAny<Exception>(), It.IsAny<int>())).Returns(true);
        _retryPolicyMock.Setup(p => p.GetRetryDelay(It.IsAny<int>())).Returns(TimeSpan.Zero);

        var decorator = CreateDecorator(_retryPolicyMock.Object);
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new TimeoutException("Timeout")));

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
            Times.Once);
    }

    #endregion

    #region ProcessAsync - Default Retry Policy

    [Fact]
    public async Task ProcessAsync_WithoutRetryPolicy_UsesDefaultExponentialBackoff()
    {
        // Arrange
        var decorator = CreateDecorator(); // No policy provided, should use default
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

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, attemptCount); // Initial + 2 retries (default is 3 max retries)
    }

    #endregion

    #region ProcessAsync - Cancellation

    [Fact]
    public async Task ProcessAsync_PassesCancellationTokenToInner()
    {
        // Arrange
        _retryPolicyMock.Setup(p => p.MaxRetries).Returns(0);
        var decorator = CreateDecorator(_retryPolicyMock.Object);
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

[Trait("Category", "Unit")]
public sealed class ExponentialBackoffRetryPolicyTests
{
    #region ShouldRetry Tests

    [Fact]
    public void ShouldRetry_WithAttemptBelowMaxRetries_ReturnsTrue()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 3);
        var exception = new TimeoutException();

        // Act
        var result = policy.ShouldRetry(exception, 1);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldRetry_WithAttemptAtMaxRetries_ReturnsFalse()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 3);
        var exception = new TimeoutException();

        // Act
        var result = policy.ShouldRetry(exception, 3);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldRetry_WithNullException_ReturnsFalse()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 3);

        // Act
        var result = policy.ShouldRetry(null, 1);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(TaskCanceledException))]
    [InlineData(typeof(OperationCanceledException))]
    public void ShouldRetry_WithTransientException_ReturnsTrue(Type exceptionType)
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 3);
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;

        // Act
        var result = policy.ShouldRetry(exception, 1);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData(typeof(OutOfMemoryException))]
    [InlineData(typeof(StackOverflowException))]
    [InlineData(typeof(AccessViolationException))]
    public void ShouldRetry_WithCriticalException_ReturnsFalse(Type exceptionType)
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 3);
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;

        // Act
        var result = policy.ShouldRetry(exception, 1);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldRetry_WithInnerTransientException_ReturnsTrue()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 3);
        var innerException = new TimeoutException("Timeout");
        var exception = new InvalidOperationException("Outer", innerException);

        // Act
        var result = policy.ShouldRetry(exception, 1);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region GetRetryDelay Tests

    [Fact]
    public void GetRetryDelay_WithAttempt0_ReturnsBaseDelay()
    {
        // Arrange
        var baseDelay = TimeSpan.FromSeconds(1);
        var policy = new ExponentialBackoffRetryPolicy(baseDelay: baseDelay, jitterFactor: 0);

        // Act
        var delay = policy.GetRetryDelay(0);

        // Assert
        Assert.Equal(baseDelay, delay);
    }

    [Fact]
    public void GetRetryDelay_WithAttempt1_ReturnsDoubledDelay()
    {
        // Arrange
        var baseDelay = TimeSpan.FromSeconds(1);
        var policy = new ExponentialBackoffRetryPolicy(baseDelay: baseDelay, jitterFactor: 0);

        // Act
        var delay = policy.GetRetryDelay(1);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(2), delay);
    }

    [Fact]
    public void GetRetryDelay_WithAttempt2_ReturnsQuadrupledDelay()
    {
        // Arrange
        var baseDelay = TimeSpan.FromSeconds(1);
        var policy = new ExponentialBackoffRetryPolicy(baseDelay: baseDelay, jitterFactor: 0);

        // Act
        var delay = policy.GetRetryDelay(2);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(4), delay);
    }

    [Fact]
    public void GetRetryDelay_WithMaxDelay_CapsAtMaximum()
    {
        // Arrange
        var baseDelay = TimeSpan.FromSeconds(1);
        var maxDelay = TimeSpan.FromSeconds(5);
        var policy = new ExponentialBackoffRetryPolicy(baseDelay: baseDelay, maxDelay: maxDelay, jitterFactor: 0);

        // Act
        var delay = policy.GetRetryDelay(10); // Would be 1024 seconds without cap

        // Assert
        Assert.Equal(maxDelay, delay);
    }

    [Fact]
    public void GetRetryDelay_WithJitter_AddsSomeRandomness()
    {
        // Arrange
        var baseDelay = TimeSpan.FromSeconds(1);
        var policy = new ExponentialBackoffRetryPolicy(baseDelay: baseDelay, jitterFactor: 0.3);

        // Act - Get multiple delays and ensure they vary due to jitter
        var delays = new List<TimeSpan>();
        for (int i = 0; i < 10; i++)
        {
            delays.Add(policy.GetRetryDelay(2));
        }

        // Assert - Not all delays should be identical due to jitter
        var uniqueDelays = delays.Distinct().Count();
        Assert.True(uniqueDelays > 1, "Expected jitter to produce varying delays");
    }

    #endregion

    #region MaxRetries Property

    [Fact]
    public void MaxRetries_ReturnsConfiguredValue()
    {
        // Arrange & Act
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 5);

        // Assert
        Assert.Equal(5, policy.MaxRetries);
    }

    #endregion
}

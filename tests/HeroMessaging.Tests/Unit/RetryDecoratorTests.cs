using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Policies;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit;

[Trait("Category", "Unit")]
public class RetryDecoratorTests
{
    private readonly Mock<IMessageProcessor> _mockInner;
    private readonly Mock<ILogger<RetryDecorator>> _mockLogger;
    private readonly Mock<IRetryPolicy> _mockRetryPolicy;
    private readonly TestMessage _testMessage;
    private readonly ProcessingContext _context;

    public RetryDecoratorTests()
    {
        _mockInner = new Mock<IMessageProcessor>();
        _mockLogger = new Mock<ILogger<RetryDecorator>>();
        _mockRetryPolicy = new Mock<IRetryPolicy>();
        _testMessage = new TestMessage { MessageId = Guid.NewGuid() };
        _context = new ProcessingContext();

        _mockRetryPolicy.Setup(p => p.MaxRetries).Returns(3);
    }

    #region Success Tests

    [Fact]
    public async Task ProcessAsync_WithSuccessOnFirstAttempt_ReturnsSuccessWithoutRetry()
    {
        // Arrange
        var sut = new RetryDecorator(_mockInner.Object, _mockLogger.Object, _mockRetryPolicy.Object);
        var successResult = ProcessingResult.Successful();

        _mockInner.Setup(i => i.ProcessAsync(_testMessage, _context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(successResult);

        // Act
        var result = await sut.ProcessAsync(_testMessage, _context);

        // Assert
        Assert.True(result.Success);
        _mockInner.Verify(i => i.ProcessAsync(_testMessage, _context, It.IsAny<CancellationToken>()), Times.Once);
        _mockRetryPolicy.Verify(p => p.GetRetryDelay(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithSuccessAfterRetries_ReturnsSuccessAndLogsRetryCount()
    {
        // Arrange
        var sut = new RetryDecorator(_mockInner.Object, _mockLogger.Object, _mockRetryPolicy.Object);
        var exception = new TimeoutException();
        var failureResult = ProcessingResult.Failed(exception, "Timeout");
        var successResult = ProcessingResult.Successful();

        _mockInner.SetupSequence(i => i.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failureResult)
            .ReturnsAsync(failureResult)
            .ReturnsAsync(successResult);

        _mockRetryPolicy.Setup(p => p.ShouldRetry(exception, It.IsAny<int>())).Returns(true);
        _mockRetryPolicy.Setup(p => p.GetRetryDelay(It.IsAny<int>())).Returns(TimeSpan.FromMilliseconds(10));

        // Act
        var result = await sut.ProcessAsync(_testMessage, _context);

        // Assert
        Assert.True(result.Success);
        _mockInner.Verify(i => i.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    #endregion

    #region Retry Tests

    [Fact]
    public async Task ProcessAsync_WithRetryableException_RetriesUpToMaxRetries()
    {
        // Arrange
        var sut = new RetryDecorator(_mockInner.Object, _mockLogger.Object, _mockRetryPolicy.Object);
        var exception = new TimeoutException("Timeout");
        var failureResult = ProcessingResult.Failed(exception, "Timeout");

        _mockInner.Setup(i => i.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failureResult);
        _mockRetryPolicy.Setup(p => p.ShouldRetry(exception, It.IsAny<int>())).Returns(true);
        _mockRetryPolicy.Setup(p => p.GetRetryDelay(It.IsAny<int>())).Returns(TimeSpan.FromMilliseconds(10));

        // Act
        var result = await sut.ProcessAsync(_testMessage, _context);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Exception);
        _mockInner.Verify(i => i.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Exactly(4)); // Initial + 3 retries
    }

    [Fact]
    public async Task ProcessAsync_WithNonRetryableException_DoesNotRetry()
    {
        // Arrange
        var sut = new RetryDecorator(_mockInner.Object, _mockLogger.Object, _mockRetryPolicy.Object);
        var exception = new InvalidOperationException("Non-retryable");
        var failureResult = ProcessingResult.Failed(exception, "Failed");

        _mockInner.Setup(i => i.ProcessAsync(_testMessage, _context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(failureResult);
        _mockRetryPolicy.Setup(p => p.ShouldRetry(exception, It.IsAny<int>())).Returns(false);

        // Act
        var result = await sut.ProcessAsync(_testMessage, _context);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(exception, result.Exception);
        _mockInner.Verify(i => i.ProcessAsync(_testMessage, _context, It.IsAny<CancellationToken>()), Times.Once);
        _mockRetryPolicy.Verify(p => p.GetRetryDelay(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithExceptionThrown_RetriesIfRetryable()
    {
        // Arrange
        var sut = new RetryDecorator(_mockInner.Object, _mockLogger.Object, _mockRetryPolicy.Object);
        var exception = new TimeoutException("Timeout");
        var callCount = 0;

        _mockInner.Setup(i => i.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // ShouldRetry returns true for first 3 attempts, then false to stop retrying
        _mockRetryPolicy.Setup(p => p.ShouldRetry(exception, It.IsAny<int>()))
            .Returns(() => callCount++ < 3);
        _mockRetryPolicy.Setup(p => p.GetRetryDelay(It.IsAny<int>())).Returns(TimeSpan.FromMilliseconds(10));

        // Act & Assert - Exception is re-thrown when ShouldRetry returns false
        await Assert.ThrowsAsync<TimeoutException>(async () => await sut.ProcessAsync(_testMessage, _context));
        _mockInner.Verify(i => i.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [Fact]
    public async Task ProcessAsync_WithExceptionThrown_DoesNotRetryIfNonRetryable()
    {
        // Arrange
        var sut = new RetryDecorator(_mockInner.Object, _mockLogger.Object, _mockRetryPolicy.Object);
        var exception = new InvalidOperationException("Non-retryable");

        _mockInner.Setup(i => i.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
        _mockRetryPolicy.Setup(p => p.ShouldRetry(exception, It.IsAny<int>())).Returns(false);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await sut.ProcessAsync(_testMessage, _context));
        _mockInner.Verify(i => i.ProcessAsync(_testMessage, _context, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Context Update Tests

    [Fact]
    public async Task ProcessAsync_WithRetries_UpdatesContextWithRetryCount()
    {
        // Arrange
        var sut = new RetryDecorator(_mockInner.Object, _mockLogger.Object, _mockRetryPolicy.Object);
        var exception = new TimeoutException();
        var contexts = new List<ProcessingContext>();

        _mockInner.Setup(i => i.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .Callback<IMessage, ProcessingContext, CancellationToken>((_, ctx, _) => contexts.Add(ctx))
            .ReturnsAsync(ProcessingResult.Failed(exception, "Failed"));

        _mockRetryPolicy.Setup(p => p.ShouldRetry(exception, It.IsAny<int>())).Returns(true);
        _mockRetryPolicy.Setup(p => p.GetRetryDelay(It.IsAny<int>())).Returns(TimeSpan.FromMilliseconds(10));

        // Act
        await sut.ProcessAsync(_testMessage, _context);

        // Assert
        Assert.Equal(4, contexts.Count); // Initial + 3 retries
        Assert.Equal(0, contexts[0].RetryCount);
        Assert.Equal(1, contexts[1].RetryCount);
        Assert.Equal(2, contexts[2].RetryCount);
        Assert.Equal(3, contexts[3].RetryCount);
    }

    #endregion

    #region ExponentialBackoffRetryPolicy Tests

    [Fact]
    public void ExponentialBackoffRetryPolicy_WithDefaults_HasExpectedValues()
    {
        // Arrange & Act
        var policy = new ExponentialBackoffRetryPolicy();

        // Assert
        Assert.Equal(3, policy.MaxRetries);
    }

    [Fact]
    public void ExponentialBackoffRetryPolicy_WithCustomValues_UsesCustomValues()
    {
        // Arrange & Act
        var policy = new ExponentialBackoffRetryPolicy(
            maxRetries: 5,
            baseDelay: TimeSpan.FromSeconds(2),
            maxDelay: TimeSpan.FromSeconds(60),
            jitterFactor: 0.5);

        // Assert
        Assert.Equal(5, policy.MaxRetries);
    }

    [Fact]
    public void ExponentialBackoffRetryPolicy_ShouldRetry_ReturnsFalseWhenMaxRetriesReached()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 3);
        var exception = new TimeoutException();

        // Act
        var shouldRetry = policy.ShouldRetry(exception, 3);

        // Assert
        Assert.False(shouldRetry);
    }

    [Fact]
    public void ExponentialBackoffRetryPolicy_ShouldRetry_ReturnsFalseForNullException()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy();

        // Act
        var shouldRetry = policy.ShouldRetry(null, 0);

        // Assert
        Assert.False(shouldRetry);
    }

    [Fact]
    public void ExponentialBackoffRetryPolicy_ShouldRetry_ReturnsFalseForCriticalErrors()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy();

        // Act & Assert
        Assert.False(policy.ShouldRetry(new OutOfMemoryException(), 0));
        Assert.False(policy.ShouldRetry(new StackOverflowException(), 0));
        Assert.False(policy.ShouldRetry(new AccessViolationException(), 0));
    }

    [Fact]
    public void ExponentialBackoffRetryPolicy_ShouldRetry_ReturnsTrueForTransientErrors()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy();

        // Act & Assert
        Assert.True(policy.ShouldRetry(new TimeoutException(), 0));
        Assert.True(policy.ShouldRetry(new TaskCanceledException(), 0));
        Assert.True(policy.ShouldRetry(new OperationCanceledException(), 0));
    }

    [Fact]
    public void ExponentialBackoffRetryPolicy_GetRetryDelay_ReturnsExponentialDelay()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(
            baseDelay: TimeSpan.FromSeconds(1),
            maxDelay: TimeSpan.FromSeconds(30),
            jitterFactor: 0);

        // Act
        var delay0 = policy.GetRetryDelay(0);
        var delay1 = policy.GetRetryDelay(1);
        var delay2 = policy.GetRetryDelay(2);

        // Assert
        Assert.True(delay0 >= TimeSpan.FromMilliseconds(900) && delay0 <= TimeSpan.FromMilliseconds(1100));
        Assert.True(delay1 >= TimeSpan.FromMilliseconds(1900) && delay1 <= TimeSpan.FromMilliseconds(2100));
        Assert.True(delay2 >= TimeSpan.FromMilliseconds(3900) && delay2 <= TimeSpan.FromMilliseconds(4100));
    }

    [Fact]
    public void ExponentialBackoffRetryPolicy_GetRetryDelay_RespectsMaxDelay()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(
            baseDelay: TimeSpan.FromSeconds(10),
            maxDelay: TimeSpan.FromSeconds(20),
            jitterFactor: 0);

        // Act
        var delay = policy.GetRetryDelay(10); // Would be 10240 seconds without cap

        // Assert
        Assert.True(delay <= TimeSpan.FromSeconds(20));
    }

    #endregion

    public class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }
}

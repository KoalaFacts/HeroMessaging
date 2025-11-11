using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.ErrorHandling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.ErrorHandling;

/// <summary>
/// Unit tests for DefaultErrorHandler
/// Tests error handling, retry logic, and dead letter queue integration
/// </summary>
[Trait("Category", "Unit")]
public sealed class DefaultErrorHandlerTests
{
    private readonly Mock<ILogger<DefaultErrorHandler>> _loggerMock;
    private readonly Mock<IDeadLetterQueue> _deadLetterQueueMock;
    private readonly FakeTimeProvider _timeProvider;
    private readonly DefaultErrorHandler _handler;

    public DefaultErrorHandlerTests()
    {
        _loggerMock = new Mock<ILogger<DefaultErrorHandler>>();
        _deadLetterQueueMock = new Mock<IDeadLetterQueue>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        _handler = new DefaultErrorHandler(_loggerMock.Object, _deadLetterQueueMock.Object, _timeProvider);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new DefaultErrorHandler(_loggerMock.Object, _deadLetterQueueMock.Object, null!));

        Assert.Equal("timeProvider", exception.ParamName);
    }

    #endregion

    #region Transient Error Tests

    [Fact]
    public async Task HandleErrorAsync_WithTransientError_ReturnsRetry()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        var error = new TimeoutException("Connection timeout");
        var context = new ErrorContext
        {
            Component = "TestComponent",
            RetryCount = 0,
            MaxRetries = 3
        };

        // Act
        var result = await _handler.HandleErrorAsync(message, error, context);

        // Assert
        Assert.Equal(ErrorAction.Retry, result.Action);
        Assert.NotNull(result.RetryDelay);
        Assert.True(result.RetryDelay > TimeSpan.Zero);
    }

    [Fact]
    public async Task HandleErrorAsync_WithTaskCanceledException_ReturnsRetry()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        var error = new TaskCanceledException("Operation cancelled");
        var context = new ErrorContext
        {
            Component = "TestComponent",
            RetryCount = 1,
            MaxRetries = 5
        };

        // Act
        var result = await _handler.HandleErrorAsync(message, error, context);

        // Assert
        Assert.Equal(ErrorAction.Retry, result.Action);
        Assert.NotNull(result.RetryDelay);
    }

    [Fact]
    public async Task HandleErrorAsync_WithTransientMessageInnerException_ReturnsRetry()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        var innerError = new TimeoutException("Inner timeout");
        var error = new InvalidOperationException("Operation failed", innerError);
        var context = new ErrorContext
        {
            Component = "TestComponent",
            RetryCount = 0,
            MaxRetries = 3
        };

        // Act
        var result = await _handler.HandleErrorAsync(message, error, context);

        // Assert
        Assert.Equal(ErrorAction.Retry, result.Action);
    }

    [Fact]
    public async Task HandleErrorAsync_WithTimeoutInMessage_ReturnsRetry()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        var error = new Exception("Connection timeout occurred");
        var context = new ErrorContext
        {
            Component = "TestComponent",
            RetryCount = 0,
            MaxRetries = 3
        };

        // Act
        var result = await _handler.HandleErrorAsync(message, error, context);

        // Assert
        Assert.Equal(ErrorAction.Retry, result.Action);
    }

    [Fact]
    public async Task HandleErrorAsync_WithTransientInMessage_ReturnsRetry()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        var error = new Exception("Transient failure detected");
        var context = new ErrorContext
        {
            Component = "TestComponent",
            RetryCount = 0,
            MaxRetries = 3
        };

        // Act
        var result = await _handler.HandleErrorAsync(message, error, context);

        // Assert
        Assert.Equal(ErrorAction.Retry, result.Action);
    }

    #endregion

    #region Critical Error Tests

    [Fact]
    public async Task HandleErrorAsync_WithOutOfMemoryException_ReturnsEscalate()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        var error = new OutOfMemoryException("Insufficient memory");
        var context = new ErrorContext
        {
            Component = "TestComponent",
            RetryCount = 0,
            MaxRetries = 3
        };

        // Act
        var result = await _handler.HandleErrorAsync(message, error, context);

        // Assert
        Assert.Equal(ErrorAction.Escalate, result.Action);
    }

    [Fact]
    public async Task HandleErrorAsync_WithStackOverflowException_ReturnsEscalate()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        var error = new StackOverflowException();
        var context = new ErrorContext
        {
            Component = "TestComponent",
            RetryCount = 0,
            MaxRetries = 3
        };

        // Act
        var result = await _handler.HandleErrorAsync(message, error, context);

        // Assert
        Assert.Equal(ErrorAction.Escalate, result.Action);
    }

    [Fact]
    public async Task HandleErrorAsync_WithAccessViolationException_ReturnsEscalate()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        var error = new AccessViolationException("Access violation");
        var context = new ErrorContext
        {
            Component = "TestComponent",
            RetryCount = 0,
            MaxRetries = 3
        };

        // Act
        var result = await _handler.HandleErrorAsync(message, error, context);

        // Assert
        Assert.Equal(ErrorAction.Escalate, result.Action);
    }

    #endregion

    #region Max Retries Tests

    [Fact]
    public async Task HandleErrorAsync_WhenMaxRetriesExceeded_SendsToDeadLetterQueue()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        var error = new InvalidOperationException("Persistent failure");
        var context = new ErrorContext
        {
            Component = "TestComponent",
            RetryCount = 3,
            MaxRetries = 3
        };

        // Act
        var result = await _handler.HandleErrorAsync(message, error, context);

        // Assert
        Assert.Equal(ErrorAction.SendToDeadLetter, result.Action);
        Assert.NotNull(result.Reason);
        Assert.Contains("Max retries", result.Reason);

        _deadLetterQueueMock.Verify(
            d => d.SendToDeadLetterAsync(
                message,
                It.Is<DeadLetterContext>(ctx =>
                    ctx.Reason.Contains("Max retries") &&
                    ctx.Exception == error &&
                    ctx.Component == "TestComponent" &&
                    ctx.RetryCount == 3),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleErrorAsync_WhenMaxRetriesExceeded_SetsFailureTime()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        var error = new Exception("Test error");
        var context = new ErrorContext
        {
            Component = "TestComponent",
            RetryCount = 5,
            MaxRetries = 5
        };

        var expectedTime = _timeProvider.GetUtcNow().DateTime;
        DeadLetterContext? capturedContext = null;

        _deadLetterQueueMock
            .Setup(d => d.SendToDeadLetterAsync(
                It.IsAny<IMessage>(),
                It.IsAny<DeadLetterContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<IMessage, DeadLetterContext, CancellationToken>((msg, ctx, ct) =>
            {
                capturedContext = ctx;
            })
            .ReturnsAsync("dead-letter-id");

        // Act
        await _handler.HandleErrorAsync(message, error, context);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal(expectedTime, capturedContext.FailureTime);
    }

    #endregion

    #region Default Error Handling Tests

    [Fact]
    public async Task HandleErrorAsync_WithNonTransientError_SendsToDeadLetterQueue()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        var error = new InvalidOperationException("Invalid state");
        var context = new ErrorContext
        {
            Component = "TestComponent",
            RetryCount = 0,
            MaxRetries = 3
        };

        // Act
        var result = await _handler.HandleErrorAsync(message, error, context);

        // Assert
        Assert.Equal(ErrorAction.SendToDeadLetter, result.Action);
        Assert.NotNull(result.Reason);
        Assert.Contains("Unhandled error", result.Reason);

        _deadLetterQueueMock.Verify(
            d => d.SendToDeadLetterAsync(
                message,
                It.Is<DeadLetterContext>(ctx =>
                    ctx.Reason.Contains("Unhandled error") &&
                    ctx.Exception == error),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Retry Delay Tests

    [Fact]
    public async Task HandleErrorAsync_RetryDelay_IncreasesWithRetryCount()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        var error = new TimeoutException("Timeout");
        var context1 = new ErrorContext { Component = "Test", RetryCount = 0, MaxRetries = 5 };
        var context2 = new ErrorContext { Component = "Test", RetryCount = 2, MaxRetries = 5 };

        // Act
        var result1 = await _handler.HandleErrorAsync(message, error, context1);
        var result2 = await _handler.HandleErrorAsync(message, error, context2);

        // Assert - Second retry should have longer delay (exponential backoff)
        Assert.NotNull(result1.RetryDelay);
        Assert.NotNull(result2.RetryDelay);
        Assert.True(result2.RetryDelay > result1.RetryDelay);
    }

    [Fact]
    public async Task HandleErrorAsync_RetryDelay_CappedAt30Seconds()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        var error = new TimeoutException("Timeout");
        var context = new ErrorContext { Component = "Test", RetryCount = 10, MaxRetries = 15 };

        // Act
        var result = await _handler.HandleErrorAsync(message, error, context);

        // Assert
        Assert.NotNull(result.RetryDelay);
        Assert.True(result.RetryDelay <= TimeSpan.FromSeconds(30));
    }

    #endregion

    #region Logging Tests

    [Fact]
    public async Task HandleErrorAsync_LogsError()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        var error = new TimeoutException("Timeout");
        var context = new ErrorContext { Component = "TestComponent", RetryCount = 1, MaxRetries = 3 };

        // Act
        await _handler.HandleErrorAsync(message, error, context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error processing message")),
                error,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleErrorAsync_WithCriticalError_LogsCritical()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        var error = new OutOfMemoryException();
        var context = new ErrorContext { Component = "TestComponent", RetryCount = 0, MaxRetries = 3 };

        // Act
        await _handler.HandleErrorAsync(message, error, context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Critical error")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Metadata Propagation Tests

    [Fact]
    public async Task HandleErrorAsync_PropagatesMetadataToDeadLetterContext()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        var error = new InvalidOperationException("Error");
        var metadata = new Dictionary<string, object>
        {
            ["TraceId"] = "trace-123",
            ["UserId"] = "user-456"
        };
        var context = new ErrorContext
        {
            Component = "TestComponent",
            RetryCount = 0,
            MaxRetries = 3,
            Metadata = metadata
        };

        DeadLetterContext? capturedContext = null;
        _deadLetterQueueMock
            .Setup(d => d.SendToDeadLetterAsync(
                It.IsAny<IMessage>(),
                It.IsAny<DeadLetterContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<IMessage, DeadLetterContext, CancellationToken>((msg, ctx, ct) =>
            {
                capturedContext = ctx;
            })
            .ReturnsAsync("dead-letter-id");

        // Act
        await _handler.HandleErrorAsync(message, error, context);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal(metadata, capturedContext.Metadata);
    }

    #endregion

    #region Test Message Class

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; }
        public DateTime Timestamp { get; set; }
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    #endregion
}

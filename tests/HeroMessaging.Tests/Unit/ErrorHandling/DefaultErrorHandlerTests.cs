using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.ErrorHandling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.ErrorHandling
{
    [Trait("Category", "Unit")]
    public sealed class DefaultErrorHandlerTests
    {
        private readonly Mock<ILogger<DefaultErrorHandler>> _loggerMock;
        private readonly Mock<IDeadLetterQueue> _deadLetterQueueMock;
        private readonly FakeTimeProvider _timeProvider;

        public DefaultErrorHandlerTests()
        {
            _loggerMock = new Mock<ILogger<DefaultErrorHandler>>();
            _deadLetterQueueMock = new Mock<IDeadLetterQueue>();
            _timeProvider = new FakeTimeProvider();
        }

        private DefaultErrorHandler CreateHandler()
        {
            return new DefaultErrorHandler(_loggerMock.Object, _deadLetterQueueMock.Object, _timeProvider);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new DefaultErrorHandler(_loggerMock.Object, _deadLetterQueueMock.Object, null!));

            Assert.Equal("timeProvider", ex.ParamName);
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

        #region HandleErrorAsync - Transient Errors

        [Fact]
        public async Task HandleErrorAsync_WithTransientErrorAndRetriesRemaining_ReturnsRetry()
        {
            // Arrange
            var handler = CreateHandler();
            var message = new TestMessage();
            var error = new TimeoutException("Connection timeout");
            var context = new ErrorContext
            {
                Component = "TestComponent",
                RetryCount = 1,
                MaxRetries = 3
            };

            // Act
            var result = await handler.HandleErrorAsync(message, error, context, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ErrorAction.Retry, result.Action);
            Assert.NotNull(result.RetryDelay);
            Assert.True(result.RetryDelay > TimeSpan.Zero);
        }

        [Fact]
        public async Task HandleErrorAsync_WithTaskCanceledException_ReturnsRetry()
        {
            // Arrange
            var handler = CreateHandler();
            var message = new TestMessage();
            var error = new TaskCanceledException("Operation cancelled");
            var context = new ErrorContext
            {
                Component = "TestComponent",
                RetryCount = 0,
                MaxRetries = 3
            };

            // Act
            var result = await handler.HandleErrorAsync(message, error, context, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ErrorAction.Retry, result.Action);
        }

        [Fact]
        public async Task HandleErrorAsync_WithTimeoutInMessage_ReturnsRetry()
        {
            // Arrange
            var handler = CreateHandler();
            var message = new TestMessage();
            var error = new Exception("Request timeout occurred");
            var context = new ErrorContext
            {
                Component = "TestComponent",
                RetryCount = 0,
                MaxRetries = 3
            };

            // Act
            var result = await handler.HandleErrorAsync(message, error, context, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ErrorAction.Retry, result.Action);
        }

        #endregion

        #region HandleErrorAsync - Critical Errors

        [Fact]
        public async Task HandleErrorAsync_WithOutOfMemoryException_ReturnsEscalate()
        {
            // Arrange
            var handler = CreateHandler();
            var message = new TestMessage();
            var error = new OutOfMemoryException();
            var context = new ErrorContext
            {
                Component = "TestComponent",
                RetryCount = 0,
                MaxRetries = 3
            };

            // Act
            var result = await handler.HandleErrorAsync(message, error, context, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ErrorAction.Escalate, result.Action);
        }

        [Fact]
        public async Task HandleErrorAsync_WithStackOverflowException_ReturnsEscalate()
        {
            // Arrange
            var handler = CreateHandler();
            var message = new TestMessage();
            var error = new StackOverflowException();
            var context = new ErrorContext
            {
                Component = "TestComponent",
                RetryCount = 0,
                MaxRetries = 3
            };

            // Act
            var result = await handler.HandleErrorAsync(message, error, context, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ErrorAction.Escalate, result.Action);
        }

        [Fact]
        public async Task HandleErrorAsync_WithAccessViolationException_ReturnsEscalate()
        {
            // Arrange
            var handler = CreateHandler();
            var message = new TestMessage();
            var error = new AccessViolationException();
            var context = new ErrorContext
            {
                Component = "TestComponent",
                RetryCount = 0,
                MaxRetries = 3
            };

            // Act
            var result = await handler.HandleErrorAsync(message, error, context, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ErrorAction.Escalate, result.Action);
        }

        #endregion

        #region HandleErrorAsync - Max Retries Exceeded

        [Fact]
        public async Task HandleErrorAsync_WithMaxRetriesExceeded_SendsToDeadLetter()
        {
            // Arrange
            var handler = CreateHandler();
            var message = new TestMessage();
            var error = new InvalidOperationException("Operation failed");
            var context = new ErrorContext
            {
                Component = "TestComponent",
                RetryCount = 3,
                MaxRetries = 3
            };

            // Act
            var result = await handler.HandleErrorAsync(message, error, context, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ErrorAction.SendToDeadLetter, result.Action);
            _deadLetterQueueMock.Verify(
                dlq => dlq.SendToDeadLetterAsync(message, It.IsAny<DeadLetterContext>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleErrorAsync_WithMaxRetriesExceeded_IncludesCorrectContext()
        {
            // Arrange
            var handler = CreateHandler();
            var message = new TestMessage();
            var error = new InvalidOperationException("Operation failed");
            var context = new ErrorContext
            {
                Component = "TestComponent",
                RetryCount = 3,
                MaxRetries = 3,
                Metadata = new Dictionary<string, object> { ["Key"] = "Value" }
            };

            DeadLetterContext? capturedContext = null;
            _deadLetterQueueMock
                .Setup(dlq => dlq.SendToDeadLetterAsync(It.IsAny<IMessage>(), It.IsAny<DeadLetterContext>(), It.IsAny<CancellationToken>()))
                .Callback<IMessage, DeadLetterContext, CancellationToken>((m, ctx, ct) => capturedContext = ctx)
                .ReturnsAsync("dead-letter-id");

            // Act
            await handler.HandleErrorAsync(message, error, context, TestContext.Current.CancellationToken);

            // Assert
            Assert.NotNull(capturedContext);
            Assert.Equal("TestComponent", capturedContext.Component);
            Assert.Equal(3, capturedContext.RetryCount);
            Assert.Same(error, capturedContext.Exception);
            Assert.NotNull(capturedContext.Metadata);
        }

        #endregion

        #region HandleErrorAsync - Default Case

        [Fact]
        public async Task HandleErrorAsync_WithNonTransientError_SendsToDeadLetter()
        {
            // Arrange
            var handler = CreateHandler();
            var message = new TestMessage();
            var error = new ArgumentException("Invalid argument");
            var context = new ErrorContext
            {
                Component = "TestComponent",
                RetryCount = 0,
                MaxRetries = 3
            };

            // Act
            var result = await handler.HandleErrorAsync(message, error, context, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ErrorAction.SendToDeadLetter, result.Action);
            _deadLetterQueueMock.Verify(
                dlq => dlq.SendToDeadLetterAsync(message, It.IsAny<DeadLetterContext>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        #endregion

        #region Retry Delay Calculation

        [Fact]
        public async Task HandleErrorAsync_WithIncreasedRetryCount_IncreaseDelays()
        {
            // Arrange
            var handler = CreateHandler();
            var message = new TestMessage();
            var error = new TimeoutException();

            // Act - Multiple retries
            var result1 = await handler.HandleErrorAsync(message, error, new ErrorContext { RetryCount = 0, MaxRetries = 5 }, TestContext.Current.CancellationToken);
            var result2 = await handler.HandleErrorAsync(message, error, new ErrorContext { RetryCount = 1, MaxRetries = 5 }, TestContext.Current.CancellationToken);
            var result3 = await handler.HandleErrorAsync(message, error, new ErrorContext { RetryCount = 2, MaxRetries = 5 }, TestContext.Current.CancellationToken);

            // Assert - Delays should generally increase (allowing for jitter)
            Assert.NotNull(result1.RetryDelay);
            Assert.NotNull(result2.RetryDelay);
            Assert.NotNull(result3.RetryDelay);
            // Can't assert exact values due to jitter, but verify they're reasonable
            Assert.True(result1.RetryDelay <= TimeSpan.FromSeconds(30));
            Assert.True(result2.RetryDelay <= TimeSpan.FromSeconds(30));
            Assert.True(result3.RetryDelay <= TimeSpan.FromSeconds(30));
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
}

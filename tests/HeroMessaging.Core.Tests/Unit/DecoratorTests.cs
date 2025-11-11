using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Tests.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit;

/// <summary>
/// Unit tests for decorator pattern implementations
/// Testing decorator pattern with logging, retry, and circuit breaker concepts
/// </summary>
public class DecoratorTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task LoggingDecorator_ProcessAsync_LogsStartAndCompletion()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<TestLoggingDecorator>>();
        var mockInnerProcessor = new Mock<IMessageProcessor>();
        var message = TestMessageBuilder.CreateValidMessage("Test message");
        var context = new ProcessingContext("test-component");

        var expectedResult = ProcessingResult.Successful("Processed successfully");
        mockInnerProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
                         .ReturnsAsync(expectedResult);

        var decorator = new TestLoggingDecorator(mockInnerProcessor.Object, mockLogger.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        mockInnerProcessor.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);

        // Verify logging calls occurred
        mockLogger.Verify(
            l => l.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Processing message")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task LoggingDecorator_ProcessAsync_LogsErrorOnFailure()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<TestLoggingDecorator>>();
        var mockInnerProcessor = new Mock<IMessageProcessor>();
        var message = TestMessageBuilder.CreateValidMessage("Test message");
        var context = new ProcessingContext("test-component");

        var exception = new InvalidOperationException("Processing failed");
        mockInnerProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
                         .Throws(exception);

        var decorator = new TestLoggingDecorator(mockInnerProcessor.Object, mockLogger.Object);

        // Act & Assert
        var thrownException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("Processing failed", thrownException.Message);

        // Verify error logging occurred
        mockLogger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error processing message")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RetryDecorator_ProcessAsync_RetriesOnFailure()
    {
        // Arrange
        var mockInnerProcessor = new Mock<IMessageProcessor>();
        var message = TestMessageBuilder.CreateValidMessage("Test message");
        var context = new ProcessingContext("test-component");

        // Setup to fail twice, then succeed on third attempt
        mockInnerProcessor.SetupSequence(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
                         .Throws(new InvalidOperationException("First failure"))
                         .Throws(new InvalidOperationException("Second failure"))
                         .ReturnsAsync(ProcessingResult.Successful("Success on retry"));

        var decorator = new TestRetryDecorator(mockInnerProcessor.Object, maxAttempts: 3);

        // Act
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Success on retry", result.Message);
        mockInnerProcessor.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RetryDecorator_ProcessAsync_ExhaustsRetriesAndFails()
    {
        // Arrange
        var mockInnerProcessor = new Mock<IMessageProcessor>();
        var message = TestMessageBuilder.CreateValidMessage("Test message");
        var context = new ProcessingContext("test-component");

        mockInnerProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
                         .Throws(new InvalidOperationException("Persistent failure"));

        var decorator = new TestRetryDecorator(mockInnerProcessor.Object, maxAttempts: 2);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("Persistent failure", exception.Message);
        mockInnerProcessor.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CircuitBreakerDecorator_ProcessAsync_OpensOnConsecutiveFailures()
    {
        // Arrange
        var mockInnerProcessor = new Mock<IMessageProcessor>();
        var message = TestMessageBuilder.CreateValidMessage("Test message");
        var context = new ProcessingContext("test-component");

        mockInnerProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
                         .Throws(new InvalidOperationException("Service unavailable"));

        var decorator = new TestCircuitBreakerDecorator(mockInnerProcessor.Object, failureThreshold: 3);

        // Act - Trigger failures to open circuit breaker
        for (int i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken).AsTask());
        }

        // Circuit breaker should now be open - subsequent calls should fail fast
        var circuitOpenException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken).AsTask());

        // Assert
        Assert.Contains("Circuit breaker is open", circuitOpenException.Message);
        // Should have only called inner processor 3 times (not 4)
        mockInnerProcessor.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DecoratorChaining_LoggingAndRetry_WorksTogether()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<TestLoggingDecorator>>();
        var mockInnerProcessor = new Mock<IMessageProcessor>();
        var message = TestMessageBuilder.CreateValidMessage("Test message");
        var context = new ProcessingContext("test-component");

        // Setup to fail once, then succeed
        mockInnerProcessor.SetupSequence(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
                         .Throws(new InvalidOperationException("First failure"))
                         .ReturnsAsync(ProcessingResult.Successful("Success after retry"));

        // Chain decorators: Logging -> Retry -> InnerProcessor
        var retryDecorator = new TestRetryDecorator(mockInnerProcessor.Object, maxAttempts: 2);
        var loggingDecorator = new TestLoggingDecorator(retryDecorator, mockLogger.Object);

        // Act
        var result = await loggingDecorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Success after retry", result.Message);
        mockInnerProcessor.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Verify logging occurred
        mockLogger.Verify(
            l => l.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Processing message")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    // Test decorator implementations
    public class TestLoggingDecorator : IMessageProcessor
    {
        private readonly IMessageProcessor _innerProcessor;
        private readonly ILogger<TestLoggingDecorator> _logger;

        public TestLoggingDecorator(IMessageProcessor innerProcessor, ILogger<TestLoggingDecorator> logger)
        {
            _innerProcessor = innerProcessor;
            _logger = logger;
        }

        public async ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Processing message {MessageId}", message.MessageId);
            try
            {
                var result = await _innerProcessor.ProcessAsync(message, context, cancellationToken);
                _logger.LogInformation("Successfully processed message {MessageId}", message.MessageId);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message {MessageId}", message.MessageId);
                throw;
            }
        }
    }

    public class TestRetryDecorator : IMessageProcessor
    {
        private readonly IMessageProcessor _innerProcessor;
        private readonly int _maxAttempts;

        public TestRetryDecorator(IMessageProcessor innerProcessor, int maxAttempts)
        {
            _innerProcessor = innerProcessor;
            _maxAttempts = maxAttempts;
        }

        public async ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context, CancellationToken cancellationToken = default)
        {
            for (int attempt = 1; attempt <= _maxAttempts; attempt++)
            {
                try
                {
                    return await _innerProcessor.ProcessAsync(message, context, cancellationToken);
                }
                catch (Exception) when (attempt < _maxAttempts)
                {
                    await Task.Delay(10, TestContext.Current.CancellationToken); // Short delay between retries
                }
            }

            // Final attempt
            return await _innerProcessor.ProcessAsync(message, context, cancellationToken);
        }
    }

    public class TestCircuitBreakerDecorator : IMessageProcessor
    {
        private readonly IMessageProcessor _innerProcessor;
        private readonly int _failureThreshold;
        private int _failureCount = 0;
        private bool _isOpen = false;

        public TestCircuitBreakerDecorator(IMessageProcessor innerProcessor, int failureThreshold)
        {
            _innerProcessor = innerProcessor;
            _failureThreshold = failureThreshold;
        }

        public async ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context, CancellationToken cancellationToken = default)
        {
            if (_isOpen)
            {
                throw new InvalidOperationException("Circuit breaker is open");
            }

            try
            {
                var result = await _innerProcessor.ProcessAsync(message, context, cancellationToken);
                _failureCount = 0; // Reset on success
                return result;
            }
            catch (Exception)
            {
                _failureCount++;
                if (_failureCount >= _failureThreshold)
                {
                    _isOpen = true;
                }
                throw;
            }
        }
    }
}
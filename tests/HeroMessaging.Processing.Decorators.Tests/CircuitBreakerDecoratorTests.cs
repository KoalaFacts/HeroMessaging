using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

/// <summary>
/// Unit tests for CircuitBreakerDecorator
/// Tests circuit breaker pattern implementation
/// </summary>
[Trait("Category", "Unit")]
public sealed class CircuitBreakerDecoratorTests
{
    private readonly Mock<IMessageProcessor> _innerProcessorMock;
    private readonly Mock<ILogger<CircuitBreakerDecorator>> _loggerMock;
    private readonly FakeTimeProvider _timeProvider;

    public CircuitBreakerDecoratorTests()
    {
        _innerProcessorMock = new Mock<IMessageProcessor>();
        _loggerMock = new Mock<ILogger<CircuitBreakerDecorator>>();
        _timeProvider = new FakeTimeProvider();
        _timeProvider.SetUtcNow(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesDecorator()
    {
        // Act
        var decorator = new CircuitBreakerDecorator(
            _innerProcessorMock.Object,
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
            new CircuitBreakerDecorator(
                _innerProcessorMock.Object,
                _loggerMock.Object,
                null!));

        Assert.Equal("timeProvider", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithCustomOptions_UsesCustomOptions()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 10,
            SamplingDuration = TimeSpan.FromMinutes(5),
            BreakDuration = TimeSpan.FromMinutes(1)
        };

        // Act
        var decorator = new CircuitBreakerDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            _timeProvider,
            options);

        // Assert
        Assert.NotNull(decorator);
    }

    #endregion

    #region Closed State Tests

    [Fact]
    public async Task ProcessAsync_InClosedState_ProcessesMessage()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var expectedResult = ProcessingResult.Successful();

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var decorator = new CircuitBreakerDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        _innerProcessorMock.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_SuccessfulProcessing_RecordsSuccess()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new CircuitBreakerDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert - Circuit remains closed (no exceptions)
        var result = await decorator.ProcessAsync(message, context);
        Assert.True(result.Success);
    }

    #endregion

    #region Open State Tests

    [Fact]
    public async Task ProcessAsync_AfterThresholdFailures_OpensCircuit()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            MinimumThroughput = 3,
            SamplingDuration = TimeSpan.FromMinutes(1)
        };

        var decorator = new CircuitBreakerDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            _timeProvider,
            options);

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test failure"), "Failed"));

        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        // Act - Record failures to open circuit
        for (int i = 0; i < 3; i++)
        {
            await decorator.ProcessAsync(message, context);
        }

        // Now circuit should be open
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.IsType<CircuitBreakerOpenException>(result.Exception);
        Assert.Contains("Circuit breaker is", result.Message);
    }

    [Fact]
    public async Task ProcessAsync_WhenCircuitOpen_RejectsMessagesWithoutProcessing()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            MinimumThroughput = 2
        };

        var decorator = new CircuitBreakerDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            _timeProvider,
            options);

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test"), "Failed"));

        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        // Open the circuit
        await decorator.ProcessAsync(message, context);
        await decorator.ProcessAsync(message, context);

        _innerProcessorMock.Invocations.Clear();

        // Act - Try to process while circuit is open
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.IsType<CircuitBreakerOpenException>(result.Exception);
        _innerProcessorMock.Verify(p => p.ProcessAsync(
            It.IsAny<IMessage>(),
            It.IsAny<ProcessingContext>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Half-Open State Tests

    [Fact]
    public async Task ProcessAsync_AfterBreakDuration_TransitionsToHalfOpen()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            MinimumThroughput = 2,
            BreakDuration = TimeSpan.FromSeconds(30)
        };

        var decorator = new CircuitBreakerDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            _timeProvider,
            options);

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test"), "Failed"));

        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        // Open the circuit
        await decorator.ProcessAsync(message, context);
        await decorator.ProcessAsync(message, context);

        // Advance time past break duration
        _timeProvider.Advance(TimeSpan.FromSeconds(31));

        // Set up success response for half-open test
        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act - Should allow processing in half-open state
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ProcessAsync_InHalfOpenWithSuccess_ClosesAfterThreeSuccesses()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            MinimumThroughput = 2,
            BreakDuration = TimeSpan.FromSeconds(30)
        };

        var decorator = new CircuitBreakerDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            _timeProvider,
            options);

        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        // Open the circuit
        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test"), "Failed"));

        await decorator.ProcessAsync(message, context);
        await decorator.ProcessAsync(message, context);

        // Wait and transition to half-open
        _timeProvider.Advance(TimeSpan.FromSeconds(31));

        // Set up successes
        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act - Three successes to close circuit
        await decorator.ProcessAsync(message, context);
        await decorator.ProcessAsync(message, context);
        await decorator.ProcessAsync(message, context);

        // Verify circuit is now closed by processing without time advance
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ProcessAsync_InHalfOpenWithFailure_ReOpensCircuit()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            MinimumThroughput = 2,
            BreakDuration = TimeSpan.FromSeconds(30)
        };

        var decorator = new CircuitBreakerDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            _timeProvider,
            options);

        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        // Open the circuit
        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test"), "Failed"));

        await decorator.ProcessAsync(message, context);
        await decorator.ProcessAsync(message, context);

        // Wait and transition to half-open
        _timeProvider.Advance(TimeSpan.FromSeconds(31));

        // Act - Fail while half-open
        await decorator.ProcessAsync(message, context);

        // Circuit should be open again
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.IsType<CircuitBreakerOpenException>(result.Exception);
    }

    #endregion

    #region Failure Rate Tests

    [Fact]
    public async Task ProcessAsync_FailureRateExceedsThreshold_OpensCircuit()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 100, // High threshold to test rate instead
            FailureRateThreshold = 0.5, // 50% failure rate
            MinimumThroughput = 10
        };

        var decorator = new CircuitBreakerDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            _timeProvider,
            options);

        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        // Act - Mix of successes and failures to exceed 50% failure rate
        for (int i = 0; i < 10; i++)
        {
            if (i < 4)
            {
                // 4 successes
                _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(ProcessingResult.Successful());
            }
            else
            {
                // 6 failures (60% failure rate)
                _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(ProcessingResult.Failed(new Exception("Test"), "Failed"));
            }
            await decorator.ProcessAsync(message, context);
        }

        // Next request should be rejected
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.IsType<CircuitBreakerOpenException>(result.Exception);
    }

    [Fact]
    public async Task ProcessAsync_BelowMinimumThroughput_DoesNotOpenCircuit()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 5,
            MinimumThroughput = 10 // Need 10 requests before circuit can open
        };

        var decorator = new CircuitBreakerDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            _timeProvider,
            options);

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test"), "Failed"));

        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        // Act - Only 5 failures (below minimum throughput)
        for (int i = 0; i < 5; i++)
        {
            await decorator.ProcessAsync(message, context);
        }

        // Circuit should still be closed
        _innerProcessorMock.Invocations.Clear();
        var result = await decorator.ProcessAsync(message, context);

        // Assert - Should still process (circuit not open)
        _innerProcessorMock.Verify(p => p.ProcessAsync(
            It.IsAny<IMessage>(),
            It.IsAny<ProcessingContext>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Exception Handling Tests

    [Fact]
    public async Task ProcessAsync_WhenInnerProcessorThrows_RecordsFailureAndThrows()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var exception = new InvalidOperationException("Test exception");

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var decorator = new CircuitBreakerDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act & Assert
        var thrownException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            decorator.ProcessAsync(message, context).AsTask());

        Assert.Equal(exception, thrownException);
    }

    [Fact]
    public async Task ProcessAsync_ExceptionsCauseCircuitToOpen()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            MinimumThroughput = 3
        };

        var decorator = new CircuitBreakerDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            _timeProvider,
            options);

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test"));

        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        // Act - Record failures via exceptions
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await decorator.ProcessAsync(message, context);
            }
            catch
            {
                // Expected
            }
        }

        // Circuit should now be open
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.IsType<CircuitBreakerOpenException>(result.Exception);
    }

    #endregion

    #region Sampling Window Tests

    [Fact]
    public async Task ProcessAsync_OldFailuresOutsideWindow_DoNotCountTowardThreshold()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            MinimumThroughput = 3,
            SamplingDuration = TimeSpan.FromMinutes(1)
        };

        var decorator = new CircuitBreakerDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            _timeProvider,
            options);

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test"), "Failed"));

        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        // Record 2 failures
        await decorator.ProcessAsync(message, context);
        await decorator.ProcessAsync(message, context);

        // Advance time beyond sampling window
        _timeProvider.Advance(TimeSpan.FromMinutes(2));

        // Record 2 more failures (but old ones don't count)
        await decorator.ProcessAsync(message, context);
        await decorator.ProcessAsync(message, context);

        // Act - Circuit should still be closed (only 2 failures in window)
        _innerProcessorMock.Invocations.Clear();
        var result = await decorator.ProcessAsync(message, context);

        // Assert - Should still attempt to process
        _innerProcessorMock.Verify(p => p.ProcessAsync(
            It.IsAny<IMessage>(),
            It.IsAny<ProcessingContext>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Cancellation Tests

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

        var decorator = new CircuitBreakerDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            _timeProvider);

        // Act
        var result = await decorator.ProcessAsync(message, context, cts.Token);

        // Assert
        Assert.True(result.Success);
        _innerProcessorMock.Verify(p => p.ProcessAsync(message, context, cts.Token), Times.Once);
    }

    #endregion

    #region State Change Logging Tests

    [Fact]
    public async Task ProcessAsync_SuccessfulProcessing_LogsStateChangeWhenClosingCircuit()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            MinimumThroughput = 2,
            BreakDuration = TimeSpan.FromSeconds(30)
        };

        var decorator = new CircuitBreakerDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            _timeProvider,
            options);

        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        // Open the circuit
        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test"), "Failed"));

        await decorator.ProcessAsync(message, context);
        await decorator.ProcessAsync(message, context);

        // Wait and transition to half-open
        _timeProvider.Advance(TimeSpan.FromSeconds(31));

        // Set up successes
        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act - Three successes to close circuit
        await decorator.ProcessAsync(message, context);
        await decorator.ProcessAsync(message, context);
        await decorator.ProcessAsync(message, context);

        // Assert - StateChanged should trigger logging on transitions
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Circuit breaker state changed to")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessAsync_FailureRecorded_LogsStateChangeWithFailureRate()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            MinimumThroughput = 3,
            SamplingDuration = TimeSpan.FromMinutes(1)
        };

        var decorator = new CircuitBreakerDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            _timeProvider,
            options);

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test failure"), "Failed"));

        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        // Act - Record failures to trigger state change
        for (int i = 0; i < 3; i++)
        {
            await decorator.ProcessAsync(message, context);
        }

        // Assert - Verify warning logged with failure rate
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Circuit breaker state changed to") && v.ToString().Contains("Failure rate")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ExceptionCausesStateChange_LogsErrorWithFailureRate()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            MinimumThroughput = 3
        };

        var decorator = new CircuitBreakerDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            _timeProvider,
            options);

        var testException = new InvalidOperationException("Test exception");
        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(testException);

        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        // Act - Record exceptions to trigger state change
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await decorator.ProcessAsync(message, context);
            }
            catch
            {
                // Expected
            }
        }

        // Assert - Verify error logged with failure rate
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Circuit breaker state changed to") && v.ToString().Contains("Failure rate")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_CircuitOpenWithoutStateChange_DoesNotLog()
    {
        // Arrange - Set up to open circuit silently on first failures
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            MinimumThroughput = 2
        };

        var decorator = new CircuitBreakerDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object,
            _timeProvider,
            options);

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test"), "Failed"));

        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        // Act - Open the circuit with failures
        await decorator.ProcessAsync(message, context);
        await decorator.ProcessAsync(message, context);

        // Reset mock to check subsequent calls
        _loggerMock.Invocations.Clear();

        // Try to process while circuit is open
        await decorator.ProcessAsync(message, context);

        // Assert - No state change logging because already open
        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("state changed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Never);
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

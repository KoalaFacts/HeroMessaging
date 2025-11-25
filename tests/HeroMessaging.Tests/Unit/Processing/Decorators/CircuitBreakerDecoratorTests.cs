using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing.Decorators;

[Trait("Category", "Unit")]
public sealed class CircuitBreakerDecoratorTests
{
    private readonly Mock<IMessageProcessor> _innerMock;
    private readonly Mock<ILogger<CircuitBreakerDecorator>> _loggerMock;
    private readonly FakeTimeProvider _timeProvider;

    public CircuitBreakerDecoratorTests()
    {
        _innerMock = new Mock<IMessageProcessor>();
        _loggerMock = new Mock<ILogger<CircuitBreakerDecorator>>();
        _timeProvider = new FakeTimeProvider();
    }

    private CircuitBreakerDecorator CreateDecorator(CircuitBreakerOptions? options = null)
    {
        return new CircuitBreakerDecorator(_innerMock.Object, _loggerMock.Object, _timeProvider, options);
    }

    #region Closed State - Success Cases

    [Fact]
    public async Task ProcessAsync_InClosedState_CallsInnerProcessor()
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
    }

    [Fact]
    public async Task ProcessAsync_InClosedState_WithSuccessfulResult_RecordsSuccess()
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
    }

    #endregion

    #region Closed State - Failure Cases

    [Fact]
    public async Task ProcessAsync_InClosedState_WithFailedResult_RecordsFailure()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 5,
            MinimumThroughput = 10,
            FailureRateThreshold = 0.5
        };
        var decorator = CreateDecorator(options);
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test error")));

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ProcessAsync_InClosedState_WithException_RecordsFailureAndRethrows()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new InvalidOperationException("Test exception");

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ThrowsAsync(testException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await decorator.ProcessAsync(message, context));

        Assert.Equal("Test exception", exception.Message);
    }

    #endregion

    #region Circuit Opening - Threshold Tests

    [Fact]
    public async Task ProcessAsync_AfterFailureThresholdExceeded_OpensCircuit()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            MinimumThroughput = 3,
            FailureRateThreshold = 0.9
        };
        var decorator = CreateDecorator(options);
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test error")));

        // Act - Trigger failures to open circuit
        for (int i = 0; i < 3; i++)
        {
            await decorator.ProcessAsync(new TestMessage(), context);
        }

        // Circuit should now be open
        var result = await decorator.ProcessAsync(new TestMessage(), context);

        // Assert
        Assert.False(result.Success);
        Assert.IsType<CircuitBreakerOpenException>(result.Exception);
    }

    [Fact]
    public async Task ProcessAsync_WithInsufficientThroughput_DoesNotOpenCircuit()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            MinimumThroughput = 10, // Higher than failure count
            FailureRateThreshold = 0.5
        };
        var decorator = CreateDecorator(options);
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test error")));

        // Act - Trigger 5 failures (below minimum throughput)
        for (int i = 0; i < 5; i++)
        {
            await decorator.ProcessAsync(new TestMessage(), context);
        }

        // Act - Next call should still process (circuit not open)
        var result = await decorator.ProcessAsync(new TestMessage(), context);

        // Assert
        Assert.False(result.Success);
        Assert.IsNotType<CircuitBreakerOpenException>(result.Exception);
        _innerMock.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), context, It.IsAny<CancellationToken>()), Times.Exactly(6));
    }

    [Fact]
    public async Task ProcessAsync_WithFailureRateAboveThreshold_OpensCircuit()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 10,
            MinimumThroughput = 10,
            FailureRateThreshold = 0.6 // 60% failure rate
        };
        var decorator = CreateDecorator(options);
        var context = new ProcessingContext();
        var callCount = 0;

        // Setup: 6 successes, then 7 failures = 7/13 = 54% rate (below threshold)
        // Then need more failures to push over 60%
        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                // First 4 successes to build throughput
                if (callCount <= 4)
                {
                    return ProcessingResult.Successful();
                }
                // Then failures to exceed rate: 7 failures out of 11 = 63.6%
                if (callCount <= 11)
                {
                    return ProcessingResult.Failed(new Exception("Test error"));
                }
                return ProcessingResult.Successful();
            });

        // Act - Make requests until circuit opens
        for (int i = 0; i < 11; i++)
        {
            await decorator.ProcessAsync(new TestMessage(), context);
        }

        // Circuit should now be open (opened on 11th request which was a failure)
        var result = await decorator.ProcessAsync(new TestMessage(), context);

        // Assert
        Assert.False(result.Success);
        Assert.IsType<CircuitBreakerOpenException>(result.Exception);
    }

    #endregion

    #region Open State Tests

    [Fact]
    public async Task ProcessAsync_InOpenState_RejectsRequestsWithoutCallingInner()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            MinimumThroughput = 2,
            FailureRateThreshold = 0.9,
            BreakDuration = TimeSpan.FromSeconds(30)
        };
        var decorator = CreateDecorator(options);
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test error")));

        // Act - Open the circuit
        await decorator.ProcessAsync(new TestMessage(), context);
        await decorator.ProcessAsync(new TestMessage(), context);

        _innerMock.ResetCalls();

        // Act - Try to process while circuit is open
        var result = await decorator.ProcessAsync(new TestMessage(), context);

        // Assert
        Assert.False(result.Success);
        Assert.IsType<CircuitBreakerOpenException>(result.Exception);
        _innerMock.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_InOpenState_LogsWarning()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            MinimumThroughput = 2,
            BreakDuration = TimeSpan.FromSeconds(30)
        };
        var decorator = CreateDecorator(options);
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test error")));

        // Act - Open the circuit
        await decorator.ProcessAsync(new TestMessage(), context);
        await decorator.ProcessAsync(new TestMessage(), context);

        // Act - Try to process while circuit is open
        var message = new TestMessage();
        await decorator.ProcessAsync(message, context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
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
        var decorator = CreateDecorator(options);
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test error")));

        // Act - Open the circuit
        await decorator.ProcessAsync(new TestMessage(), context);
        await decorator.ProcessAsync(new TestMessage(), context);

        // Advance time past break duration
        _timeProvider.Advance(TimeSpan.FromSeconds(31));

        _innerMock.ResetCalls();
        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act - Should transition to half-open and allow request
        var result = await decorator.ProcessAsync(new TestMessage(), context);

        // Assert
        Assert.True(result.Success);
        _innerMock.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_InHalfOpenState_AfterThreeSuccesses_ClosesCircuit()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            MinimumThroughput = 2,
            BreakDuration = TimeSpan.FromSeconds(30)
        };
        var decorator = CreateDecorator(options);
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test error")));

        // Act - Open the circuit
        await decorator.ProcessAsync(new TestMessage(), context);
        await decorator.ProcessAsync(new TestMessage(), context);

        // Advance time to half-open
        _timeProvider.Advance(TimeSpan.FromSeconds(31));

        _innerMock.ResetCalls();
        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act - 3 successful requests to close circuit
        await decorator.ProcessAsync(new TestMessage(), context);
        await decorator.ProcessAsync(new TestMessage(), context);
        await decorator.ProcessAsync(new TestMessage(), context);

        // Act - Should now be closed and continue processing
        var result = await decorator.ProcessAsync(new TestMessage(), context);

        // Assert
        Assert.True(result.Success);
        _innerMock.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), context, It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [Fact]
    public async Task ProcessAsync_InHalfOpenState_OnFailure_ReOpensCircuit()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            MinimumThroughput = 2,
            BreakDuration = TimeSpan.FromSeconds(30)
        };
        var decorator = CreateDecorator(options);
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test error")));

        // Act - Open the circuit
        await decorator.ProcessAsync(new TestMessage(), context);
        await decorator.ProcessAsync(new TestMessage(), context);

        // Advance time to half-open
        _timeProvider.Advance(TimeSpan.FromSeconds(31));

        // Act - Fail in half-open state (should re-open circuit)
        await decorator.ProcessAsync(new TestMessage(), context);

        _innerMock.ResetCalls();

        // Act - Next request should be rejected
        var result = await decorator.ProcessAsync(new TestMessage(), context);

        // Assert
        Assert.False(result.Success);
        Assert.IsType<CircuitBreakerOpenException>(result.Exception);
        _innerMock.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region State Transition Logging

    [Fact]
    public async Task ProcessAsync_WhenCircuitOpens_LogsWarningWithFailureRate()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            MinimumThroughput = 2
        };
        var decorator = CreateDecorator(options);
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test error")));

        // Act
        await decorator.ProcessAsync(new TestMessage(), context);
        await decorator.ProcessAsync(new TestMessage(), context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessAsync_WhenCircuitCloses_LogsInformation()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            MinimumThroughput = 2,
            BreakDuration = TimeSpan.FromSeconds(30)
        };
        var decorator = CreateDecorator(options);
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test error")));

        // Act - Open the circuit
        await decorator.ProcessAsync(new TestMessage(), context);
        await decorator.ProcessAsync(new TestMessage(), context);

        // Advance time to half-open
        _timeProvider.Advance(TimeSpan.FromSeconds(31));

        _innerMock.ResetCalls();
        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        _loggerMock.ResetCalls();

        // Act - 3 successes to close circuit
        await decorator.ProcessAsync(new TestMessage(), context);
        await decorator.ProcessAsync(new TestMessage(), context);
        await decorator.ProcessAsync(new TestMessage(), context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region Cancellation Tests

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

    #endregion

    #region Sampling Duration Tests

    [Fact]
    public async Task ProcessAsync_CleansOldResultsOutsideSamplingDuration()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            MinimumThroughput = 3,
            SamplingDuration = TimeSpan.FromMinutes(1)
        };
        var decorator = CreateDecorator(options);
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test error")));

        // Act - Generate failures
        await decorator.ProcessAsync(new TestMessage(), context);
        await decorator.ProcessAsync(new TestMessage(), context);

        // Advance time beyond sampling duration
        _timeProvider.Advance(TimeSpan.FromMinutes(2));

        _innerMock.ResetCalls();
        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act - Old failures should be ignored, circuit should stay closed
        var result = await decorator.ProcessAsync(new TestMessage(), context);

        // Assert
        Assert.True(result.Success);
        _innerMock.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), context, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Test Helper Classes

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    #endregion
}

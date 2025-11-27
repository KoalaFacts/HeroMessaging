using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit;

[Trait("Category", "Unit")]
public class CircuitBreakerDecoratorTests
{
    private readonly Mock<IMessageProcessor> _mockInner;
    private readonly Mock<ILogger<CircuitBreakerDecorator>> _mockLogger;
    private readonly FakeTimeProvider _timeProvider;
    private readonly TestMessage _testMessage;
    private readonly ProcessingContext _context;

    public CircuitBreakerDecoratorTests()
    {
        _mockInner = new Mock<IMessageProcessor>();
        _mockLogger = new Mock<ILogger<CircuitBreakerDecorator>>();
        _timeProvider = new FakeTimeProvider();
        _testMessage = new TestMessage { MessageId = Guid.NewGuid() };
        _context = new ProcessingContext();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new CircuitBreakerDecorator(
            _mockInner.Object,
            _mockLogger.Object,
            null!,
            new CircuitBreakerOptions()));
        Assert.Equal("timeProvider", ex.ParamName);
    }

    #endregion

    #region Closed State Tests

    [Fact]
    public async Task ProcessAsync_WithClosedCircuit_AllowsRequestsThrough()
    {
        // Arrange
        var sut = new CircuitBreakerDecorator(_mockInner.Object, _mockLogger.Object, _timeProvider);
        var successResult = ProcessingResult.Successful();

        _mockInner.Setup(i => i.ProcessAsync(_testMessage, _context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(successResult);

        // Act
        var result = await sut.ProcessAsync(_testMessage, _context);

        // Assert
        Assert.True(result.Success);
        _mockInner.Verify(i => i.ProcessAsync(_testMessage, _context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithMultipleSuccesses_KeepsCircuitClosed()
    {
        // Arrange
        var sut = new CircuitBreakerDecorator(_mockInner.Object, _mockLogger.Object, _timeProvider);
        var successResult = ProcessingResult.Successful();

        _mockInner.Setup(i => i.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successResult);

        // Act
        for (int i = 0; i < 10; i++)
        {
            var result = await sut.ProcessAsync(_testMessage, _context);
            Assert.True(result.Success);
        }

        // Assert
        _mockInner.Verify(i => i.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Exactly(10));
    }

    #endregion

    #region Circuit Opening Tests

    [Fact]
    public async Task ProcessAsync_WithFailuresExceedingThreshold_OpensCircuit()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            MinimumThroughput = 3,
            SamplingDuration = TimeSpan.FromMinutes(1)
        };
        var sut = new CircuitBreakerDecorator(_mockInner.Object, _mockLogger.Object, _timeProvider, options);
        var failureResult = ProcessingResult.Failed(new Exception("Test failure"), "Failed");

        _mockInner.Setup(i => i.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failureResult);

        // Act - Generate enough failures to open circuit
        for (int i = 0; i < 3; i++)
        {
            await sut.ProcessAsync(_testMessage, _context);
        }

        var result = await sut.ProcessAsync(_testMessage, _context);

        // Assert - Circuit should be open and reject the request
        Assert.False(result.Success);
        Assert.IsType<CircuitBreakerOpenException>(result.Exception);
    }

    [Fact]
    public async Task ProcessAsync_WithFailureRateExceedingThreshold_OpensCircuit()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 10,
            FailureRateThreshold = 0.5,
            MinimumThroughput = 10,
            SamplingDuration = TimeSpan.FromMinutes(1)
        };
        var sut = new CircuitBreakerDecorator(_mockInner.Object, _mockLogger.Object, _timeProvider, options);

        // Act - Generate 4 successes first, then 6 failures (60% failure rate)
        // Circuit only opens on RecordFailure(), so failures must come last
        for (int i = 0; i < 4; i++)
        {
            _mockInner.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessingResult.Successful());
            await sut.ProcessAsync(_testMessage, _context);
        }

        for (int i = 0; i < 6; i++)
        {
            _mockInner.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessingResult.Failed(new Exception("Failure"), "Failed"));
            await sut.ProcessAsync(_testMessage, _context);
        }

        // Circuit should now be open (6/10 = 60% > 50% threshold)
        var result = await sut.ProcessAsync(_testMessage, _context);

        // Assert - Circuit should be open
        Assert.False(result.Success);
        Assert.IsType<CircuitBreakerOpenException>(result.Exception);
    }

    [Fact]
    public async Task ProcessAsync_WithExceptionThrown_OpensCircuitAfterThreshold()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            MinimumThroughput = 3,
            SamplingDuration = TimeSpan.FromMinutes(1)
        };
        var sut = new CircuitBreakerDecorator(_mockInner.Object, _mockLogger.Object, _timeProvider, options);

        _mockInner.Setup(i => i.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        // Act & Assert - First 3 exceptions should propagate
        for (int i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await sut.ProcessAsync(_testMessage, _context));
        }

        // Fourth call should be rejected by circuit breaker
        var result = await sut.ProcessAsync(_testMessage, _context);
        Assert.False(result.Success);
        Assert.IsType<CircuitBreakerOpenException>(result.Exception);
    }

    #endregion

    #region Half-Open State Tests

    [Fact]
    public async Task ProcessAsync_WithOpenCircuitAfterBreakDuration_TransitionsToHalfOpen()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            MinimumThroughput = 3,
            BreakDuration = TimeSpan.FromSeconds(30)
        };
        var sut = new CircuitBreakerDecorator(_mockInner.Object, _mockLogger.Object, _timeProvider, options);

        // Open the circuit
        _mockInner.Setup(i => i.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Failure"), "Failed"));
        for (int i = 0; i < 3; i++)
        {
            await sut.ProcessAsync(_testMessage, _context);
        }

        // Advance time past break duration
        _timeProvider.Advance(TimeSpan.FromSeconds(31));

        // Act - Next request should transition to half-open and be allowed
        _mockInner.Setup(i => i.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());
        var result = await sut.ProcessAsync(_testMessage, _context);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ProcessAsync_WithHalfOpenCircuitAndSuccess_ClosesCircuitAfterThreeSuccesses()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            MinimumThroughput = 3,
            BreakDuration = TimeSpan.FromSeconds(30)
        };
        var sut = new CircuitBreakerDecorator(_mockInner.Object, _mockLogger.Object, _timeProvider, options);

        // Open the circuit
        _mockInner.Setup(i => i.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Failure"), "Failed"));
        for (int i = 0; i < 3; i++)
        {
            await sut.ProcessAsync(_testMessage, _context);
        }

        // Advance time and get 3 successes in half-open state
        _timeProvider.Advance(TimeSpan.FromSeconds(31));
        _mockInner.Setup(i => i.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act - 3 successes should close the circuit
        for (int i = 0; i < 3; i++)
        {
            var result = await sut.ProcessAsync(_testMessage, _context);
            Assert.True(result.Success);
        }

        // Circuit should now be closed and continue accepting requests
        var finalResult = await sut.ProcessAsync(_testMessage, _context);
        Assert.True(finalResult.Success);
    }

    [Fact]
    public async Task ProcessAsync_WithHalfOpenCircuitAndFailure_ReopensCircuit()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            MinimumThroughput = 3,
            BreakDuration = TimeSpan.FromSeconds(30)
        };
        var sut = new CircuitBreakerDecorator(_mockInner.Object, _mockLogger.Object, _timeProvider, options);

        // Open the circuit
        _mockInner.Setup(i => i.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Failure"), "Failed"));
        for (int i = 0; i < 3; i++)
        {
            await sut.ProcessAsync(_testMessage, _context);
        }

        // Advance time to half-open state and fail
        _timeProvider.Advance(TimeSpan.FromSeconds(31));
        var halfOpenResult = await sut.ProcessAsync(_testMessage, _context);
        Assert.False(halfOpenResult.Success);

        // Act - Next request should be rejected by open circuit
        var result = await sut.ProcessAsync(_testMessage, _context);

        // Assert
        Assert.False(result.Success);
        Assert.IsType<CircuitBreakerOpenException>(result.Exception);
    }

    #endregion

    #region Minimum Throughput Tests

    [Fact]
    public async Task ProcessAsync_WithFailuresBelowMinimumThroughput_DoesNotOpenCircuit()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 5,
            MinimumThroughput = 10,
            SamplingDuration = TimeSpan.FromMinutes(1)
        };
        var sut = new CircuitBreakerDecorator(_mockInner.Object, _mockLogger.Object, _timeProvider, options);

        _mockInner.Setup(i => i.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Failure"), "Failed"));

        // Act - Only 5 failures, below minimum throughput of 10
        for (int i = 0; i < 5; i++)
        {
            var result = await sut.ProcessAsync(_testMessage, _context);
            Assert.False(result.Success);
            Assert.IsNotType<CircuitBreakerOpenException>(result.Exception);
        }

        // Circuit should still be closed
        var finalResult = await sut.ProcessAsync(_testMessage, _context);
        Assert.False(finalResult.Success);
        Assert.IsNotType<CircuitBreakerOpenException>(finalResult.Exception);
    }

    #endregion

    #region Sampling Duration Tests

    [Fact]
    public async Task ProcessAsync_WithOldFailures_DoesNotCountTowardThreshold()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            MinimumThroughput = 3,
            SamplingDuration = TimeSpan.FromSeconds(30)
        };
        var sut = new CircuitBreakerDecorator(_mockInner.Object, _mockLogger.Object, _timeProvider, options);

        // Generate 2 failures
        _mockInner.Setup(i => i.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Failure"), "Failed"));
        await sut.ProcessAsync(_testMessage, _context);
        await sut.ProcessAsync(_testMessage, _context);

        // Advance time past sampling duration
        _timeProvider.Advance(TimeSpan.FromSeconds(31));

        // Act - Add 2 more failures (should not trigger as old failures are outside window)
        await sut.ProcessAsync(_testMessage, _context);
        await sut.ProcessAsync(_testMessage, _context);
        var result = await sut.ProcessAsync(_testMessage, _context);

        // Assert - Circuit should still be closed (only 2 failures in window)
        Assert.False(result.Success);
        Assert.IsNotType<CircuitBreakerOpenException>(result.Exception);
    }

    #endregion

    #region CircuitBreakerOptions Tests

    [Fact]
    public void CircuitBreakerOptions_HasExpectedDefaults()
    {
        // Arrange & Act
        var options = new CircuitBreakerOptions();

        // Assert
        Assert.Equal(5, options.FailureThreshold);
        Assert.Equal(TimeSpan.FromMinutes(1), options.SamplingDuration);
        Assert.Equal(10, options.MinimumThroughput);
        Assert.Equal(TimeSpan.FromSeconds(30), options.BreakDuration);
        Assert.Equal(0.5, options.FailureRateThreshold);
    }

    #endregion

    #region CircuitBreakerOpenException Tests

    [Fact]
    public void CircuitBreakerOpenException_WithDefaultConstructor_HasDefaultMessage()
    {
        // Arrange & Act
        var exception = new CircuitBreakerOpenException();

        // Assert
        Assert.Equal("Circuit breaker is open", exception.Message);
    }

    [Fact]
    public void CircuitBreakerOpenException_WithCustomMessage_HasCustomMessage()
    {
        // Arrange & Act
        var exception = new CircuitBreakerOpenException("Custom message");

        // Assert
        Assert.Equal("Custom message", exception.Message);
    }

    [Fact]
    public void CircuitBreakerOpenException_WithInnerException_HasInnerException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner");

        // Act
        var exception = new CircuitBreakerOpenException("Custom message", innerException);

        // Assert
        Assert.Equal("Custom message", exception.Message);
        Assert.Equal(innerException, exception.InnerException);
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

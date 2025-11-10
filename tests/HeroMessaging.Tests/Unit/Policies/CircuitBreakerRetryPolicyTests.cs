using HeroMessaging.Policies;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Tests.Unit.Policies;

/// <summary>
/// Unit tests for CircuitBreakerRetryPolicy
/// Tests circuit breaker behavior in retry policy
/// </summary>
[Trait("Category", "Unit")]
public sealed class CircuitBreakerRetryPolicyTests
{
    private readonly FakeTimeProvider _timeProvider;

    public CircuitBreakerRetryPolicyTests()
    {
        _timeProvider = new FakeTimeProvider();
        _timeProvider.SetUtcNow(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesPolicy()
    {
        // Act
        var policy = new CircuitBreakerRetryPolicy(_timeProvider);

        // Assert
        Assert.NotNull(policy);
        Assert.Equal(3, policy.MaxRetries);
    }

    [Fact]
    public void Constructor_WithCustomMaxRetries_UsesCustomValue()
    {
        // Act
        var policy = new CircuitBreakerRetryPolicy(_timeProvider, maxRetries: 5);

        // Assert
        Assert.Equal(5, policy.MaxRetries);
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new CircuitBreakerRetryPolicy(null!));

        Assert.Equal("timeProvider", exception.ParamName);
    }

    #endregion

    #region Basic Retry Tests

    [Fact]
    public void ShouldRetry_WithValidException_ReturnsTrue()
    {
        // Arrange
        var policy = new CircuitBreakerRetryPolicy(_timeProvider);
        var exception = new InvalidOperationException("Test");

        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);

        // Assert
        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_WithNullException_ReturnsFalse()
    {
        // Arrange
        var policy = new CircuitBreakerRetryPolicy(_timeProvider);

        // Act
        var shouldRetry = policy.ShouldRetry(null, 0);

        // Assert
        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_WhenAttemptExceedsMaxRetries_ReturnsFalse()
    {
        // Arrange
        var policy = new CircuitBreakerRetryPolicy(_timeProvider, maxRetries: 3);
        var exception = new TimeoutException("Test");

        // Act
        var shouldRetry = policy.ShouldRetry(exception, 3);

        // Assert
        Assert.False(shouldRetry);
    }

    #endregion

    #region Circuit Breaker Tests

    [Fact]
    public void ShouldRetry_WhenFailureThresholdReached_OpenCircuitAndReturnsFalse()
    {
        // Arrange
        var policy = new CircuitBreakerRetryPolicy(
            _timeProvider,
            maxRetries: 10,
            failureThreshold: 3);
        var exception = new TimeoutException("Test failure");

        // Act - Record failures until threshold
        policy.ShouldRetry(exception, 0); // Failure 1
        policy.ShouldRetry(exception, 1); // Failure 2
        var shouldRetry = policy.ShouldRetry(exception, 2); // Failure 3 - should open circuit

        // Assert - Circuit should open and not retry
        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_WhenCircuitOpen_ReturnsFalse()
    {
        // Arrange
        var policy = new CircuitBreakerRetryPolicy(
            _timeProvider,
            maxRetries: 10,
            failureThreshold: 2);
        var exception = new InvalidOperationException("Test");

        // Open the circuit
        policy.ShouldRetry(exception, 0);
        policy.ShouldRetry(exception, 1); // Opens circuit

        // Act - Try to retry while circuit is open
        var shouldRetry = policy.ShouldRetry(exception, 2);

        // Assert
        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_AfterOpenCircuitDuration_ClosesCircuitAndReturnsTrue()
    {
        // Arrange
        var openDuration = TimeSpan.FromMinutes(1);
        var policy = new CircuitBreakerRetryPolicy(
            _timeProvider,
            maxRetries: 10,
            failureThreshold: 2,
            openCircuitDuration: openDuration);
        var exception = new TimeoutException("Test");

        // Open the circuit
        policy.ShouldRetry(exception, 0);
        policy.ShouldRetry(exception, 1); // Opens circuit

        // Advance time past the open circuit duration
        _timeProvider.Advance(openDuration + TimeSpan.FromSeconds(1));

        // Act - Should close circuit and allow retry
        var shouldRetry = policy.ShouldRetry(exception, 2);

        // Assert
        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_DifferentExceptionTypes_MaintainsSeparateCircuits()
    {
        // Arrange
        var policy = new CircuitBreakerRetryPolicy(
            _timeProvider,
            maxRetries: 10,
            failureThreshold: 2);
        var exception1 = new TimeoutException("Timeout");
        var exception2 = new InvalidOperationException("Invalid");

        // Act - Open circuit for exception1
        policy.ShouldRetry(exception1, 0);
        policy.ShouldRetry(exception1, 1); // Opens circuit for TimeoutException

        // Try exception2 - should still allow retry (different circuit)
        var shouldRetry = policy.ShouldRetry(exception2, 0);

        // Assert
        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_SameExceptionTypeAndMessage_UsesSameCircuit()
    {
        // Arrange
        var policy = new CircuitBreakerRetryPolicy(
            _timeProvider,
            maxRetries: 10,
            failureThreshold: 2);
        var exception1 = new TimeoutException("Connection timeout");
        var exception2 = new TimeoutException("Connection timeout");

        // Act - Open circuit with first exception
        policy.ShouldRetry(exception1, 0);
        policy.ShouldRetry(exception1, 1); // Opens circuit

        // Try second exception (same type and message)
        var shouldRetry = policy.ShouldRetry(exception2, 2);

        // Assert - Should use same circuit (already open)
        Assert.False(shouldRetry);
    }

    #endregion

    #region Retry Delay Tests

    [Fact]
    public void GetRetryDelay_FirstAttempt_ReturnsBaseDelay()
    {
        // Arrange
        var baseDelay = TimeSpan.FromSeconds(2);
        var policy = new CircuitBreakerRetryPolicy(
            _timeProvider,
            baseDelay: baseDelay);

        // Act
        var delay = policy.GetRetryDelay(0);

        // Assert
        Assert.Equal(baseDelay, delay);
    }

    [Fact]
    public void GetRetryDelay_IncreasesWithAttemptNumber()
    {
        // Arrange
        var baseDelay = TimeSpan.FromSeconds(1);
        var policy = new CircuitBreakerRetryPolicy(
            _timeProvider,
            baseDelay: baseDelay);

        // Act
        var delay0 = policy.GetRetryDelay(0);
        var delay1 = policy.GetRetryDelay(1);
        var delay2 = policy.GetRetryDelay(2);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(1), delay0);  // base * 1
        Assert.Equal(TimeSpan.FromSeconds(2), delay1);  // base * 2
        Assert.Equal(TimeSpan.FromSeconds(3), delay2);  // base * 3
    }

    [Fact]
    public void GetRetryDelay_WithDefaultBaseDelay_UsesOneSecond()
    {
        // Arrange
        var policy = new CircuitBreakerRetryPolicy(_timeProvider);

        // Act
        var delay = policy.GetRetryDelay(0);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(1), delay);
    }

    #endregion

    #region Circuit State Recovery Tests

    [Fact]
    public void ShouldRetry_AfterCircuitReset_AllowsRetries()
    {
        // Arrange
        var openDuration = TimeSpan.FromMinutes(1);
        var policy = new CircuitBreakerRetryPolicy(
            _timeProvider,
            maxRetries: 10,
            failureThreshold: 2,
            openCircuitDuration: openDuration);
        var exception = new TimeoutException("Test");

        // Open the circuit
        policy.ShouldRetry(exception, 0); // Failure 1
        policy.ShouldRetry(exception, 1); // Failure 2 - opens circuit

        // Verify circuit is open
        var shouldRetryWhileOpen = policy.ShouldRetry(exception, 2);
        Assert.False(shouldRetryWhileOpen);

        // Wait for circuit to close
        _timeProvider.Advance(openDuration + TimeSpan.FromSeconds(1));

        // After duration, circuit should reset and allow retry again
        var shouldRetryAfterReset = policy.ShouldRetry(exception, 0);

        // Assert - Circuit should allow retry after reset
        Assert.True(shouldRetryAfterReset);
    }

    #endregion

    #region Multiple Circuit Tests

    [Fact]
    public void ShouldRetry_MultipleExceptionTypes_MaintainsIndependentCircuits()
    {
        // Arrange
        var policy = new CircuitBreakerRetryPolicy(
            _timeProvider,
            maxRetries: 10,
            failureThreshold: 2);

        var timeout1 = new TimeoutException("Type1");
        var timeout2 = new TimeoutException("Type2");
        var invalid = new InvalidOperationException("Invalid");

        // Act & Assert
        // Open circuit for timeout1
        Assert.True(policy.ShouldRetry(timeout1, 0));
        Assert.False(policy.ShouldRetry(timeout1, 1)); // Opens

        // timeout2 (different message) should have its own circuit
        Assert.True(policy.ShouldRetry(timeout2, 0));

        // invalid should have its own circuit
        Assert.True(policy.ShouldRetry(invalid, 0));
        Assert.False(policy.ShouldRetry(invalid, 1)); // Opens
    }

    #endregion
}

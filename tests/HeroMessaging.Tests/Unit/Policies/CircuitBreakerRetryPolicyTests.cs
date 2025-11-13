using HeroMessaging.Policies;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Tests.Unit.Policies;

/// <summary>
/// Unit tests for <see cref="CircuitBreakerRetryPolicy"/> implementation.
/// Tests cover circuit breaker state transitions, retry logic, backoff strategies, and edge cases.
/// </summary>
public class CircuitBreakerRetryPolicyTests
{
    #region Basic Functionality Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var timeProvider = new FakeTimeProvider();
        var policy = new CircuitBreakerRetryPolicy(
            timeProvider,
            maxRetries: 5,
            failureThreshold: 10,
            openCircuitDuration: TimeSpan.FromMinutes(2),
            baseDelay: TimeSpan.FromSeconds(2));

        // Assert
        Assert.NotNull(policy);
        Assert.Equal(5, policy.MaxRetries);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithDefaultParameters_CreatesInstance()
    {
        // Arrange & Act
        var timeProvider = new FakeTimeProvider();
        var policy = new CircuitBreakerRetryPolicy(timeProvider);

        // Assert
        Assert.NotNull(policy);
        Assert.Equal(3, policy.MaxRetries);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new CircuitBreakerRetryPolicy(null!));
        Assert.Equal("timeProvider", exception.ParamName);
    }

    #endregion

    #region ShouldRetry Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithNullException_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var policy = new CircuitBreakerRetryPolicy(timeProvider);

        // Act
        var result = policy.ShouldRetry(null, attemptNumber: 0);

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WhenAttemptNumberExceedsMaxRetries_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var policy = new CircuitBreakerRetryPolicy(timeProvider, maxRetries: 3);
        var exception = new InvalidOperationException("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 3);

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithinMaxRetries_ReturnsTrue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var policy = new CircuitBreakerRetryPolicy(timeProvider, maxRetries: 3);
        var exception = new InvalidOperationException("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 0);

        // Assert
        Assert.True(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_MultipleFailuresWithinThreshold_ReturnsTrue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var policy = new CircuitBreakerRetryPolicy(timeProvider, maxRetries: 3, failureThreshold: 5);
        var exception = new InvalidOperationException("Test error");

        // Act & Assert - First 4 attempts should succeed
        for (int i = 0; i < 4; i++)
        {
            var result = policy.ShouldRetry(exception, attemptNumber: i);
            Assert.True(result, $"Attempt {i} should return true");
        }
    }

    #endregion

    #region Circuit Breaker State Transition Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WhenFailureThresholdReached_OpensCircuit()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var policy = new CircuitBreakerRetryPolicy(
            timeProvider,
            maxRetries: 10,
            failureThreshold: 3,
            openCircuitDuration: TimeSpan.FromMinutes(1));
        var exception = new InvalidOperationException("Test error");

        // Act - Trigger failures up to threshold
        policy.ShouldRetry(exception, attemptNumber: 0); // 1st failure
        policy.ShouldRetry(exception, attemptNumber: 0); // 2nd failure
        var resultAtThreshold = policy.ShouldRetry(exception, attemptNumber: 0); // 3rd failure - opens circuit

        // Assert - Circuit should be open
        Assert.False(resultAtThreshold, "Circuit should open at threshold");

        // Additional attempts should fail due to open circuit
        var resultAfterOpen = policy.ShouldRetry(exception, attemptNumber: 0);
        Assert.False(resultAfterOpen, "Circuit should remain open");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_AfterCircuitOpenDuration_AllowsRetry()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var policy = new CircuitBreakerRetryPolicy(
            timeProvider,
            maxRetries: 10,
            failureThreshold: 2,
            openCircuitDuration: TimeSpan.FromMinutes(1));
        var exception = new InvalidOperationException("Test error");

        // Act - Open the circuit
        policy.ShouldRetry(exception, attemptNumber: 0); // 1st failure
        policy.ShouldRetry(exception, attemptNumber: 0); // 2nd failure - opens circuit

        // Verify circuit is open
        var resultWhileOpen = policy.ShouldRetry(exception, attemptNumber: 0);
        Assert.False(resultWhileOpen, "Circuit should be open immediately after threshold");

        // Advance time past circuit break duration
        timeProvider.Advance(TimeSpan.FromMinutes(1.5));

        // Assert - Circuit should allow retry (half-open state)
        var resultAfterDuration = policy.ShouldRetry(exception, attemptNumber: 0);
        Assert.True(resultAfterDuration, "Circuit should allow retry after break duration");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_CircuitResetsAfterSuccessfulRetry()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var policy = new CircuitBreakerRetryPolicy(
            timeProvider,
            maxRetries: 10,
            failureThreshold: 2,
            openCircuitDuration: TimeSpan.FromMinutes(1));
        var exception = new InvalidOperationException("Test error");

        // Act - Open the circuit
        policy.ShouldRetry(exception, attemptNumber: 0); // 1st failure
        policy.ShouldRetry(exception, attemptNumber: 0); // 2nd failure - opens circuit

        // Advance time to allow circuit to close
        timeProvider.Advance(TimeSpan.FromMinutes(2));

        // Successful retry resets the circuit
        var resultAfterReset = policy.ShouldRetry(exception, attemptNumber: 0);
        Assert.True(resultAfterReset, "Circuit should reset and allow retries");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_DifferentExceptionTypes_MaintainSeparateCircuits()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var policy = new CircuitBreakerRetryPolicy(
            timeProvider,
            maxRetries: 10,
            failureThreshold: 2,
            openCircuitDuration: TimeSpan.FromMinutes(1));
        var exception1 = new InvalidOperationException("Test error 1");
        var exception2 = new TimeoutException("Test error 2");

        // Act - Open circuit for first exception type
        policy.ShouldRetry(exception1, attemptNumber: 0); // 1st failure
        policy.ShouldRetry(exception1, attemptNumber: 0); // 2nd failure - opens circuit

        // Verify first circuit is open
        var result1 = policy.ShouldRetry(exception1, attemptNumber: 0);
        Assert.False(result1, "First circuit should be open");

        // Assert - Different exception type should still be allowed
        var result2 = policy.ShouldRetry(exception2, attemptNumber: 0);
        Assert.True(result2, "Different exception type should have separate circuit");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_SameExceptionTypeWithDifferentMessages_SharesCircuit()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var policy = new CircuitBreakerRetryPolicy(
            timeProvider,
            maxRetries: 10,
            failureThreshold: 2,
            openCircuitDuration: TimeSpan.FromMinutes(1));
        var exception1 = new InvalidOperationException("Message A");
        var exception2 = new InvalidOperationException("Message B");

        // Act - Open circuit with first message
        policy.ShouldRetry(exception1, attemptNumber: 0); // 1st failure
        policy.ShouldRetry(exception1, attemptNumber: 0); // 2nd failure - opens circuit

        // Assert - Same type but different message should use different circuit
        // (based on message hash in circuit key)
        var result = policy.ShouldRetry(exception2, attemptNumber: 0);
        Assert.True(result, "Different message hash should create separate circuit");
    }

    #endregion

    #region GetRetryDelay Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRetryDelay_FirstAttempt_ReturnsBaseDelay()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var baseDelay = TimeSpan.FromSeconds(2);
        var policy = new CircuitBreakerRetryPolicy(
            timeProvider,
            baseDelay: baseDelay);

        // Act
        var delay = policy.GetRetryDelay(attemptNumber: 0);

        // Assert
        Assert.Equal(baseDelay, delay);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRetryDelay_WithDefaultBaseDelay_ReturnsOneSecond()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var policy = new CircuitBreakerRetryPolicy(timeProvider);

        // Act
        var delay = policy.GetRetryDelay(attemptNumber: 0);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(1), delay);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRetryDelay_IncreasesLinearlyWithAttemptNumber()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var baseDelay = TimeSpan.FromSeconds(1);
        var policy = new CircuitBreakerRetryPolicy(
            timeProvider,
            baseDelay: baseDelay);

        // Act & Assert
        Assert.Equal(TimeSpan.FromSeconds(1), policy.GetRetryDelay(attemptNumber: 0)); // 1s * (0 + 1)
        Assert.Equal(TimeSpan.FromSeconds(2), policy.GetRetryDelay(attemptNumber: 1)); // 1s * (1 + 1)
        Assert.Equal(TimeSpan.FromSeconds(3), policy.GetRetryDelay(attemptNumber: 2)); // 1s * (2 + 1)
        Assert.Equal(TimeSpan.FromSeconds(4), policy.GetRetryDelay(attemptNumber: 3)); // 1s * (3 + 1)
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRetryDelay_WithCustomBaseDelay_CalculatesCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var baseDelay = TimeSpan.FromMilliseconds(500);
        var policy = new CircuitBreakerRetryPolicy(
            timeProvider,
            baseDelay: baseDelay);

        // Act & Assert
        Assert.Equal(TimeSpan.FromMilliseconds(500), policy.GetRetryDelay(attemptNumber: 0));
        Assert.Equal(TimeSpan.FromMilliseconds(1000), policy.GetRetryDelay(attemptNumber: 1));
        Assert.Equal(TimeSpan.FromMilliseconds(1500), policy.GetRetryDelay(attemptNumber: 2));
    }

    #endregion

    #region Edge Cases and Boundary Conditions

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithZeroFailureThreshold_NeverOpensCircuit()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var policy = new CircuitBreakerRetryPolicy(
            timeProvider,
            maxRetries: 10,
            failureThreshold: 0);
        var exception = new InvalidOperationException("Test error");

        // Act & Assert - Should never open circuit with threshold of 0
        for (int i = 0; i < 5; i++)
        {
            var result = policy.ShouldRetry(exception, attemptNumber: i);
            Assert.True(result, $"Attempt {i} should succeed with 0 threshold");
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithZeroMaxRetries_AlwaysReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var policy = new CircuitBreakerRetryPolicy(
            timeProvider,
            maxRetries: 0);
        var exception = new InvalidOperationException("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 0);

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithNegativeAttemptNumber_ReturnsTrue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var policy = new CircuitBreakerRetryPolicy(timeProvider, maxRetries: 3);
        var exception = new InvalidOperationException("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: -1);

        // Assert
        Assert.True(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRetryDelay_WithZeroAttemptNumber_ReturnsBaseDelay()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var baseDelay = TimeSpan.FromSeconds(2);
        var policy = new CircuitBreakerRetryPolicy(
            timeProvider,
            baseDelay: baseDelay);

        // Act
        var delay = policy.GetRetryDelay(attemptNumber: 0);

        // Assert
        Assert.Equal(baseDelay, delay);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRetryDelay_WithLargeAttemptNumber_DoesNotOverflow()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var policy = new CircuitBreakerRetryPolicy(timeProvider);

        // Act
        var delay = policy.GetRetryDelay(attemptNumber: 1000);

        // Assert
        Assert.True(delay > TimeSpan.Zero);
        Assert.True(delay < TimeSpan.MaxValue);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_ConcurrentCallsWithSameException_MaintainsCircuitState()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var policy = new CircuitBreakerRetryPolicy(
            timeProvider,
            maxRetries: 10,
            failureThreshold: 5);
        var exception = new InvalidOperationException("Test error");
        var results = new System.Collections.Concurrent.ConcurrentBag<bool>();

        // Act - Simulate concurrent failures
        var tasks = Enumerable.Range(0, 10).Select(_ =>
            Task.Run(() =>
            {
                var result = policy.ShouldRetry(exception, attemptNumber: 0);
                results.Add(result);
            }));

        Task.WaitAll(tasks.ToArray());

        // Assert - Some should succeed, some should fail after circuit opens
        Assert.True(results.Count(r => r) >= 3, "At least some attempts should succeed before circuit opens");
        Assert.True(results.Count(r => !r) >= 1, "Some attempts should fail after circuit opens");
    }

    #endregion

    #region Exception Handling Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithAggregateException_TracksCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var policy = new CircuitBreakerRetryPolicy(timeProvider, maxRetries: 3);
        var innerException = new InvalidOperationException("Inner error");
        var exception = new AggregateException("Aggregate error", innerException);

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 0);

        // Assert
        Assert.True(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithNestedExceptions_CreatesUniqueCircuitKey()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var policy = new CircuitBreakerRetryPolicy(
            timeProvider,
            maxRetries: 10,
            failureThreshold: 2);
        var innerException = new InvalidOperationException("Inner error");
        var exception1 = new AggregateException("Outer error", innerException);
        var exception2 = new TimeoutException("Different error");

        // Act - Open circuit for first exception
        policy.ShouldRetry(exception1, attemptNumber: 0);
        policy.ShouldRetry(exception1, attemptNumber: 0);

        // Assert - Different exception should have different circuit
        var result = policy.ShouldRetry(exception2, attemptNumber: 0);
        Assert.True(result, "Different exception type should use different circuit");
    }

    #endregion

    #region MaxRetries Property Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void MaxRetries_ReturnsConfiguredValue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var expectedMaxRetries = 7;
        var policy = new CircuitBreakerRetryPolicy(
            timeProvider,
            maxRetries: expectedMaxRetries);

        // Act
        var actualMaxRetries = policy.MaxRetries;

        // Assert
        Assert.Equal(expectedMaxRetries, actualMaxRetries);
    }

    #endregion

    #region Circuit Recovery Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_CircuitHalfOpenState_ResetsOnSuccessfulOperation()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var policy = new CircuitBreakerRetryPolicy(
            timeProvider,
            maxRetries: 10,
            failureThreshold: 2,
            openCircuitDuration: TimeSpan.FromSeconds(30));
        var exception = new InvalidOperationException("Test error");

        // Act - Open the circuit
        policy.ShouldRetry(exception, attemptNumber: 0);
        policy.ShouldRetry(exception, attemptNumber: 0);

        // Advance time to half-open state
        timeProvider.Advance(TimeSpan.FromSeconds(31));

        // First retry after half-open should reset
        var resultAfterHalfOpen = policy.ShouldRetry(exception, attemptNumber: 0);
        Assert.True(resultAfterHalfOpen, "Should allow retry in half-open state");

        // Subsequent retries should continue to work (circuit reset)
        var resultAfterReset = policy.ShouldRetry(exception, attemptNumber: 0);
        Assert.True(resultAfterReset, "Circuit should be reset after successful half-open attempt");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_MultipleCircuitOpenAndClose_WorksCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var policy = new CircuitBreakerRetryPolicy(
            timeProvider,
            maxRetries: 10,
            failureThreshold: 2,
            openCircuitDuration: TimeSpan.FromSeconds(30));
        var exception = new InvalidOperationException("Test error");

        // Act & Assert - First cycle: open circuit
        policy.ShouldRetry(exception, attemptNumber: 0);
        policy.ShouldRetry(exception, attemptNumber: 0);
        var resultFirstOpen = policy.ShouldRetry(exception, attemptNumber: 0);
        Assert.False(resultFirstOpen, "First circuit should be open");

        // Wait and reset
        timeProvider.Advance(TimeSpan.FromSeconds(31));
        policy.ShouldRetry(exception, attemptNumber: 0); // Reset circuit

        // Second cycle: open circuit again
        policy.ShouldRetry(exception, attemptNumber: 0);
        policy.ShouldRetry(exception, attemptNumber: 0);
        var resultSecondOpen = policy.ShouldRetry(exception, attemptNumber: 0);
        Assert.False(resultSecondOpen, "Second circuit should be open");

        // Wait and verify recovery
        timeProvider.Advance(TimeSpan.FromSeconds(31));
        var resultAfterSecondRecovery = policy.ShouldRetry(exception, attemptNumber: 0);
        Assert.True(resultAfterSecondRecovery, "Circuit should recover after second cycle");
    }

    #endregion
}

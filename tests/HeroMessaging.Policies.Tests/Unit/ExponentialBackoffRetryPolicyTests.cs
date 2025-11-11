using HeroMessaging.Processing.Decorators;
using Xunit;

namespace HeroMessaging.Tests.Unit.Policies;

/// <summary>
/// Unit tests for ExponentialBackoffRetryPolicy
/// Tests exponential backoff retry logic with jitter
/// </summary>
[Trait("Category", "Unit")]
public sealed class ExponentialBackoffRetryPolicyTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithDefaults_SetsCorrectValues()
    {
        // Act
        var policy = new ExponentialBackoffRetryPolicy();

        // Assert
        Assert.Equal(3, policy.MaxRetries);
    }

    [Fact]
    public void Constructor_WithCustomMaxRetries_SetsCorrectValue()
    {
        // Act
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 5);

        // Assert
        Assert.Equal(5, policy.MaxRetries);
    }

    #endregion

    #region ShouldRetry Tests

    [Fact]
    public void ShouldRetry_WithNullException_ReturnsFalse()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy();

        // Act
        var shouldRetry = policy.ShouldRetry(null, 0);

        // Assert
        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_WhenAttemptNumberExceedsMaxRetries_ReturnsFalse()
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
    public void ShouldRetry_WithTimeoutException_ReturnsTrue()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy();
        var exception = new TimeoutException("Request timeout");

        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);

        // Assert
        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_WithTaskCanceledException_ReturnsTrue()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy();
        var exception = new TaskCanceledException("Task was canceled");

        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);

        // Assert
        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_WithOperationCanceledException_ReturnsTrue()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy();
        var exception = new OperationCanceledException("Operation canceled");

        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);

        // Assert
        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_WithOutOfMemoryException_ReturnsFalse()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy();
        var exception = new OutOfMemoryException();

        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);

        // Assert - Critical errors should never retry
        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_WithStackOverflowException_ReturnsFalse()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy();
        var exception = new StackOverflowException();

        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);

        // Assert - Critical errors should never retry
        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_WithAccessViolationException_ReturnsFalse()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy();
        var exception = new AccessViolationException();

        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);

        // Assert - Critical errors should never retry
        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_WithNonTransientException_ReturnsFalse()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy();
        var exception = new InvalidOperationException("Non-transient error");

        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);

        // Assert
        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_WithInnerTransientException_ReturnsTrue()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy();
        var innerException = new TimeoutException("Inner timeout");
        var outerException = new InvalidOperationException("Outer error", innerException);

        // Act
        var shouldRetry = policy.ShouldRetry(outerException, 0);

        // Assert - Should retry because inner exception is transient
        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_AcrossMultipleAttempts_RespectsMaxRetries()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 3);
        var exception = new TimeoutException();

        // Act & Assert
        Assert.True(policy.ShouldRetry(exception, 0)); // Attempt 1
        Assert.True(policy.ShouldRetry(exception, 1)); // Attempt 2
        Assert.True(policy.ShouldRetry(exception, 2)); // Attempt 3
        Assert.False(policy.ShouldRetry(exception, 3)); // Exceeded max
        Assert.False(policy.ShouldRetry(exception, 4)); // Exceeded max
    }

    #endregion

    #region GetRetryDelay Tests

    [Fact]
    public void GetRetryDelay_WithDefaultSettings_IncreasesExponentially()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(
            baseDelay: TimeSpan.FromSeconds(1),
            jitterFactor: 0); // No jitter for predictable testing

        // Act
        var delay0 = policy.GetRetryDelay(0);
        var delay1 = policy.GetRetryDelay(1);
        var delay2 = policy.GetRetryDelay(2);

        // Assert - Exponential growth: 1s, 2s, 4s (without jitter)
        Assert.Equal(TimeSpan.FromSeconds(1), delay0);
        Assert.Equal(TimeSpan.FromSeconds(2), delay1);
        Assert.Equal(TimeSpan.FromSeconds(4), delay2);
    }

    [Fact]
    public void GetRetryDelay_WithMaxDelay_CapsAtMaximum()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(
            baseDelay: TimeSpan.FromSeconds(1),
            maxDelay: TimeSpan.FromSeconds(5),
            jitterFactor: 0);

        // Act
        var delay0 = policy.GetRetryDelay(0); // 1s
        var delay1 = policy.GetRetryDelay(1); // 2s
        var delay2 = policy.GetRetryDelay(2); // 4s
        var delay3 = policy.GetRetryDelay(3); // 8s -> capped at 5s
        var delay4 = policy.GetRetryDelay(4); // 16s -> capped at 5s

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(1), delay0);
        Assert.Equal(TimeSpan.FromSeconds(2), delay1);
        Assert.Equal(TimeSpan.FromSeconds(4), delay2);
        Assert.Equal(TimeSpan.FromSeconds(5), delay3);
        Assert.Equal(TimeSpan.FromSeconds(5), delay4);
    }

    [Fact]
    public void GetRetryDelay_WithJitter_AddsRandomness()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(
            baseDelay: TimeSpan.FromSeconds(1),
            jitterFactor: 0.3); // 30% jitter

        // Act - Get multiple delays for same attempt
        var delays = new List<TimeSpan>();
        for (int i = 0; i < 10; i++)
        {
            delays.Add(policy.GetRetryDelay(1)); // All for attempt 1
        }

        // Assert - With jitter, delays should vary within expected range
        // Base delay for attempt 1: 2s
        // With 30% jitter: 2s to 2.6s (2 * 1.3)
        var minExpected = TimeSpan.FromSeconds(2);
        var maxExpected = TimeSpan.FromSeconds(2.6);

        Assert.All(delays, delay =>
        {
            Assert.True(delay >= minExpected, $"Delay {delay} is less than minimum {minExpected}");
            Assert.True(delay <= maxExpected, $"Delay {delay} exceeds maximum {maxExpected}");
        });

        // At least some variation should exist (not all identical)
        Assert.True(delays.Distinct().Count() > 1, "Jitter should create variation in delays");
    }

    [Fact]
    public void GetRetryDelay_WithCustomBaseDelay_UsesSpecifiedBase()
    {
        // Arrange
        var customBase = TimeSpan.FromSeconds(3);
        var policy = new ExponentialBackoffRetryPolicy(
            baseDelay: customBase,
            jitterFactor: 0);

        // Act
        var delay0 = policy.GetRetryDelay(0);
        var delay1 = policy.GetRetryDelay(1);

        // Assert - 3s, 6s
        Assert.Equal(customBase, delay0);
        Assert.Equal(TimeSpan.FromSeconds(6), delay1);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldRetry_WithZeroMaxRetries_NeverRetries()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 0);
        var exception = new TimeoutException();

        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);

        // Assert
        Assert.False(shouldRetry);
    }

    [Fact]
    public void GetRetryDelay_WithZeroJitterFactor_HasNoRandomness()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(jitterFactor: 0);

        // Act - Get multiple delays for same attempt
        var delay1 = policy.GetRetryDelay(1);
        var delay2 = policy.GetRetryDelay(1);
        var delay3 = policy.GetRetryDelay(1);

        // Assert - With no jitter, all delays should be identical
        Assert.Equal(delay1, delay2);
        Assert.Equal(delay2, delay3);
    }

    #endregion
}

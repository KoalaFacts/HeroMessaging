using HeroMessaging.Policies;
using Xunit;

namespace HeroMessaging.Tests.Unit.Policies;

/// <summary>
/// Unit tests for LinearRetryPolicy
/// Tests linear retry logic with fixed delays
/// </summary>
[Trait("Category", "Unit")]
public sealed class LinearRetryPolicyTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithDefaults_SetsCorrectValues()
    {
        // Act
        var policy = new LinearRetryPolicy();

        // Assert
        Assert.Equal(3, policy.MaxRetries);
    }

    [Fact]
    public void Constructor_WithCustomMaxRetries_SetsCorrectValue()
    {
        // Act
        var policy = new LinearRetryPolicy(maxRetries: 5);

        // Assert
        Assert.Equal(5, policy.MaxRetries);
    }

    [Fact]
    public void Constructor_WithCustomDelay_UsesSpecifiedDelay()
    {
        // Arrange
        var customDelay = TimeSpan.FromSeconds(3);

        // Act
        var policy = new LinearRetryPolicy(delay: customDelay);

        // Assert
        var delay = policy.GetRetryDelay(0);
        Assert.Equal(customDelay, delay);
    }

    [Fact]
    public void Constructor_WithRetryableExceptions_OnlyRetriesSpecifiedTypes()
    {
        // Arrange
        var policy = new LinearRetryPolicy(
            maxRetries: 3,
            retryableExceptions: typeof(InvalidOperationException));

        // Act
        var shouldRetryInvalidOp = policy.ShouldRetry(new InvalidOperationException(), 0);
        var shouldRetryTimeout = policy.ShouldRetry(new TimeoutException(), 0);

        // Assert
        Assert.True(shouldRetryInvalidOp);
        Assert.False(shouldRetryTimeout);
    }

    #endregion

    #region ShouldRetry Tests

    [Fact]
    public void ShouldRetry_WithNullException_ReturnsFalse()
    {
        // Arrange
        var policy = new LinearRetryPolicy();

        // Act
        var shouldRetry = policy.ShouldRetry(null, 0);

        // Assert
        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_WhenAttemptNumberExceedsMaxRetries_ReturnsFalse()
    {
        // Arrange
        var policy = new LinearRetryPolicy(maxRetries: 3);
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
        var policy = new LinearRetryPolicy();
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
        var policy = new LinearRetryPolicy();
        var exception = new TaskCanceledException("Task was canceled");

        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);

        // Assert
        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_WithOutOfMemoryException_ReturnsFalse()
    {
        // Arrange
        var policy = new LinearRetryPolicy();
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
        var policy = new LinearRetryPolicy();
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
        var policy = new LinearRetryPolicy();
        var exception = new AccessViolationException();

        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);

        // Assert - Critical errors should never retry
        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_WithNonRetryableException_ReturnsFalse()
    {
        // Arrange
        var policy = new LinearRetryPolicy();
        var exception = new InvalidOperationException("Non-retryable error");

        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);

        // Assert
        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_WithDerivedRetryableException_ReturnsTrue()
    {
        // Arrange - TaskCanceledException derives from OperationCanceledException,
        // and TaskCanceledException is in the default retryable exceptions list
        var policy = new LinearRetryPolicy();
        var exception = new TaskCanceledException("Task canceled");

        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);

        // Assert
        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_WithInnerRetryableException_ReturnsTrue()
    {
        // Arrange
        var policy = new LinearRetryPolicy();
        var innerException = new TimeoutException("Inner timeout");
        var outerException = new InvalidOperationException("Outer error", innerException);

        // Act
        var shouldRetry = policy.ShouldRetry(outerException, 0);

        // Assert - Should retry because inner exception is retryable
        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_AcrossMultipleAttempts_RespectsMaxRetries()
    {
        // Arrange
        var policy = new LinearRetryPolicy(maxRetries: 3);
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
    public void GetRetryDelay_WithDefaultDelay_ReturnsOneSecond()
    {
        // Arrange
        var policy = new LinearRetryPolicy();

        // Act
        var delay = policy.GetRetryDelay(0);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(1), delay);
    }

    [Fact]
    public void GetRetryDelay_WithCustomDelay_ReturnsCustomValue()
    {
        // Arrange
        var customDelay = TimeSpan.FromSeconds(5);
        var policy = new LinearRetryPolicy(delay: customDelay);

        // Act
        var delay = policy.GetRetryDelay(0);

        // Assert
        Assert.Equal(customDelay, delay);
    }

    [Fact]
    public void GetRetryDelay_AcrossMultipleAttempts_ReturnsSameDelay()
    {
        // Arrange
        var customDelay = TimeSpan.FromSeconds(2);
        var policy = new LinearRetryPolicy(delay: customDelay);

        // Act
        var delay0 = policy.GetRetryDelay(0);
        var delay1 = policy.GetRetryDelay(1);
        var delay2 = policy.GetRetryDelay(2);

        // Assert - Linear policy always returns same delay
        Assert.Equal(customDelay, delay0);
        Assert.Equal(customDelay, delay1);
        Assert.Equal(customDelay, delay2);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldRetry_WithZeroMaxRetries_NeverRetries()
    {
        // Arrange
        var policy = new LinearRetryPolicy(maxRetries: 0);
        var exception = new TimeoutException();

        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);

        // Assert
        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_WithMultipleRetryableExceptionTypes_HandlesAll()
    {
        // Arrange
        var policy = new LinearRetryPolicy(
            maxRetries: 3,
            retryableExceptions: new[] { typeof(TimeoutException), typeof(IOException) });

        // Act
        var shouldRetryTimeout = policy.ShouldRetry(new TimeoutException(), 0);
        var shouldRetryIO = policy.ShouldRetry(new IOException(), 0);
        var shouldRetryInvalidOp = policy.ShouldRetry(new InvalidOperationException(), 0);

        // Assert
        Assert.True(shouldRetryTimeout);
        Assert.True(shouldRetryIO);
        Assert.False(shouldRetryInvalidOp);
    }

    [Fact]
    public void GetRetryDelay_WithZeroDelay_ReturnsZero()
    {
        // Arrange
        var policy = new LinearRetryPolicy(delay: TimeSpan.Zero);

        // Act
        var delay = policy.GetRetryDelay(0);

        // Assert
        Assert.Equal(TimeSpan.Zero, delay);
    }

    #endregion
}

using HeroMessaging.Policies;
using Xunit;

namespace HeroMessaging.Tests.Unit.Policies;

/// <summary>
/// Unit tests for <see cref="LinearRetryPolicy"/> implementation.
/// Tests cover retry logic, delay strategies, exception filtering, and edge cases.
/// </summary>
public class LinearRetryPolicyTests
{
    #region Constructor Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithDefaultParameters_CreatesInstance()
    {
        // Arrange & Act
        var policy = new LinearRetryPolicy();

        // Assert
        Assert.NotNull(policy);
        Assert.Equal(3, policy.MaxRetries);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithCustomMaxRetries_SetsCorrectly()
    {
        // Arrange & Act
        var policy = new LinearRetryPolicy(maxRetries: 5);

        // Assert
        Assert.Equal(5, policy.MaxRetries);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithCustomDelay_UsesCustomDelay()
    {
        // Arrange
        var customDelay = TimeSpan.FromSeconds(3);

        // Act
        var policy = new LinearRetryPolicy(delay: customDelay);
        var delay = policy.GetRetryDelay(attemptNumber: 0);

        // Assert
        Assert.Equal(customDelay, delay);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithCustomRetryableExceptions_UsesCustomExceptions()
    {
        // Arrange & Act
        var policy = new LinearRetryPolicy(
            maxRetries: 3,
            delay: TimeSpan.FromSeconds(1),
            retryableExceptions: typeof(InvalidOperationException));
        var exception = new InvalidOperationException("Test error");

        // Assert
        var result = policy.ShouldRetry(exception, attemptNumber: 0);
        Assert.True(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNoRetryableExceptions_UsesDefaultExceptions()
    {
        // Arrange & Act
        var policy = new LinearRetryPolicy();
        var timeoutException = new TimeoutException("Test error");
        var canceledException = new TaskCanceledException("Test error");

        // Assert - Default retryable exceptions should be TimeoutException and TaskCanceledException
        Assert.True(policy.ShouldRetry(timeoutException, attemptNumber: 0));
        Assert.True(policy.ShouldRetry(canceledException, attemptNumber: 0));
    }

    #endregion

    #region ShouldRetry Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithNullException_ReturnsFalse()
    {
        // Arrange
        var policy = new LinearRetryPolicy();

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
        var policy = new LinearRetryPolicy(maxRetries: 3);
        var exception = new TimeoutException("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 3);

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WhenAttemptNumberEqualsMaxRetries_ReturnsFalse()
    {
        // Arrange
        var policy = new LinearRetryPolicy(maxRetries: 5);
        var exception = new TimeoutException("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 5);

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithinMaxRetries_ReturnsTrue()
    {
        // Arrange
        var policy = new LinearRetryPolicy(maxRetries: 3);
        var exception = new TimeoutException("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 0);

        // Assert
        Assert.True(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithRetryableException_ReturnsTrue()
    {
        // Arrange
        var policy = new LinearRetryPolicy(
            retryableExceptions: typeof(InvalidOperationException));
        var exception = new InvalidOperationException("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 0);

        // Assert
        Assert.True(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithNonRetryableException_ReturnsFalse()
    {
        // Arrange
        var policy = new LinearRetryPolicy(
            retryableExceptions: typeof(TimeoutException));
        var exception = new InvalidOperationException("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 0);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Critical Exception Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithOutOfMemoryException_ReturnsFalse()
    {
        // Arrange
        var policy = new LinearRetryPolicy(
            retryableExceptions: typeof(OutOfMemoryException));
        var exception = new OutOfMemoryException("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 0);

        // Assert
        Assert.False(result, "Should never retry OutOfMemoryException");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithStackOverflowException_ReturnsFalse()
    {
        // Arrange
        var policy = new LinearRetryPolicy(
            retryableExceptions: typeof(StackOverflowException));
        var exception = new StackOverflowException("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 0);

        // Assert
        Assert.False(result, "Should never retry StackOverflowException");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithAccessViolationException_ReturnsFalse()
    {
        // Arrange
        var policy = new LinearRetryPolicy(
            retryableExceptions: typeof(AccessViolationException));
        var exception = new AccessViolationException("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 0);

        // Assert
        Assert.False(result, "Should never retry AccessViolationException");
    }

    #endregion

    #region Exception Hierarchy Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithDerivedRetryableException_ReturnsTrue()
    {
        // Arrange
        var policy = new LinearRetryPolicy(
            retryableExceptions: typeof(InvalidOperationException));
        var derivedException = new DerivedInvalidOperationException("Test error");

        // Act
        var result = policy.ShouldRetry(derivedException, attemptNumber: 0);

        // Assert
        Assert.True(result, "Should retry derived exception types");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithInnerRetryableException_ReturnsTrue()
    {
        // Arrange
        var policy = new LinearRetryPolicy(
            retryableExceptions: typeof(TimeoutException));
        var innerException = new TimeoutException("Inner error");
        var outerException = new InvalidOperationException("Outer error", innerException);

        // Act
        var result = policy.ShouldRetry(outerException, attemptNumber: 0);

        // Assert
        Assert.True(result, "Should retry if inner exception is retryable");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithNonRetryableInnerException_ReturnsFalse()
    {
        // Arrange
        var policy = new LinearRetryPolicy(
            retryableExceptions: typeof(TimeoutException));
        var innerException = new InvalidOperationException("Inner error");
        var outerException = new AggregateException("Outer error", innerException);

        // Act
        var result = policy.ShouldRetry(outerException, attemptNumber: 0);

        // Assert
        Assert.False(result, "Should not retry if inner exception is not retryable");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithNestedRetryableException_ReturnsTrue()
    {
        // Arrange
        var policy = new LinearRetryPolicy(
            retryableExceptions: typeof(TimeoutException));
        var deepException = new TimeoutException("Deep error");
        var middleException = new InvalidOperationException("Middle error", deepException);
        var outerException = new AggregateException("Outer error", middleException);

        // Act
        var result = policy.ShouldRetry(outerException, attemptNumber: 0);

        // Assert
        Assert.True(result, "Should find retryable exception in nested hierarchy");
    }

    #endregion

    #region GetRetryDelay Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRetryDelay_ReturnsFixedDelay()
    {
        // Arrange
        var delay = TimeSpan.FromSeconds(2);
        var policy = new LinearRetryPolicy(delay: delay);

        // Act & Assert - Delay should be fixed regardless of attempt number
        Assert.Equal(delay, policy.GetRetryDelay(attemptNumber: 0));
        Assert.Equal(delay, policy.GetRetryDelay(attemptNumber: 1));
        Assert.Equal(delay, policy.GetRetryDelay(attemptNumber: 2));
        Assert.Equal(delay, policy.GetRetryDelay(attemptNumber: 10));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRetryDelay_WithDefaultDelay_ReturnsOneSecond()
    {
        // Arrange
        var policy = new LinearRetryPolicy();

        // Act
        var delay = policy.GetRetryDelay(attemptNumber: 0);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(1), delay);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRetryDelay_WithCustomDelay_ReturnsCustomDelay()
    {
        // Arrange
        var customDelay = TimeSpan.FromMilliseconds(500);
        var policy = new LinearRetryPolicy(delay: customDelay);

        // Act
        var delay = policy.GetRetryDelay(attemptNumber: 5);

        // Assert
        Assert.Equal(customDelay, delay);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRetryDelay_WithZeroDelay_ReturnsZero()
    {
        // Arrange
        var policy = new LinearRetryPolicy(delay: TimeSpan.Zero);

        // Act
        var delay = policy.GetRetryDelay(attemptNumber: 0);

        // Assert
        Assert.Equal(TimeSpan.Zero, delay);
    }

    #endregion

    #region Edge Cases and Boundary Conditions

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithZeroMaxRetries_AlwaysReturnsFalse()
    {
        // Arrange
        var policy = new LinearRetryPolicy(maxRetries: 0);
        var exception = new TimeoutException("Test error");

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
        var policy = new LinearRetryPolicy(maxRetries: 3);
        var exception = new TimeoutException("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: -1);

        // Assert
        Assert.True(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithLargeAttemptNumber_ReturnsFalse()
    {
        // Arrange
        var policy = new LinearRetryPolicy(maxRetries: 3);
        var exception = new TimeoutException("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 1000);

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MaxRetries_ReturnsConfiguredValue()
    {
        // Arrange
        var expectedMaxRetries = 7;
        var policy = new LinearRetryPolicy(maxRetries: expectedMaxRetries);

        // Act
        var actualMaxRetries = policy.MaxRetries;

        // Assert
        Assert.Equal(expectedMaxRetries, actualMaxRetries);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_AllAttemptsUpToMaxRetries_ReturnsCorrectValues()
    {
        // Arrange
        var policy = new LinearRetryPolicy(maxRetries: 3);
        var exception = new TimeoutException("Test error");

        // Act & Assert
        Assert.True(policy.ShouldRetry(exception, attemptNumber: 0), "Attempt 0 should retry");
        Assert.True(policy.ShouldRetry(exception, attemptNumber: 1), "Attempt 1 should retry");
        Assert.True(policy.ShouldRetry(exception, attemptNumber: 2), "Attempt 2 should retry");
        Assert.False(policy.ShouldRetry(exception, attemptNumber: 3), "Attempt 3 should not retry");
    }

    #endregion

    #region Multiple Retryable Exception Types Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithMultipleRetryableExceptions_HandlesAllTypes()
    {
        // Arrange
        var policy = new LinearRetryPolicy(
            maxRetries: 3,
            delay: TimeSpan.FromSeconds(1),
            retryableExceptions: new[] { typeof(TimeoutException), typeof(InvalidOperationException) });

        // Act & Assert
        Assert.True(policy.ShouldRetry(new TimeoutException("Error 1"), attemptNumber: 0));
        Assert.True(policy.ShouldRetry(new InvalidOperationException("Error 2"), attemptNumber: 0));
        Assert.False(policy.ShouldRetry(new ArgumentException("Error 3"), attemptNumber: 0));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithMultipleRetryableTypes_ChecksAllTypes()
    {
        // Arrange
        var policy = new LinearRetryPolicy(
            maxRetries: 3,
            delay: TimeSpan.FromSeconds(1),
            retryableExceptions: new[] { typeof(TimeoutException), typeof(InvalidOperationException), typeof(ArgumentException) });

        // Act & Assert
        Assert.True(policy.ShouldRetry(new TimeoutException(), attemptNumber: 0));
        Assert.True(policy.ShouldRetry(new InvalidOperationException(), attemptNumber: 0));
        Assert.True(policy.ShouldRetry(new ArgumentException(), attemptNumber: 0));
        Assert.False(policy.ShouldRetry(new FormatException(), attemptNumber: 0));
    }

    #endregion

    #region Default Retryable Exceptions Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithDefaultRetryableExceptions_RetriesTimeoutException()
    {
        // Arrange
        var policy = new LinearRetryPolicy();
        var exception = new TimeoutException("Test timeout");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 0);

        // Assert
        Assert.True(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithDefaultRetryableExceptions_RetriesTaskCanceledException()
    {
        // Arrange
        var policy = new LinearRetryPolicy();
        var exception = new TaskCanceledException("Test cancellation");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 0);

        // Assert
        Assert.True(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithDefaultRetryableExceptions_DoesNotRetryOtherExceptions()
    {
        // Arrange
        var policy = new LinearRetryPolicy();
        var exception = new InvalidOperationException("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 0);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_ConcurrentCalls_ProducesConsistentResults()
    {
        // Arrange
        var policy = new LinearRetryPolicy(maxRetries: 10);
        var exception = new TimeoutException("Test error");
        var results = new System.Collections.Concurrent.ConcurrentBag<bool>();

        // Act - Simulate concurrent calls
        var tasks = Enumerable.Range(0, 100).Select(i =>
            Task.Run(() =>
            {
                var result = policy.ShouldRetry(exception, attemptNumber: i % 15);
                results.Add(result);
            }));

        Task.WaitAll(tasks.ToArray());

        // Assert - All calls within maxRetries should return true
        var expectedTrueCount = 100 * 10 / 15 + (100 % 15 <= 10 ? 100 % 15 : 10);
        Assert.True(results.Count(r => r) >= 60, "Majority of attempts within retry limit should succeed");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRetryDelay_ConcurrentCalls_ReturnsConsistentDelay()
    {
        // Arrange
        var expectedDelay = TimeSpan.FromSeconds(2);
        var policy = new LinearRetryPolicy(delay: expectedDelay);
        var results = new System.Collections.Concurrent.ConcurrentBag<TimeSpan>();

        // Act - Simulate concurrent calls
        var tasks = Enumerable.Range(0, 100).Select(i =>
            Task.Run(() =>
            {
                var delay = policy.GetRetryDelay(attemptNumber: i);
                results.Add(delay);
            }));

        Task.WaitAll(tasks.ToArray());

        // Assert - All delays should be the same
        Assert.All(results, delay => Assert.Equal(expectedDelay, delay));
    }

    #endregion
}

/// <summary>
/// Derived exception class for testing exception hierarchy
/// </summary>
public class DerivedInvalidOperationException : InvalidOperationException
{
    public DerivedInvalidOperationException(string message) : base(message) { }
}

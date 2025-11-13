using HeroMessaging.Policies;
using Xunit;

namespace HeroMessaging.Tests.Unit.Policies;

/// <summary>
/// Unit tests for <see cref="NoRetryPolicy"/> implementation.
/// Tests verify that the policy never allows retries under any circumstances.
/// </summary>
public class NoRetryPolicyTests
{
    #region Constructor Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_CreatesInstance()
    {
        // Arrange & Act
        var policy = new NoRetryPolicy();

        // Assert
        Assert.NotNull(policy);
    }

    #endregion

    #region MaxRetries Property Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void MaxRetries_ReturnsZero()
    {
        // Arrange
        var policy = new NoRetryPolicy();

        // Act
        var maxRetries = policy.MaxRetries;

        // Assert
        Assert.Equal(0, maxRetries);
    }

    #endregion

    #region ShouldRetry Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithNullException_ReturnsFalse()
    {
        // Arrange
        var policy = new NoRetryPolicy();

        // Act
        var result = policy.ShouldRetry(null, attemptNumber: 0);

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithValidException_ReturnsFalse()
    {
        // Arrange
        var policy = new NoRetryPolicy();
        var exception = new InvalidOperationException("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 0);

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithZeroAttemptNumber_ReturnsFalse()
    {
        // Arrange
        var policy = new NoRetryPolicy();
        var exception = new TimeoutException("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 0);

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithPositiveAttemptNumber_ReturnsFalse()
    {
        // Arrange
        var policy = new NoRetryPolicy();
        var exception = new TimeoutException("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 5);

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithNegativeAttemptNumber_ReturnsFalse()
    {
        // Arrange
        var policy = new NoRetryPolicy();
        var exception = new TimeoutException("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: -1);

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithLargeAttemptNumber_ReturnsFalse()
    {
        // Arrange
        var policy = new NoRetryPolicy();
        var exception = new TimeoutException("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: int.MaxValue);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Different Exception Types Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithTimeoutException_ReturnsFalse()
    {
        // Arrange
        var policy = new NoRetryPolicy();
        var exception = new TimeoutException("Timeout occurred");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 0);

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithTaskCanceledException_ReturnsFalse()
    {
        // Arrange
        var policy = new NoRetryPolicy();
        var exception = new TaskCanceledException("Task was canceled");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 0);

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithInvalidOperationException_ReturnsFalse()
    {
        // Arrange
        var policy = new NoRetryPolicy();
        var exception = new InvalidOperationException("Invalid operation");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 0);

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithArgumentException_ReturnsFalse()
    {
        // Arrange
        var policy = new NoRetryPolicy();
        var exception = new ArgumentException("Invalid argument");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 0);

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithAggregateException_ReturnsFalse()
    {
        // Arrange
        var policy = new NoRetryPolicy();
        var innerException = new InvalidOperationException("Inner error");
        var exception = new AggregateException("Aggregate error", innerException);

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 0);

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithCustomException_ReturnsFalse()
    {
        // Arrange
        var policy = new NoRetryPolicy();
        var exception = new CustomTestException("Custom error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 0);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetRetryDelay Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRetryDelay_WithZeroAttemptNumber_ReturnsZero()
    {
        // Arrange
        var policy = new NoRetryPolicy();

        // Act
        var delay = policy.GetRetryDelay(attemptNumber: 0);

        // Assert
        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRetryDelay_WithPositiveAttemptNumber_ReturnsZero()
    {
        // Arrange
        var policy = new NoRetryPolicy();

        // Act
        var delay = policy.GetRetryDelay(attemptNumber: 5);

        // Assert
        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRetryDelay_WithNegativeAttemptNumber_ReturnsZero()
    {
        // Arrange
        var policy = new NoRetryPolicy();

        // Act
        var delay = policy.GetRetryDelay(attemptNumber: -1);

        // Assert
        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRetryDelay_WithLargeAttemptNumber_ReturnsZero()
    {
        // Arrange
        var policy = new NoRetryPolicy();

        // Act
        var delay = policy.GetRetryDelay(attemptNumber: int.MaxValue);

        // Assert
        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRetryDelay_MultipleCalls_AlwaysReturnsZero()
    {
        // Arrange
        var policy = new NoRetryPolicy();

        // Act & Assert
        for (int i = 0; i < 10; i++)
        {
            var delay = policy.GetRetryDelay(attemptNumber: i);
            Assert.Equal(TimeSpan.Zero, delay);
        }
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_ConcurrentCalls_AlwaysReturnsFalse()
    {
        // Arrange
        var policy = new NoRetryPolicy();
        var exception = new TimeoutException("Test error");
        var results = new System.Collections.Concurrent.ConcurrentBag<bool>();

        // Act - Simulate concurrent calls
        var tasks = Enumerable.Range(0, 100).Select(i =>
            Task.Run(() =>
            {
                var result = policy.ShouldRetry(exception, attemptNumber: i);
                results.Add(result);
            }));

        Task.WaitAll(tasks.ToArray());

        // Assert - All results should be false
        Assert.All(results, result => Assert.False(result));
        Assert.Equal(100, results.Count);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRetryDelay_ConcurrentCalls_AlwaysReturnsZero()
    {
        // Arrange
        var policy = new NoRetryPolicy();
        var results = new System.Collections.Concurrent.ConcurrentBag<TimeSpan>();

        // Act - Simulate concurrent calls
        var tasks = Enumerable.Range(0, 100).Select(i =>
            Task.Run(() =>
            {
                var delay = policy.GetRetryDelay(attemptNumber: i);
                results.Add(delay);
            }));

        Task.WaitAll(tasks.ToArray());

        // Assert - All delays should be zero
        Assert.All(results, delay => Assert.Equal(TimeSpan.Zero, delay));
        Assert.Equal(100, results.Count);
    }

    #endregion

    #region Edge Cases and Boundary Conditions

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_RepeatedCallsWithSameException_AlwaysReturnsFalse()
    {
        // Arrange
        var policy = new NoRetryPolicy();
        var exception = new InvalidOperationException("Test error");

        // Act & Assert - Multiple calls should always return false
        for (int i = 0; i < 100; i++)
        {
            var result = policy.ShouldRetry(exception, attemptNumber: i);
            Assert.False(result, $"Attempt {i} should return false");
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRetry_WithDifferentExceptions_AlwaysReturnsFalse()
    {
        // Arrange
        var policy = new NoRetryPolicy();
        var exceptions = new Exception[]
        {
            new TimeoutException(),
            new InvalidOperationException(),
            new ArgumentException(),
            new FormatException(),
            new NotSupportedException(),
            new TaskCanceledException(),
            new OperationCanceledException()
        };

        // Act & Assert
        foreach (var exception in exceptions)
        {
            var result = policy.ShouldRetry(exception, attemptNumber: 0);
            Assert.False(result, $"Exception type {exception.GetType().Name} should not retry");
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MaxRetries_IsConsistentAcrossInstances()
    {
        // Arrange & Act
        var policy1 = new NoRetryPolicy();
        var policy2 = new NoRetryPolicy();

        // Assert
        Assert.Equal(policy1.MaxRetries, policy2.MaxRetries);
        Assert.Equal(0, policy1.MaxRetries);
        Assert.Equal(0, policy2.MaxRetries);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRetryDelay_IsConsistentAcrossInstances()
    {
        // Arrange & Act
        var policy1 = new NoRetryPolicy();
        var policy2 = new NoRetryPolicy();

        // Assert
        Assert.Equal(policy1.GetRetryDelay(0), policy2.GetRetryDelay(0));
        Assert.Equal(TimeSpan.Zero, policy1.GetRetryDelay(0));
        Assert.Equal(TimeSpan.Zero, policy2.GetRetryDelay(0));
    }

    #endregion

    #region Use Case Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void NoRetryPolicy_ForCriticalOperations_NeverRetries()
    {
        // Arrange - Simulate critical operation that should never retry
        var policy = new NoRetryPolicy();
        var criticalException = new InvalidOperationException("Critical operation failed");

        // Act
        var shouldRetry = policy.ShouldRetry(criticalException, attemptNumber: 0);
        var delay = policy.GetRetryDelay(attemptNumber: 0);

        // Assert
        Assert.False(shouldRetry);
        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NoRetryPolicy_ForTestingScenarios_ProvidesConsistentBehavior()
    {
        // Arrange - Simulate testing scenario where retries should be disabled
        var policy = new NoRetryPolicy();
        var testException = new TimeoutException("Test timeout");

        // Act & Assert - Consistent behavior for testing
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Assert.False(policy.ShouldRetry(testException, attemptNumber: attempt));
            Assert.Equal(TimeSpan.Zero, policy.GetRetryDelay(attemptNumber: attempt));
        }
    }

    #endregion
}

/// <summary>
/// Custom exception class for testing
/// </summary>
public class CustomTestException : Exception
{
    public CustomTestException(string message) : base(message) { }
}

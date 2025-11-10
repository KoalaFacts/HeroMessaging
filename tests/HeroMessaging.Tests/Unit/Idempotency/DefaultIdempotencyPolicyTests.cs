using HeroMessaging.Idempotency;
using HeroMessaging.Idempotency.KeyGeneration;
using Xunit;

namespace HeroMessaging.Tests.Unit.Idempotency;

/// <summary>
/// Unit tests for DefaultIdempotencyPolicy
/// Tests the default policy implementation for idempotency behavior
/// </summary>
[Trait("Category", "Unit")]
public sealed class DefaultIdempotencyPolicyTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithNoArguments_UsesDefaults()
    {
        // Act
        var policy = new DefaultIdempotencyPolicy();

        // Assert
        Assert.NotNull(policy.KeyGenerator);
        Assert.IsType<MessageIdKeyGenerator>(policy.KeyGenerator);
        Assert.Equal(TimeSpan.FromHours(24), policy.SuccessTtl);
        Assert.Equal(TimeSpan.FromHours(1), policy.FailureTtl);
        Assert.True(policy.CacheFailures);
    }

    [Fact]
    public void Constructor_WithCustomSuccessTtl_SetsCorrectly()
    {
        // Arrange
        var customTtl = TimeSpan.FromDays(7);

        // Act
        var policy = new DefaultIdempotencyPolicy(successTtl: customTtl);

        // Assert
        Assert.Equal(customTtl, policy.SuccessTtl);
    }

    [Fact]
    public void Constructor_WithCustomFailureTtl_SetsCorrectly()
    {
        // Arrange
        var customTtl = TimeSpan.FromMinutes(30);

        // Act
        var policy = new DefaultIdempotencyPolicy(failureTtl: customTtl);

        // Assert
        Assert.Equal(customTtl, policy.FailureTtl);
    }

    [Fact]
    public void Constructor_WithCustomKeyGenerator_SetsCorrectly()
    {
        // Arrange
        var customGenerator = new MessageIdKeyGenerator();

        // Act
        var policy = new DefaultIdempotencyPolicy(keyGenerator: customGenerator);

        // Assert
        Assert.Same(customGenerator, policy.KeyGenerator);
    }

    [Fact]
    public void Constructor_WithCacheFailuresFalse_SetsCorrectly()
    {
        // Act
        var policy = new DefaultIdempotencyPolicy(cacheFailures: false);

        // Assert
        Assert.False(policy.CacheFailures);
    }

    [Fact]
    public void Constructor_WithAllCustomParameters_SetsAllCorrectly()
    {
        // Arrange
        var customSuccessTtl = TimeSpan.FromDays(30);
        var customFailureTtl = TimeSpan.FromMinutes(15);
        var customGenerator = new MessageIdKeyGenerator();
        var cacheFailures = false;

        // Act
        var policy = new DefaultIdempotencyPolicy(
            successTtl: customSuccessTtl,
            failureTtl: customFailureTtl,
            keyGenerator: customGenerator,
            cacheFailures: cacheFailures);

        // Assert
        Assert.Equal(customSuccessTtl, policy.SuccessTtl);
        Assert.Equal(customFailureTtl, policy.FailureTtl);
        Assert.Same(customGenerator, policy.KeyGenerator);
        Assert.Equal(cacheFailures, policy.CacheFailures);
    }

    #endregion

    #region IsIdempotentFailure Tests - Idempotent Exceptions

    [Fact]
    public void IsIdempotentFailure_WithArgumentException_ReturnsTrue()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var exception = new ArgumentException("Invalid argument");

        // Act
        var result = policy.IsIdempotentFailure(exception);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsIdempotentFailure_WithArgumentNullException_ReturnsTrue()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var exception = new ArgumentNullException("paramName");

        // Act
        var result = policy.IsIdempotentFailure(exception);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsIdempotentFailure_WithArgumentOutOfRangeException_ReturnsTrue()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var exception = new ArgumentOutOfRangeException("paramName");

        // Act
        var result = policy.IsIdempotentFailure(exception);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsIdempotentFailure_WithInvalidOperationException_ReturnsTrue()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var exception = new InvalidOperationException("Invalid operation");

        // Act
        var result = policy.IsIdempotentFailure(exception);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsIdempotentFailure_WithNotSupportedException_ReturnsTrue()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var exception = new NotSupportedException("Operation not supported");

        // Act
        var result = policy.IsIdempotentFailure(exception);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsIdempotentFailure_WithFormatException_ReturnsTrue()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var exception = new FormatException("Invalid format");

        // Act
        var result = policy.IsIdempotentFailure(exception);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsIdempotentFailure_WithUnauthorizedAccessException_ReturnsTrue()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var exception = new UnauthorizedAccessException("Access denied");

        // Act
        var result = policy.IsIdempotentFailure(exception);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsIdempotentFailure_WithKeyNotFoundException_ReturnsTrue()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var exception = new KeyNotFoundException("Key not found");

        // Act
        var result = policy.IsIdempotentFailure(exception);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region IsIdempotentFailure Tests - Non-Idempotent (Transient) Exceptions

    [Fact]
    public void IsIdempotentFailure_WithTimeoutException_ReturnsFalse()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var exception = new TimeoutException("Operation timed out");

        // Act
        var result = policy.IsIdempotentFailure(exception);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsIdempotentFailure_WithTaskCanceledException_ReturnsFalse()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var exception = new TaskCanceledException("Task was canceled");

        // Act
        var result = policy.IsIdempotentFailure(exception);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsIdempotentFailure_WithOperationCanceledException_ReturnsFalse()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var exception = new OperationCanceledException("Operation was canceled");

        // Act
        var result = policy.IsIdempotentFailure(exception);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsIdempotentFailure_WithIOException_ReturnsFalse()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var exception = new System.IO.IOException("I/O error");

        // Act
        var result = policy.IsIdempotentFailure(exception);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsIdempotentFailure_WithHttpRequestException_ReturnsFalse()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var exception = new HttpRequestException("Network error");

        // Act
        var result = policy.IsIdempotentFailure(exception);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsIdempotentFailure_WithSocketException_ReturnsFalse()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var exception = new System.Net.Sockets.SocketException();

        // Act
        var result = policy.IsIdempotentFailure(exception);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region IsIdempotentFailure Tests - Unknown/Generic Exceptions

    [Fact]
    public void IsIdempotentFailure_WithGenericException_ReturnsFalse()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var exception = new Exception("Generic error");

        // Act
        var result = policy.IsIdempotentFailure(exception);

        // Assert
        Assert.False(result); // Conservative default: don't cache unknown exceptions
    }

    [Fact]
    public void IsIdempotentFailure_WithCustomException_ReturnsFalse()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var exception = new CustomTestException("Custom error");

        // Act
        var result = policy.IsIdempotentFailure(exception);

        // Assert
        Assert.False(result); // Conservative default
    }

    #endregion

    #region IsIdempotentFailure Tests - Exception Hierarchy

    [Fact]
    public void IsIdempotentFailure_WithDerivedArgumentException_ReturnsTrue()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        // ArgumentNullException derives from ArgumentException
        var exception = new ArgumentNullException("param");

        // Act
        var result = policy.IsIdempotentFailure(exception);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsIdempotentFailure_WithInnerException_ClassifiesOuterException()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var innerException = new TimeoutException("Timeout");
        var outerException = new InvalidOperationException("Wrapper", innerException);

        // Act
        var result = policy.IsIdempotentFailure(outerException);

        // Assert
        Assert.True(result); // Classifies based on outer exception type
    }

    #endregion

    #region Thread Safety and Performance Tests

    [Fact]
    public void IsIdempotentFailure_CalledConcurrently_ThreadSafe()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var exceptions = new Exception[]
        {
            new ArgumentException(),
            new TimeoutException(),
            new InvalidOperationException(),
            new TaskCanceledException()
        };

        var tasks = new List<Task<bool>>();

        // Act - call IsIdempotentFailure concurrently
        for (int i = 0; i < 100; i++)
        {
            var exception = exceptions[i % exceptions.Length];
            tasks.Add(Task.Run(() => policy.IsIdempotentFailure(exception)));
        }

        var results = Task.WhenAll(tasks).Result;

        // Assert - verify results are consistent
        Assert.Equal(100, results.Length);
        // Should have mix of true and false based on exception types
        Assert.Contains(true, results);
        Assert.Contains(false, results);
    }

    [Fact]
    public void IsIdempotentFailure_CalledMultipleTimes_ReturnsConsistentResults()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var exception = new ArgumentException("Test");

        // Act
        var results = new bool[10];
        for (int i = 0; i < 10; i++)
        {
            results[i] = policy.IsIdempotentFailure(exception);
        }

        // Assert - all results should be the same
        Assert.All(results, r => Assert.True(r));
    }

    #endregion

    #region Null Handling Tests

    [Fact]
    public void IsIdempotentFailure_WithNullException_ThrowsArgumentNullException()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            policy.IsIdempotentFailure(null!));
        Assert.Equal("exception", exception.ParamName);
    }

    #endregion

    #region Custom Exception for Testing

    private class CustomTestException : Exception
    {
        public CustomTestException(string message) : base(message) { }
    }

    #endregion
}

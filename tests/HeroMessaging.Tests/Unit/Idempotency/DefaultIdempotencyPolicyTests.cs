using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Idempotency;
using HeroMessaging.Idempotency.KeyGeneration;
using Xunit;

namespace HeroMessaging.Tests.Unit.Idempotency;

[Trait("Category", "Unit")]
public sealed class DefaultIdempotencyPolicyTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithDefaults_SetsExpectedValues()
    {
        // Act
        var policy = new DefaultIdempotencyPolicy();

        // Assert
        Assert.Equal(TimeSpan.FromHours(24), policy.SuccessTtl);
        Assert.Equal(TimeSpan.FromHours(1), policy.FailureTtl);
        Assert.True(policy.CacheFailures);
        Assert.NotNull(policy.KeyGenerator);
        Assert.IsType<MessageIdKeyGenerator>(policy.KeyGenerator);
    }

    [Fact]
    public void Constructor_WithCustomSuccessTtl_SetsCorrectValue()
    {
        // Arrange
        var customTtl = TimeSpan.FromDays(7);

        // Act
        var policy = new DefaultIdempotencyPolicy(successTtl: customTtl);

        // Assert
        Assert.Equal(customTtl, policy.SuccessTtl);
        Assert.Equal(TimeSpan.FromHours(1), policy.FailureTtl);
        Assert.True(policy.CacheFailures);
    }

    [Fact]
    public void Constructor_WithCustomFailureTtl_SetsCorrectValue()
    {
        // Arrange
        var customTtl = TimeSpan.FromMinutes(30);

        // Act
        var policy = new DefaultIdempotencyPolicy(failureTtl: customTtl);

        // Assert
        Assert.Equal(TimeSpan.FromHours(24), policy.SuccessTtl);
        Assert.Equal(customTtl, policy.FailureTtl);
        Assert.True(policy.CacheFailures);
    }

    [Fact]
    public void Constructor_WithCacheFailuresFalse_SetsCorrectValue()
    {
        // Act
        var policy = new DefaultIdempotencyPolicy(cacheFailures: false);

        // Assert
        Assert.False(policy.CacheFailures);
        Assert.Equal(TimeSpan.FromHours(24), policy.SuccessTtl);
        Assert.Equal(TimeSpan.FromHours(1), policy.FailureTtl);
    }

    [Fact]
    public void Constructor_WithCustomKeyGenerator_SetsCorrectValue()
    {
        // Arrange
        var customGenerator = new MessageIdKeyGenerator();

        // Act
        var policy = new DefaultIdempotencyPolicy(keyGenerator: customGenerator);

        // Assert
        Assert.Same(customGenerator, policy.KeyGenerator);
    }

    [Fact]
    public void Constructor_WithAllCustomParameters_SetsCorrectValues()
    {
        // Arrange
        var successTtl = TimeSpan.FromDays(30);
        var failureTtl = TimeSpan.FromMinutes(15);
        var keyGenerator = new MessageIdKeyGenerator();

        // Act
        var policy = new DefaultIdempotencyPolicy(
            successTtl: successTtl,
            failureTtl: failureTtl,
            keyGenerator: keyGenerator,
            cacheFailures: false);

        // Assert
        Assert.Equal(successTtl, policy.SuccessTtl);
        Assert.Equal(failureTtl, policy.FailureTtl);
        Assert.Same(keyGenerator, policy.KeyGenerator);
        Assert.False(policy.CacheFailures);
    }

    #endregion

    #region IsIdempotentFailure - Idempotent Exceptions (Should Cache)

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
        var exception = new NotSupportedException("Not supported");

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

    #region IsIdempotentFailure - Non-Idempotent Exceptions (Should NOT Cache)

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
        var exception = new IOException("I/O error");

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
        var exception = new HttpRequestException("HTTP request failed");

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

    [Fact]
    public void IsIdempotentFailure_WithUnknownException_ReturnsFalse()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var exception = new Exception("Unknown exception");

        // Act
        var result = policy.IsIdempotentFailure(exception);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsIdempotentFailure_WithCustomException_ReturnsFalse()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var exception = new CustomTestException("Custom exception");

        // Act
        var result = policy.IsIdempotentFailure(exception);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region IsIdempotentFailure - Edge Cases

    [Fact]
    public void IsIdempotentFailure_WithNullException_ThrowsArgumentNullException()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => policy.IsIdempotentFailure(null!));
        Assert.Equal("exception", exception.ParamName);
    }

    [Fact]
    public void IsIdempotentFailure_WithNestedArgumentException_ReturnsTrue()
    {
        // Arrange
        var policy = new DefaultIdempotencyPolicy();
        var innerException = new ArgumentException("Inner exception");
        var exception = new Exception("Outer exception", innerException);

        // Act - Note: Only the outer exception type is checked
        var result = policy.IsIdempotentFailure(exception);

        // Assert
        Assert.False(result); // Outer exception is not idempotent
    }

    #endregion

    #region Test Helper Classes

    public class CustomTestException : Exception
    {
        public CustomTestException(string message) : base(message) { }
    }

    #endregion
}

using HeroMessaging.Core.Policies;

namespace HeroMessaging.Tests.Unit.Policies;

public class LinearRetryPolicyTests
{
    [Fact]
    public void ShouldRetry_WithRetryableException_ReturnsTrue()
    {
        // Arrange
        var policy = new LinearRetryPolicy(
            maxRetries: 3,
            retryableExceptions: new[] { typeof(TimeoutException) });
        var exception = new TimeoutException();
        
        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);
        
        // Assert
        Assert.True(shouldRetry);
    }
    
    [Fact]
    public void ShouldRetry_WithNonRetryableException_ReturnsFalse()
    {
        // Arrange
        var policy = new LinearRetryPolicy(
            maxRetries: 3,
            retryableExceptions: new[] { typeof(TimeoutException) });
        var exception = new ArgumentException();
        
        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);
        
        // Assert
        Assert.False(shouldRetry);
    }
    
    [Fact]
    public void GetRetryDelay_ReturnsConstantDelay()
    {
        // Arrange
        var delay = TimeSpan.FromSeconds(2);
        var policy = new LinearRetryPolicy(delay: delay);
        
        // Act
        var delay0 = policy.GetRetryDelay(0);
        var delay1 = policy.GetRetryDelay(1);
        var delay2 = policy.GetRetryDelay(2);
        
        // Assert
        Assert.Equal(delay, delay0);
        Assert.Equal(delay, delay1);
        Assert.Equal(delay, delay2);
    }
    
    [Fact]
    public void ShouldRetry_WithDefaultRetryableExceptions_HandlesTimeoutException()
    {
        // Arrange
        var policy = new LinearRetryPolicy(); // Uses default retryable exceptions
        var exception = new TimeoutException();
        
        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);
        
        // Assert
        Assert.True(shouldRetry);
    }
    
    [Fact]
    public void ShouldRetry_WithDefaultRetryableExceptions_HandlesTaskCanceledException()
    {
        // Arrange
        var policy = new LinearRetryPolicy();
        var exception = new TaskCanceledException();
        
        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);
        
        // Assert
        Assert.True(shouldRetry);
    }
    
    [Fact]
    public void ShouldRetry_WithDerivedExceptionType_ReturnsTrue()
    {
        // Arrange
        var policy = new LinearRetryPolicy(
            retryableExceptions: new[] { typeof(InvalidOperationException) });
        var exception = new ObjectDisposedException("test"); // Derives from InvalidOperationException
        
        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);
        
        // Assert
        Assert.True(shouldRetry);
    }
    
    [Fact]
    public void ShouldRetry_WithNestedRetryableException_ReturnsTrue()
    {
        // Arrange
        var policy = new LinearRetryPolicy(
            retryableExceptions: new[] { typeof(TimeoutException) });
        var innerException = new TimeoutException();
        var exception = new AggregateException(innerException);
        
        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);
        
        // Assert
        Assert.True(shouldRetry);
    }
    
    [Fact]
    public void ShouldRetry_ExceedsMaxRetries_ReturnsFalse()
    {
        // Arrange
        var policy = new LinearRetryPolicy(maxRetries: 2);
        var exception = new TimeoutException();
        
        // Act
        var shouldRetry = policy.ShouldRetry(exception, 2);
        
        // Assert
        Assert.False(shouldRetry);
    }
    
    [Fact]
    public void ShouldRetry_WithCriticalException_ReturnsFalse()
    {
        // Arrange
        var policy = new LinearRetryPolicy();
        var exception = new StackOverflowException();
        
        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);
        
        // Assert
        Assert.False(shouldRetry);
    }
    
    [Fact]
    public void MaxRetries_ReturnsConfiguredValue()
    {
        // Arrange
        var policy = new LinearRetryPolicy(maxRetries: 5);
        
        // Assert
        Assert.Equal(5, policy.MaxRetries);
    }
}
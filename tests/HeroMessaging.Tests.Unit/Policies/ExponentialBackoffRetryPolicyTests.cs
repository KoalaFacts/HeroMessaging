using HeroMessaging.Core.Processing.Decorators;

namespace HeroMessaging.Tests.Unit.Policies;

public class ExponentialBackoffRetryPolicyTests
{
    [Fact]
    public void ShouldRetry_WithTransientError_ReturnsTrue()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 3);
        var exception = new TimeoutException();
        
        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);
        
        // Assert
        Assert.True(shouldRetry);
    }
    
    [Fact]
    public void ShouldRetry_WithCriticalError_ReturnsFalse()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 3);
        var exception = new OutOfMemoryException();
        
        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);
        
        // Assert
        Assert.False(shouldRetry);
    }
    
    [Fact]
    public void ShouldRetry_ExceedsMaxRetries_ReturnsFalse()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(maxRetries: 2);
        var exception = new TimeoutException();
        
        // Act
        var shouldRetry = policy.ShouldRetry(exception, 2); // Already at max
        
        // Assert
        Assert.False(shouldRetry);
    }
    
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
    public void GetRetryDelay_IncreasesExponentially()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(
            baseDelay: TimeSpan.FromSeconds(1),
            jitterFactor: 0); // No jitter for predictable testing
        
        // Act
        var delay0 = policy.GetRetryDelay(0);
        var delay1 = policy.GetRetryDelay(1);
        var delay2 = policy.GetRetryDelay(2);
        
        // Assert
        Assert.True(delay1 > delay0);
        Assert.True(delay2 > delay1);
        Assert.True(delay1.TotalMilliseconds >= delay0.TotalMilliseconds * 2);
        Assert.True(delay2.TotalMilliseconds >= delay1.TotalMilliseconds * 2);
    }
    
    [Fact]
    public void GetRetryDelay_RespectsMaxDelay()
    {
        // Arrange
        var maxDelay = TimeSpan.FromSeconds(5);
        var policy = new ExponentialBackoffRetryPolicy(
            baseDelay: TimeSpan.FromSeconds(2),
            maxDelay: maxDelay,
            jitterFactor: 0);
        
        // Act
        var delay10 = policy.GetRetryDelay(10); // Should hit max
        
        // Assert
        Assert.True(delay10 <= maxDelay);
    }
    
    [Fact]
    public void GetRetryDelay_WithJitter_VariesResults()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy(
            baseDelay: TimeSpan.FromSeconds(1),
            jitterFactor: 0.5);
        
        // Act
        var delays = new List<TimeSpan>();
        for (int i = 0; i < 10; i++)
        {
            delays.Add(policy.GetRetryDelay(1));
        }
        
        // Assert
        // With jitter, not all delays should be exactly the same
        var distinctDelays = delays.Distinct().Count();
        Assert.True(distinctDelays > 1);
    }
    
    [Fact]
    public void ShouldRetry_WithTaskCanceledException_ReturnsTrue()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy();
        var exception = new TaskCanceledException();
        
        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);
        
        // Assert
        Assert.True(shouldRetry);
    }
    
    [Fact]
    public void ShouldRetry_WithNestedTransientException_ReturnsTrue()
    {
        // Arrange
        var policy = new ExponentialBackoffRetryPolicy();
        var innerException = new TimeoutException();
        var exception = new InvalidOperationException("Outer", innerException);
        
        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);
        
        // Assert
        Assert.True(shouldRetry);
    }
}
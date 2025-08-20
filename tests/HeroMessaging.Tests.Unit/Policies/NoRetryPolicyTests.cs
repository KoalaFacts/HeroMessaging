using HeroMessaging.Core.Policies;

namespace HeroMessaging.Tests.Unit.Policies;

public class NoRetryPolicyTests
{
    private readonly NoRetryPolicy _policy = new();
    
    [Fact]
    public void ShouldRetry_AlwaysReturnsFalse()
    {
        // Arrange
        var exceptions = new Exception[]
        {
            new TimeoutException(),
            new ArgumentException(),
            new InvalidOperationException(),
            new OutOfMemoryException()
        };
        
        // Act & Assert
        foreach (var exception in exceptions)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                Assert.False(_policy.ShouldRetry(exception, attempt));
            }
        }
    }
    
    [Fact]
    public void ShouldRetry_WithNull_ReturnsFalse()
    {
        // Act
        var shouldRetry = _policy.ShouldRetry(null, 0);
        
        // Assert
        Assert.False(shouldRetry);
    }
    
    [Fact]
    public void GetRetryDelay_AlwaysReturnsZero()
    {
        // Act & Assert
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Assert.Equal(TimeSpan.Zero, _policy.GetRetryDelay(attempt));
        }
    }
    
    [Fact]
    public void MaxRetries_ReturnsZero()
    {
        // Assert
        Assert.Equal(0, _policy.MaxRetries);
    }
}
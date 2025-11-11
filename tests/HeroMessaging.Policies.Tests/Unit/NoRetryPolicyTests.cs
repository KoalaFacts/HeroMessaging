using HeroMessaging.Policies;
using Xunit;

namespace HeroMessaging.Tests.Unit.Policies;

/// <summary>
/// Unit tests for NoRetryPolicy
/// Tests that policy never retries
/// </summary>
[Trait("Category", "Unit")]
public sealed class NoRetryPolicyTests
{
    [Fact]
    public void MaxRetries_ReturnsZero()
    {
        // Arrange
        var policy = new NoRetryPolicy();

        // Act
        var maxRetries = policy.MaxRetries;

        // Assert
        Assert.Equal(0, maxRetries);
    }

    [Fact]
    public void ShouldRetry_WithException_ReturnsFalse()
    {
        // Arrange
        var policy = new NoRetryPolicy();
        var exception = new InvalidOperationException("Test");

        // Act
        var shouldRetry = policy.ShouldRetry(exception, 0);

        // Assert
        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_WithNullException_ReturnsFalse()
    {
        // Arrange
        var policy = new NoRetryPolicy();

        // Act
        var shouldRetry = policy.ShouldRetry(null, 0);

        // Assert
        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetry_WithAnyAttemptNumber_ReturnsFalse()
    {
        // Arrange
        var policy = new NoRetryPolicy();
        var exception = new TimeoutException("Test");

        // Act & Assert
        for (int i = 0; i < 10; i++)
        {
            Assert.False(policy.ShouldRetry(exception, i));
        }
    }

    [Fact]
    public void GetRetryDelay_ReturnsZero()
    {
        // Arrange
        var policy = new NoRetryPolicy();

        // Act
        var delay = policy.GetRetryDelay(0);

        // Assert
        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public void GetRetryDelay_WithAnyAttemptNumber_ReturnsZero()
    {
        // Arrange
        var policy = new NoRetryPolicy();

        // Act & Assert
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(TimeSpan.Zero, policy.GetRetryDelay(i));
        }
    }
}

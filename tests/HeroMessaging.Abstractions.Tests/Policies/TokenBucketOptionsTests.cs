using HeroMessaging.Abstractions.Policies;

namespace HeroMessaging.Abstractions.Tests.Policies;

[Trait("Category", "Unit")]
public class TokenBucketOptionsTests
{
    [Fact]
    public void Constructor_SetsDefaultValues()
    {
        // Arrange & Act
        var options = new TokenBucketOptions();

        // Assert
        Assert.Equal(100, options.Capacity);
        Assert.Equal(10.0, options.RefillRate);
        Assert.Equal(TimeSpan.FromMilliseconds(100), options.RefillPeriod);
        Assert.Equal(RateLimitBehavior.Queue, options.Behavior);
        Assert.Equal(TimeSpan.FromSeconds(30), options.MaxQueueWait);
        Assert.False(options.EnableScoping);
        Assert.Equal(1000, options.MaxScopedKeys);
    }

    [Fact]
    public void Capacity_CanBeSet()
    {
        // Arrange
        var options = new TokenBucketOptions();

        // Act
        options.Capacity = 500;

        // Assert
        Assert.Equal(500, options.Capacity);
    }

    [Fact]
    public void RefillRate_CanBeSet()
    {
        // Arrange
        var options = new TokenBucketOptions();

        // Act
        options.RefillRate = 50.5;

        // Assert
        Assert.Equal(50.5, options.RefillRate);
    }

    [Fact]
    public void RefillPeriod_CanBeSet()
    {
        // Arrange
        var options = new TokenBucketOptions();

        // Act
        options.RefillPeriod = TimeSpan.FromSeconds(1);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(1), options.RefillPeriod);
    }

    [Fact]
    public void Behavior_CanBeSetToReject()
    {
        // Arrange
        var options = new TokenBucketOptions();

        // Act
        options.Behavior = RateLimitBehavior.Reject;

        // Assert
        Assert.Equal(RateLimitBehavior.Reject, options.Behavior);
    }

    [Fact]
    public void MaxQueueWait_CanBeSet()
    {
        // Arrange
        var options = new TokenBucketOptions();

        // Act
        options.MaxQueueWait = TimeSpan.FromMinutes(5);

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(5), options.MaxQueueWait);
    }

    [Fact]
    public void EnableScoping_CanBeEnabled()
    {
        // Arrange
        var options = new TokenBucketOptions();

        // Act
        options.EnableScoping = true;

        // Assert
        Assert.True(options.EnableScoping);
    }

    [Fact]
    public void MaxScopedKeys_CanBeSet()
    {
        // Arrange
        var options = new TokenBucketOptions();

        // Act
        options.MaxScopedKeys = 5000;

        // Assert
        Assert.Equal(5000, options.MaxScopedKeys);
    }

    [Fact]
    public void Validate_WithValidOptions_DoesNotThrow()
    {
        // Arrange
        var options = new TokenBucketOptions
        {
            Capacity = 100,
            RefillRate = 10,
            RefillPeriod = TimeSpan.FromMilliseconds(100),
            MaxQueueWait = TimeSpan.FromSeconds(30),
            MaxScopedKeys = 1000
        };

        // Act & Assert
        options.Validate(); // Should not throw
    }

    [Fact]
    public void Validate_WithZeroCapacity_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var options = new TokenBucketOptions { Capacity = 0 };

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal("Capacity", exception.ParamName);
        Assert.Contains("must be positive", exception.Message);
    }

    [Fact]
    public void Validate_WithNegativeCapacity_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var options = new TokenBucketOptions { Capacity = -1 };

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal("Capacity", exception.ParamName);
    }

    [Fact]
    public void Validate_WithZeroRefillRate_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var options = new TokenBucketOptions { RefillRate = 0 };

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal("RefillRate", exception.ParamName);
        Assert.Contains("must be positive", exception.Message);
    }

    [Fact]
    public void Validate_WithNegativeRefillRate_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var options = new TokenBucketOptions { RefillRate = -1.0 };

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal("RefillRate", exception.ParamName);
    }

    [Fact]
    public void Validate_WithZeroRefillPeriod_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var options = new TokenBucketOptions { RefillPeriod = TimeSpan.Zero };

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal("RefillPeriod", exception.ParamName);
        Assert.Contains("must be positive", exception.Message);
    }

    [Fact]
    public void Validate_WithNegativeRefillPeriod_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var options = new TokenBucketOptions { RefillPeriod = TimeSpan.FromSeconds(-1) };

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal("RefillPeriod", exception.ParamName);
    }

    [Fact]
    public void Validate_WithZeroMaxQueueWait_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var options = new TokenBucketOptions { MaxQueueWait = TimeSpan.Zero };

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal("MaxQueueWait", exception.ParamName);
        Assert.Contains("must be positive", exception.Message);
    }

    [Fact]
    public void Validate_WithNegativeMaxQueueWait_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var options = new TokenBucketOptions { MaxQueueWait = TimeSpan.FromSeconds(-1) };

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal("MaxQueueWait", exception.ParamName);
    }

    [Fact]
    public void Validate_WithZeroMaxScopedKeys_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var options = new TokenBucketOptions { MaxScopedKeys = 0 };

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal("MaxScopedKeys", exception.ParamName);
        Assert.Contains("must be positive", exception.Message);
    }

    [Fact]
    public void Validate_WithNegativeMaxScopedKeys_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var options = new TokenBucketOptions { MaxScopedKeys = -1 };

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal("MaxScopedKeys", exception.ParamName);
    }

    [Fact]
    public void Validate_WithVeryLargeCapacity_DoesNotThrow()
    {
        // Arrange
        var options = new TokenBucketOptions { Capacity = long.MaxValue };

        // Act & Assert
        options.Validate();
    }

    [Fact]
    public void Validate_WithVeryHighRefillRate_DoesNotThrow()
    {
        // Arrange
        var options = new TokenBucketOptions { RefillRate = 1_000_000.0 };

        // Act & Assert
        options.Validate();
    }

    [Fact]
    public void Validate_WithVerySmallRefillRate_DoesNotThrow()
    {
        // Arrange
        var options = new TokenBucketOptions { RefillRate = 0.001 };

        // Act & Assert
        options.Validate();
    }

    [Fact]
    public void Validate_WithVeryLongMaxQueueWait_DoesNotThrow()
    {
        // Arrange
        var options = new TokenBucketOptions { MaxQueueWait = TimeSpan.FromDays(365) };

        // Act & Assert
        options.Validate();
    }

    [Fact]
    public void Validate_WithVeryShortRefillPeriod_DoesNotThrow()
    {
        // Arrange
        var options = new TokenBucketOptions { RefillPeriod = TimeSpan.FromMilliseconds(1) };

        // Act & Assert
        options.Validate();
    }

    [Fact]
    public void RateLimitBehavior_Reject_HasValueZero()
    {
        // Arrange & Act
        var behavior = RateLimitBehavior.Reject;

        // Assert
        Assert.Equal(0, (int)behavior);
    }

    [Fact]
    public void RateLimitBehavior_Queue_HasValueOne()
    {
        // Arrange & Act
        var behavior = RateLimitBehavior.Queue;

        // Assert
        Assert.Equal(1, (int)behavior);
    }

    [Fact]
    public void RateLimitBehavior_CanBeUsedInSwitch()
    {
        // Arrange & Act
        var rejectMessage = GetBehaviorMessage(RateLimitBehavior.Reject);
        var queueMessage = GetBehaviorMessage(RateLimitBehavior.Queue);

        // Assert
        Assert.Equal("Reject immediately", rejectMessage);
        Assert.Equal("Queue and wait", queueMessage);

        static string GetBehaviorMessage(RateLimitBehavior behavior) => behavior switch
        {
            RateLimitBehavior.Reject => "Reject immediately",
            RateLimitBehavior.Queue => "Queue and wait",
            _ => throw new ArgumentOutOfRangeException(nameof(behavior))
        };
    }

    [Fact]
    public void HighThroughputConfiguration_ValidatesCorrectly()
    {
        // Arrange
        var options = new TokenBucketOptions
        {
            Capacity = 10_000,
            RefillRate = 1_000,
            RefillPeriod = TimeSpan.FromMilliseconds(10),
            Behavior = RateLimitBehavior.Reject
        };

        // Act & Assert
        options.Validate();
        Assert.Equal(10_000, options.Capacity);
        Assert.Equal(1_000, options.RefillRate);
    }

    [Fact]
    public void LowThroughputConfiguration_ValidatesCorrectly()
    {
        // Arrange
        var options = new TokenBucketOptions
        {
            Capacity = 10,
            RefillRate = 1,
            RefillPeriod = TimeSpan.FromSeconds(1),
            Behavior = RateLimitBehavior.Queue
        };

        // Act & Assert
        options.Validate();
        Assert.Equal(10, options.Capacity);
        Assert.Equal(1, options.RefillRate);
    }
}

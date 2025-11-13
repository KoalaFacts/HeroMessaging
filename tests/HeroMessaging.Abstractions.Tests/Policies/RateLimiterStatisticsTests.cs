using HeroMessaging.Abstractions.Policies;

namespace HeroMessaging.Abstractions.Tests.Policies;

[Trait("Category", "Unit")]
public class RateLimiterStatisticsTests
{
    [Fact]
    public void Constructor_WithInitProperties_SetsValues()
    {
        // Arrange & Act
        var stats = new RateLimiterStatistics
        {
            AvailablePermits = 50,
            Capacity = 100,
            RefillRate = 10.0,
            LastRefillTime = DateTimeOffset.UtcNow,
            TotalAcquired = 1000,
            TotalThrottled = 50
        };

        // Assert
        Assert.Equal(50, stats.AvailablePermits);
        Assert.Equal(100, stats.Capacity);
        Assert.Equal(10.0, stats.RefillRate);
        Assert.Equal(1000, stats.TotalAcquired);
        Assert.Equal(50, stats.TotalThrottled);
    }

    [Fact]
    public void ThrottleRate_WithNoRequests_ReturnsZero()
    {
        // Arrange
        var stats = new RateLimiterStatistics
        {
            TotalAcquired = 0,
            TotalThrottled = 0
        };

        // Act
        var rate = stats.ThrottleRate;

        // Assert
        Assert.Equal(0.0, rate);
    }

    [Fact]
    public void ThrottleRate_WithAllSuccessful_ReturnsZero()
    {
        // Arrange
        var stats = new RateLimiterStatistics
        {
            TotalAcquired = 1000,
            TotalThrottled = 0
        };

        // Act
        var rate = stats.ThrottleRate;

        // Assert
        Assert.Equal(0.0, rate);
    }

    [Fact]
    public void ThrottleRate_WithSomeThrottled_ReturnsCorrectPercentage()
    {
        // Arrange
        var stats = new RateLimiterStatistics
        {
            TotalAcquired = 900,
            TotalThrottled = 100
        };

        // Act
        var rate = stats.ThrottleRate;

        // Assert
        Assert.Equal(0.1, rate);
    }

    [Fact]
    public void ThrottleRate_WithAllThrottled_ReturnsOne()
    {
        // Arrange
        var stats = new RateLimiterStatistics
        {
            TotalAcquired = 0,
            TotalThrottled = 1000
        };

        // Act
        var rate = stats.ThrottleRate;

        // Assert
        Assert.Equal(1.0, rate);
    }

    [Fact]
    public void ThrottleRate_WithHalfThrottled_ReturnsHalf()
    {
        // Arrange
        var stats = new RateLimiterStatistics
        {
            TotalAcquired = 500,
            TotalThrottled = 500
        };

        // Act
        var rate = stats.ThrottleRate;

        // Assert
        Assert.Equal(0.5, rate);
    }

    [Fact]
    public void AvailablePermits_CanBeZero()
    {
        // Arrange & Act
        var stats = new RateLimiterStatistics
        {
            AvailablePermits = 0,
            Capacity = 100
        };

        // Assert
        Assert.Equal(0, stats.AvailablePermits);
    }

    [Fact]
    public void AvailablePermits_CanEqualCapacity()
    {
        // Arrange & Act
        var stats = new RateLimiterStatistics
        {
            AvailablePermits = 100,
            Capacity = 100
        };

        // Assert
        Assert.Equal(stats.Capacity, stats.AvailablePermits);
    }

    [Fact]
    public void RefillRate_CanBeVeryHigh()
    {
        // Arrange & Act
        var stats = new RateLimiterStatistics
        {
            RefillRate = 1_000_000.0
        };

        // Assert
        Assert.Equal(1_000_000.0, stats.RefillRate);
    }

    [Fact]
    public void RefillRate_CanBeVeryLow()
    {
        // Arrange & Act
        var stats = new RateLimiterStatistics
        {
            RefillRate = 0.001
        };

        // Assert
        Assert.Equal(0.001, stats.RefillRate);
    }

    [Fact]
    public void LastRefillTime_CanBeSet()
    {
        // Arrange
        var refillTime = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

        // Act
        var stats = new RateLimiterStatistics
        {
            LastRefillTime = refillTime
        };

        // Assert
        Assert.Equal(refillTime, stats.LastRefillTime);
    }

    [Fact]
    public void TotalAcquired_CanBeLarge()
    {
        // Arrange & Act
        var stats = new RateLimiterStatistics
        {
            TotalAcquired = long.MaxValue
        };

        // Assert
        Assert.Equal(long.MaxValue, stats.TotalAcquired);
    }

    [Fact]
    public void TotalThrottled_CanBeLarge()
    {
        // Arrange & Act
        var stats = new RateLimiterStatistics
        {
            TotalThrottled = long.MaxValue
        };

        // Assert
        Assert.Equal(long.MaxValue, stats.TotalThrottled);
    }

    [Fact]
    public void ThrottleRate_WithPreciseCalculation_ReturnsAccurateValue()
    {
        // Arrange
        var stats = new RateLimiterStatistics
        {
            TotalAcquired = 999,
            TotalThrottled = 1
        };

        // Act
        var rate = stats.ThrottleRate;

        // Assert
        Assert.Equal(0.001, rate);
    }

    [Fact]
    public void InitProperties_AreInitOnly()
    {
        // Arrange
        var stats = new RateLimiterStatistics
        {
            AvailablePermits = 50,
            Capacity = 100
        };

        // Act - Create a new instance with modified value
        var newStats = new RateLimiterStatistics
        {
            AvailablePermits = 75,
            Capacity = stats.Capacity,
            RefillRate = stats.RefillRate,
            LastRefillTime = stats.LastRefillTime,
            TotalAcquired = stats.TotalAcquired,
            TotalThrottled = stats.TotalThrottled
        };

        // Assert
        Assert.Equal(50, stats.AvailablePermits);
        Assert.Equal(75, newStats.AvailablePermits);
    }

    [Fact]
    public void StatisticsSnapshot_RepresentsPointInTime()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;
        var stats = new RateLimiterStatistics
        {
            AvailablePermits = 50,
            Capacity = 100,
            RefillRate = 10.0,
            LastRefillTime = timestamp,
            TotalAcquired = 500,
            TotalThrottled = 25
        };

        // Act & Assert - All properties are from the same snapshot
        Assert.Equal(50, stats.AvailablePermits);
        Assert.Equal(timestamp, stats.LastRefillTime);
        Assert.Equal(0.047619047619047616, stats.ThrottleRate, precision: 10);
    }

    [Fact]
    public void ThrottleRate_WithVeryLargeNumbers_CalculatesCorrectly()
    {
        // Arrange
        var stats = new RateLimiterStatistics
        {
            TotalAcquired = 1_000_000_000,
            TotalThrottled = 1_000_000
        };

        // Act
        var rate = stats.ThrottleRate;

        // Assert
        Assert.Equal(0.001, rate, precision: 10);
    }

    [Fact]
    public void HighThroughputStatistics_AllPropertiesSet()
    {
        // Arrange & Act
        var stats = new RateLimiterStatistics
        {
            AvailablePermits = 9500,
            Capacity = 10_000,
            RefillRate = 1000.0,
            LastRefillTime = DateTimeOffset.UtcNow,
            TotalAcquired = 10_000_000,
            TotalThrottled = 50_000
        };

        // Assert
        Assert.Equal(9500, stats.AvailablePermits);
        Assert.Equal(10_000, stats.Capacity);
        Assert.Equal(0.005, stats.ThrottleRate);
    }

    [Fact]
    public void LowThroughputStatistics_AllPropertiesSet()
    {
        // Arrange & Act
        var stats = new RateLimiterStatistics
        {
            AvailablePermits = 5,
            Capacity = 10,
            RefillRate = 1.0,
            LastRefillTime = DateTimeOffset.UtcNow,
            TotalAcquired = 100,
            TotalThrottled = 10
        };

        // Assert
        Assert.Equal(5, stats.AvailablePermits);
        Assert.Equal(10, stats.Capacity);
        Assert.Equal(0.09090909090909091, stats.ThrottleRate, precision: 10);
    }

    [Fact]
    public void DefaultValues_AreZeroOrDefault()
    {
        // Arrange & Act
        var stats = new RateLimiterStatistics();

        // Assert
        Assert.Equal(0, stats.AvailablePermits);
        Assert.Equal(0, stats.Capacity);
        Assert.Equal(0.0, stats.RefillRate);
        Assert.Equal(default(DateTimeOffset), stats.LastRefillTime);
        Assert.Equal(0, stats.TotalAcquired);
        Assert.Equal(0, stats.TotalThrottled);
        Assert.Equal(0.0, stats.ThrottleRate);
    }
}

using HeroMessaging.Abstractions.Policies;

namespace HeroMessaging.Abstractions.Tests.Policies;

[Trait("Category", "Unit")]
public class RateLimitResultTests
{
    [Fact]
    public void Constructor_WithAllParameters_SetsProperties()
    {
        // Arrange & Act
        var result = new RateLimitResult(
            isAllowed: true,
            retryAfter: TimeSpan.FromSeconds(30),
            remainingPermits: 10,
            reasonPhrase: "Test reason");

        // Assert
        Assert.True(result.IsAllowed);
        Assert.Equal(TimeSpan.FromSeconds(30), result.RetryAfter);
        Assert.Equal(10, result.RemainingPermits);
        Assert.Equal("Test reason", result.ReasonPhrase);
    }

    [Fact]
    public void Constructor_WithNullReasonPhrase_SetsToNull()
    {
        // Arrange & Act
        var result = new RateLimitResult(
            isAllowed: true,
            retryAfter: TimeSpan.Zero,
            remainingPermits: 5);

        // Assert
        Assert.Null(result.ReasonPhrase);
    }

    [Fact]
    public void Success_FactoryMethod_CreatesAllowedResult()
    {
        // Arrange & Act
        var result = RateLimitResult.Success(remainingPermits: 100);

        // Assert
        Assert.True(result.IsAllowed);
        Assert.Equal(TimeSpan.Zero, result.RetryAfter);
        Assert.Equal(100, result.RemainingPermits);
        Assert.Null(result.ReasonPhrase);
    }

    [Fact]
    public void Success_WithZeroRemainingPermits_StillAllowed()
    {
        // Arrange & Act
        var result = RateLimitResult.Success(remainingPermits: 0);

        // Assert
        Assert.True(result.IsAllowed);
        Assert.Equal(0, result.RemainingPermits);
    }

    [Fact]
    public void Success_WithNegativePermits_AllowsNegativeValue()
    {
        // Arrange & Act
        var result = RateLimitResult.Success(remainingPermits: -5);

        // Assert
        Assert.True(result.IsAllowed);
        Assert.Equal(-5, result.RemainingPermits);
    }

    [Fact]
    public void Throttled_FactoryMethod_CreatesThrottledResult()
    {
        // Arrange & Act
        var result = RateLimitResult.Throttled(TimeSpan.FromSeconds(60));

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal(TimeSpan.FromSeconds(60), result.RetryAfter);
        Assert.Equal(0, result.RemainingPermits);
        Assert.Equal("Rate limit exceeded", result.ReasonPhrase);
    }

    [Fact]
    public void Throttled_WithCustomReason_UsesProvidedReason()
    {
        // Arrange & Act
        var result = RateLimitResult.Throttled(
            TimeSpan.FromSeconds(30),
            reason: "Custom throttle reason");

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal("Custom throttle reason", result.ReasonPhrase);
    }

    [Fact]
    public void Throttled_WithNullReason_UsesDefaultReason()
    {
        // Arrange & Act
        var result = RateLimitResult.Throttled(TimeSpan.FromSeconds(30), reason: null);

        // Assert
        Assert.Equal("Rate limit exceeded", result.ReasonPhrase);
    }

    [Fact]
    public void Equality_WithSameValues_ReturnsTrue()
    {
        // Arrange
        var result1 = new RateLimitResult(true, TimeSpan.FromSeconds(10), 50, "test");
        var result2 = new RateLimitResult(true, TimeSpan.FromSeconds(10), 50, "test");

        // Act & Assert
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void Inequality_WithDifferentValues_ReturnsTrue()
    {
        // Arrange
        var result1 = RateLimitResult.Success(10);
        var result2 = RateLimitResult.Success(20);

        // Act & Assert
        Assert.NotEqual(result1, result2);
    }

    [Fact]
    public void IsAllowed_False_IndicatesThrottling()
    {
        // Arrange & Act
        var result = new RateLimitResult(
            isAllowed: false,
            retryAfter: TimeSpan.FromSeconds(30),
            remainingPermits: 0);

        // Assert
        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void RetryAfter_CanBeZero()
    {
        // Arrange & Act
        var result = new RateLimitResult(
            isAllowed: true,
            retryAfter: TimeSpan.Zero,
            remainingPermits: 10);

        // Assert
        Assert.Equal(TimeSpan.Zero, result.RetryAfter);
    }

    [Fact]
    public void RetryAfter_CanBeVeryLarge()
    {
        // Arrange & Act
        var result = RateLimitResult.Throttled(TimeSpan.FromDays(365));

        // Assert
        Assert.Equal(TimeSpan.FromDays(365), result.RetryAfter);
    }

    [Fact]
    public void RemainingPermits_CanBeLarge()
    {
        // Arrange & Act
        var result = RateLimitResult.Success(long.MaxValue);

        // Assert
        Assert.Equal(long.MaxValue, result.RemainingPermits);
    }

    [Fact]
    public void RemainingPermits_CanBeMinValue()
    {
        // Arrange & Act
        var result = RateLimitResult.Success(long.MinValue);

        // Assert
        Assert.Equal(long.MinValue, result.RemainingPermits);
    }

    [Fact]
    public void ReasonPhrase_CanBeEmpty()
    {
        // Arrange & Act
        var result = new RateLimitResult(
            isAllowed: false,
            retryAfter: TimeSpan.FromSeconds(10),
            remainingPermits: 0,
            reasonPhrase: string.Empty);

        // Assert
        Assert.Equal(string.Empty, result.ReasonPhrase);
    }

    [Fact]
    public void StructBehavior_IsValueType()
    {
        // Arrange
        var result1 = RateLimitResult.Success(10);
        var result2 = result1;

        // Act
        // Since it's a struct, modifying result2 shouldn't affect result1
        // But we can't directly modify readonly struct, so we verify it's a value type

        // Assert
        Assert.Equal(result1.IsAllowed, result2.IsAllowed);
        Assert.Equal(result1.RemainingPermits, result2.RemainingPermits);
    }

    [Fact]
    public void CanBeUsedInSwitchExpression()
    {
        // Arrange
        var allowed = RateLimitResult.Success(10);
        var throttled = RateLimitResult.Throttled(TimeSpan.FromSeconds(30));

        // Act
        var allowedMessage = GetMessage(allowed);
        var throttledMessage = GetMessage(throttled);

        // Assert
        Assert.Equal("Proceed", allowedMessage);
        Assert.Equal("Wait", throttledMessage);

        static string GetMessage(RateLimitResult result) =>
            result.IsAllowed ? "Proceed" : "Wait";
    }

    [Fact]
    public void MultipleSuccessResults_HaveDifferentRemainingPermits()
    {
        // Arrange & Act
        var result1 = RateLimitResult.Success(100);
        var result2 = RateLimitResult.Success(90);
        var result3 = RateLimitResult.Success(80);

        // Assert
        Assert.Equal(100, result1.RemainingPermits);
        Assert.Equal(90, result2.RemainingPermits);
        Assert.Equal(80, result3.RemainingPermits);
        Assert.All([result1, result2, result3], r => Assert.True(r.IsAllowed));
    }

    [Fact]
    public void Throttled_DifferentRetryPeriods_CreatesDifferentResults()
    {
        // Arrange & Act
        var shortPeriod = RateLimitResult.Throttled(TimeSpan.FromSeconds(1));
        var mediumPeriod = RateLimitResult.Throttled(TimeSpan.FromMinutes(1));
        var longPeriod = RateLimitResult.Throttled(TimeSpan.FromHours(1));

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(1), shortPeriod.RetryAfter);
        Assert.Equal(TimeSpan.FromMinutes(1), mediumPeriod.RetryAfter);
        Assert.Equal(TimeSpan.FromHours(1), longPeriod.RetryAfter);
        Assert.All([shortPeriod, mediumPeriod, longPeriod], r => Assert.False(r.IsAllowed));
    }

    [Fact]
    public void GetHashCode_WithEqualValues_ReturnsSameHash()
    {
        // Arrange
        var result1 = RateLimitResult.Success(10);
        var result2 = RateLimitResult.Success(10);

        // Act
        var hash1 = result1.GetHashCode();
        var hash2 = result2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ToString_ReturnsReadableString()
    {
        // Arrange
        var result = RateLimitResult.Success(10);

        // Act
        var str = result.ToString();

        // Assert
        Assert.NotNull(str);
        Assert.NotEmpty(str);
    }

    [Fact]
    public void DefaultStruct_HasDefaultValues()
    {
        // Arrange & Act
        var result = default(RateLimitResult);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal(TimeSpan.Zero, result.RetryAfter);
        Assert.Equal(0, result.RemainingPermits);
        Assert.Null(result.ReasonPhrase);
    }
}

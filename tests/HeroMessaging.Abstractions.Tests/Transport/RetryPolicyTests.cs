using HeroMessaging.Abstractions.Transport;

namespace HeroMessaging.Abstractions.Tests.Transport;

[Trait("Category", "Unit")]
public class RetryPolicyTests
{
    public class Delays
    {
        [Fact]
        public void ExponentialDelayIsClampedAndInvalidAttemptHasNoDelay()
        {
            var policy = RetryPolicy.Exponential(4, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));

            Assert.Equal(TimeSpan.Zero, policy.CalculateDelay(0));
            Assert.Equal(TimeSpan.FromSeconds(2), policy.CalculateDelay(1));
            Assert.Equal(TimeSpan.FromSeconds(4), policy.CalculateDelay(2));
            Assert.Equal(TimeSpan.FromSeconds(5), policy.CalculateDelay(3));
        }

        [Theory]
        [InlineData(0, 1025, 0)]
        [InlineData(0, int.MaxValue, 0)]
        [InlineData(100, 1025, 60_000)]
        [InlineData(100, int.MaxValue, 60_000)]
        public void CalculateDelay_VeryHighAttempt_StaysFiniteAndClamped(int initialMs, int attempt, int expectedMs)
        {
            var policy = new RetryPolicy
            {
                InitialDelay = TimeSpan.FromMilliseconds(initialMs),
                MaxDelay = TimeSpan.FromMinutes(1),
                UseExponentialBackoff = true
            };

            Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), policy.CalculateDelay(attempt));
        }

        [Fact]
        public void LinearDelayDoesNotGrow()
        {
            var policy = RetryPolicy.Linear(2, TimeSpan.FromMilliseconds(250));

            Assert.False(policy.UseExponentialBackoff);
            Assert.Equal(TimeSpan.FromMilliseconds(250), policy.CalculateDelay(1));
            Assert.Equal(TimeSpan.FromMilliseconds(250), policy.CalculateDelay(20));
            Assert.True(policy.ShouldRetry(2));
            Assert.False(policy.ShouldRetry(3));
        }
    }

    public class Presets
    {
        [Fact]
        public void NoneAndInfiniteHaveDistinctRetryBehavior()
        {
            Assert.False(RetryPolicy.None.ShouldRetry(1));
            Assert.True(RetryPolicy.Infinite.ShouldRetry(1000));
            Assert.Equal(-1, RetryPolicy.Infinite.MaxAttempts);
        }

        [Fact]
        public void AggressiveAndConservativeExposeTheirIntendedLimits()
        {
            Assert.Equal(10, RetryPolicy.Aggressive.MaxAttempts);
            Assert.Equal(TimeSpan.FromSeconds(30), RetryPolicy.Aggressive.MaxDelay);
            Assert.Equal(3, RetryPolicy.Conservative.MaxAttempts);
            Assert.Equal(TimeSpan.FromMinutes(5), RetryPolicy.Conservative.MaxDelay);
        }
    }
}

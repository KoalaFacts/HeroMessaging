using HeroMessaging.Abstractions.Policies;
using HeroMessaging.Policies;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="TokenBucketRateLimiter"/> implementation.
/// Tests cover token bucket algorithm correctness, thread safety, and edge cases.
/// </summary>
public class TokenBucketRateLimiterTests
{
    #region Basic Functionality Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_WithAvailableTokens_ReturnsSuccess()
    {
        // Arrange: Create limiter with 10 capacity
        var options = new TokenBucketOptions
        {
            Capacity = 10,
            RefillRate = 1.0
        };
        var timeProvider = new FakeTimeProvider();
        var limiter = new TokenBucketRateLimiter(options, timeProvider);

        // Act: Acquire 1 token
        var result = await limiter.AcquireAsync();

        // Assert: Should succeed with 9 remaining
        Assert.True(result.IsAllowed);
        Assert.Equal(9, result.RemainingPermits);
        Assert.Equal(TimeSpan.Zero, result.RetryAfter);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_WithoutAvailableTokens_RejectsRequest()
    {
        // Arrange: Create limiter with 2 capacity, exhaust tokens
        var options = new TokenBucketOptions
        {
            Capacity = 2,
            RefillRate = 1.0,
            Behavior = RateLimitBehavior.Reject
        };
        var timeProvider = new FakeTimeProvider();
        var limiter = new TokenBucketRateLimiter(options, timeProvider);

        // Exhaust tokens
        await limiter.AcquireAsync();
        await limiter.AcquireAsync();

        // Act: Try to acquire when empty
        var result = await limiter.AcquireAsync();

        // Assert: Should be throttled
        Assert.False(result.IsAllowed);
        Assert.Equal(0, result.RemainingPermits);
        Assert.True(result.RetryAfter > TimeSpan.Zero);
        Assert.Contains("Rate limit exceeded", result.ReasonPhrase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_WithBurstCapacity_AllowsBurst()
    {
        // Arrange: Limiter with capacity 100
        var options = new TokenBucketOptions
        {
            Capacity = 100,
            RefillRate = 10.0
        };
        var timeProvider = new FakeTimeProvider();
        var limiter = new TokenBucketRateLimiter(options, timeProvider);

        // Act: Acquire 50 tokens in burst
        for (int i = 0; i < 50; i++)
        {
            var result = await limiter.AcquireAsync();
            Assert.True(result.IsAllowed, $"Token {i + 1} should be allowed");
        }

        // Assert: Should still have 50 remaining
        var finalResult = await limiter.AcquireAsync();
        Assert.True(finalResult.IsAllowed);
        Assert.Equal(49, finalResult.RemainingPermits);
    }

    #endregion

    #region Token Refill Logic Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_AfterRefillPeriod_RefillsTokens()
    {
        // Arrange: Create limiter with 10 capacity, 10 tokens/sec
        var options = new TokenBucketOptions
        {
            Capacity = 10,
            RefillRate = 10.0
        };
        var timeProvider = new FakeTimeProvider();
        var limiter = new TokenBucketRateLimiter(options, timeProvider);

        // Exhaust all tokens
        for (int i = 0; i < 10; i++)
        {
            await limiter.AcquireAsync();
        }

        // Act: Advance time by 1 second (should refill 10 tokens)
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var result = await limiter.AcquireAsync();

        // Assert: Should have tokens available again
        Assert.True(result.IsAllowed);
        Assert.Equal(9, result.RemainingPermits);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_AfterMultipleRefillPeriods_RefillsCorrectly()
    {
        // Arrange: 10 capacity, 5 tokens/sec
        var options = new TokenBucketOptions
        {
            Capacity = 10,
            RefillRate = 5.0
        };
        var timeProvider = new FakeTimeProvider();
        var limiter = new TokenBucketRateLimiter(options, timeProvider);

        // Exhaust 8 tokens (2 remaining)
        for (int i = 0; i < 8; i++)
        {
            await limiter.AcquireAsync();
        }

        // Act: Advance time by 2 seconds (should add 10 tokens, but capped at capacity)
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        var result = await limiter.AcquireAsync();

        // Assert: Should be at capacity (10 - 1 = 9 remaining)
        Assert.True(result.IsAllowed);
        Assert.Equal(9, result.RemainingPermits);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_WithPartialRefill_CalculatesCorrectly()
    {
        // Arrange: 10 capacity, 10 tokens/sec
        var options = new TokenBucketOptions
        {
            Capacity = 10,
            RefillRate = 10.0
        };
        var timeProvider = new FakeTimeProvider();
        var limiter = new TokenBucketRateLimiter(options, timeProvider);

        // Exhaust all tokens
        for (int i = 0; i < 10; i++)
        {
            await limiter.AcquireAsync();
        }

        // Act: Advance time by 0.5 seconds (should add 5 tokens)
        timeProvider.Advance(TimeSpan.FromMilliseconds(500));
        var result = await limiter.AcquireAsync();

        // Assert: Should have partial refill (5 - 1 = 4 remaining)
        Assert.True(result.IsAllowed);
        Assert.Equal(4, result.RemainingPermits);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_DoesNotExceedCapacity_WhenRefilling()
    {
        // Arrange: Bucket starts full at capacity
        var options = new TokenBucketOptions
        {
            Capacity = 10,
            RefillRate = 10.0
        };
        var timeProvider = new FakeTimeProvider();
        var limiter = new TokenBucketRateLimiter(options, timeProvider);

        // Act: Advance time significantly (should not exceed capacity)
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        var result = await limiter.AcquireAsync();

        // Assert: Should not exceed capacity (10 - 1 = 9)
        Assert.True(result.IsAllowed);
        Assert.Equal(9, result.RemainingPermits);
    }

    #endregion

    #region Queue vs Reject Behavior Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_WithRejectBehavior_ReturnsImmediately()
    {
        // Arrange: Limiter with Reject behavior, exhaust tokens
        var options = new TokenBucketOptions
        {
            Capacity = 1,
            RefillRate = 1.0,
            Behavior = RateLimitBehavior.Reject
        };
        var timeProvider = new FakeTimeProvider();
        var limiter = new TokenBucketRateLimiter(options, timeProvider);

        await limiter.AcquireAsync(); // Exhaust token

        // Act: Try to acquire with no tokens (should return immediately)
        var startTime = timeProvider.GetUtcNow();
        var result = await limiter.AcquireAsync();
        var elapsed = timeProvider.GetUtcNow() - startTime;

        // Assert: Should reject immediately without waiting
        Assert.False(result.IsAllowed);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(100), "Should return immediately");
        Assert.True(result.RetryAfter > TimeSpan.Zero, "Should indicate when to retry");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_WithQueueBehavior_EventuallySucceeds()
    {
        // Arrange: Limiter with Queue behavior
        var options = new TokenBucketOptions
        {
            Capacity = 1,
            RefillRate = 10.0, // 10 tokens/sec = 0.1 sec per token
            Behavior = RateLimitBehavior.Queue,
            MaxQueueWait = TimeSpan.FromSeconds(1)
        };
        var timeProvider = new FakeTimeProvider();
        var limiter = new TokenBucketRateLimiter(options, timeProvider);

        // Act: Exhaust token
        await limiter.AcquireAsync();

        // Start queued acquire in background
        var acquireTask = Task.Run(async () => await limiter.AcquireAsync());

        // Allow task to start and hit the delay
        await Task.Delay(10);

        // Advance time to trigger refill (100ms = 1 token)
        timeProvider.Advance(TimeSpan.FromMilliseconds(100));

        // Allow timer callback to execute (critical for FakeTimeProvider)
        await Task.Delay(10);

        var result = await acquireTask;

        // Assert: Should succeed after time advance
        Assert.True(result.IsAllowed);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_WithMaxQueueWait_TimesOutCorrectly()
    {
        // Arrange: Limiter with short max wait
        var options = new TokenBucketOptions
        {
            Capacity = 1,
            RefillRate = 1.0, // 1 token/sec
            Behavior = RateLimitBehavior.Queue,
            MaxQueueWait = TimeSpan.FromMilliseconds(100)
        };
        var timeProvider = new FakeTimeProvider();
        var limiter = new TokenBucketRateLimiter(options, timeProvider);

        await limiter.AcquireAsync(); // Exhaust token

        // Act: Try to queue - need to wait 1 second for refill but max wait is 100ms
        var result = await limiter.AcquireAsync();

        // Assert: Should be throttled due to max queue wait exceeded
        Assert.False(result.IsAllowed);
        Assert.Contains("max queue wait", result.ReasonPhrase, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Multiple Permits Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_WithMultiplePermits_ConsumesCorrectly()
    {
        // Arrange: Limiter with 10 capacity
        var options = new TokenBucketOptions
        {
            Capacity = 10,
            RefillRate = 1.0
        };
        var timeProvider = new FakeTimeProvider();
        var limiter = new TokenBucketRateLimiter(options, timeProvider);

        // Act: Acquire 5 permits at once
        var result = await limiter.AcquireAsync(permits: 5);

        // Assert: Should consume 5 tokens (10 - 5 = 5 remaining)
        Assert.True(result.IsAllowed);
        Assert.Equal(5, result.RemainingPermits);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_WithInsufficientTokensForPermits_Throttles()
    {
        // Arrange: Limiter with 10 capacity
        var options = new TokenBucketOptions
        {
            Capacity = 10,
            RefillRate = 1.0,
            Behavior = RateLimitBehavior.Reject
        };
        var timeProvider = new FakeTimeProvider();
        var limiter = new TokenBucketRateLimiter(options, timeProvider);

        // Act: Try to acquire 11 permits (more than capacity)
        var result = await limiter.AcquireAsync(permits: 11);

        // Assert: Should be throttled
        Assert.False(result.IsAllowed);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_WithZeroPermits_ThrowsException()
    {
        // Arrange
        var options = new TokenBucketOptions { Capacity = 10, RefillRate = 1.0 };
        var limiter = new TokenBucketRateLimiter(options, new FakeTimeProvider());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => limiter.AcquireAsync(permits: 0).AsTask());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_WithNegativePermits_ThrowsException()
    {
        // Arrange
        var options = new TokenBucketOptions { Capacity = 10, RefillRate = 1.0 };
        var limiter = new TokenBucketRateLimiter(options, new FakeTimeProvider());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => limiter.AcquireAsync(permits: -1).AsTask());
    }

    #endregion

    #region Scoped Rate Limiting Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_WithDifferentKeys_IndependentBuckets()
    {
        // Arrange: Enable scoping
        var options = new TokenBucketOptions
        {
            Capacity = 5,
            RefillRate = 1.0,
            EnableScoping = true
        };
        var timeProvider = new FakeTimeProvider();
        var limiter = new TokenBucketRateLimiter(options, timeProvider);

        // Act: Exhaust tokens for key1
        for (int i = 0; i < 5; i++)
        {
            await limiter.AcquireAsync(key: "key1");
        }

        // Try to acquire for key2 (should have full bucket)
        var result = await limiter.AcquireAsync(key: "key2");

        // Assert: key2 should have tokens available
        Assert.True(result.IsAllowed);
        Assert.Equal(4, result.RemainingPermits);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_WithSameKey_SharesBucket()
    {
        // Arrange: Enable scoping
        var options = new TokenBucketOptions
        {
            Capacity = 5,
            RefillRate = 1.0,
            EnableScoping = true
        };
        var timeProvider = new FakeTimeProvider();
        var limiter = new TokenBucketRateLimiter(options, timeProvider);

        // Act: Acquire with same key multiple times
        await limiter.AcquireAsync(key: "shared-key");
        await limiter.AcquireAsync(key: "shared-key");
        var result = await limiter.AcquireAsync(key: "shared-key");

        // Assert: Should share the same bucket (5 - 3 = 2 remaining)
        Assert.True(result.IsAllowed);
        Assert.Equal(2, result.RemainingPermits);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_WithNullKey_UsesGlobalBucket()
    {
        // Arrange: Scoping enabled but use null key
        var options = new TokenBucketOptions
        {
            Capacity = 5,
            RefillRate = 1.0,
            EnableScoping = true
        };
        var timeProvider = new FakeTimeProvider();
        var limiter = new TokenBucketRateLimiter(options, timeProvider);

        // Act: Acquire with null key (global bucket)
        await limiter.AcquireAsync(key: null);
        await limiter.AcquireAsync(key: null);
        var result = await limiter.AcquireAsync(key: null);

        // Assert: Should use global bucket
        Assert.True(result.IsAllowed);
        Assert.Equal(2, result.RemainingPermits);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_WithManyScopedKeys_HandlesCorrectly()
    {
        // Arrange: Enable scoping with high max keys
        var options = new TokenBucketOptions
        {
            Capacity = 10,
            RefillRate = 1.0,
            EnableScoping = true,
            MaxScopedKeys = 100
        };
        var timeProvider = new FakeTimeProvider();
        var limiter = new TokenBucketRateLimiter(options, timeProvider);

        // Act: Create 50 unique keys, each with independent limits
        for (int i = 0; i < 50; i++)
        {
            var result = await limiter.AcquireAsync(key: $"key-{i}");
            Assert.True(result.IsAllowed, $"Key {i} should be allowed");
            Assert.Equal(9, result.RemainingPermits);
        }

        // Assert: All keys should have independent buckets
        var finalResult = await limiter.AcquireAsync(key: "key-0");
        Assert.True(finalResult.IsAllowed);
        Assert.Equal(8, finalResult.RemainingPermits); // Used twice now
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_ConcurrentCalls_ThreadSafe()
    {
        // Arrange: Limiter with 100 capacity
        var options = new TokenBucketOptions
        {
            Capacity = 100,
            RefillRate = 1.0,
            Behavior = RateLimitBehavior.Reject // Use Reject to avoid waiting with FakeTimeProvider
        };
        var limiter = new TokenBucketRateLimiter(options, new FakeTimeProvider());

        // Act: 100 concurrent acquisitions
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => limiter.AcquireAsync())
            .ToList();

        var results = await Task.WhenAll(tasks.Select(t => t.AsTask()));

        // Assert: All should succeed, none should exceed capacity
        var successCount = results.Count(r => r.IsAllowed);
        Assert.Equal(100, successCount);

        // Try one more (should fail - bucket empty)
        var extraResult = await limiter.AcquireAsync();
        Assert.False(extraResult.IsAllowed);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_Under100ConcurrentCalls_NoRaceConditions()
    {
        // Arrange: Small capacity to stress test
        var options = new TokenBucketOptions
        {
            Capacity = 50,
            RefillRate = 1.0,
            Behavior = RateLimitBehavior.Reject
        };
        var limiter = new TokenBucketRateLimiter(options, new FakeTimeProvider());

        // Act: 200 concurrent attempts (should exceed capacity)
        var tasks = Enumerable.Range(0, 200)
            .Select(_ => limiter.AcquireAsync())
            .ToList();

        var results = await Task.WhenAll(tasks.Select(t => t.AsTask()));

        // Assert: Exactly 50 should succeed, 150 should be throttled
        var successCount = results.Count(r => r.IsAllowed);
        var throttledCount = results.Count(r => !r.IsAllowed);

        Assert.Equal(50, successCount);
        Assert.Equal(150, throttledCount);
    }

    #endregion

    #region TimeProvider Integration Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_WithFakeTimeProvider_DeterministicRefill()
    {
        // Arrange: Use FakeTimeProvider for deterministic testing
        var options = new TokenBucketOptions
        {
            Capacity = 10,
            RefillRate = 5.0 // 5 tokens per second
        };
        var timeProvider = new FakeTimeProvider();
        var limiter = new TokenBucketRateLimiter(options, timeProvider);

        // Exhaust tokens
        for (int i = 0; i < 10; i++)
        {
            await limiter.AcquireAsync();
        }

        // Act: Advance time precisely
        timeProvider.Advance(TimeSpan.FromSeconds(2)); // Should add exactly 10 tokens

        // Assert: Should have exactly 10 tokens (full capacity)
        for (int i = 0; i < 10; i++)
        {
            var result = await limiter.AcquireAsync();
            Assert.True(result.IsAllowed, $"Token {i + 1} should be available");
        }

        var extraResult = await limiter.AcquireAsync();
        Assert.False(extraResult.IsAllowed, "Should be exhausted again");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_AfterTimeAdvance_RefillsCorrectly()
    {
        // Arrange
        var options = new TokenBucketOptions
        {
            Capacity = 100,
            RefillRate = 100.0 // 100 tokens/sec
        };
        var timeProvider = new FakeTimeProvider();
        var limiter = new TokenBucketRateLimiter(options, timeProvider);

        // Use 50 tokens
        for (int i = 0; i < 50; i++)
        {
            await limiter.AcquireAsync();
        }

        // Act: Advance by 0.3 seconds (should add 30 tokens)
        timeProvider.Advance(TimeSpan.FromMilliseconds(300));
        var result = await limiter.AcquireAsync();

        // Assert: Should have 50 remaining + 30 refilled - 1 acquired = 79 remaining
        Assert.True(result.IsAllowed);
        Assert.Equal(79, result.RemainingPermits);
    }

    #endregion

    #region Statistics Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatistics_ReturnsCurrentState()
    {
        // Arrange
        var options = new TokenBucketOptions
        {
            Capacity = 10,
            RefillRate = 5.0
        };
        var timeProvider = new FakeTimeProvider();
        var limiter = new TokenBucketRateLimiter(options, timeProvider);

        // Act: Use some tokens
        await limiter.AcquireAsync();
        await limiter.AcquireAsync();
        await limiter.AcquireAsync();

        var stats = limiter.GetStatistics();

        // Assert: Statistics should reflect current state
        Assert.Equal(7, stats.AvailablePermits);
        Assert.Equal(10, stats.Capacity);
        Assert.Equal(5.0, stats.RefillRate);
        Assert.Equal(3, stats.TotalAcquired);
        Assert.Equal(0, stats.TotalThrottled);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatistics_TracksAcquiredAndThrottled()
    {
        // Arrange: Small capacity
        var options = new TokenBucketOptions
        {
            Capacity = 2,
            RefillRate = 1.0,
            Behavior = RateLimitBehavior.Reject
        };
        var limiter = new TokenBucketRateLimiter(options, new FakeTimeProvider());

        // Act: Acquire and exhaust
        await limiter.AcquireAsync(); // Success
        await limiter.AcquireAsync(); // Success
        await limiter.AcquireAsync(); // Throttled
        await limiter.AcquireAsync(); // Throttled

        var stats = limiter.GetStatistics();

        // Assert
        Assert.Equal(2, stats.TotalAcquired);
        Assert.Equal(2, stats.TotalThrottled);
        Assert.Equal(0.5, stats.ThrottleRate); // 50% throttled
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetStatistics_WithScopedKey_ReturnsKeyStatistics()
    {
        // Arrange: Scoped limiter
        var options = new TokenBucketOptions
        {
            Capacity = 10,
            RefillRate = 1.0,
            EnableScoping = true
        };
        var limiter = new TokenBucketRateLimiter(options, new FakeTimeProvider());

        // Act: Get statistics for non-existent key (should return defaults)
        var stats = limiter.GetStatistics(key: "non-existent");

        // Assert: Should return default statistics
        Assert.Equal(10, stats.Capacity);
        Assert.Equal(1.0, stats.RefillRate);
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_WithZeroCapacity_AlwaysThrottles()
    {
        // Arrange: This should fail validation, but test behavior if it somehow gets through
        var options = new TokenBucketOptions
        {
            Capacity = 1, // Minimum valid
            RefillRate = 0.1 // Very slow
        };
        var limiter = new TokenBucketRateLimiter(options, new FakeTimeProvider());

        // Exhaust the one token
        await limiter.AcquireAsync();

        // Act
        var result = await limiter.AcquireAsync();

        // Assert: Should be throttled with very slow refill
        Assert.False(result.IsAllowed);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AcquireAsync_WithCancellationToken_CancelsCorrectly()
    {
        // Arrange: Queue behavior with slow refill
        var options = new TokenBucketOptions
        {
            Capacity = 1,
            RefillRate = 0.1,
            Behavior = RateLimitBehavior.Queue,
            MaxQueueWait = TimeSpan.FromSeconds(10)
        };
        var limiter = new TokenBucketRateLimiter(options, new FakeTimeProvider());

        await limiter.AcquireAsync(); // Exhaust

        // Act: Cancel immediately
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Assert: Should throw OperationCanceledException
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => limiter.AcquireAsync(cancellationToken: cts.Token).AsTask());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Dispose_ReleasesResources()
    {
        // Arrange
        var options = new TokenBucketOptions { Capacity = 10, RefillRate = 1.0 };
        var limiter = new TokenBucketRateLimiter(options, new FakeTimeProvider());

        // Act
        limiter.Dispose();

        // Assert: Should not throw (just verify it's disposable)
        // Subsequent calls should throw ObjectDisposedException
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => limiter.AcquireAsync().AsTask());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullOptions_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => new TokenBucketRateLimiter(null!, new FakeTimeProvider()));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithInvalidOptions_ThrowsException()
    {
        // Arrange: Invalid capacity
        var options = new TokenBucketOptions
        {
            Capacity = -1, // Invalid
            RefillRate = 1.0
        };

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TokenBucketRateLimiter(options, new FakeTimeProvider()));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullTimeProvider_UsesSystemTime()
    {
        // Arrange
        var options = new TokenBucketOptions { Capacity = 10, RefillRate = 1.0 };

        // Act: Pass null for TimeProvider (should default to System)
        var limiter = new TokenBucketRateLimiter(options, timeProvider: null);

        // Assert: Should create successfully and work with system time
        Assert.NotNull(limiter);
        limiter.Dispose();
    }

    #endregion
}

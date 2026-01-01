using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Policies;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Policies;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="RateLimitingDecorator"/> implementation.
/// Tests cover rate limiting integration with message processing pipeline.
/// </summary>
public class RateLimitingDecoratorTests
{
    #region Test Helpers

    public class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; } = [];
    }

    public class MockMessageProcessor : IMessageProcessor
    {
        public int CallCount { get; private set; }
        public List<IMessage> ProcessedMessages { get; } = [];
        public bool ShouldFail { get; set; }
        public TimeSpan ProcessingDelay { get; set; } = TimeSpan.Zero;

        public async ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context, CancellationToken cancellationToken = default)
        {
            CallCount++;
            ProcessedMessages.Add(message);

            if (ProcessingDelay > TimeSpan.Zero)
            {
                await Task.Delay(ProcessingDelay, cancellationToken);
            }

            if (ShouldFail)
            {
                return ProcessingResult.Failed(new Exception("Processing failed"), "Test failure");
            }

            return ProcessingResult.Successful();
        }
    }

    #endregion

    #region Basic Functionality Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_WithAvailableTokens_AllowsProcessing()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter(
            new TokenBucketOptions { Capacity = 10, RefillRate = 10 },
            TimeProvider.System);
        var innerProcessor = new MockMessageProcessor();
        var decorator = new RateLimitingDecorator(innerProcessor, rateLimiter, NullLogger<RateLimitingDecorator>.Instance);

        var message = new TestMessage();
        var context = new ProcessingContext();

        // Act
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, innerProcessor.CallCount);
        Assert.Single(innerProcessor.ProcessedMessages);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_WithoutAvailableTokens_ThrottlesRequest()
    {
        // Arrange: Limiter with capacity 1, reject behavior
        var rateLimiter = new TokenBucketRateLimiter(
            new TokenBucketOptions
            {
                Capacity = 1,
                RefillRate = 1,
                Behavior = RateLimitBehavior.Reject
            },
            TimeProvider.System);
        var innerProcessor = new MockMessageProcessor();
        var decorator = new RateLimitingDecorator(innerProcessor, rateLimiter, NullLogger<RateLimitingDecorator>.Instance);

        var message1 = new TestMessage();
        var message2 = new TestMessage();
        var context = new ProcessingContext();

        // Act: First message should succeed, second should be throttled
        var result1 = await decorator.ProcessAsync(message1, context, TestContext.Current.CancellationToken);
        var result2 = await decorator.ProcessAsync(message2, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result1.Success);
        Assert.False(result2.Success);
        Assert.Equal(1, innerProcessor.CallCount); // Only first message processed
        Assert.Contains("rate limit", result2.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_WhenThrottled_ReturnsFailureWithRetryAfter()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter(
            new TokenBucketOptions
            {
                Capacity = 1,
                RefillRate = 10, // 10 tokens/sec = 100ms per token
                Behavior = RateLimitBehavior.Reject
            },
            TimeProvider.System);
        var innerProcessor = new MockMessageProcessor();
        var decorator = new RateLimitingDecorator(innerProcessor, rateLimiter, NullLogger<RateLimitingDecorator>.Instance);

        var message = new TestMessage();
        var context = new ProcessingContext();

        // Exhaust capacity
        await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Act: Try again (should be throttled)
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Message);
        Assert.Contains("rate limit", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Scoped Rate Limiting Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_WithScopedRateLimiter_UsesMessageTypeAsKey()
    {
        // Arrange: Enable scoping
        var rateLimiter = new TokenBucketRateLimiter(
            new TokenBucketOptions
            {
                Capacity = 1,
                RefillRate = 1,
                EnableScoping = true,
                Behavior = RateLimitBehavior.Reject
            },
            TimeProvider.System);
        var innerProcessor = new MockMessageProcessor();
        var decorator = new RateLimitingDecorator(innerProcessor, rateLimiter, NullLogger<RateLimitingDecorator>.Instance);

        var message1 = new TestMessage();
        var message2 = new TestMessage();
        var context = new ProcessingContext();

        // Act: Both messages of same type should share rate limit
        var result1 = await decorator.ProcessAsync(message1, context, TestContext.Current.CancellationToken);
        var result2 = await decorator.ProcessAsync(message2, context, TestContext.Current.CancellationToken);

        // Assert: First succeeds, second throttled (same type)
        Assert.True(result1.Success);
        Assert.False(result2.Success);
        Assert.Equal(1, innerProcessor.CallCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_WithoutScoping_SharesGlobalRateLimit()
    {
        // Arrange: No scoping (global limit)
        var rateLimiter = new TokenBucketRateLimiter(
            new TokenBucketOptions
            {
                Capacity = 1,
                RefillRate = 1,
                EnableScoping = false,
                Behavior = RateLimitBehavior.Reject
            },
            TimeProvider.System);
        var innerProcessor = new MockMessageProcessor();
        var decorator = new RateLimitingDecorator(innerProcessor, rateLimiter, NullLogger<RateLimitingDecorator>.Instance);

        var message1 = new TestMessage();
        var message2 = new TestMessage();
        var context = new ProcessingContext();

        // Act
        var result1 = await decorator.ProcessAsync(message1, context, TestContext.Current.CancellationToken);
        var result2 = await decorator.ProcessAsync(message2, context, TestContext.Current.CancellationToken);

        // Assert: Both use global limit
        Assert.True(result1.Success);
        Assert.False(result2.Success);
        Assert.Equal(1, innerProcessor.CallCount);
    }

    #endregion

    #region Queue Behavior Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_WithQueueBehavior_WaitsForTokens()
    {
        // Arrange: Queue behavior with real time provider
        var rateLimiter = new TokenBucketRateLimiter(
            new TokenBucketOptions
            {
                Capacity = 1,
                RefillRate = 10, // 10 tokens/sec = 100ms per token
                Behavior = RateLimitBehavior.Queue,
                MaxQueueWait = TimeSpan.FromSeconds(1)
            },
            TimeProvider.System);
        var innerProcessor = new MockMessageProcessor();
        var decorator = new RateLimitingDecorator(innerProcessor, rateLimiter, NullLogger<RateLimitingDecorator>.Instance);

        var message1 = new TestMessage();
        var message2 = new TestMessage();
        var context = new ProcessingContext();

        // Act: First exhausts token, second should wait and succeed
        var result1 = await decorator.ProcessAsync(message1, context, TestContext.Current.CancellationToken);
        var result2 = await decorator.ProcessAsync(message2, context, TestContext.Current.CancellationToken);

        // Assert: Both should eventually succeed
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(2, innerProcessor.CallCount);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_WhenInnerProcessorFails_StillConsumesToken()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter(
            new TokenBucketOptions { Capacity = 2, RefillRate = 1, Behavior = RateLimitBehavior.Reject },
            TimeProvider.System);
        var innerProcessor = new MockMessageProcessor { ShouldFail = true };
        var decorator = new RateLimitingDecorator(innerProcessor, rateLimiter, NullLogger<RateLimitingDecorator>.Instance);

        var message = new TestMessage();
        var context = new ProcessingContext();

        // Act: Process two messages (both should fail, but consume tokens)
        var result1 = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);
        var result2 = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);
        var result3 = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert: First two consumed tokens and failed, third rate limited
        Assert.False(result1.Success);
        Assert.False(result2.Success);
        Assert.False(result3.Success);
        Assert.Equal(2, innerProcessor.CallCount); // Only first two reached inner processor
        Assert.Contains("Test failure", result1.Message ?? string.Empty);
        Assert.Contains("rate limit", result3.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_WithCancellationToken_PropagatesCancellation()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter(
            new TokenBucketOptions { Capacity = 1, RefillRate = 0.1, Behavior = RateLimitBehavior.Queue, MaxQueueWait = TimeSpan.FromSeconds(10) },
            TimeProvider.System);
        var innerProcessor = new MockMessageProcessor();
        var decorator = new RateLimitingDecorator(innerProcessor, rateLimiter, NullLogger<RateLimitingDecorator>.Instance);

        var message = new TestMessage();
        var context = new ProcessingContext();

        // Exhaust capacity
        await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Act: Cancel while queued
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Assert: Should throw OperationCanceledException
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => decorator.ProcessAsync(message, context, cts.Token).AsTask());
    }

    #endregion

    #region Burst and Refill Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_WithBurstCapacity_AllowsBurst()
    {
        // Arrange: Large capacity for bursting
        var rateLimiter = new TokenBucketRateLimiter(
            new TokenBucketOptions { Capacity = 100, RefillRate = 10 },
            TimeProvider.System);
        var innerProcessor = new MockMessageProcessor();
        var decorator = new RateLimitingDecorator(innerProcessor, rateLimiter, NullLogger<RateLimitingDecorator>.Instance);

        var context = new ProcessingContext();

        // Act: Process 50 messages in burst
        var tasks = Enumerable.Range(0, 50)
            .Select(_ => decorator.ProcessAsync(new TestMessage(), context).AsTask());
        var results = await Task.WhenAll(tasks);

        // Assert: All should succeed
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal(50, innerProcessor.CallCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_AfterRefill_AllowsMoreRequests()
    {
        // Arrange: Use FakeTimeProvider for deterministic refill
        var timeProvider = new FakeTimeProvider();
        var rateLimiter = new TokenBucketRateLimiter(
            new TokenBucketOptions
            {
                Capacity = 2,
                RefillRate = 10, // 10 tokens/sec
                Behavior = RateLimitBehavior.Reject
            },
            timeProvider);
        var innerProcessor = new MockMessageProcessor();
        var decorator = new RateLimitingDecorator(innerProcessor, rateLimiter, NullLogger<RateLimitingDecorator>.Instance);

        var message = new TestMessage();
        var context = new ProcessingContext();

        // Exhaust capacity (2 tokens)
        await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);
        await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Verify exhausted
        var resultExhausted = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);
        Assert.False(resultExhausted.Success);

        // Act: Advance time by 1 second (should refill 10 tokens, capped at capacity 2)
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        // Assert: Should allow 2 more requests
        var result1 = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);
        var result2 = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);
        var result3 = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.False(result3.Success); // Third should be throttled again
        Assert.Equal(4, innerProcessor.CallCount); // 2 initial + 2 after refill
    }

    #endregion

    #region Constructor Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullInnerProcessor_ThrowsException()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter(
            new TokenBucketOptions { Capacity = 10, RefillRate = 10 },
            TimeProvider.System);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => new RateLimitingDecorator(null!, rateLimiter, NullLogger<RateLimitingDecorator>.Instance));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullRateLimiter_ThrowsException()
    {
        // Arrange
        var innerProcessor = new MockMessageProcessor();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => new RateLimitingDecorator(innerProcessor, null!, NullLogger<RateLimitingDecorator>.Instance));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullLogger_ThrowsException()
    {
        // Arrange
        var innerProcessor = new MockMessageProcessor();
        var rateLimiter = new TokenBucketRateLimiter(
            new TokenBucketOptions { Capacity = 10, RefillRate = 10 },
            TimeProvider.System);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => new RateLimitingDecorator(innerProcessor, rateLimiter, null!));
    }

    #endregion
}

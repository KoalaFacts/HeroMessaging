# ADR 0005: Rate Limiting

**Status**: Proposed
**Date**: 2025-11-06
**Deciders**: Development Team
**Context**: Phase 2 - Resiliency and Reliability Enhancements

## Context and Problem Statement

HeroMessaging currently lacks built-in rate limiting capabilities to control the throughput of message processing. This capability is essential for:

- **Resource Protection**: Preventing downstream services from being overwhelmed with too many requests
- **API Rate Limit Compliance**: Adhering to external API rate limits (e.g., 100 requests per minute)
- **Fair Resource Allocation**: Ensuring equitable resource distribution across multiple message types or tenants
- **Backpressure Management**: Controlling message processing rate during degraded conditions
- **Cost Control**: Limiting throughput to stay within pricing tiers for cloud services
- **Testing and Development**: Simulating production constraints in lower environments

Without rate limiting, developers must implement custom throttling logic, leading to inconsistent patterns, potential resource exhaustion, and difficulty testing under realistic load conditions.

## Decision Drivers

* **Performance**: Must maintain <1ms p99 latency overhead for rate limit checks
* **Throughput**: Must support >100K msg/s baseline with rate limiting enabled
* **Accuracy**: Rate limit enforcement should be consistent and predictable
* **Flexibility**: Support multiple rate limiting algorithms and scopes (global, per-type, per-tenant)
* **Testability**: Deterministic testing with `TimeProvider` integration
* **Zero-Allocation**: Minimize memory allocations in hot path
* **Integration**: Seamless integration with existing decorator pipeline
* **Multi-framework**: Support netstandard2.0 through net10.0
* **Thread Safety**: Correct behavior under high concurrency

## Considered Options

### Option 1: Token Bucket Algorithm (Chosen)
* **Pros**: Industry standard, smooth rate limiting, allows bursts, well-understood
* **Cons**: Requires periodic token refill, slightly more complex than fixed window
* **Algorithm**: Tokens added at fixed rate, each message consumes token(s), rejects when bucket empty
* **Use Cases**: General-purpose rate limiting, API compliance, steady-state throttling

### Option 2: Sliding Window Algorithm
* **Pros**: More accurate than fixed window, prevents boundary gaming
* **Cons**: Higher memory overhead (track individual timestamps), more complex
* **Algorithm**: Tracks timestamps in sliding time window, counts requests within window
* **Use Cases**: High-precision rate limiting where accuracy is paramount

### Option 3: Fixed Window Algorithm
* **Pros**: Simple, minimal memory, easy to understand
* **Cons**: Boundary issues (2x burst at window edges), less smooth
* **Algorithm**: Count requests in fixed time windows (e.g., per minute)
* **Use Cases**: Simple scenarios where boundary bursts are acceptable

### Option 4: Leaky Bucket Algorithm
* **Pros**: Very smooth output rate, prevents bursts entirely
* **Cons**: May reject valid traffic during legitimate spikes, less flexible
* **Algorithm**: Queue fills at any rate, drains at fixed rate
* **Use Cases**: When consistent output rate is critical (e.g., video streaming)

### Option 5: Concurrency Limiter (Semaphore-Based)
* **Pros**: Simple, controls concurrent processing, built-in .NET support
* **Cons**: Doesn't limit rate over time, only concurrent count
* **Algorithm**: Limits number of simultaneously executing operations
* **Use Cases**: Already exists via `ConsumerOptions.ConcurrentMessageLimit`

## Decision Outcome

Implement **Token Bucket Algorithm (Option 1)** as the primary rate limiting strategy, with extensibility points for additional algorithms:

1. **TokenBucketRateLimiter**: Primary implementation using token bucket algorithm
2. **IRateLimiter**: Abstraction allowing pluggable rate limiting strategies
3. **RateLimitingDecorator**: Decorator integrating rate limiter into processing pipeline
4. **Multiple Scopes**: Support global, per-message-type, and custom key-based rate limiting

This approach balances performance, accuracy, and flexibility while maintaining simplicity.

### Architecture Design

```
┌─────────────────────────────────────────────────────────────┐
│            MessageProcessingPipelineBuilder                 │
│  UseRateLimiting(options) → adds RateLimitingDecorator     │
└─────────────────┬───────────────────────────────────────────┘
                  │
                  ↓
┌─────────────────────────────────────────────────────────────┐
│              RateLimitingDecorator                          │
│  (MessageProcessorDecorator)                                │
│  • Intercepts ProcessAsync calls                            │
│  • Delegates to IRateLimiter for admission decision         │
│  • Records metrics (throttled_messages_total)               │
└─────────────────┬───────────────────────────────────────────┘
                  │
                  │ uses
                  ↓
┌─────────────────────────────────────────────────────────────┐
│                   IRateLimiter                              │
│  AcquireAsync(key?, permits?, cancellationToken)            │
└──────────────┬──────────────────────────────────┬───────────┘
               │                                   │
               ↓                                   ↓
┌──────────────────────────┐      ┌───────────────────────────┐
│  TokenBucketRateLimiter  │      │ Future: SlidingWindowRL   │
│  • Token refill timer    │      │ • Timestamp tracking      │
│  • Burst capacity        │      │ • Higher accuracy         │
│  • Smooth rate control   │      │ • Higher memory cost      │
│  • Lock-based thread-safe│      │                           │
└──────────────────────────┘      └───────────────────────────┘
```

### Token Bucket Algorithm Details

**Parameters**:
- `Capacity`: Maximum tokens in bucket (burst size)
- `RefillRate`: Tokens added per time period (e.g., 100/second)
- `RefillPeriod`: How often tokens are added (e.g., 100ms)

**Behavior**:
1. Bucket starts full (capacity tokens)
2. Every `RefillPeriod`, add `RefillRate * elapsed` tokens (up to capacity)
3. Each message attempt consumes N tokens (default: 1)
4. If insufficient tokens, request is rate-limited
5. Can wait for tokens or reject immediately based on configuration

**Example**:
- Capacity: 100 tokens
- RefillRate: 10 tokens/second
- Allows burst of 100 messages, then steady 10/second

### API Design

```csharp
// Core abstraction (in HeroMessaging.Abstractions/Policies/IRateLimiter.cs)
public interface IRateLimiter
{
    /// <summary>
    /// Attempts to acquire permits for rate limiting.
    /// </summary>
    /// <param name="key">Optional key for scoped rate limiting (e.g., message type, tenant ID)</param>
    /// <param name="permits">Number of permits to acquire (default: 1)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success, wait time if throttled, or failure</returns>
    ValueTask<RateLimitResult> AcquireAsync(
        string? key = null,
        int permits = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current statistics for the rate limiter.
    /// </summary>
    RateLimiterStatistics GetStatistics(string? key = null);
}

// Token bucket implementation (in HeroMessaging/Policies/TokenBucketRateLimiter.cs)
public sealed class TokenBucketRateLimiter : IRateLimiter, IDisposable
{
    public TokenBucketRateLimiter(
        TokenBucketOptions options,
        TimeProvider? timeProvider = null);

    public ValueTask<RateLimitResult> AcquireAsync(
        string? key = null,
        int permits = 1,
        CancellationToken cancellationToken = default);

    public RateLimiterStatistics GetStatistics(string? key = null);
    public void Dispose();
}

// Options (in HeroMessaging.Abstractions/Policies/RateLimitOptions.cs)
public sealed class TokenBucketOptions
{
    /// <summary>Maximum tokens in bucket (burst capacity)</summary>
    public long Capacity { get; set; } = 100;

    /// <summary>Tokens added per second</summary>
    public double RefillRate { get; set; } = 10.0;

    /// <summary>How often to refill tokens</summary>
    public TimeSpan RefillPeriod { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Whether to queue requests when rate limited</summary>
    public RateLimitBehavior Behavior { get; set; } = RateLimitBehavior.Queue;

    /// <summary>Maximum time to wait for tokens when queuing</summary>
    public TimeSpan MaxQueueWait { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Enable per-key scoping (e.g., per message type)</summary>
    public bool EnableScoping { get; set; } = false;

    /// <summary>Function to extract scope key from processing context</summary>
    public Func<ProcessingContext, string>? KeySelector { get; set; }
}

public enum RateLimitBehavior
{
    /// <summary>Reject immediately when rate limited</summary>
    Reject,

    /// <summary>Queue and wait for tokens (up to MaxQueueWait)</summary>
    Queue
}

// Result value object
public readonly struct RateLimitResult
{
    public bool IsAllowed { get; init; }
    public TimeSpan RetryAfter { get; init; }
    public long RemainingPermits { get; init; }
    public string? ReasonPhrase { get; init; }

    public static RateLimitResult Success(long remainingPermits);
    public static RateLimitResult Throttled(TimeSpan retryAfter, string reason);
}

// Statistics
public sealed class RateLimiterStatistics
{
    public long AvailableTokens { get; init; }
    public long Capacity { get; init; }
    public double RefillRate { get; init; }
    public DateTimeOffset LastRefillTime { get; init; }
    public long TotalAcquired { get; init; }
    public long TotalThrottled { get; init; }
}

// Decorator (in HeroMessaging/Processing/Decorators/RateLimitingDecorator.cs)
public sealed class RateLimitingDecorator : MessageProcessorDecorator
{
    private readonly IRateLimiter _rateLimiter;
    private readonly RateLimitingDecoratorOptions _options;

    public RateLimitingDecorator(
        IMessageProcessor inner,
        IRateLimiter rateLimiter,
        RateLimitingDecoratorOptions options)
        : base(inner);

    public override async ValueTask<ProcessingResult> ProcessAsync(
        ProcessingContext context,
        CancellationToken cancellationToken = default)
    {
        var key = _options.KeySelector?.Invoke(context);
        var result = await _rateLimiter.AcquireAsync(key, permits: 1, cancellationToken);

        if (!result.IsAllowed)
        {
            return ProcessingResult.Failed(
                errorCode: "RATE_LIMITED",
                errorMessage: result.ReasonPhrase ?? "Rate limit exceeded",
                retryAfter: result.RetryAfter);
        }

        return await Inner.ProcessAsync(context, cancellationToken);
    }
}

public sealed class RateLimitingDecoratorOptions
{
    /// <summary>Function to extract rate limit key from context</summary>
    public Func<ProcessingContext, string>? KeySelector { get; set; }

    /// <summary>Number of permits to acquire per message</summary>
    public int PermitsPerMessage { get; set; } = 1;

    /// <summary>Action to invoke when message is rate limited</summary>
    public Action<ProcessingContext, RateLimitResult>? OnRateLimited { get; set; }
}
```

### Configuration

```csharp
// Global rate limiting (all messages)
services.AddHeroMessaging(builder => builder
    .AddMessageProcessing(pipeline => pipeline
        .UseRateLimiting(options => options
            .WithCapacity(1000)        // Burst capacity: 1000 messages
            .WithRefillRate(100)       // Steady rate: 100 msg/s
            .WithBehavior(RateLimitBehavior.Queue)
            .WithMaxQueueWait(TimeSpan.FromSeconds(10)))));

// Per-message-type rate limiting
services.AddHeroMessaging(builder => builder
    .AddMessageProcessing(pipeline => pipeline
        .UseRateLimiting(options => options
            .WithCapacity(100)
            .WithRefillRate(10)
            .EnableScoping()
            .WithKeySelector(ctx => ctx.Message.GetType().Name))));

// Custom key-based rate limiting (e.g., per tenant)
services.AddHeroMessaging(builder => builder
    .AddMessageProcessing(pipeline => pipeline
        .UseRateLimiting(options => options
            .WithCapacity(500)
            .WithRefillRate(50)
            .EnableScoping()
            .WithKeySelector(ctx => ctx.Headers.GetValueOrDefault("TenantId", "default")))));

// Direct usage
var rateLimiter = new TokenBucketRateLimiter(new TokenBucketOptions
{
    Capacity = 100,
    RefillRate = 10.0,
    Behavior = RateLimitBehavior.Queue
}, timeProvider: TimeProvider.System);

var result = await rateLimiter.AcquireAsync(key: "my-api");
if (result.IsAllowed)
{
    // Proceed with operation
}
else
{
    // Wait or reject: result.RetryAfter
}
```

## Consequences

### Positive

* **Resource Protection**: Prevents downstream service overload and cascading failures
* **Compliance**: Enables adherence to external API rate limits
* **Performance**: <1ms overhead for token acquisition (lock-based, in-memory)
* **Flexibility**: Supports global, per-type, and custom scoped rate limiting
* **Burst Support**: Token bucket allows controlled bursts while maintaining steady rate
* **Testability**: `TimeProvider` integration enables deterministic testing
* **Integration**: Seamless decorator pattern integration with existing pipeline
* **Observability**: Rate limit metrics (throttled count, available tokens) exposed
* **Zero-Allocation**: Struct-based results minimize GC pressure

### Negative

* **Single-Process**: Rate limiting is per-process, not distributed across instances
* **Token Refill Overhead**: Periodic refill calculation adds minimal CPU cost
* **Memory Overhead**: Scoped rate limiting requires dictionary storage per key
* **Configuration Complexity**: Advanced scenarios (per-tenant) require careful setup
* **No Persistence**: Rate limit state lost on process restart

### Mitigations

* **Distributed Rate Limiting**: Future enhancement with distributed cache (Redis) for multi-instance scenarios
* **Token Refill Overhead**: Lazy refill on acquire (not timer-based) reduces overhead
* **Memory Overhead**: Configurable key eviction policy for long-running processes
* **Configuration Complexity**: Comprehensive documentation and examples
* **Persistence**: Document ephemeral nature; distributed approach for stateful needs

## Performance Considerations

### Token Bucket Performance Characteristics

**Lock-Based Synchronization**:
- Use `lock` statement for thread-safety (not `SemaphoreSlim` to avoid allocation)
- Critical section: token refill calculation + token consumption (~10 instructions)
- Expected overhead: <100 nanoseconds per acquire on modern CPU

**Zero-Allocation Design**:
- `ValueTask<RateLimitResult>` return type (no heap allocation for synchronous fast path)
- `readonly struct RateLimitResult` (stack-allocated)
- Pre-allocated dictionary for scoped limiters (avoid per-request allocations)

**Refill Strategy**:
- Lazy refill: Calculate tokens on each `AcquireAsync` based on elapsed time
- No background timer (eliminates timer overhead and thread contention)
- Formula: `tokens = Math.Min(capacity, current + refillRate * elapsed.TotalSeconds)`

**Expected Performance**:
- **Latency**: <1ms p99 (lock acquisition + arithmetic)
- **Throughput**: >1M acquires/second (single-threaded, no scoping)
- **Memory**: ~100 bytes overhead (global limiter), +200 bytes per scope key
- **GC Pressure**: Near-zero allocations in steady state

## Implementation Plan

### Phase 1: Core Abstractions and Token Bucket (1-2 days)
1. Create `IRateLimiter` interface in `HeroMessaging.Abstractions`
2. Implement `TokenBucketRateLimiter` with TDD
3. Write comprehensive unit tests:
   - Token refill logic
   - Burst capacity enforcement
   - Queue vs. Reject behavior
   - Thread-safety under concurrent load
   - `TimeProvider` integration for deterministic tests
4. Verify 100% test coverage for public API

### Phase 2: Decorator Integration (1 day)
1. Create `RateLimitingDecorator` with TDD
2. Add `UseRateLimiting()` extension to `MessageProcessingPipelineBuilder`
3. Write integration tests:
   - Global rate limiting
   - Per-message-type scoping
   - Custom key selector
   - Interaction with other decorators (retry, circuit breaker)
4. Verify decorator chaining works correctly

### Phase 3: Scoped Rate Limiting (1 day)
1. Implement per-key rate limiter dictionary
2. Add key eviction policy (LRU or time-based)
3. Write tests for scoped scenarios:
   - Multiple concurrent keys
   - Memory bounds enforcement
   - Key eviction behavior
4. Performance test with 1000+ unique keys

### Phase 4: Performance and Documentation (1 day)
1. Create benchmarks in `HeroMessaging.Benchmarks`:
   - `RateLimiterBenchmarks`: Token acquisition latency
   - `RateLimitingDecoratorBenchmarks`: End-to-end overhead
   - Compare with and without rate limiting enabled
2. Verify <1ms p99, >100K msg/s maintained
3. Write comprehensive XML documentation
4. Create usage examples in code comments
5. Update README with rate limiting section

### Phase 5: Verification and Finalization (0.5 day)
1. Run full test suite: `dotnet test`
2. Generate coverage report: Verify 80%+ overall, 100% public APIs
3. Run benchmarks: Verify performance targets met
4. Review constitutional compliance checklist
5. Update changelog and version number

**Total Estimated Effort**: 4-5 days

## Compliance Checklist

- [ ] **TDD**: Tests written before implementation for all components
- [ ] **Coverage**: 80%+ overall (100% for public APIs)
- [ ] **Performance**: <1ms p99 latency, >100K msg/s throughput maintained
- [ ] **Documentation**: Comprehensive XML docs for all public APIs
- [ ] **Multi-framework**: netstandard2.0 compatibility verified
- [ ] **SOLID**: Single responsibility, interface segregation, dependency injection
- [ ] **Plugin Architecture**: `IRateLimiter` abstraction for extensibility
- [ ] **Error Handling**: Actionable error messages (RATE_LIMITED code, RetryAfter)
- [ ] **Observability**: Metrics integration (throttled_messages_total counter)
- [ ] **Thread Safety**: Correct behavior under high concurrency verified
- [ ] **Zero-Allocation**: Struct-based results, ValueTask returns
- [ ] **Testability**: `TimeProvider` integration for deterministic tests

## Future Enhancements

### Distributed Rate Limiting (Phase 2)
- Implement `IDistributedRateLimiter` using Redis or similar
- Lua scripts for atomic token acquisition
- Eventual consistency tradeoffs documented

### Additional Algorithms (Phase 3)
- Sliding window rate limiter
- Leaky bucket rate limiter
- Adaptive rate limiting based on error rates

### Advanced Features (Phase 4)
- Hierarchical rate limiting (global + per-tenant)
- Dynamic rate adjustment based on metrics
- Rate limit warming (gradual increase after outage)
- Priority-based token allocation

## References

* **Token Bucket Algorithm**: https://en.wikipedia.org/wiki/Token_bucket
* **ASP.NET Core Rate Limiting**: https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit
* **Polly Rate Limit**: https://github.com/App-vNext/Polly/wiki/Rate-Limit
* **Google Cloud Rate Limiting**: https://cloud.google.com/architecture/rate-limiting-strategies
* **Stripe Rate Limiting**: https://stripe.com/docs/rate-limits
* **System.Threading.RateLimiting (.NET 7+)**: https://learn.microsoft.com/en-us/dotnet/api/system.threading.ratelimiting

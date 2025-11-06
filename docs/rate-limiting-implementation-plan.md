# Rate Limiting Implementation Plan

**Feature Branch**: `feature/rate-limiting`
**Target Version**: 1.x.0
**Estimated Effort**: 4-5 days
**Status**: In Progress
**Created**: 2025-11-06

## Overview

This document provides a detailed implementation plan for adding rate limiting capabilities to HeroMessaging. The implementation follows the Token Bucket algorithm as documented in [ADR 0005: Rate Limiting](adr/0005-rate-limiting.md).

## Constitutional Requirements

All implementation must adhere to HeroMessaging constitutional principles:

- ✅ **TDD**: Tests written before implementation
- ✅ **Coverage**: 80%+ minimum (100% for public APIs)
- ✅ **Performance**: <1ms overhead, >100K msg/s maintained
- ✅ **Quality**: SOLID principles, <20 lines/method, <10 complexity
- ✅ **Testing Framework**: Xunit.v3 exclusively (NO FluentAssertions)
- ✅ **Multi-Framework**: netstandard2.0, net6.0-net10.0 support
- ✅ **Documentation**: Comprehensive XML docs for all public APIs

## Architecture Summary

```
HeroMessaging.Abstractions
├── Policies/
│   ├── IRateLimiter.cs              # Core abstraction
│   ├── RateLimitResult.cs           # Result value object (struct)
│   ├── RateLimiterStatistics.cs     # Statistics DTO
│   └── TokenBucketOptions.cs        # Configuration options

HeroMessaging
├── Policies/
│   └── TokenBucketRateLimiter.cs    # Token bucket implementation
├── Processing/
│   └── Decorators/
│       ├── RateLimitingDecorator.cs           # Pipeline decorator
│       └── RateLimitingDecoratorOptions.cs    # Decorator configuration
└── Extensions/
    └── ExtensionsToMessageProcessingPipelineBuilder.cs  # .UseRateLimiting()

HeroMessaging.Tests
├── Unit/
│   ├── TokenBucketRateLimiterTests.cs      # Algorithm tests
│   └── RateLimitingDecoratorTests.cs       # Decorator tests
└── Integration/
    └── RateLimitingIntegrationTests.cs     # End-to-end tests

HeroMessaging.Benchmarks
└── RateLimitingBenchmarks.cs              # Performance benchmarks
```

## Phase 1: Core Abstractions and Token Bucket Algorithm

### 1.1 Create IRateLimiter Interface

**File**: `src/HeroMessaging.Abstractions/Policies/IRateLimiter.cs`

**Test-First Approach**:
```csharp
// tests/HeroMessaging.Tests/Unit/TokenBucketRateLimiterTests.cs
[Fact]
[Trait("Category", "Unit")]
public async Task AcquireAsync_WithAvailableTokens_ReturnsSuccess()
{
    // Arrange: Create limiter with 10 capacity, no refill needed for first 10
    var limiter = new TokenBucketRateLimiter(new TokenBucketOptions
    {
        Capacity = 10,
        RefillRate = 1.0
    }, new FakeTimeProvider());

    // Act: Acquire 1 token
    var result = await limiter.AcquireAsync();

    // Assert: Should succeed with 9 remaining
    Assert.True(result.IsAllowed);
    Assert.Equal(9, result.RemainingPermits);
}
```

**Implementation Requirements**:
- Interface with `AcquireAsync()` method
- `RateLimitResult` struct (stack-allocated, no heap)
- `RateLimiterStatistics` class for observability
- Comprehensive XML documentation
- Support for scoped rate limiting (optional key parameter)

**Definition of Done**:
- [ ] Interface defined with XML docs
- [ ] Value objects created (RateLimitResult, RateLimiterStatistics)
- [ ] Compiles without errors
- [ ] All public APIs have XML documentation

### 1.2 Implement TokenBucketRateLimiter

**File**: `src/HeroMessaging/Policies/TokenBucketRateLimiter.cs`

**TDD Test Cases** (write tests BEFORE implementation):

```csharp
// Basic Functionality
[Fact] AcquireAsync_WithAvailableTokens_ReturnsSuccess()
[Fact] AcquireAsync_WithoutAvailableTokens_ReturnsThrottled()
[Fact] AcquireAsync_AfterRefillPeriod_RefillsTokens()
[Fact] AcquireAsync_WithBurstCapacity_AllowsBurst()

// Token Refill Logic
[Fact] AcquireAsync_AfterMultipleRefillPeriods_RefillsCorrectly()
[Fact] AcquireAsync_WithPartialRefill_CalculatesCorrectly()
[Fact] AcquireAsync_DoesNotExceedCapacity_WhenRefilling()

// Queue vs Reject Behavior
[Fact] AcquireAsync_WithQueueBehavior_WaitsForTokens()
[Fact] AcquireAsync_WithRejectBehavior_ReturnsImmediately()
[Fact] AcquireAsync_WithMaxQueueWait_TimesOutCorrectly()

// Multiple Permits
[Fact] AcquireAsync_WithMultiplePermits_ConsumesCorrectly()
[Fact] AcquireAsync_WithInsufficientTokensForPermits_Throttles()

// Scoped Rate Limiting
[Fact] AcquireAsync_WithDifferentKeys_IndependentBuckets()
[Fact] AcquireAsync_WithSameKey_SharesBucket()
[Fact] AcquireAsync_WithNullKey_UsesGlobalBucket()

// Thread Safety
[Fact] AcquireAsync_ConcurrentCalls_ThreadSafe()
[Fact] AcquireAsync_Under100ConcurrentCalls_NoRaceConditions()

// TimeProvider Integration
[Fact] AcquireAsync_WithFakeTimeProvider_DeterministicRefill()
[Fact] AcquireAsync_AfterTimeAdvance_RefillsCorrectly()

// Statistics
[Fact] GetStatistics_ReturnsCurrentState()
[Fact] GetStatistics_TracksAcquiredAndThrottled()

// Edge Cases
[Fact] AcquireAsync_WithZeroCapacity_AlwaysThrottles()
[Fact] AcquireAsync_WithCancellationToken_CancelsCorrectly()
[Fact] Dispose_ReleasesResources()
```

**Implementation Checklist**:
- [ ] Constructor accepts `TokenBucketOptions` and `TimeProvider`
- [ ] Thread-safe using `lock` (not `SemaphoreSlim` for zero-allocation)
- [ ] Lazy refill calculation on each acquire (no background timer)
- [ ] Support for Queue vs Reject behavior
- [ ] Scoped rate limiting with `ConcurrentDictionary<string, Bucket>`
- [ ] `GetStatistics()` for observability
- [ ] `IDisposable` implementation (though minimal cleanup needed)
- [ ] All private methods < 20 lines, < 10 complexity
- [ ] Zero-allocation hot path (struct returns, ValueTask)

**Performance Targets**:
- [ ] <100 nanoseconds per acquire (lock + arithmetic)
- [ ] >1M acquires/second (single-threaded benchmark)
- [ ] <100 bytes overhead per global limiter
- [ ] <200 bytes overhead per scoped key

**Definition of Done**:
- [ ] All 20+ test cases pass
- [ ] 100% line coverage for public API
- [ ] Performance targets met (verified with BenchmarkDotNet)
- [ ] Thread safety verified with concurrent test
- [ ] XML documentation complete

## Phase 2: Decorator Integration

### 2.1 Create RateLimitingDecorator

**File**: `src/HeroMessaging/Processing/Decorators/RateLimitingDecorator.cs`

**TDD Test Cases**:

```csharp
// Basic Functionality
[Fact] ProcessAsync_WithAvailableTokens_InvokesInnerProcessor()
[Fact] ProcessAsync_WithoutAvailableTokens_ReturnsRateLimitedResult()
[Fact] ProcessAsync_RecordsRateLimitMetrics()

// Key Selection
[Fact] ProcessAsync_WithKeySelector_UsesCustomKey()
[Fact] ProcessAsync_WithoutKeySelector_UsesGlobalLimit()
[Fact] ProcessAsync_WithMessageTypeKeySelector_LimitsPerType()

// Permits Configuration
[Fact] ProcessAsync_WithCustomPermitsPerMessage_ConsumesCorrectly()

// Callback
[Fact] ProcessAsync_WhenThrottled_InvokesOnRateLimitedCallback()

// Integration with Other Decorators
[Fact] ProcessAsync_WithRetryDecorator_ChainsCorrectly()
[Fact] ProcessAsync_WithCircuitBreaker_ChainsCorrectly()

// Cancellation
[Fact] ProcessAsync_WithCancellation_PropagatesCorrectly()
```

**Implementation Checklist**:
- [ ] Inherits from `MessageProcessorDecorator`
- [ ] Constructor: `IMessageProcessor inner`, `IRateLimiter rateLimiter`, `RateLimitingDecoratorOptions options`
- [ ] `ProcessAsync()` calls `rateLimiter.AcquireAsync()`
- [ ] Returns `ProcessingResult.Failed()` with RATE_LIMITED error code when throttled
- [ ] Supports custom `KeySelector` function
- [ ] Supports `OnRateLimited` callback
- [ ] Uses primary constructor pattern (C# 12)
- [ ] XML documentation

**Definition of Done**:
- [ ] All test cases pass
- [ ] 100% line coverage
- [ ] Decorator chains correctly with existing decorators
- [ ] XML documentation complete

### 2.2 Add Pipeline Builder Extension

**File**: `src/HeroMessaging/Extensions/ExtensionsToMessageProcessingPipelineBuilder.cs`

**Test Cases**:
```csharp
[Fact] UseRateLimiting_AddsDecoratorToPipeline()
[Fact] UseRateLimiting_WithOptions_ConfiguresCorrectly()
[Fact] UseRateLimiting_InPreConfiguredPipeline_Works()
```

**Implementation Checklist**:
- [ ] Extension method: `UseRateLimiting(this MessageProcessingPipelineBuilder builder, Action<RateLimitingOptions> configure)`
- [ ] Creates `TokenBucketRateLimiter` with configured options
- [ ] Wraps inner processor with `RateLimitingDecorator`
- [ ] Fluent API support (returns builder)
- [ ] XML documentation with usage example

**Usage Example** (in XML docs):
```csharp
/// <example>
/// services.AddHeroMessaging(builder => builder
///     .AddMessageProcessing(pipeline => pipeline
///         .UseRateLimiting(options => options
///             .WithCapacity(1000)
///             .WithRefillRate(100))));
/// </example>
```

**Definition of Done**:
- [ ] Extension method works in pipeline configuration
- [ ] Tests verify decorator is added
- [ ] XML documentation with example
- [ ] Follows naming convention: `ExtensionsToMessageProcessingPipelineBuilder`

## Phase 3: Scoped Rate Limiting

### 3.1 Implement Per-Key Rate Limiting

**Enhancement to TokenBucketRateLimiter**:

**Test Cases**:
```csharp
[Fact] AcquireAsync_With1000UniqueKeys_CreatesIndependentBuckets()
[Fact] AcquireAsync_WithKeyEviction_RemovesStaleKeys()
[Fact] AcquireAsync_WithMemoryBounds_LimitsKeyCount()
```

**Implementation Checklist**:
- [ ] `ConcurrentDictionary<string, TokenBucket>` for per-key buckets
- [ ] Lazy bucket creation on first access
- [ ] Optional key eviction policy (LRU or time-based)
- [ ] Memory bounds enforcement (max keys)
- [ ] Thread-safe key access

**Definition of Done**:
- [ ] Supports 1000+ unique keys efficiently
- [ ] Memory overhead < 200 bytes per key
- [ ] Thread-safe under concurrent access
- [ ] Key eviction works correctly

### 3.2 Integration Tests for Scoped Limiting

**File**: `tests/HeroMessaging.Tests/Integration/RateLimitingIntegrationTests.cs`

**Test Cases**:
```csharp
[Fact] EndToEnd_GlobalRateLimiting_ThrottlesCorrectly()
[Fact] EndToEnd_PerMessageTypeRateLimiting_IndependentLimits()
[Fact] EndToEnd_CustomKeySelectorRateLimiting_Works()
[Fact] EndToEnd_WithMultipleDecorators_ChainsCorrectly()
```

**Definition of Done**:
- [ ] End-to-end tests with real pipeline
- [ ] Multiple message types tested
- [ ] Custom key selectors tested
- [ ] Integration with other decorators verified

## Phase 4: Performance and Documentation

### 4.1 Performance Benchmarks

**File**: `tests/HeroMessaging.Benchmarks/RateLimitingBenchmarks.cs`

**Benchmarks to Implement**:

```csharp
[Benchmark]
[BenchmarkCategory("RateLimiter")]
public async Task TokenBucket_AcquireAsync_SingleThreaded()

[Benchmark]
[BenchmarkCategory("RateLimiter")]
public async Task TokenBucket_AcquireAsync_With100ConcurrentTasks()

[Benchmark]
[BenchmarkCategory("RateLimiter")]
public async Task TokenBucket_AcquireAsync_With1000ScopedKeys()

[Benchmark]
[BenchmarkCategory("Decorator")]
public async Task RateLimitingDecorator_ProcessAsync_Baseline()

[Benchmark]
[BenchmarkCategory("Decorator")]
public async Task RateLimitingDecorator_ProcessAsync_WithRateLimiting()

[Benchmark]
[BenchmarkCategory("EndToEnd")]
public async Task Pipeline_WithAndWithoutRateLimiting_Comparison()
```

**Performance Targets**:
- [ ] Token acquisition: <100ns p99 (single-threaded)
- [ ] Decorator overhead: <1ms p99
- [ ] Throughput: >100K msg/s with rate limiting enabled
- [ ] Memory: <1KB allocation per message
- [ ] No performance regression: <10% difference from baseline

**Benchmark Execution**:
```bash
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --filter "*RateLimiting*"
```

**Definition of Done**:
- [ ] All benchmarks implemented
- [ ] Performance targets met
- [ ] Results documented in benchmark summary
- [ ] No performance regressions detected

### 4.2 Comprehensive Documentation

**XML Documentation Requirements**:

All public APIs must have:
- Summary (what it does)
- Parameter descriptions
- Return value description
- Exceptions (if any)
- Example usage (for main entry points)
- Remarks (important behavior notes)

**Files to Document**:
- [ ] `IRateLimiter.cs` - Interface and all methods
- [ ] `TokenBucketRateLimiter.cs` - Class and public methods
- [ ] `RateLimitingDecorator.cs` - Decorator and ProcessAsync
- [ ] `RateLimitResult.cs` - Struct and all properties
- [ ] `TokenBucketOptions.cs` - All configuration properties
- [ ] `RateLimitingDecoratorOptions.cs` - All configuration properties
- [ ] Extension methods - UseRateLimiting with examples

**Code Example to Add**:

```csharp
/// <example>
/// <code>
/// // Global rate limiting
/// services.AddHeroMessaging(builder => builder
///     .AddMessageProcessing(pipeline => pipeline
///         .UseRateLimiting(options => options
///             .WithCapacity(1000)
///             .WithRefillRate(100))));
///
/// // Per-message-type rate limiting
/// services.AddHeroMessaging(builder => builder
///     .AddMessageProcessing(pipeline => pipeline
///         .UseRateLimiting(options => options
///             .WithCapacity(100)
///             .WithRefillRate(10)
///             .EnableScoping()
///             .WithKeySelector(ctx => ctx.Message.GetType().Name))));
/// </code>
/// </example>
```

**Definition of Done**:
- [ ] All public APIs have XML documentation
- [ ] At least one complete usage example
- [ ] Remarks explain important behaviors (token refill, thread safety)
- [ ] Parameter descriptions are clear and actionable

### 4.3 Update README

**File**: `README.md`

**Section to Add**:

```markdown
## Rate Limiting

HeroMessaging includes built-in rate limiting using the Token Bucket algorithm to control message processing throughput.

### Features

- **Token Bucket Algorithm**: Industry-standard algorithm supporting controlled bursts
- **Global Rate Limiting**: Apply rate limits to all messages
- **Scoped Rate Limiting**: Per-message-type or custom key-based limits
- **Queue or Reject**: Configure behavior when rate limited
- **High Performance**: <1ms overhead, >100K msg/s maintained
- **Thread-Safe**: Correct behavior under high concurrency
- **Testable**: Deterministic testing with TimeProvider

### Usage

```csharp
// Global rate limiting (100 msg/s steady, burst of 1000)
services.AddHeroMessaging(builder => builder
    .AddMessageProcessing(pipeline => pipeline
        .UseRateLimiting(options => options
            .WithCapacity(1000)
            .WithRefillRate(100)
            .WithBehavior(RateLimitBehavior.Queue))));

// Per-message-type rate limiting
services.AddHeroMessaging(builder => builder
    .AddMessageProcessing(pipeline => pipeline
        .UseRateLimiting(options => options
            .WithCapacity(100)
            .WithRefillRate(10)
            .EnableScoping()
            .WithKeySelector(ctx => ctx.Message.GetType().Name))));
```

### Configuration Options

- `Capacity`: Maximum tokens (burst size)
- `RefillRate`: Tokens added per second
- `Behavior`: Queue or Reject when rate limited
- `MaxQueueWait`: Maximum wait time when queuing
- `KeySelector`: Function to extract scope key for per-type/tenant limiting

See [ADR 0005: Rate Limiting](docs/adr/0005-rate-limiting.md) for design details.
```

**Definition of Done**:
- [ ] Rate limiting section added to README
- [ ] Features list updated
- [ ] Usage examples provided
- [ ] Link to ADR included

## Phase 5: Verification and Finalization

### 5.1 Test Suite Verification

**Commands to Run**:
```bash
# Build
dotnet build --no-restore --verbosity normal

# Unit tests
dotnet test --filter Category=Unit --no-build --verbosity normal

# Integration tests
dotnet test --filter Category=Integration --no-build --verbosity normal

# All tests
dotnet test --no-build --verbosity normal

# Coverage
dotnet test --collect:"XPlat Code Coverage" --no-build
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report
```

**Acceptance Criteria**:
- [ ] All tests pass (0 failures)
- [ ] Overall coverage ≥ 80%
- [ ] Public API coverage = 100%
- [ ] No warnings or errors in build

### 5.2 Performance Verification

**Commands to Run**:
```bash
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --filter "*RateLimiting*"
```

**Acceptance Criteria**:
- [ ] Token acquisition: <100ns p99
- [ ] Decorator overhead: <1ms p99
- [ ] Throughput: >100K msg/s maintained
- [ ] No regression: <10% slower than baseline

### 5.3 Constitutional Compliance Review

**Checklist**:
- [ ] **TDD**: All tests written before implementation
- [ ] **Coverage**: 80%+ overall, 100% public APIs
- [ ] **Performance**: <1ms overhead, >100K msg/s
- [ ] **Quality**: SOLID principles, <20 lines/method, <10 complexity
- [ ] **Testing**: Xunit.v3 only (no FluentAssertions)
- [ ] **Multi-Framework**: netstandard2.0, net6.0-net10.0
- [ ] **Documentation**: XML docs for all public APIs
- [ ] **Error Handling**: RATE_LIMITED error code with RetryAfter
- [ ] **Observability**: Metrics and statistics exposed
- [ ] **Thread Safety**: Verified under concurrent load
- [ ] **Zero-Allocation**: Struct returns, ValueTask, minimal heap

### 5.4 File Verification

**Files Created** (verify existence):
```bash
# Abstractions
ls src/HeroMessaging.Abstractions/Policies/IRateLimiter.cs
ls src/HeroMessaging.Abstractions/Policies/RateLimitResult.cs
ls src/HeroMessaging.Abstractions/Policies/RateLimiterStatistics.cs
ls src/HeroMessaging.Abstractions/Policies/TokenBucketOptions.cs

# Implementation
ls src/HeroMessaging/Policies/TokenBucketRateLimiter.cs
ls src/HeroMessaging/Processing/Decorators/RateLimitingDecorator.cs
ls src/HeroMessaging/Processing/Decorators/RateLimitingDecoratorOptions.cs
ls src/HeroMessaging/Extensions/ExtensionsToMessageProcessingPipelineBuilder.cs

# Tests
ls tests/HeroMessaging.Tests/Unit/TokenBucketRateLimiterTests.cs
ls tests/HeroMessaging.Tests/Unit/RateLimitingDecoratorTests.cs
ls tests/HeroMessaging.Tests/Integration/RateLimitingIntegrationTests.cs

# Benchmarks
ls tests/HeroMessaging.Benchmarks/RateLimitingBenchmarks.cs

# Documentation
ls docs/adr/0005-rate-limiting.md
ls docs/rate-limiting-implementation-plan.md
```

**Definition of Done**:
- [ ] All specified files exist
- [ ] No compilation errors
- [ ] All tests pass
- [ ] Coverage meets requirements
- [ ] Performance meets requirements
- [ ] Documentation complete
- [ ] Constitutional compliance verified

## Task Breakdown Summary

### Phase 1: Core (1-2 days)
- [ ] 1.1: Create IRateLimiter interface (2 hours)
- [ ] 1.2: Implement TokenBucketRateLimiter (6-8 hours)

### Phase 2: Decorator (1 day)
- [ ] 2.1: Create RateLimitingDecorator (4 hours)
- [ ] 2.2: Add pipeline builder extension (2 hours)

### Phase 3: Scoped Limiting (1 day)
- [ ] 3.1: Implement per-key rate limiting (4 hours)
- [ ] 3.2: Integration tests (3 hours)

### Phase 4: Performance & Docs (1 day)
- [ ] 4.1: Performance benchmarks (3 hours)
- [ ] 4.2: Comprehensive documentation (3 hours)
- [ ] 4.3: Update README (1 hour)

### Phase 5: Verification (0.5 day)
- [ ] 5.1: Test suite verification (1 hour)
- [ ] 5.2: Performance verification (1 hour)
- [ ] 5.3: Constitutional compliance review (1 hour)
- [ ] 5.4: File verification (0.5 hour)

**Total**: 4-5 days

## Success Criteria

Rate limiting implementation is complete when:

1. ✅ **Functionality**: All features from ADR implemented and working
2. ✅ **Tests**: 80%+ coverage, 100% public API coverage, all tests pass
3. ✅ **Performance**: <1ms overhead, >100K msg/s, no regressions
4. ✅ **Quality**: SOLID principles, <20 lines/method, <10 complexity
5. ✅ **Documentation**: XML docs on all public APIs, README updated
6. ✅ **Constitutional**: All constitutional requirements met
7. ✅ **Verification**: All verification steps completed successfully

## Next Steps

After completing this plan:
1. Merge feature branch to main
2. Update changelog with new feature
3. Tag release with semantic version
4. Publish NuGet packages
5. Announce feature in documentation/blog

## References

- [ADR 0005: Rate Limiting](adr/0005-rate-limiting.md)
- [CLAUDE.md Constitutional Principles](../CLAUDE.md)
- [Token Bucket Algorithm](https://en.wikipedia.org/wiki/Token_bucket)
- [ASP.NET Core Rate Limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit)

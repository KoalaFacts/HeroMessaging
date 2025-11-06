# HeroMessaging Idempotency Framework

## Overview

The idempotency framework enables exactly-once processing semantics for at-least-once delivery scenarios by caching processing results. When a duplicate message is received, the cached response is returned without re-executing the handler.

## Status

**Phase 1 Complete** (2025-11-07):
- ✅ Core abstractions and interfaces
- ✅ Default implementation with in-memory storage
- ✅ Comprehensive unit test coverage (84 tests passing)
- ⏳ Configuration API (Phase 2)
- ⏳ SQL Server / PostgreSQL storage adapters (Phase 2)

## Core Concepts

### Response Caching
- **Success Responses**: Always cached (default TTL: 24 hours)
- **Failure Responses**: Cached if idempotent (default TTL: 1 hour)
- **TTL Expiration**: Automatic cleanup of expired entries

### Exception Classification

The `DefaultIdempotencyPolicy` classifies exceptions to determine caching behavior:

**Idempotent Failures (Cached)**:
- `ArgumentException` - Invalid input parameters
- `InvalidOperationException` - Business rule violations
- `NotSupportedException` - Unsupported operations
- `FormatException` - Invalid data formats
- `UnauthorizedAccessException` - Authorization failures
- `KeyNotFoundException` - Missing required data

**Non-Idempotent Failures (Not Cached)**:
- `TimeoutException` - Transient timeouts
- `TaskCanceledException` / `OperationCanceledException` - Cancellations
- `IOException` - I/O errors
- `HttpRequestException` / `SocketException` - Network errors
- **Unknown exceptions** - Conservative default (don't cache)

### Key Generation

**MessageId-Based (Default)**:
```
Format: idempotency:{MessageId}
Example: idempotency:3fa85f64-5717-4562-b3fc-2c963f66afa6
```

Advantages:
- Simple and deterministic
- Globally unique (leverages GUID)
- No hash computation overhead
- Easy to trace and debug

## Components

### Abstractions (`HeroMessaging.Abstractions.Idempotency`)

```csharp
// Storage abstraction
public interface IIdempotencyStore
{
    ValueTask<IdempotencyResponse?> GetAsync(string key, CancellationToken ct);
    ValueTask StoreSuccessAsync(string key, object? result, TimeSpan ttl, CancellationToken ct);
    ValueTask StoreFailureAsync(string key, Exception exception, TimeSpan ttl, CancellationToken ct);
    ValueTask<bool> ExistsAsync(string key, CancellationToken ct);
    ValueTask<int> CleanupExpiredAsync(CancellationToken ct);
}

// Key generation strategy
public interface IIdempotencyKeyGenerator
{
    string GenerateKey(IMessage message, ProcessingContext context);
}

// Policy configuration
public interface IIdempotencyPolicy
{
    TimeSpan SuccessTtl { get; }
    TimeSpan FailureTtl { get; }
    bool CacheFailures { get; }
    bool IsIdempotentFailure(Exception exception);
    IIdempotencyKeyGenerator KeyGenerator { get; }
}
```

### Implementations (`HeroMessaging.Idempotency`)

#### DefaultIdempotencyPolicy
Production-ready policy with sensible defaults:
- Success TTL: 24 hours
- Failure TTL: 1 hour
- Cache Failures: Enabled
- Key Generator: MessageIdKeyGenerator

#### InMemoryIdempotencyStore
Thread-safe in-memory storage using `ConcurrentDictionary`:
- Suitable for testing and non-persistent scenarios
- Automatic TTL-based expiration
- O(1) get/store operations
- O(n) cleanup operations

#### IdempotencyDecorator
Pipeline decorator that implements the idempotency logic:
- Checks cache before invoking handler
- Stores successful results
- Stores idempotent failures
- Returns cached responses for duplicates

## Usage Examples

### Creating a Custom Policy

```csharp
// Financial transactions: longer TTLs
var policy = new DefaultIdempotencyPolicy(
    successTtl: TimeSpan.FromDays(30),     // Regulatory compliance
    failureTtl: TimeSpan.FromDays(7),      // Allow investigation
    cacheFailures: true
);

// High-throughput events: shorter TTLs
var policy = new DefaultIdempotencyPolicy(
    successTtl: TimeSpan.FromHours(1),
    failureTtl: TimeSpan.FromMinutes(15),
    cacheFailures: true
);

// Disable failure caching (always retry)
var policy = new DefaultIdempotencyPolicy(
    cacheFailures: false
);
```

### Manual Decorator Construction

```csharp
// For testing or manual pipeline construction
var store = new InMemoryIdempotencyStore(TimeProvider.System);
var policy = new DefaultIdempotencyPolicy();
var logger = loggerFactory.CreateLogger<IdempotencyDecorator>();

var decorator = new IdempotencyDecorator(
    inner: actualHandler,
    store: store,
    policy: policy,
    logger: logger,
    timeProvider: TimeProvider.System
);

var result = await decorator.ProcessAsync(message, context, cancellationToken);
```

### Testing with FakeTimeProvider

```csharp
// Deterministic time control for TTL testing
var fakeTimeProvider = new FakeTimeProvider();
var store = new InMemoryIdempotencyStore(fakeTimeProvider);

// Store a response
await store.StoreSuccessAsync("key1", "result", TimeSpan.FromHours(1));

// Advance time beyond TTL
fakeTimeProvider.Advance(TimeSpan.FromHours(2));

// Entry should be expired
var cached = await store.GetAsync("key1");
Assert.Null(cached); // Expired
```

## Architecture

### Pipeline Position
The idempotency decorator should be positioned strategically:

```
Request → [1] ValidationDecorator      ← Validate first
        ↓
      [2] IdempotencyDecorator    ← Check cache early
        ↓
      [3] RetryDecorator          ← Don't retry cached responses
        ↓
      [4] CircuitBreakerDecorator ← Return cache even if circuit open
        ↓
      [5] Handler Execution
```

### Storage Providers

**Current**:
- `InMemoryIdempotencyStore` - Thread-safe, TTL-based expiration

**Planned (Phase 2)**:
- SQL Server: Persistent storage with indexed queries
- PostgreSQL: Persistent storage with JSON support
- Redis: Distributed cache with automatic TTL

## Performance Characteristics

### InMemoryIdempotencyStore
- **Get**: O(1) average case
- **Store**: O(1) average case
- **Cleanup**: O(n) where n is total entries
- **Memory**: ~500 bytes per cached response

### Performance Targets
- Cache hit latency: <0.5ms p99
- Cache miss overhead: <1ms p99
- Throughput: >100K messages/second
- Memory overhead: <1KB per cached response

## Testing

### Unit Tests
84 comprehensive unit tests covering:
- Policy configuration and customization
- Exception classification for all known types
- Key generation consistency and formats
- Storage operations (get, store, cleanup)
- TTL expiration behavior
- Concurrency and thread-safety
- Decorator pipeline behavior

Run tests:
```bash
dotnet test --filter "Category=Unit&FullyQualifiedName~Idempotency"
```

### Test Guidelines
- Use `FakeTimeProvider` for deterministic time control
- Test concurrency with `Task.WhenAll` and multiple threads
- Verify TTL expiration at boundaries (before, at, after)
- Test both cache hit and cache miss paths
- Verify exception reconstruction for cached failures

## Documentation

- **ADR**: [docs/adr/0005-idempotency-framework.md](../../../docs/adr/0005-idempotency-framework.md)
- **Guidelines**: [CLAUDE.md - Idempotency Framework Guidelines](../../../CLAUDE.md#idempotency-framework-guidelines)
- **Code Documentation**: Comprehensive XML docs on all public APIs

## Roadmap

### Phase 2: Integration (In Progress)
- Configuration API with fluent builder
- SQL Server storage adapter
- PostgreSQL storage adapter
- Integration tests
- Telemetry and logging

### Phase 3: Advanced Features
- Content hash key generator (SHA256-based)
- Composite key generator (multi-factor)
- Concurrency handling (processing locks)
- Background cleanup service
- Performance benchmarks

### Phase 4: Documentation & Polish
- User documentation with examples
- Sample projects
- Performance optimization
- NuGet package preparation

## Contributing

When contributing to the idempotency framework:
1. Follow TDD principles (write tests first)
2. Maintain 100% test coverage for public APIs
3. Use `FakeTimeProvider` for time-sensitive tests
4. Update this README and ADR for significant changes
5. Ensure SOLID principles and decorator pattern consistency

## References

- [RFC 7231 HTTP Semantics](https://datatracker.ietf.org/doc/html/rfc7231) - Idempotent methods
- [Stripe API Idempotency](https://stripe.com/docs/api/idempotent_requests)
- [AWS: Making Retries Safe with Idempotent APIs](https://aws.amazon.com/builders-library/making-retries-safe-with-idempotent-APIs/)
- [Microsoft: Idempotency Patterns](https://learn.microsoft.com/en-us/azure/architecture/patterns/idempotency)

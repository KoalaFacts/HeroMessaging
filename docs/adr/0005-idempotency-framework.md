# ADR 0005: Idempotency Framework

**Status**: In Progress (Phase 1 Complete)
**Date**: 2025-11-06
**Last Updated**: 2025-11-07
**Decision Makers**: Engineering Team
**Technical Story**: Implement comprehensive idempotency framework for at-least-once delivery guarantees

## Context and Problem Statement

HeroMessaging currently provides basic duplicate detection via the inbox pattern using `MessageId` matching. However, this approach has limitations:

1. **No Response Caching**: Duplicate messages are rejected, but the original response is not cached, preventing clients from retrieving results
2. **Limited Key Strategies**: Only supports `MessageId`-based deduplication, not content-based or custom keys
3. **No Failure Idempotency**: Failed operations retry without considering if the failure itself should be cached
4. **Storage Coupling**: Deduplication logic is tightly coupled to inbox storage implementation
5. **Incomplete Integration**: Idempotency concerns are not fully integrated into the message processing pipeline

These limitations prevent HeroMessaging from providing true **at-least-once processing with exactly-once semantics** required for:
- External API integration (retrying HTTP calls)
- Financial transactions (avoiding double charges)
- State mutations (preventing duplicate side effects)
- Distributed systems (handling network partitions and retries)

## Decision Drivers

### Constitutional Principles
- **Code Quality & Maintainability**: SOLID principles, decorator pattern consistency
- **Testing Excellence**: TDD approach, 80%+ coverage, performance verification
- **Performance & Efficiency**: <1ms overhead target, zero-allocation paths
- **Plugin Architecture**: Extensible storage backends
- **User Experience**: Intuitive API, actionable errors

### Technical Requirements
1. **Performance**: <0.5ms cache hit latency, <1ms cache miss overhead
2. **Scalability**: Support >100K messages/second throughput
3. **Reliability**: Guarantee exactly-once semantics for at-least-once delivery
4. **Flexibility**: Multiple key generation strategies (MessageId, content hash, custom)
5. **Storage Agnostic**: Work with in-memory, SQL Server, PostgreSQL, and future providers
6. **Observability**: Full telemetry integration for cache hits/misses
7. **Compatibility**: Integrate seamlessly with existing decorators (retry, error handling)

### Business Requirements
1. Enable reliable integration with external systems
2. Support financial and transactional use cases
3. Provide industry-standard idempotency patterns
4. Maintain backward compatibility with existing inbox pattern

## Considered Options

### Option 1: Extend Inbox Pattern with Response Caching
**Approach**: Add response caching to existing `InboxProcessor` and `IInboxStorage`

**Pros**:
- Minimal architectural changes
- Reuses existing storage infrastructure
- Simple to implement

**Cons**:
- Couples idempotency concerns to inbox pattern (not all messages use inbox)
- Limits flexibility for different key strategies
- Mixes concerns (message receipt vs result caching)
- Doesn't work for commands/queries/events that bypass inbox
- Violates Single Responsibility Principle

**Verdict**: ❌ Rejected - Too coupled, limited scope

### Option 2: Decorator-Based Idempotency Layer (Selected)
**Approach**: Create dedicated idempotency decorator in processing pipeline with pluggable storage

**Pros**:
- Follows existing architectural patterns (decorator-based pipeline)
- Separation of concerns (idempotency logic separate from message receipt)
- Works across all message types (commands, queries, events, inbox, outbox)
- Pluggable key generation strategies
- Extensible storage backends
- Composable with other decorators (retry, circuit breaker)
- Testable in isolation

**Cons**:
- Additional layer of abstraction
- Slightly more complex configuration

**Verdict**: ✅ **Selected** - Best architectural fit, maximum flexibility

### Option 3: Aspect-Oriented Programming (AOP) with Attributes
**Approach**: Use `[Idempotent]` attributes on handlers with interceptor framework

**Pros**:
- Declarative syntax
- No explicit decorator registration

**Cons**:
- Introduces new programming model (not consistent with existing patterns)
- Requires reflection or source generators
- Harder to test and debug
- Performance overhead from interception
- Less flexible for dynamic configuration

**Verdict**: ❌ Rejected - Inconsistent with existing patterns, performance concerns

### Option 4: Message Broker-Level Idempotency
**Approach**: Rely on message broker (RabbitMQ, Kafka) for deduplication

**Pros**:
- Offloads work to infrastructure
- No application-level code changes

**Cons**:
- Only works when using message brokers (not in-memory or direct calls)
- Limited to broker capabilities (no custom key strategies)
- No response caching (only duplicate suppression)
- Vendor lock-in
- Doesn't solve internal processing idempotency

**Verdict**: ❌ Rejected - Too limited, external dependency

## Decision Outcome

**Chosen option**: **Option 2: Decorator-Based Idempotency Layer**

We will implement a comprehensive idempotency framework consisting of:

### Core Components

#### 1. Abstractions (in `HeroMessaging.Abstractions`)
```csharp
namespace HeroMessaging.Abstractions.Idempotency;

// Core store interface
public interface IIdempotencyStore
{
    ValueTask<IdempotencyResponse?> GetAsync(string key, CancellationToken ct);
    ValueTask StoreSuccessAsync(string key, object? result, TimeSpan ttl, CancellationToken ct);
    ValueTask StoreFailureAsync(string key, Exception exception, TimeSpan ttl, CancellationToken ct);
    ValueTask<bool> ExistsAsync(string key, CancellationToken ct);
    ValueTask CleanupExpiredAsync(CancellationToken ct);
}

// Key generation strategy
public interface IIdempotencyKeyGenerator
{
    string GenerateKey(IMessage message, ProcessingContext context);
}

// Configuration policy
public interface IIdempotencyPolicy
{
    TimeSpan SuccessTtl { get; }
    TimeSpan FailureTtl { get; }
    bool CacheFailures { get; }
    bool IsIdempotentFailure(Exception exception);
    IIdempotencyKeyGenerator KeyGenerator { get; }
}

// Cached response
public sealed class IdempotencyResponse
{
    public required string IdempotencyKey { get; init; }
    public object? SuccessResult { get; init; }
    public string? FailureType { get; init; }
    public string? FailureMessage { get; init; }
    public DateTime StoredAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public IdempotencyStatus Status { get; init; }
}

public enum IdempotencyStatus
{
    Success,
    Failure,
    Processing // Lock for concurrent requests
}
```

#### 2. Implementation (in `HeroMessaging`)
```csharp
// Decorator in pipeline
public sealed class IdempotencyDecorator(
    IMessageProcessor inner,
    IIdempotencyStore store,
    IIdempotencyPolicy policy,
    ILogger<IdempotencyDecorator> logger,
    TimeProvider timeProvider) : MessageProcessorDecorator(inner)
{
    public override async ValueTask<ProcessingResult> ProcessAsync(
        IMessage message,
        ProcessingContext context,
        CancellationToken cancellationToken)
    {
        var key = policy.KeyGenerator.GenerateKey(message, context);

        // Check cache
        var cached = await store.GetAsync(key, cancellationToken);
        if (cached != null)
        {
            logger.IdempotentCacheHit(key, cached.Status);
            return cached.ToProcessingResult();
        }

        // Execute handler
        var result = await _inner.ProcessAsync(message, context, cancellationToken);

        // Store result
        if (result.Success)
        {
            await store.StoreSuccessAsync(key, result.Data, policy.SuccessTtl, cancellationToken);
        }
        else if (policy.CacheFailures && policy.IsIdempotentFailure(result.Exception))
        {
            await store.StoreFailureAsync(key, result.Exception!, policy.FailureTtl, cancellationToken);
        }

        return result;
    }
}

// Default key generators
public sealed class MessageIdKeyGenerator : IIdempotencyKeyGenerator
{
    public string GenerateKey(IMessage message, ProcessingContext context)
        => $"idempotency:{message.MessageId}";
}

public sealed class ContentHashKeyGenerator(IHashAlgorithm hasher) : IIdempotencyKeyGenerator
{
    public string GenerateKey(IMessage message, ProcessingContext context)
    {
        var content = JsonSerializer.Serialize(message);
        var hash = hasher.ComputeHash(content);
        return $"idempotency:hash:{hash}";
    }
}

public sealed class CompositeKeyGenerator(params IIdempotencyKeyGenerator[] generators)
    : IIdempotencyKeyGenerator
{
    public string GenerateKey(IMessage message, ProcessingContext context)
    {
        var keys = generators.Select(g => g.GenerateKey(message, context));
        return string.Join(":", keys);
    }
}
```

#### 3. Storage Implementations

**In-Memory** (for testing and non-persistent scenarios):
```csharp
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, IdempotencyResponse> _cache = new();
    private readonly TimeProvider _timeProvider;
}
```

**SQL Server / PostgreSQL** (for production):
```sql
CREATE TABLE IdempotencyStore (
    IdempotencyKey NVARCHAR(255) PRIMARY KEY,
    MessageId UNIQUEIDENTIFIER NOT NULL,
    Status INT NOT NULL, -- 0=Success, 1=Failure, 2=Processing
    ResultData NVARCHAR(MAX) NULL,
    FailureType NVARCHAR(255) NULL,
    FailureMessage NVARCHAR(MAX) NULL,
    StoredAt DATETIME2 NOT NULL,
    ExpiresAt DATETIME2 NOT NULL,
    INDEX IX_ExpiresAt (ExpiresAt),
    INDEX IX_MessageId (MessageId)
);
```

#### 4. Configuration API
```csharp
// Fluent builder extension
public static IHeroMessagingBuilder WithIdempotency(
    this IHeroMessagingBuilder builder,
    Action<IdempotencyBuilder>? configure = null)
{
    var idempotencyBuilder = new IdempotencyBuilder(builder.Services);

    // Defaults
    idempotencyBuilder
        .UseMessageIdKeyGenerator()
        .WithSuccessTtl(TimeSpan.FromHours(24))
        .WithFailureTtl(TimeSpan.FromHours(1))
        .WithInMemoryStore();

    configure?.Invoke(idempotencyBuilder);

    // Register decorator in pipeline
    builder.Services.TryAddEnumerable(
        ServiceDescriptor.Singleton<IMessageProcessorDecorator, IdempotencyDecorator>());

    return builder;
}

// Usage example
services.AddHeroMessaging(builder =>
{
    builder.WithIdempotency(idempotency =>
    {
        idempotency
            .UseSqlServerStore(connectionString)
            .UseContentHashKeyGenerator()
            .WithSuccessTtl(TimeSpan.FromDays(7))
            .CacheIdempotentFailures(ex => ex is not TransientException);
    });
});
```

### Architecture Integration

#### Pipeline Order
The idempotency decorator will be positioned strategically in the pipeline:

```
Request → [1] ValidationDecorator
        ↓
      [2] IdempotencyDecorator ← Check cache first
        ↓
      [3] RetryDecorator
        ↓
      [4] CircuitBreakerDecorator
        ↓
      [5] TransactionDecorator
        ↓
      [6] ErrorHandlingDecorator
        ↓
      [7] Handler Execution
```

**Rationale**:
- **After Validation**: Only cache valid messages
- **Before Retry**: Avoid retrying if cached response exists
- **Before Circuit Breaker**: Return cached response even if circuit is open
- **Before Transaction**: Avoid unnecessary transaction overhead

#### Inbox Integration
The existing inbox pattern will complement idempotency:

```csharp
// Inbox handles duplicate detection (reject duplicates)
// Idempotency handles response caching (return cached results)

public class EnhancedInboxOptions : InboxOptions
{
    public bool UseIdempotency { get; set; } = true;
    public IIdempotencyPolicy? IdempotencyPolicy { get; set; }
}
```

### Key Generation Strategies

#### Strategy 1: MessageId-Based (Default)
- **Key Format**: `idempotency:{MessageId}`
- **Use Case**: Standard message deduplication
- **Pros**: Simple, globally unique
- **Cons**: Doesn't detect semantically identical messages with different IDs

#### Strategy 2: Content Hash
- **Key Format**: `idempotency:hash:{SHA256(message)}`
- **Use Case**: Detect duplicate content with different MessageIds
- **Pros**: Detects semantic duplicates
- **Cons**: Hash computation overhead (~0.1ms), doesn't work for non-deterministic messages

#### Strategy 3: Composite (MessageId + CorrelationId + UserId)
- **Key Format**: `idempotency:{MessageId}:{CorrelationId}:{UserId}`
- **Use Case**: Multi-tenant systems, scoped idempotency
- **Pros**: Flexible, tenant-aware
- **Cons**: More complex key management

#### Strategy 4: Custom (User-Provided)
- **Key Format**: User-defined via `message.Metadata["IdempotencyKey"]`
- **Use Case**: External API integration (match client-provided keys)
- **Pros**: Client controls deduplication logic
- **Cons**: Requires client awareness

### Failure Handling Policy

Not all failures should be cached. The framework will classify exceptions:

```csharp
public class DefaultIdempotencyPolicy : IIdempotencyPolicy
{
    public bool IsIdempotentFailure(Exception exception) => exception switch
    {
        // Idempotent failures (cache these)
        ValidationException => true,
        BusinessRuleException => true,
        UnauthorizedException => true,
        NotFoundException => true,

        // Non-idempotent failures (retry these)
        TimeoutException => false,
        TransientException => false,
        NetworkException => false,

        _ => false // Conservative default: don't cache unknown failures
    };
}
```

### TTL Strategy

Different TTLs for different scenarios:

| Scenario | Success TTL | Failure TTL | Rationale |
|----------|-------------|-------------|-----------|
| **Financial Transactions** | 30 days | 7 days | Regulatory compliance, long audit trail |
| **API Calls** | 24 hours | 1 hour | Balance storage vs retry overhead |
| **Event Processing** | 1 hour | 15 minutes | Short-lived, high throughput |
| **Saga Compensation** | 7 days | 24 hours | Long-running processes |

### Concurrency Handling

Prevent duplicate processing of concurrent requests:

```csharp
// Optimistic locking approach
public async ValueTask<IdempotencyResponse?> GetAsync(string key, CancellationToken ct)
{
    var response = await _store.GetAsync(key, ct);

    if (response?.Status == IdempotencyStatus.Processing)
    {
        // Another request is processing, wait and retry
        await Task.Delay(50, ct);
        return await GetAsync(key, ct); // Recursive retry
    }

    return response;
}

// Set processing lock before handler execution
await _store.StoreLockAsync(key, TimeSpan.FromSeconds(30), ct);
```

### Observability Integration

Full telemetry support:

```csharp
// Metrics
- idempotency_cache_hit_total (counter)
- idempotency_cache_miss_total (counter)
- idempotency_store_latency_ms (histogram)
- idempotency_key_collision_total (counter)

// Logs
- Cache hit: "Idempotent request {IdempotencyKey} returned from cache"
- Cache miss: "Idempotent request {IdempotencyKey} processing"
- Cache store: "Stored idempotent response {IdempotencyKey} with TTL {Ttl}"

// Traces (OpenTelemetry)
- Span: "IdempotencyDecorator.ProcessAsync"
  - Attributes: idempotency.key, idempotency.cache_hit, idempotency.ttl
```

## Consequences

### Positive

1. **Exactly-Once Semantics**: Achieve exactly-once processing for at-least-once delivery
2. **Architectural Consistency**: Follows existing decorator pattern
3. **Flexibility**: Multiple key strategies, configurable policies
4. **Performance**: <0.5ms cache hit latency, minimal overhead
5. **Storage Agnostic**: Works with any storage provider
6. **Testability**: Easy to test in isolation with mocks
7. **Observability**: Full telemetry integration
8. **Backward Compatible**: Existing code continues to work

### Negative

1. **Storage Overhead**: Additional storage required for cached responses (estimated ~500 bytes/message)
2. **Complexity**: Additional layer in pipeline (mitigated by clear abstractions)
3. **TTL Management**: Requires periodic cleanup of expired entries
4. **Key Collisions**: Risk of false positives with hash-based keys (mitigated by using MessageId by default)

### Risks and Mitigation

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| **Storage Exhaustion** | High | Medium | Automatic TTL cleanup, monitoring, configurable TTLs |
| **Key Collisions** | Medium | Low | Use MessageId by default, SHA256 for hashes |
| **Performance Regression** | High | Low | Benchmarks in CI, <1ms overhead target, async operations |
| **Cache Poisoning** | Medium | Low | Validate before caching, separate TTL for failures |
| **Breaking Changes** | Medium | Low | Opt-in API, backward compatible, comprehensive tests |

## Implementation Plan

### Phase 1: Foundation (Week 1)
1. Create abstractions in `HeroMessaging.Abstractions.Idempotency`
2. Implement `IdempotencyDecorator` with basic key generation
3. Implement `InMemoryIdempotencyStore`
4. Write unit tests (80%+ coverage target)
5. Write ADR (this document)

### Phase 2: Integration (Week 2)
1. Implement `IdempotencyBuilder` configuration API
2. Add storage adapters for SQL Server and PostgreSQL
3. Integrate with `HeroMessagingBuilder` pipeline
4. Write integration tests
5. Add telemetry and logging

### Phase 3: Advanced Features (Week 3)
1. Implement content hash key generator
2. Implement composite key generator
3. Add concurrency handling (processing locks)
4. Add background cleanup task for expired entries
5. Write performance benchmarks

### Phase 4: Documentation & Polish (Week 4)
1. Write user documentation with examples
2. Add sample projects demonstrating patterns
3. Performance optimization based on benchmarks
4. Update CLAUDE.md with idempotency guidelines
5. Publish to NuGet

## Validation & Success Criteria

### Performance Benchmarks
- [ ] Cache hit latency: <0.5ms p99
- [ ] Cache miss overhead: <1ms p99
- [ ] Throughput: >100K messages/second with idempotency enabled
- [ ] Memory overhead: <1KB per cached response
- [ ] Storage cleanup: <100ms for 10K expired entries

### Test Coverage
- [ ] Unit tests: 80%+ coverage
- [ ] Integration tests: End-to-end scenarios
- [ ] Performance tests: Regression detection within 10%
- [ ] Contract tests: Public API stability

### Functional Requirements
- [ ] Idempotent success responses cached and retrievable
- [ ] Idempotent failures cached per policy configuration
- [ ] Multiple key generation strategies supported
- [ ] TTL expiration works correctly
- [ ] Concurrent request handling (no duplicate processing)
- [ ] Works with all storage providers (in-memory, SQL Server, PostgreSQL)

### Non-Functional Requirements
- [ ] Backward compatible with existing code
- [ ] Zero breaking changes to public APIs
- [ ] Full telemetry integration (logs, metrics, traces)
- [ ] Documentation complete with examples
- [ ] Constitutional compliance verified

## References

### Industry Standards
- **RFC 7231 HTTP Semantics**: Idempotent methods (GET, PUT, DELETE)
- **Stripe API**: `Idempotency-Key` header pattern
- **AWS SDK**: Idempotent API design patterns
- **PayPal API**: Idempotency best practices

### Internal Documents
- [ADR 0001: Message Scheduling](0001-message-scheduling.md)
- [ADR 0002: Transport Abstraction Layer](0002-transport-abstraction-layer.md)
- [ADR 0004: Saga Patterns](0004-saga-patterns.md)
- [CLAUDE.md](../../CLAUDE.md) - Constitutional principles

### External Resources
- [Microsoft: Idempotency patterns](https://learn.microsoft.com/en-us/azure/architecture/patterns/idempotency)
- [Martin Fowler: Idempotent Receiver](https://www.enterpriseintegrationpatterns.com/patterns/messaging/IdempotentReceiver.html)
- [AWS: Idempotency in distributed systems](https://aws.amazon.com/builders-library/making-retries-safe-with-idempotent-APIs/)

## Approval

**Status**: Awaiting approval
**Reviewers**: Engineering team, Architecture committee
**Target Decision Date**: 2025-11-08

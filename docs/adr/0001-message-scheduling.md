# ADR 0001: Message Scheduling

**Status**: Accepted
**Date**: 2025-10-26
**Deciders**: Development Team
**Context**: Phase 1 - Core Infrastructure Enhancements

## Context and Problem Statement

HeroMessaging currently lacks the ability to schedule messages for future delivery. This capability is essential for:
- Implementing timeout patterns in saga state machines
- Delayed retry logic with configurable backoff
- Scheduled recurring tasks (daily reports, cleanup jobs, etc.)
- Workflow orchestration requiring time-based transitions
- Job consumer implementations with retry delays

Without message scheduling, developers must implement their own timer-based solutions, leading to inconsistent patterns and potential reliability issues.

## Decision Drivers

* **Performance**: Must maintain <1ms p99 latency overhead for scheduling operations
* **Reliability**: Scheduled messages must not be lost (persistent storage option required)
* **Scalability**: Must support >100K scheduled messages with efficient retrieval
* **Flexibility**: Support both in-memory (development) and persistent (production) strategies
* **Testability**: Easy to test with deterministic time control
* **Integration**: Seamless integration with existing HeroMessaging patterns
* **Multi-framework**: Support netstandard2.0 through net10.0

## Considered Options

### Option 1: Timer-Based In-Memory Scheduler (Chosen for Development)
* **Pros**: Simple, fast, no external dependencies, deterministic for testing
* **Cons**: Messages lost on restart, not suitable for production, memory limited
* **Implementation**: Use `System.Threading.Timer` for delayed callbacks

### Option 2: Storage-Backed Polling Scheduler (Chosen for Production)
* **Pros**: Persistent, survives restarts, scalable with proper indexing
* **Cons**: Polling overhead, potential for drift, requires storage dependency
* **Implementation**: Background worker polls storage for due messages

### Option 3: Transport-Native Scheduling
* **Pros**: Leverages broker capabilities (RabbitMQ delayed exchange, Azure SB ScheduledEnqueueTime)
* **Cons**: Transport-specific, limits portability, not available for all transports
* **Decision**: Defer to transport implementations when available

### Option 4: External Scheduler (Quartz.NET, Hangfire)
* **Pros**: Battle-tested, feature-rich (cron expressions, clustering, etc.)
* **Cons**: Heavy dependency, additional infrastructure, complexity overkill for basic scheduling
* **Decision**: Not needed for initial implementation; can add as plugin later

## Decision Outcome

Implement **both Option 1 and Option 2** as pluggable strategies via `ISchedulingStrategy` abstraction:

1. **InMemoryScheduler**: For development, testing, and scenarios where message loss is acceptable
2. **StorageBackedScheduler**: For production with persistent storage

This hybrid approach provides flexibility while maintaining simplicity.

### Architecture Design

```
┌─────────────────────────────────────────────────────────────┐
│                     IMessageScheduler                       │
│  ScheduleAsync(), CancelScheduledAsync(), etc.             │
└─────────────────┬───────────────────────────────────────────┘
                  │
                  │ delegates to
                  ↓
┌─────────────────────────────────────────────────────────────┐
│                   ISchedulingStrategy                       │
│  (Strategy Pattern)                                         │
└──────────────┬──────────────────────────────────┬───────────┘
               │                                   │
               ↓                                   ↓
┌──────────────────────────┐      ┌───────────────────────────┐
│  InMemoryScheduler       │      │ StorageBackedScheduler    │
│  (Timer-based)           │      │ (Polling-based)           │
│  • System.Threading.Timer│      │ • IScheduledMessageStorage│
│  • ConcurrentDictionary  │      │ • Background worker       │
│  • Fast, ephemeral       │      │ • Persistent, reliable    │
└──────────────────────────┘      └───────────────────────────┘
```

### API Design

```csharp
// Core abstraction
public interface IMessageScheduler
{
    Task<ScheduleResult> ScheduleAsync<T>(T message, TimeSpan delay,
        SchedulingOptions? options = null, CancellationToken cancellationToken = default) where T : IMessage;

    Task<ScheduleResult> ScheduleAsync<T>(T message, DateTimeOffset deliverAt,
        SchedulingOptions? options = null, CancellationToken cancellationToken = default) where T : IMessage;

    Task<bool> CancelScheduledAsync(Guid scheduleId, CancellationToken cancellationToken = default);

    Task<ScheduledMessageInfo?> GetScheduledAsync(Guid scheduleId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScheduledMessageInfo>> GetPendingAsync(ScheduledMessageQuery? query = null,
        CancellationToken cancellationToken = default);
}

// Strategy interface (internal)
internal interface ISchedulingStrategy
{
    Task<ScheduleResult> ScheduleAsync(ScheduledMessage message, CancellationToken cancellationToken = default);
    Task<bool> CancelAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    Task<ScheduledMessageInfo?> GetAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduledMessageInfo>> GetPendingAsync(ScheduledMessageQuery? query = null,
        CancellationToken cancellationToken = default);
}

// Storage plugin interface (in Abstractions)
public interface IScheduledMessageStorage
{
    Task<ScheduledMessageEntry> AddAsync(ScheduledMessage message, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduledMessageEntry>> GetDueAsync(DateTimeOffset asOf, int limit = 100,
        CancellationToken cancellationToken = default);
    Task<ScheduledMessageEntry?> GetAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    Task<bool> CancelAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    Task<bool> MarkDeliveredAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    Task<bool> MarkFailedAsync(Guid scheduleId, string error, CancellationToken cancellationToken = default);
    Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduledMessageEntry>> QueryAsync(ScheduledMessageQuery query,
        CancellationToken cancellationToken = default);
}

// Value objects
public class ScheduledMessage
{
    public Guid ScheduleId { get; init; }
    public IMessage Message { get; init; }
    public DateTimeOffset DeliverAt { get; init; }
    public SchedulingOptions Options { get; init; }
}

public class SchedulingOptions
{
    public string? Destination { get; set; }
    public int Priority { get; set; } = 0;
    public Dictionary<string, object>? Metadata { get; set; }
}

public class ScheduleResult
{
    public Guid ScheduleId { get; init; }
    public DateTimeOffset ScheduledFor { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

public enum ScheduledMessageStatus
{
    Pending,
    Delivered,
    Cancelled,
    Failed
}
```

### Configuration

```csharp
// Builder extensions
services.AddHeroMessaging(builder => builder
    .WithScheduling(scheduling => scheduling
        .UseInMemory() // For development/testing
        // OR
        .UseStorageBacked(options => options
            .PollingInterval = TimeSpan.FromSeconds(1)
            .BatchSize = 100
            .MaxConcurrency = 10))
    .UseInMemoryStorage());

// Automatic scheduling via options
await messaging.Publish(new OrderTimeout { OrderId = "123" },
    options: new PublishOptions
    {
        ScheduleFor = DateTimeOffset.UtcNow.AddMinutes(30)
    });

// Manual scheduling
var scheduleResult = await scheduler.ScheduleAsync(
    message: new SendReminderEmail { UserId = "abc" },
    delay: TimeSpan.FromHours(24));
```

## Consequences

### Positive

* **Flexibility**: Developers can choose strategy based on environment (dev vs prod)
* **Testability**: InMemoryScheduler enables fast, deterministic tests
* **Reliability**: StorageBackedScheduler prevents message loss
* **Performance**: <1ms overhead for scheduling operation (in-memory add/timer creation)
* **Scalability**: Storage-backed approach scales with proper database indexing
* **Extensibility**: Plugin architecture allows custom storage implementations
* **Integration**: Seamless with existing HeroMessaging patterns (IMessage, options, etc.)

### Negative

* **Complexity**: Two implementations increase testing surface area
* **Polling Overhead**: StorageBackedScheduler requires background worker
* **Clock Drift**: Potential for minor timing inaccuracies in polling approach
* **Storage Dependency**: Production use requires persistent storage setup

### Mitigations

* **Complexity**: Comprehensive test coverage (unit + integration + performance benchmarks)
* **Polling Overhead**: Configurable polling interval with batching (default: 1s, batch: 100)
* **Clock Drift**: Document acceptable tolerance (±1 second for storage-backed)
* **Storage Dependency**: Provide in-memory implementation for quick starts

## Implementation Plan

1. **Phase 1**: Core abstractions (IMessageScheduler, ISchedulingStrategy, IScheduledMessageStorage)
2. **Phase 2**: InMemoryScheduler implementation with tests
3. **Phase 3**: IScheduledMessageStorage interface and in-memory implementation
4. **Phase 4**: StorageBackedScheduler with polling worker
5. **Phase 5**: Builder extensions and integration
6. **Phase 6**: Performance benchmarks and documentation

## Compliance Checklist

- [x] **TDD**: Tests will be written first for all components
- [x] **Coverage**: Target 80%+ (100% for public APIs)
- [x] **Performance**: <1ms scheduling overhead (in-memory), <10ms (storage-backed)
- [x] **Documentation**: XML docs for all public APIs
- [x] **Multi-framework**: netstandard2.0 compatibility
- [x] **SOLID**: Strategy pattern, interface segregation, dependency injection
- [x] **Plugin Architecture**: IScheduledMessageStorage as plugin interface
- [x] **Error Handling**: Actionable error messages with remediation steps
- [x] **Observability**: Integration with existing metrics/tracing

## References

* MassTransit Scheduling: https://masstransit.io/documentation/configuration/scheduling
* Quartz.NET: https://www.quartz-scheduler.net/
* Azure Service Bus Scheduled Messages: https://docs.microsoft.com/azure/service-bus-messaging/message-sequencing
* RabbitMQ Delayed Message Plugin: https://github.com/rabbitmq/rabbitmq-delayed-message-exchange

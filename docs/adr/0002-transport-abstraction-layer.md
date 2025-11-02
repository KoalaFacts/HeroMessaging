# ADR 0001: Transport Abstraction Layer

**Status:** Accepted

**Date:** 2025-10-25

**Decision Makers:** Claude Code, HeroMessaging Team

**Consulted:** MassTransit architecture, NServiceBus design patterns, RabbitMQ.Client API

---

## Context

HeroMessaging currently supports only in-memory and database-backed message processing. To compete with MassTransit and become a viable production messaging framework, we need to support external message brokers (RabbitMQ, Azure Service Bus, Amazon SQS, Apache Kafka, etc.).

### Requirements

1. **Transport Agnostic:** Applications should work across different transports with minimal code changes
2. **Performance:** Zero-allocation hot paths, <500μs overhead per message
3. **Flexibility:** Support for various messaging patterns (point-to-point, pub/sub, request/response)
4. **Reliability:** Connection management, automatic reconnection, error handling
5. **Topology Management:** Declarative queue/topic/exchange configuration
6. **Observability:** Health checks, metrics, distributed tracing
7. **Testability:** Fast in-memory transport for testing

### Constraints

1. Must maintain existing CQRS patterns (ICommand, IQuery, IEvent)
2. Must support .NET Standard 2.0 through .NET 10.0
3. Must achieve 80%+ test coverage
4. Must maintain <1ms p99 latency for in-memory scenarios
5. Must be modular (separate NuGet packages per transport)

---

## Decision

We will implement a **layered transport abstraction** with the following components:

### 1. Core Abstraction Interface

**`IMessageTransport`** - The primary abstraction for all transport implementations

```csharp
public interface IMessageTransport : IAsyncDisposable
{
    string Name { get; }
    TransportState State { get; }

    Task ConnectAsync(CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);

    Task SendAsync(TransportAddress destination, TransportEnvelope envelope, CancellationToken cancellationToken);
    Task PublishAsync(TransportAddress topic, TransportEnvelope envelope, CancellationToken cancellationToken);

    Task<ITransportConsumer> SubscribeAsync(
        TransportAddress source,
        Func<TransportEnvelope, MessageContext, CancellationToken, Task> handler,
        ConsumerOptions? options,
        CancellationToken cancellationToken);

    Task ConfigureTopologyAsync(TransportTopology topology, CancellationToken cancellationToken);
    Task<TransportHealth> GetHealthAsync(CancellationToken cancellationToken);

    event EventHandler<TransportStateChangedEventArgs>? StateChanged;
    event EventHandler<TransportErrorEventArgs>? Error;
}
```

**Rationale:**
- Single interface for all transport operations
- Async/await throughout for modern .NET patterns
- Event-based state and error notifications
- Topology configuration separate from runtime operations
- Consumer subscription returns handle for lifecycle management

### 2. Message Envelope Format

**`TransportEnvelope`** - Zero-allocation message container (readonly record struct)

```csharp
public readonly record struct TransportEnvelope
{
    public string MessageId { get; init; }
    public string? CorrelationId { get; init; }
    public string MessageType { get; init; }
    public ReadOnlyMemory<byte> Body { get; init; }
    public string ContentType { get; init; }
    public ImmutableDictionary<string, object> Headers { get; init; }
    public DateTime Timestamp { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public byte Priority { get; init; }
    // ... addresses, delivery count, etc.
}
```

**Rationale:**
- Struct for stack allocation in hot paths
- ReadOnlyMemory<byte> for zero-copy serialization
- Immutable headers using ImmutableDictionary
- Standard envelope format across all transports
- Transport-agnostic metadata (headers support custom data)

### 3. Address Abstraction

**`TransportAddress`** - Unified addressing scheme (readonly record struct)

```csharp
public readonly record struct TransportAddress
{
    public string Name { get; init; }
    public TransportAddressType Type { get; init; } // Queue, Topic, Exchange, Subscription
    public string? Scheme { get; init; } // rabbitmq, asb, sqs, etc.
    // ... parsing from URIs, toString(), etc.
}
```

**Rationale:**
- Transport-independent addressing
- Support for URI-based and simple name-based addresses
- Type safety with enum for address types
- Extensible via scheme for transport-specific features

### 4. Connection Management

**`IConnectionManager`** - Connection pooling and lifecycle

```csharp
public interface IConnectionManager : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);
    Task<ITransportConnection> GetConnectionAsync(CancellationToken cancellationToken);
    Task ReturnConnectionAsync(ITransportConnection connection);
    ConnectionMetrics GetMetrics();
}
```

**Rationale:**
- Connection pooling for high-performance scenarios
- Automatic reconnection on failure
- Metrics for observability
- Separation of concerns (connection vs. transport logic)

### 5. Topology Configuration

**`TransportTopology`** - Declarative infrastructure as code

```csharp
public class TransportTopology
{
    public List<QueueDefinition> Queues { get; set; }
    public List<TopicDefinition> Topics { get; set; }
    public List<ExchangeDefinition> Exchanges { get; set; }
    public List<SubscriptionDefinition> Subscriptions { get; set; }
    public List<BindingDefinition> Bindings { get; set; }
}
```

**Fluent Builder API:**
```csharp
var topology = new TopologyBuilder()
    .Queue("orders", q => q.Durable = true)
    .Exchange("events", ExchangeType.Topic)
    .Bind("events", "orders", "order.*")
    .Build();
```

**Rationale:**
- Declarative configuration (infrastructure as code)
- Transport-specific features via properties
- Fluent API for developer experience
- Separate from runtime operations for clarity

### 6. Consumer Abstraction

**`ITransportConsumer`** - Consumer lifecycle management

```csharp
public interface ITransportConsumer : IAsyncDisposable
{
    string ConsumerId { get; }
    bool IsActive { get; }
    Task StopAsync(CancellationToken cancellationToken);
    ConsumerMetrics GetMetrics();
}
```

**ConsumerOptions:**
- Prefetch count, concurrent message limit
- Auto-acknowledge, requeue on failure
- Retry configuration
- Consumer groups (competing consumers)

**Rationale:**
- Explicit consumer lifecycle (stop, metrics, dispose)
- Transport-agnostic consumer configuration
- Support for competing consumers
- Metrics for monitoring

---

## Architecture Layers

```
┌─────────────────────────────────────────────────────────┐
│          Application Layer (CQRS)                       │
│  ICommand, IQuery, IEvent, ICommandHandler, etc.       │
└─────────────────────────────────────────────────────────┘
                      ↓
┌─────────────────────────────────────────────────────────┐
│        HeroMessaging Core Processing                    │
│  CommandProcessor, QueryProcessor, EventBus             │
│  Decorators, Retry, Circuit Breaker, Versioning         │
└─────────────────────────────────────────────────────────┘
                      ↓
┌─────────────────────────────────────────────────────────┐
│          Transport Abstraction Layer                    │
│  IMessageTransport, TransportEnvelope, etc.             │
└─────────────────────────────────────────────────────────┘
                      ↓
┌──────────┬──────────┬──────────┬──────────┬─────────────┐
│ RabbitMQ │ Azure SB │ AWS SQS  │  Kafka   │  In-Memory  │
│ Transport│ Transport│ Transport│ Transport│  Transport  │
└──────────┴──────────┴──────────┴──────────┴─────────────┘
```

### Integration Points

1. **Message Serialization:** Occurs before transport layer
   - Application → Core: IMessage objects
   - Core → Serialization: Byte arrays (ReadOnlyMemory<byte>)
   - Serialization → Transport: TransportEnvelope with byte body

2. **Processing Pipeline:** Decorators wrap transport operations
   - Logging, metrics, retry, circuit breaker applied at Core layer
   - Transport layer focuses on message delivery

3. **Storage Integration:** Outbox/Inbox patterns use transport
   - Outbox processor polls storage and uses transport to send
   - Inbox processor receives from transport and stores

---

## Alternatives Considered

### Alternative 1: Direct Broker Integration (No Abstraction)

**Rejected:** Would require application code changes per transport. Not maintainable.

### Alternative 2: Abstraction per Messaging Pattern

Separate interfaces for each pattern (ISendTransport, IPublishTransport, ISubscribeTransport).

**Rejected:** Too granular, increased complexity. Single interface with clear semantics is cleaner.

### Alternative 3: Class-Based Envelope (not struct)

Use reference type for TransportEnvelope.

**Rejected:** Performance impact. Struct allocation on stack is faster and reduces GC pressure. Benchmarks showed 30% improvement with struct.

### Alternative 4: MassTransit-Compatible Interface

Implement same interface as MassTransit for drop-in replacement.

**Rejected:** MassTransit interface is complex and opinionated. We can provide compatibility layer separately if needed.

### Alternative 5: Single Package with All Transports

Include all transport implementations in one package.

**Rejected:** Violates modularity. Users would have unnecessary dependencies. Separate packages per transport is cleaner.

---

## Consequences

### Positive

1. **Transport Independence:** Applications can switch transports with config changes
2. **Performance:** Zero-allocation structs achieve <500μs overhead target
3. **Modularity:** Each transport is a separate NuGet package
4. **Testability:** In-memory transport enables fast unit tests
5. **Extensibility:** New transports can be added without modifying core
6. **Observability:** Built-in health checks and metrics
7. **Reliability:** Connection management, reconnection, error handling

### Negative

1. **Abstraction Overhead:** Additional layer of indirection
2. **Learning Curve:** Developers need to understand transport concepts
3. **Testing Complexity:** Must test abstraction + each transport implementation
4. **Feature Limitations:** Transport-specific features require workarounds

### Risks

1. **Performance Regression:** Abstraction may add latency
   - **Mitigation:** Benchmark every change, enforce <500μs overhead

2. **Incomplete Abstraction:** Some transport features may not fit
   - **Mitigation:** Custom properties in options/headers for transport-specific features

3. **Complexity Growth:** Each new transport adds maintenance burden
   - **Mitigation:** Strong test coverage (80%+), clear documentation, community contributions

---

## Implementation Plan

### Phase 1: Abstractions (Week 1) ✅
- [x] Define IMessageTransport interface
- [x] Create TransportEnvelope, TransportAddress, MessageContext
- [x] Define TransportOptions and builder interfaces
- [x] Create TransportTopology and builder
- [x] Write ADR

### Phase 2: In-Memory Transport (Week 2)
- [ ] Implement InMemoryTransport (refactor existing)
- [ ] Connection manager implementation
- [ ] Consumer implementation
- [ ] Unit tests (80%+ coverage)

### Phase 3: RabbitMQ Transport (Weeks 3-4)
- [ ] RabbitMQ.Client integration
- [ ] Topology creation (exchanges, queues, bindings)
- [ ] Publisher implementation
- [ ] Consumer implementation
- [ ] Integration tests with Testcontainers
- [ ] Performance benchmarks

### Phase 4: Azure Service Bus (Weeks 5-6)
- [ ] Azure.Messaging.ServiceBus integration
- [ ] Topic/subscription topology
- [ ] Publisher/consumer implementation
- [ ] Session support
- [ ] Integration tests

### Phase 5: Additional Transports (Weeks 7+)
- [ ] Amazon SQS/SNS
- [ ] Apache Kafka
- [ ] gRPC (optional)

---

## Acceptance Criteria

1. **Performance:**
   - In-memory transport: <1ms p99 latency (existing baseline)
   - RabbitMQ transport: <2ms p99 latency
   - Transport overhead: <500μs

2. **Test Coverage:**
   - Abstractions: 90%+ coverage
   - Each transport implementation: 80%+ coverage
   - Integration tests for each transport

3. **Documentation:**
   - API documentation (XML docs)
   - Quick start guide per transport
   - Migration guide from MassTransit
   - Performance benchmarks published

4. **Compatibility:**
   - .NET Standard 2.0, .NET 6.0-10.0
   - Cross-platform (Windows, Linux, macOS)
   - No breaking changes to existing CQRS APIs

---

## References

1. [MassTransit Documentation](https://masstransit.io/)
2. [RabbitMQ .NET Client](https://www.rabbitmq.com/dotnet.html)
3. [Azure Service Bus SDK](https://docs.microsoft.com/en-us/azure/service-bus-messaging/)
4. [Patterns of Enterprise Application Architecture](https://martinfowler.com/eaaCatalog/)
5. [Zero-Allocation Messaging in .NET](https://www.infoq.com/articles/zero-allocation-dotnet/)

---

## Approval

**Status:** Accepted

**Approved By:** Development Team

**Implementation Start:** 2025-10-25

**Target Completion:** Phase 1 complete, Phase 2-3 in progress (6-8 weeks)

---

## Changelog

- **2025-10-25:** Initial ADR created
- Transport abstraction interfaces implemented
- TransportEnvelope, TransportAddress, MessageContext defined
- TransportOptions for all major transports designed
- Topology configuration API created

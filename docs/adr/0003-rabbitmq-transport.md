# ADR 0002: RabbitMQ Transport Implementation

**Date**: 2025-10-27
**Status**: Accepted
**Context**: Implementation of RabbitMQ message transport for production deployments
**Decision Makers**: Architecture Team
**Related ADRs**: [ADR 0001: Message Scheduling](./0001-message-scheduling.md)

---

## Context and Problem Statement

HeroMessaging currently supports only an in-memory transport suitable for development and testing. To enable production deployments, we need a robust, production-grade message transport implementation. RabbitMQ is the most popular open-source message broker and is widely adopted across industries.

**Key Requirements:**
- Production-ready reliability and performance
- Full integration with HeroMessaging's transport abstractions
- Connection pooling and channel management
- Support for publisher confirms and acknowledgments
- Topology management (exchanges, queues, bindings)
- Automatic reconnection on failures
- Dead letter queue support
- Message prioritization and TTL

---

## Decision Drivers

1. **Production Readiness**: Need a battle-tested broker for production use
2. **Industry Adoption**: RabbitMQ is the most widely used OSS message broker
3. **Feature Richness**: Advanced routing, DLQ, priority queues, TTL, etc.
4. **Performance**: 20,000+ messages/second single node capability
5. **Ecosystem**: Excellent tooling, documentation, and community support
6. **Multi-Protocol**: AMQP 0-9-1, AMQP 1.0, STOMP, MQTT support
7. **Cloud Availability**: Available on all major cloud platforms
8. **Cost**: Free and open-source

---

## Considered Options

### Option 1: RabbitMQ.Client (Official .NET Client) ⭐ **SELECTED**

**Pros:**
- Official library maintained by RabbitMQ team
- Full AMQP 0-9-1 protocol support
- Excellent performance and reliability
- Active development and community support
- Connection pooling built-in
- Publisher confirms and consumer acknowledgments
- Comprehensive API for topology management
- Well-documented and battle-tested

**Cons:**
- Lower-level API requires more implementation code
- Need to implement connection pooling ourselves
- Channel lifecycle management responsibility

### Option 2: EasyNetQ (High-Level Wrapper)

**Pros:**
- Simpler, more opinionated API
- Built-in connection management
- Convention-based routing
- Good for rapid development

**Cons:**
- Additional abstraction layer adds overhead
- Less control over RabbitMQ features
- Opinionated patterns may conflict with HeroMessaging
- Extra dependency to maintain
- May not expose all RabbitMQ capabilities

### Option 3: MassTransit.RabbitMQ (Extract Transport)

**Pros:**
- Proven, production-tested transport
- Comprehensive feature set
- Well-optimized

**Cons:**
- Heavy dependency (entire MassTransit framework)
- Tight coupling to MassTransit abstractions
- Defeats purpose of creating HeroMessaging
- License compatibility concerns

---

## Decision Outcome

**Chosen Option**: **Option 1 - RabbitMQ.Client (Official .NET Client)**

We will use the official `RabbitMQ.Client` NuGet package directly, implementing our own connection pooling and channel management optimized for HeroMessaging's patterns.

### Rationale

1. **Full Control**: Direct access to all RabbitMQ features
2. **Performance**: No middleware overhead, optimal performance
3. **Maintainability**: Official library with long-term support
4. **Flexibility**: Can implement patterns specific to HeroMessaging
5. **Learning**: Team gains deep RabbitMQ knowledge
6. **Future-Proof**: Easy to add advanced features as needed

---

## Implementation Strategy

### 1. Connection Management

```csharp
// Connection Pool Architecture
IConnectionFactory
  └─> ConnectionPool (1-N connections)
       └─> Connection (persistent, auto-reconnect)
            └─> Channel Pool (1-N channels per connection)
                 └─> Channel (short-lived, request-scoped)
```

**Design Decisions:**
- **Connection Pooling**: Maintain pool of persistent connections (default: 1-10)
- **Channel Pooling**: Pool channels per connection (lightweight, fast creation)
- **Reconnection Strategy**: Exponential backoff with jitter (5s → 30s → 60s max)
- **Health Monitoring**: Heartbeat-based connection health checks (60s interval)
- **Thread Safety**: Connections are thread-safe, channels are not (use pooling)

### 2. Publisher Confirms

**Pattern**: Async publisher confirms with timeout

```csharp
// Enable publisher confirms on channel
channel.ConfirmSelect();

// Publish with confirmation
channel.BasicPublish(exchange, routingKey, props, body);
bool confirmed = channel.WaitForConfirms(timeout);
```

**Configuration:**
- Default timeout: 10 seconds
- Configurable via `RabbitMqTransportOptions.PublisherConfirmTimeout`
- Optional: Can disable for fire-and-forget scenarios

### 3. Consumer Pattern

**Pattern**: Push-based async consumers (EventingBasicConsumer)

```csharp
var consumer = new AsyncEventingBasicConsumer(channel);
consumer.Received += async (sender, ea) =>
{
    try
    {
        await handler(envelope, context, ct);
        channel.BasicAck(ea.DeliveryTag, multiple: false);
    }
    catch
    {
        channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
    }
};

channel.BasicConsume(queue, autoAck: false, consumer);
```

**Design Decisions:**
- **Push Model**: More efficient than polling (long-lived consumers)
- **Manual Acknowledgment**: Reliability over throughput
- **Prefetch Count**: Configurable (default: 10) for flow control
- **Nack with Requeue**: Transient failures trigger redelivery
- **Concurrent Processing**: Multiple consumers per queue supported

### 4. Topology Management

**Strategy**: Declarative topology with idempotent operations

```csharp
// Exchanges
channel.ExchangeDeclare(name, type, durable, autoDelete, arguments);

// Queues
channel.QueueDeclare(name, durable, exclusive, autoDelete, arguments);

// Bindings
channel.QueueBind(queue, exchange, routingKey, arguments);
```

**Design Decisions:**
- **Idempotency**: Redeclaring existing topology is safe (validates compatibility)
- **Startup Validation**: Verify topology on startup if `ValidateTopologyOnStartup = true`
- **Auto-Creation**: Create missing topology if `CreateTopologyIfNotExists = true`
- **Dead Letter Queues**: Support via queue arguments (`x-dead-letter-exchange`)

### 5. Error Handling & Resilience

**Strategies:**

| Scenario | Strategy |
|----------|----------|
| Connection Lost | Auto-reconnect with exponential backoff |
| Channel Error | Close channel, create new from pool |
| Publish Timeout | Retry with backoff (configurable attempts) |
| Consumer Error | Nack + requeue (transient), Nack + DLQ (persistent) |
| Topology Mismatch | Fail fast with clear error message |

### 6. Message Routing Patterns

**Supported Patterns:**

1. **Point-to-Point** (SendAsync):
   ```
   Producer → Exchange (default/direct) → Queue → Consumer
   ```

2. **Pub/Sub** (PublishAsync):
   ```
   Producer → Exchange (topic/fanout) → Multiple Queues → Multiple Consumers
   ```

3. **Routing** (topic exchange):
   ```
   Producer → Exchange (topic) → Queues (via routing keys) → Consumers
   ```

---

## Technical Decisions

### TD-1: Connection Pooling Strategy

**Decision**: Lazy connection pool with min/max bounds

- **Min Connections**: 1 (default, configurable)
- **Max Connections**: 10 (default, configurable)
- **Creation Strategy**: Create on demand up to max
- **Idle Timeout**: 5 minutes (configurable)
- **Distribution**: Round-robin across connections

**Rationale**: Balance between resource usage and performance. Most scenarios need 1-2 connections.

### TD-2: Channel Lifecycle

**Decision**: Short-lived, pooled channels per connection

- **Creation**: Fast (~1ms), create on demand
- **Pooling**: Object pool pattern with ArrayPool backing
- **Disposal**: Return to pool after operation
- **Timeout**: 30 seconds max lifetime before recreation

**Rationale**: Channels are lightweight. Pooling reduces GC pressure while maintaining safety.

### TD-3: Message Serialization

**Decision**: Delegate to HeroMessaging's serialization abstractions

- **Format**: Determined by configured serializer (JSON, MessagePack, Protobuf)
- **Content-Type**: Set in message properties for interoperability
- **Encoding**: UTF-8 for text formats, binary for binary formats

**Rationale**: Maintain consistency with rest of HeroMessaging. Allow format flexibility.

### TD-4: Error Classification

**Decision**: Distinguish transient vs. permanent failures

```csharp
// Transient (retry with backoff)
- Connection lost
- Timeout
- Broker unavailable
- Resource exhausted (prefetch limit)

// Permanent (fail fast or DLQ)
- Serialization error
- Invalid message format
- Topology mismatch
- Authorization error
```

**Rationale**: Proper error handling improves reliability and debuggability.

### TD-5: Observability Integration

**Decision**: Emit metrics and traces for all operations

**Metrics:**
- Connection pool stats (active, idle, waiting)
- Publish/consume rates
- Confirm/ack/nack counts
- Error rates by type
- Message latency (end-to-end)

**Traces:**
- Distributed tracing via OpenTelemetry
- Correlation IDs propagated in message headers
- Trace context injection/extraction

**Rationale**: Production observability is critical for debugging and SLA monitoring.

---

### TD-6: Consumer Retry Policy on Handler Failure (2026-11-06)

**Decision**: `ConsumerOptions.MessageRetryPolicy` governs RabbitMQ direct-consumer handler failures; the earlier immediate, unbounded requeue is replaced.

- With `RequeueOnFailure = true` (default), the consumer computes retry *n* = previous retries + 1. While `RetryPolicy.ShouldRetry(n)` holds it waits `CalculateDelay(n)` in process (the delivery stays unacknowledged and holds a prefetch slot, as `DeferAsync` does) and then NACKs with requeue. Stopping the consumer during the wait requeues at once.
- When retries are exhausted, or with `RequeueOnFailure = false`, it NACKs without requeue and counts `MessagesDeadLettered`. The broker retains the message only if the queue has a dead-letter exchange (`QueueDefinition.DeadLetterExchange`); otherwise RabbitMQ discards it.
- `MaxAttempts` counts retries after the first delivery: `0` never retries, `-1` retries forever. `TransportEnvelope.DeliveryCount` exposes previous retries to handlers.
- Retry state: a broker `x-delivery-count` header (quorum queues) is used as a floor and survives consumer restarts. It counts every return to the queue (handler failures, `DeferAsync`, `RejectAsync(requeue: true)`, channel loss, a stop during backoff), so those also use up the retry budget. RabbitMQ 4 quorum queues additionally enforce their own `delivery-limit` (default 20) and dead-letter or drop the message when it is reached; set `x-delivery-limit` above `MaxAttempts` (or disable it for Inbox queues).
- Classic queues carry no count, so the consumer keeps one per message ID in memory (atomic increments; bounded at 10,000 IDs, least recently recorded evicted). Copies with the same ID share it, and a success clears it. It resets when the consumer restarts or another consumer receives the message, so on classic queues `MaxAttempts` bounds retries per consumer instance, not globally. A message with neither an ID nor a broker count gets one retry and is rejected when it comes back redelivered.
- `InboxTransportSubscription` overrides the policy to unlimited retries with the configured backoff so an Inbox write failure does not reject the delivery (a quorum queue's own delivery limit still applies). The in-memory consumer stops unlimited retries when it is stopped, because a stop skips retry delays.
- This is separate from Outbox retries (`OutboxOptions.MaxRetries`), which count failed publishes from the PostgreSQL Outbox to the broker.

**Consequence**: a default consumer now dead-letters a persistently failing message after 3 retries (5 s, 10 s, 20 s) instead of redelivering it forever; configure a dead-letter exchange to keep it. Unit tests: `RabbitMqConsumerRetryPolicyTests`, `ConsumerRetryAttemptsTests`; integration (RabbitMQ 3.13): `RabbitMqErrorScenarioIntegrationTests.Consumer_WhenHandlerKeepsFailing_RetriesWithBackoffThenDeadLetters` (classic queue) and `Consumer_OnQuorumQueueAfterConsumerRestart_ContinuesRetryCountThenDeadLetters`.

---

### TD-7: Dead-Letter Replay (2026-11-06)

**Decision**: `RabbitMqTransport.ReplayDeadLettersAsync(deadLetterQueue, options)` moves retained messages from a dead-letter queue back to the queue that rejected them, for operators to run after the cause is fixed. It is an explicit call, not a background process.

- The source queue is the first (most recent) `x-death` entry's `queue`, falling back to `x-first-death-queue`; `DeadLetterReplayOptions.TargetQueue` overrides it. A message with neither (for example placed in the queue by hand) is held unacknowledged until the call ends, then returned to the dead-letter queue and counted in `DeadLetterReplayResult.Unresolved`.
- Each message is fetched with `basic.get` (manual acknowledgement), published to the default exchange with mandatory routing and a publisher confirmation, and acknowledged on the dead-letter queue only after the confirmation. Publisher confirms are therefore required. An unroutable target throws `QueueDeliveryException`; any failure closes the replay channel, which returns every unacknowledged message to the dead-letter queue. Messages already replayed stay replayed.
- Delivery is at least once: a process stop between the confirmation and the acknowledgement leaves both copies, and the next call replays the dead-letter copy again.
- The replayed copy keeps the body, properties and `x-death` history, drops the broker's `x-delivery-count` and increments a `hero-replay-count` header. Dropping `x-delivery-count` matters for quorum dead-letter queues: a message returned to one by a failed replay comes back with `x-delivery-count`, which the consumer (TD-6) would otherwise count against the replayed message's new retry budget.
- `MaxMessages` (default 100) bounds the messages taken per call, including unresolved ones, so replaying into a queue that dead-letters into the same dead-letter queue cannot loop.

**Consequence**: retained failures can be replayed without broker plugins or hand-written shovels. The consumer's exception is not yet attached to the dead-lettered message; the broker's `x-death` (queue, reason, time, count) and the consumer's error log (message ID, queue, exception) are the failure context. Integration (RabbitMQ 3.13): `RabbitMqDeadLetterReplayIntegrationTests` (7 tests); unit: `DeadLetterReplayHeadersTests`, `RabbitMqDeadLetterReplayValidationTests`.

---

## Architectural Consequences

### Positive Consequences

1. ✅ **Production Ready**: Enables real-world deployments
2. ✅ **Performance**: 20,000+ msg/s throughput capability
3. ✅ **Reliability**: Persistent messaging with guarantees
4. ✅ **Scalability**: Horizontal scaling via competing consumers
5. ✅ **Flexibility**: Rich routing and topology options
6. ✅ **Ecosystem**: Wide tooling and hosting support
7. ✅ **Patterns**: Foundation for other AMQP transports

### Negative Consequences

1. ❌ **Complexity**: More complex than in-memory transport
2. ❌ **Dependency**: External broker infrastructure required
3. ❌ **Learning Curve**: Team needs RabbitMQ knowledge
4. ❌ **Operational Overhead**: Broker monitoring and maintenance

### Mitigation Strategies

- **Complexity**: Comprehensive documentation and examples
- **Dependency**: Docker Compose for local development
- **Learning Curve**: Training sessions and reference implementations
- **Operations**: Health check integration, alerts, runbooks

---

## Compliance & Standards

- **AMQP 0-9-1**: Full protocol compliance
- **Constitutional Principles**:
  - ✅ TDD approach (write tests first)
  - ✅ 80%+ code coverage target
  - ✅ Performance: <5ms publish latency (p99)
  - ✅ Plugin architecture maintained

---

## Implementation Checklist

- [x] Research transport abstractions
- [x] Create HeroMessaging.Transport.RabbitMQ project
- [x] Implement connection pool (`RabbitMqConnectionPool`)
- [x] Implement channel pool (`RabbitMqChannelPool`)
- [x] Implement transport (`RabbitMqTransport : IMessageTransport`)
- [x] Implement consumer (`RabbitMqConsumer : ITransportConsumer`)
- [x] Implement topology configuration
- [x] Add builder extensions (`.WithRabbitMq()`)
- [x] Write unit tests (112 tests with mocking)
- [x] Write integration tests (31 tests with Testcontainers)
- [ ] Performance benchmarks (deferred to follow-up PR)
- [ ] Documentation and examples (deferred to follow-up PR)

---

## References

- [RabbitMQ .NET Client Guide](https://www.rabbitmq.com/dotnet-api-guide.html)
- [AMQP 0-9-1 Protocol Specification](https://www.rabbitmq.com/resources/specs/amqp0-9-1.pdf)
- [RabbitMQ Best Practices](https://www.rabbitmq.com/best-practices.html)
- [Connection and Channel Lifecycle](https://www.rabbitmq.com/api-guide.html#connection-and-channel-lifecycle)
- [Publisher Confirms](https://www.rabbitmq.com/confirms.html)

---

## Version History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-10-27 | Claude Code | Initial ADR for RabbitMQ transport |
| 1.1 | 2026-11-06 | HeroMessaging maintainers | TD-6 consumer retry policy |

---

**Next Steps**: Proceed with project creation and core implementation following this architecture.

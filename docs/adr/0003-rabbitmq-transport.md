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

- The source queue is the first (most recent) `x-death` entry's `queue`, falling back to `x-first-death-queue`; `DeadLetterReplayOptions.TargetQueue` overrides it (it may not be the dead-letter queue itself). A message whose history names no queue, names the dead-letter queue, or names a queue that no longer routes (deleted or renamed) is counted in `DeadLetterReplayResult.Unresolved` and moved to the back of the dead-letter queue: republished there with a confirmation and then acknowledged, not requeued, because a quorum queue counts a requeue against its delivery limit (default 20; RabbitMQ's quorum queue guide says `basic.nack` returns stop counting from 4.3) and would eventually drop a message that was never replayed. Unresolved messages are held until the fetch ends and moved afterwards, so a high-priority one is not fetched again by the same call (a priority queue keeps their priority). The rest of the queue is still replayed.
- A call looks at no more messages than the dead-letter queue held at its start. It first takes up to `MaxMessages` (default 100) replayable messages with `basic.get` and holds them unacknowledged, then publishes each to the default exchange with mandatory routing and a publisher confirmation and acknowledges it on the dead-letter queue only after the confirmation. Taking the batch first means a replayed message that fails again during the call (which a priority queue can put ahead of older messages) cannot be fetched twice by it. Unresolved messages do not count against `MaxMessages`. Publisher confirms are required. An explicit target that does not route throws `QueueDeliveryException`; any failure or cancellation closes the replay channel, which returns every held message to the dead-letter queue. Messages already replayed stay replayed. A missing dead-letter queue throws `InvalidOperationException`.
- Delivery is at least once: a process stop, connection loss or confirmation timeout after the broker accepted the copy leaves the dead-letter copy too, and a later call replays it again; the same windows can leave an unresolved message twice in the dead-letter queue. `MaxMessages` also bounds the message bodies held in memory.
- The replayed copy keeps the body, properties and `x-death` history, drops `UserId` (the broker rejects a copy claiming another user), drops the broker's `x-delivery-count` and increments a `hero-replay-count` header. Dropping `x-delivery-count` matters for quorum dead-letter queues: a message returned to one by a failed replay comes back with `x-delivery-count`, which the consumer (TD-6) would otherwise count against the replayed message's new retry budget. RabbitMQ removes a per-message `expiration` when dead-lettering (kept as `original-expiration` in `x-death`), so the copy has no TTL.

**Consequence**: retained failures can be replayed without broker plugins or hand-written shovels. Failure context is the broker's `x-death` (queue, reason, time, count), or the `hero-fault-*` headers when an error queue is configured (TD-8). Integration (RabbitMQ 3.13): `RabbitMqDeadLetterReplayIntegrationTests` (22 tests: two replay a message published by another broker user, four cover TD-8); unit: `DeadLetterReplayHeadersTests`, `RabbitMqDeadLetterReplayValidationTests`.

---

### TD-8: Error Queue with Failure Context (2026-11-06)

**Decision**: `RabbitMqTransportOptions.ErrorQueueSuffix` (default `null`) names a per-queue error queue (`orders` + `"_error"` = `orders_error`). When it is set, a terminal handler failure (retries used up, `RequeueOnFailure = false`) or a handler's `DeadLetterAsync(reason)` publishes a copy to that queue and then acknowledges the original, instead of rejecting it.

- The copy keeps the body, properties and headers, is made persistent, drops `expiration` (the original is gone once the copy is confirmed, so the copy must not expire), `x-delivery-count` and `UserId` (the broker rejects a copy naming another publisher, as in TD-7), replaces any earlier `hero-fault-*` headers and adds `hero-fault-queue` (source queue), `hero-fault-consumer`, `hero-fault-retries`, `hero-fault-time` (UTC, ISO 8601), and either `hero-fault-exception-type` and `hero-fault-exception-message` (truncated to 4,096 characters) or `hero-fault-reason`.
- The copy is published with mandatory routing and a publisher confirmation; the original is acknowledged only after the confirmation. If the error queue does not exist, publisher confirms are disabled, or the copy is not confirmed, the original is rejected without requeue as before, so a dead-letter exchange still keeps it. A confirmation timeout after the broker accepted the copy can leave the copy and a dead-lettered original. Delivery is at least once: if the consumer channel closes after the confirmation but before the acknowledgement, the original is redelivered, so the error queue can hold a duplicate.
- A handler's `DeadLetterAsync` claims the delivery before publishing the copy, so a call after the delivery was settled throws without copying, and cancelling the handler's token during the move does not lead to a retry.
- `hero-fault-exception-message` carries the exception message (up to 4,096 characters, never splitting a surrogate pair) to anyone who can read the error queue; handlers whose exceptions can contain secrets or personal data should not set `ErrorQueueSuffix` or should throw sanitised exceptions. `MessagesDeadLettered` counts both moved and rejected messages.
- The error queue is not declared automatically; declare it with the topology like any queue.
- `ReplayDeadLettersAsync` reads `hero-fault-queue` before `x-death`, so an error queue replays like a dead-letter queue, and removes the `hero-fault-*` headers from the replayed copy.
- Additive public API: a new settable property on `RabbitMqTransportOptions`; no signature changes.

**Consequence**: operators see why a message failed without searching logs, matching MassTransit's `_error` queues. Unit: `FaultHeadersTests`, `RabbitMqConsumerRetryPolicyTests` (error-queue cases); integration (RabbitMQ 3.13): `RabbitMqDeadLetterReplayIntegrationTests.ErrorQueue_KeepsFailureContextAndReplaysToTheSourceQueue`, `ErrorQueue_WhenMissing_FallsBackToTheDeadLetterExchange`, `ErrorQueue_WithoutPublisherConfirms_FallsBackToTheDeadLetterExchange`, `ErrorQueue_MessagePublishedByAnotherBrokerUser_IsMovedWithItsFailureContext`.

### TD-9: Confirmed Publish to a Topic (2026-11-06)

**Decision**: `RabbitMqTransport` implements the new `IConfirmedPublishTransport.PublishConfirmedAsync(topic, envelope)`, the publish counterpart of `SendConfirmedAsync`. It publishes to the topic's exchange with mandatory routing and waits for the publisher confirmation, so it completes only when at least one bound queue accepted the message. When no binding matches (including an exchange with no bindings), the broker returns the message and the call throws `PublishRouteException` with the topic and routing key; plain `PublishAsync` silently drops such a message.

- The routing key comes from the envelope's `RoutingKey` header, `#` when absent, as for `PublishAsync`; no extra parameter.
- Publisher confirms are required: with `UsePublisherConfirms = false` the call throws `InvalidOperationException` before publishing.
- A confirmation timeout after the broker accepted the message throws `TimeoutException`; the message may still have been routed, so callers that retry must tolerate a duplicate.
- Additive public API: one new interface and one exception in `HeroMessaging.Abstractions.Transport`; no signature changes. It is the transport step for transactional external publish (F07, roadmap R4); routing Outbox entries to a topic is the next step.

**Consequence**: Outbox delivery can mark a published event processed only when a subscriber queue has it. Integration (RabbitMQ 3.13): `RabbitMqConfirmedPublishTests` (matching binding reaches every bound queue; no matching binding and no bindings throw `PublishRouteException`; the channel stays usable after a returned message — the three failure cases passed silently with routing not required). Unit: `RabbitMqTransportTests.PublishConfirmedAsync_WithoutPublisherConfirms_ThrowsActionableError`, `PublishConfirmedAsync_WhenDisconnected_ThrowsInvalidOperationException`.

### TD-10: Outbox topic destinations (2026-11-07)

**Decision**: `OutboxOptions.Destination` keeps its single string (and its existing storage column). `topic:<name>` addresses a topic and is delivered with `IConfirmedPublishTransport.PublishConfirmedAsync`; `queue:<name>` or a bare name addresses a queue and is sent with `SendConfirmedAsync`, as before. The two prefixes are the ones `TransportAddress.ToString()` writes for queues and topics; they are lower case and matched exactly, so `Topic:x` is a queue name. Other prefixes, such as `exchange:`, are not recognised and are read as part of a queue name.

- No schema change and no new public type: `Processing/OutboxDestination` (internal) parses the string at publish time and again at delivery, so entries written directly to storage (for example through `PostgreSqlUnitOfWork`) follow the same rules.
- `PublishToOutboxAsync` rejects a prefix without a name or with white space around the name (`ArgumentException`) and a topic when the registered transport does not implement `IConfirmedPublishTransport` (`NotSupportedException` naming the interface). At delivery, before connecting or serializing, the same checks mark the entry failed at once with that message: these are configuration errors that a retry cannot fix.
- An unroutable topic publish (`PublishRouteException`) is an ordinary delivery failure: the entry stays pending and is retried, so a subscriber queue bound later still gets it.
- The envelope carries no `RoutingKey` header, so the transport publishes with the literal key `#`. On a topic exchange only bindings `#` or `*` match it; a subscriber bound with a specific pattern (`orders.*`) never receives the entry, which fails after its retries. Per-entry routing keys are a later step.
- Compatibility: a queue whose name starts with `topic:` or `queue:` must be written as `queue:<name>`. The feature has not been released.

**Consequence**: `PostgreSqlRabbitMqOutboxRecoveryTests.TopicDestination_RetriesWhileUnboundThenReachesEveryBoundQueue` (PostgreSQL 17 + RabbitMQ 3.13) retries while the exchange has no binding, then delivers to both bound queues and marks the entry processed; before the change the entry was sent to a queue literally named `topic:<exchange>` and never arrived. Unit: `ExternalOutboxProcessorTests` topic cases.

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

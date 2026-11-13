# Changelog

All notable changes to HeroMessaging will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **.NET 11 target**: every library and test project that targeted net8.0/net9.0/net10.0 now also targets `net11.0` (.NET 11 GA, 2026-11-10), and CI builds and tests net11.0 on Ubuntu, Windows and macOS. Building from source needs the .NET 11 SDK (`global.json` 11.0.100, `latestPatch`). Benchmark projects stay on net8.0–net10.0 because BenchmarkDotNet 0.15.8 does not recognise the .NET 11 runtime.

### Fixed

<<<<<<< HEAD
- **Inbox processor metrics**: `InboxProcessor.GetMetrics()` now reports the processed, duplicate and failed messages this processor instance has seen and the deduplication rate (duplicates divided by all incoming messages); it returned zeros before. Counts are in memory per instance.
=======
- **Idempotency for commands**: with `WithIdempotency(...)` configured, `CommandProcessor` now deduplicates commands without a response, keyed per handler as `{configured key}:handler:{handler type}`. A redelivered command is not handled again; a cached idempotent failure (for example `ArgumentException`, `InvalidOperationException`) is rethrown without running the handler; transient failures and cancellations are not cached, so a redelivery runs the handler. If the idempotency store is unavailable, the command is processed without deduplication (logged). Limits: commands with a response, queries, Inbox and transport consumers are not covered yet; two copies of one command processed at the same moment by different hosts can both run; a replayed failure keeps its exception type only for the built-in allow-listed types (a custom exception arrives as `System.Exception` with the original type name in its message); under `WithTransactions()` / `WithCommandTransactions()` the success record is written only after the business transaction commits (a failed commit leaves the command to run again on redelivery; a crash between commit and that write, or a store outage at that point (not logged), also means one more run; event idempotency records are still written before an event transaction commits). The store lookup and write run inside the command processor's serialization, so a slow durable store slows every command (an unreachable store waits for its timeout before falling back), and a replayed cached failure is counted and logged as a failure each time a duplicate arrives.
>>>>>>> origin/main
- **Persisted events marked processed before their handlers ran**: the Inbox, Outbox and queue processors now dispatch events with `PublishAndWaitAsync` and mark an entry processed (or acknowledge it) only after every handler succeeded. Before, they used `PublishAsync`, which returns once the event bus has admitted the event, so an entry became `Processed` while its handler was still queued or running, and a throwing handler still left it `Processed` — a crash or handler failure lost the event. An unsuccessful handler now raises `[HM-DISPATCH-EVENT-HANDLER]` naming each failed handler and its cause, and the processor's own failure policy applies (Inbox entry `Failed`, Outbox retry, queue reject). Throughput of event entries now follows handler completion rather than bus admission. The processor's stop token reaches the event handlers (as it did with `PublishAsync`), so a handler still running at stop is cancelled (through the built-in `HeroMessagingService`, `EventBus` and `TransactionEventBusDecorator`; a custom `IHeroMessaging` or `IEventBus` wrapper falls back to `PublishAndWaitAsync`, whose token only cancels the wait). Because a processor worker now holds its slot until the event's handlers finish, a handler that synchronously waits on admitting more work to the same processor (for example `ProcessIncomingAsync` on a full Inbox) can stall that worker; enqueue follow-up work through the Outbox or a separate processor instead.
- **Inbox, Outbox and queues without the mediator**: `WithInbox()`, `WithOutbox()` and `WithQueues()` now also register the command, query and event processors they dispatch through. Before, configuring one of them without `WithMediator()` and `WithEventBus()` — including the `Microservice()` preset — left `IHeroMessaging` unresolvable, so every persisted message failed with "Unable to resolve service for type ICommandProcessor" instead of reaching its handler.
- **Idempotency on the event bus**: `WithIdempotency(...)` now deduplicates events. `EventBus` runs each handler delivery through `IdempotencyDecorator` (new `MessageProcessingPipelineBuilder.UseIdempotency()`), keyed per handler as `{configured key}:handler:{handler type}` so a redelivered event is suppressed for every handler without one handler's cached result hiding another. If the idempotency store is unavailable, the event is processed without deduplication (logged) instead of failing. Limits: two copies of one event processed at the same moment can both run; commands, queries, Inbox and transport consumers are not covered yet.

### Added

- **Two-process Outbox crash tests**: `tests/HeroMessaging.ReliabilityHarness` runs the PostgreSQL Outbox publisher as a separate OS process that the integration tests kill before a publish or after the broker confirmed it; a second process takes over once the lease expires and delivers every entry (the message killed after confirmation arrives twice with the same `MessageId`).
- **Competing Outbox publisher processes test**: `CompetingPublisherProcesses_DeliverEveryEntryExactlyOnce` starts two reliability-harness publisher processes together on the same PostgreSQL Outbox; each message reaches RabbitMQ exactly once and the processes deliver disjoint shares. The harness gains `--start manual` (start processing on a `go` line) and `--send-delay-ms`.
- **Outbox duplicate absorbed by an Inbox consumer process test**: `PostgreSqlRabbitMqOutboxToInboxProcessCrashTests` kills a PostgreSQL Outbox publisher process after the broker confirmed a send; a second publisher delivers that message again, and a separate Inbox consumer process rejects the duplicate (the harness prints `DUPLICATE <id>`) so its handler's business ledger has exactly one row per message.
- **Two-process Inbox crash tests**: the reliability harness also runs a RabbitMQ-to-PostgreSQL Inbox consumer; `PostgreSqlRabbitMqInboxProcessCrashTests` kill it holding an unacknowledged delivery, before or after the Inbox entry is persisted, and a second consumer process handles every message exactly once in the business ledger. A third case kills the consumer after its handler's business effect committed but before the Inbox entry was completed: the second process runs the effect again (two ledger rows), recording that Inbox delivery is at-least-once for handler effects. A fourth case repeats that kill with a deduplicating handler (`--effect idempotent`: the message ID is recorded in the effect's own transaction and an already-recorded ID is skipped); the second process completes the entry and the ledger keeps exactly one row per message.
- **Outbox entries published to a topic**: `OutboxOptions.Topic` (and optional `RoutingKey`) stores an event with your business transaction and publishes it to that topic (for RabbitMQ, the exchange) through `PublishConfirmedAsync`, so the entry is marked processed only after the broker confirms that a bound queue received it. An unroutable publish is retried with the entry's retry policy and then failed, with the `PublishRouteException` text as the entry's error, instead of being dropped. Requires PostgreSQL leased Outbox storage and a transport implementing `IConfirmedPublishTransport`; setting both `Destination` and `Topic`, a blank `Topic`, or `RoutingKey` without `Topic` throws `ArgumentException` before anything is stored, both from `PublishToOutboxAsync` and from `PostgreSqlOutboxStorage.AddAsync` (the unit-of-work path); `HeroMessaging.Utilities.OutboxAddressValidator` holds the rule. As a result the unit-of-work path now also rejects a blank `Destination` (such as `""`), which it used to store. The PostgreSQL outbox table gains nullable `destination_type` and `routing_key` columns (added automatically with `AutoCreateTables`).
- **Confirmed RabbitMQ publish**: `IConfirmedPublishTransport.PublishConfirmedAsync(topic, envelope)` (implemented by `RabbitMqTransport`) completes only when the broker confirms that at least one queue bound to the topic received the message, and throws `PublishRouteException` (topic and routing key) when no binding matches; `PublishAsync` drops such messages silently. Requires publisher confirms. See [ADR 0003, TD-9](docs/adr/0003-rabbitmq-transport.md#td-9-confirmed-publish-to-a-topic-2026-11-06).
- **RabbitMQ error queues with failure context**: set `RabbitMqTransportOptions.ErrorQueueSuffix` (for example `"_error"`) to move a message whose retries are used up, or that a handler dead-letters, to `<queue><suffix>` with `hero-fault-*` headers (source queue, exception type and message or reason, retries, time, consumer). The original is acknowledged only after a publisher-confirmed copy; a missing error queue falls back to rejecting it as before. `ReplayDeadLettersAsync` replays error queues to their source queue.
- **RabbitMQ dead-letter replay**: `RabbitMqTransport.ReplayDeadLettersAsync(deadLetterQueue, DeadLetterReplayOptions?)` moves retained messages back to the queue that rejected them (from `x-death`) or to `TargetQueue`. Each message is removed from the dead-letter queue only after a publisher-confirmed, mandatory-routed copy; a failure leaves it retained (at least once: a crash, connection loss or confirm timeout after the broker accepted the copy can duplicate it). Messages whose source queue is unknown or gone stay retained (republished to the dead-letter queue, keeping their priority). The copy drops `x-delivery-count` (fresh retry budget) and carries a `hero-replay-count` header. Returns `DeadLetterReplayResult(Replayed, Unresolved)`.

#### Saga Orchestration System
- **State Machine DSL**: Fluent API for defining saga state machines with `StateMachineBuilder`
  - `Initially()`, `During()`, `When()`, `Then()`, `TransitionTo()` methods
  - Support for conditional transitions with `If().Then().Else()`
  - Finalize() for marking terminal states

- **Saga Infrastructure**: Complete orchestration framework
  - `ISaga` and `SagaBase` abstractions for saga instances
  - `SagaOrchestrator<TSaga>` for routing events and executing state transitions
  - `InMemorySagaRepository<TSaga>` with optimistic concurrency control
  - Support for correlation tracking across saga instances

- **Compensation Framework**: Automatic compensating transaction support
  - `CompensationContext` for registering compensating actions
  - LIFO (Last-In-First-Out) execution order
  - Transient per-event compensation with state-based cross-event patterns

- **Saga Timeout Handling**: Background monitoring for stale sagas
  - `SagaTimeoutHandler<TSaga>` hosted service
  - Configurable check intervals and default timeouts
  - Automatic marking of timed-out sagas with audit trail

- **TimeProvider Integration**: Deterministic time control for testing
  - Complete integration across all saga operations
  - Support for `FakeTimeProvider` in tests
  - Framework-specific package configuration (built-in for .NET 8+, polyfill for older versions)
  - All saga timestamps (CreatedAt, UpdatedAt) use TimeProvider

- **Documentation**:
  - `docs/orchestration-pattern.md` - Complete orchestration guide
  - `docs/choreography-pattern.md` - Choreography pattern documentation
  - `docs/adr/0004-saga-patterns.md` - Implementation summary
  - Comprehensive code examples in tests

- **PostgreSQL business-transaction Outbox**: `PostgreSqlUnitOfWork.Connection` and `PostgreSqlUnitOfWork.Transaction` let business commands run in the same transaction as `OutboxStorage.AddAsync`, so the business rows and the outbox entry commit or roll back together.

### Breaking
- **RabbitMQ consumer handler failures are no longer requeued forever.** With default `ConsumerOptions`, a message whose handler keeps failing is rejected without requeue after 3 retries (about 35 s of backoff). RabbitMQ keeps it only if the queue has a dead-letter exchange. To upgrade, set `QueueDefinition.DeadLetterExchange` on the queue, or set `MessageRetryPolicy.MaxAttempts = -1` to keep retrying indefinitely (now with backoff).

### Fixed
- Inbox retention cleanup (in-memory, PostgreSQL, SQL Server) no longer deletes a processed or failed entry before its own `InboxOptions.DeduplicationWindow` has elapsed. Previously the worker's fixed seven-day cleanup removed entries with a longer window, so a redelivery after day seven was admitted and processed again.

### Changed
- **PostgreSQL Outbox schema check**: `PostgreSqlOutboxStorage` reads the catalog and skips its `ALTER TABLE`/`CREATE INDEX IF NOT EXISTS` statements when the table is already complete. Previously every unit-of-work outbox write ran them inside the business transaction and held an exclusive table lock until commit, so concurrent business transactions waited for each other and outbox workers blocked.
- **RabbitMQ consumer retries**: `ConsumerOptions.MessageRetryPolicy` now governs handler failures. A failing delivery is requeued after the policy's backoff until its retries are used up, then rejected without requeue (retained only with a dead-letter exchange) and counted in `ConsumerMetrics.MessagesDeadLettered`. Previously it was requeued immediately and indefinitely. Inbox transport subscriptions keep retrying indefinitely, now with backoff.
- **RetryPolicy**: `CalculateDelay` returns zero for a zero initial delay at any attempt number (it threw for attempts above 1024 with exponential backoff).
- **In-memory consumer**: `RetryPolicy.MaxAttempts = -1` now retries indefinitely, as documented, instead of dead-lettering at once; stopping the consumer ends those retries.
- **SagaBase**: CreatedAt and UpdatedAt timestamps now set by repository (not in constructor)
- **PostgreSQL unit of work queue and message storage (fix)**: `PostgreSqlUnitOfWork.QueueStorage` and `MessageStorage` failed on every call against a password-authenticated server, because they created their table on a new connection built from the open connection's `ConnectionString`, which Npgsql strips of its password. Their schema setup now runs on the unit of work's own connection and transaction and is skipped when the table and its indexes already exist, so an open business transaction no longer holds a table lock (`CREATE INDEX IF NOT EXISTS` takes a `ShareLock` even when the index exists) that blocks other transactions' inserts. A missing table or index is created inside the business transaction (a failed or racing creation aborts that transaction); create the tables at start-up to avoid that. Known gap: the non-interface `PostgreSqlMessageStorage` overloads that take an `IStorageTransaction`, `QueryAsync(MessageQuery)`, `DeleteAsync(Guid)` and `BeginTransactionAsync` still open a new connection and fail the same way on a unit-of-work instance.
- **PostgreSQL unit of work Inbox (fix)**: two business transactions that each admitted an Inbox entry through `PostgreSqlUnitOfWork.InboxStorage` serialised: the second waited for the first to commit (a 30 s Npgsql read timeout in `PostgreSqlUnitOfWorkInboxTests`), because the no-op `CREATE INDEX IF NOT EXISTS` in the Inbox schema setup takes a `ShareLock` held until the business transaction ends. The Inbox schema setup now skips the DDL when the table and its indexes exist (checked in the catalog, with custom schema and table names and PostgreSQL's 63-byte index-name truncation), so neither a unit-of-work admission nor a new Inbox instance starting while another transaction holds an admitted entry waits for that transaction; the schema setup also honours the caller's cancellation token.
- **Package Dependencies**:
  - Added `Microsoft.Bcl.TimeProvider` for netstandard2.0, net6.0, net7.0
  - Added `Microsoft.Extensions.TimeProvider.Testing` for test projects
  - .NET 8+ uses built-in TimeProvider (no additional package)

### Technical Details

**Performance:**
- State transition overhead: < 1ms (target met)
- Zero-allocation paths where possible
- Optimistic concurrency for saga updates

**Testing:**
- 158 tests passing (100% green)
- 80%+ code coverage maintained
- Full integration test suite for orchestration workflows
- TimeProvider enables deterministic time-based testing

**Constitutional Compliance:**
- ✅ TDD: Tests written before implementation
- ✅ Coverage: 80%+ maintained
- ✅ Performance: <1ms overhead target met
- ✅ Quality: No compiler warnings
- ✅ Multi-framework: netstandard2.0, net6.0, net7.0, net8.0, net9.0

### Known Limitations
- Only `InMemorySagaRepository` provided (SQL/PostgreSQL repositories planned for future releases)
- TimeProvider integration limited to saga system (other subsystems in future releases)

### Migration Guide
This release adds new functionality with no breaking changes. Saga orchestration is opt-in.

**To use saga orchestration:**
```csharp
services.AddHeroMessaging(builder =>
{
    builder.AddSaga<OrderSaga>(OrderSagaStateMachine.Build);
    builder.UseInMemorySagaRepository<OrderSaga>();
});
```

### References
- ADR-0004: Saga Patterns - Choreography vs Orchestration
- Examples: `tests/HeroMessaging.Tests/Examples/OrderSagaExample.cs`
- Integration Tests: `tests/HeroMessaging.Tests/Integration/OrchestrationWorkflowTests.cs`

---

## [0.1.0] - Previous releases
_(Changelog started with saga orchestration feature)_

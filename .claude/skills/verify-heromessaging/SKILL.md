---
name: verify-heromessaging
description: How to exercise HeroMessaging's features for real (in-process dispatch, RabbitMQ, PostgreSQL, SQL Server, NativeAOT, benchmarks) and what evidence each acceptance row F01–F25 / P01–P05 needs before a change is called working.
---

# Verify HeroMessaging

Use with `verification-first`. Rows match `docs/masstransit-replacement-acceptance.md` (F01–F25, P01–P05); keep the row status there in its own vocabulary.

## Setup

- SDK from `global.json` (10.0.402, `rollForward: latestPatch`) plus runtimes 8.0 and 9.0:
  `dotnet-install.sh --channel 10.0 --install-dir <dir>`, then `--runtime dotnet --channel 8.0` and `--channel 9.0` into the same dir; export `DOTNET_ROOT=<dir>` and put it first on `PATH`.
- Node 22.18+ (CI uses 24) for `.github/scripts/check-docs.ts`.
- Real infrastructure, same images as `.github/workflows/run-integration-tests.yml`. If Docker Hub answers 429, pull through `mirror.gcr.io/library/...`. If `docker info` fails, start the daemon (`dockerd &`).
  ```bash
  docker run -d --name pg  -p 127.0.0.1:5432:5432 -e POSTGRES_USER=postgres -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=heromessaging_tests mirror.gcr.io/library/postgres:17
  docker run -d --name rmq -p 127.0.0.1:5672:5672 mirror.gcr.io/library/rabbitmq:3.13-management-alpine
  export PostgreSql__ConnectionString="Host=localhost;Database=heromessaging_tests;Username=postgres;Password=postgres"
  export RabbitMq__Host=localhost RabbitMq__Port=5672
  # SQL Server: mcr.microsoft.com/mssql/server with SqlServer__ConnectionString as in run-integration-tests.yml
  ```
  These are throwaway, task-owned containers bound to loopback only; never point the tests at a shared or production database or broker (the test schemas are created and dropped).
  Without these variables the fixtures fall back to Testcontainers (`postgres:17-alpine`, `rabbitmq:3.13-management-alpine`), which needs Docker Hub.
- Build once: `dotnet build HeroMessaging.slnx -c Release -warnaserror`, then run tests with `--no-build`.
- Run one row: `dotnet test --project tests/<Project> -c Release --no-build --framework net9.0 --no-progress --filter-class "*.<ClassName>"` (repeat `--filter-class` for several classes).

## Feature map

"Exercise" names the closest real harness that exists on main; "none" means the row has no harness yet and stays Open. Counts are from the last verification run (net9.0, real PostgreSQL 17 and RabbitMQ 3.13 containers).

| Row | Feature | Code entry points | Exercise it for real | Evidence to capture | Neighbours at risk |
| --- | --- | --- | --- | --- | --- |
| F01 | In-process commands | `src/HeroMessaging/Processing/CommandProcessor.cs`, `HeroMessagingService` | HeroMessaging.Tests `CommandProcessorTests`, `QuickStartTests` (18 pass) | handler ran once, exception/cancellation propagated | F02, F03, decorators |
| F02 | In-process queries | `Processing/QueryProcessor.cs`; NativeAOT consumer `tests/NativeAotConsumer` | `QueryProcessorTests`, `QueryExceptionPropagationTests` (20 pass); NativeAOT: `python3 .github/scripts/verify_nativeaot.py --source . --commit $(git rev-parse HEAD) --output artifacts/nativeaot --sdk $(dotnet --version) --tfm net10.0 --runtime <newest 10.0.x> --rid linux-x64` | response value, fault, cancellation; NativeAOT `consumer passed` line and evidence JSON | source generators, trimming |
| F03 | In-process publish | `Processing/EventBus.cs`, `MessageProcessingPipeline.cs` | `EventBusTests`, `EventPublishReceiptTests`, `EventBusSettingsTests` (29 pass) | every handler completed, receipt vs completion, bounded admission | idempotency (F18), F19 |
| F04 | RabbitMQ queue send | `src/HeroMessaging.Transport.RabbitMQ` (`RabbitMqTransport`, publisher confirms) | RabbitMQ.Tests `RabbitMqConfirmedQueueDeliveryTests` (2), `RabbitMqPublisherConfirmTests`, `RabbitMqMessageFlowIntegrationTests` (8) | confirmed delivery, unroutable/failure outcome | F05, F06 |
| F05 | RabbitMQ publish/subscribe | same, topology declaration | `RabbitMqMessageFlowIntegrationTests`, `RabbitMqTransportTests` | each subscriber received once, routing/type identity | F04 |
| F06 | PostgreSQL external Outbox | `src/HeroMessaging.Storage.PostgreSql` (`PostgreSqlOutboxStorage`, `PostgreSqlUnitOfWork`), `Processing/OutboxProcessor.cs`, `Processing/ExternalOutboxDelivery.cs` | PostgreSql.Tests `PostgreSqlOutboxLeaseTests` (3), `PostgreSqlOutboxBusinessTransactionTests` (9: business row and entry commit/roll back together, no lock wait between concurrent business transactions); RabbitMQ.Tests `PostgreSqlRabbitMqOutboxRecoveryTests` (2), `PostgreSqlRabbitMqOutboxProcessCrashTests` (2, two OS processes from `tests/HeroMessaging.ReliabilityHarness`, publisher killed `before-publish` / `after-confirm`; about 65 s each for the real lease expiry); HeroMessaging.Tests `ExternalOutboxConcurrencyTests`, `ExternalOutboxProcessorTests` (12) | committed vs rolled-back rows, lease fencing, delivered count on the broker | F07, F08, F04 |
| F07 | Transactional external publish | `IConfirmedPublishTransport.PublishConfirmedAsync` in `RabbitMqTransport` (mandatory + confirm, `PublishRouteException`); `OutboxOptions.Topic`/`RoutingKey` → `ExternalOutboxDelivery.SendAsync`; PostgreSQL `destination_type`/`routing_key` columns | RabbitMQ.Tests `RabbitMqConfirmedPublishTests` (4) and `PostgreSqlRabbitMqOutboxPublishTests` (2; need `RabbitMq__Host=localhost` and `PostgreSql__ConnectionString`), PostgreSql.Tests `PostgreSqlOutboxLeaseTests`, unit `ExternalOutboxProcessorTests`, `RabbitMqTransportTests` `PublishConfirmedAsync_*` (2) — Partial | every bound queue received it; unroutable key throws naming topic and routing key | F05, F06 |
| F08 | Durable Inbox | `Processing/InboxProcessor`, `PostgreSqlInboxStorage`, `SqlServerInboxStorage` | `PostgreSqlInboxRecoveryTests`, `PostgreSqlInboxDeduplicationWindowTests`, `PostgreSqlInboxFairnessTests` (7); `PostgreSqlUnitOfWorkInboxTests` (concurrent unit-of-work admissions and a new Inbox instance do not wait on an open transaction; rollback leaves no entry); `PostgreSqlRabbitMqInboxRecoveryTests` (1); `PostgreSqlRabbitMqInboxProcessCrashTests` (2, consumer OS process from `tests/HeroMessaging.ReliabilityHarness` killed holding an unacknowledged delivery `before-inbox`/`after-inbox`; a second process handles it; `business_ledger` has one row per message); `InboxTransportSubscriptionTests`; `InboxProcessorTests`, `InboxHostLifecycleTests` (23) | persisted before ACK, duplicate rejected, recovery after host rebuild | F09, F18 |
| F09 | Consumer retries/redelivery | `RabbitMqConsumer` retry policy, `ConsumerRetryAttempts` | `RabbitMqConsumerRetryPolicyTests`, `ConsumerRetryAttemptsTests`, `RabbitMqErrorScenarioIntegrationTests` (12) | attempt count and delays, `x-death` / `x-delivery-count`, terminal disposition | F10 |
| F10 | Dead letters and replay | dead-letter replay API, `FaultHeaders`, `RabbitMqTransportOptions.ErrorQueueSuffix` | `RabbitMqDeadLetterReplayIntegrationTests` (22; 3 skip against an external broker — they need the Testcontainers broker to add a user), `FaultHeadersTests`, `DeadLetterReplayHeadersTests` | retained message with `hero-fault-*`, replayed body/headers, counts left retained | F09 |
| F11 | SQL Server registration | `src/HeroMessaging.Storage.SqlServer` builder extensions | SqlServer.Tests `SqlServerBuilderRegistrationTests` (30 pass, no database needed) | resolved providers/options, null-argument errors | A02 |
| F12 | SQL Server external Outbox | none | none — Open | — | F06 |
| F13 | Saga persistence/concurrency | `PostgreSqlSagaRepository`, `SqlServerSagaRepository`, `InMemorySagaRepository` | `PostgreSqlSagaRepositoryTests` (38; the filter matches both the `Unit` and `Integration` class), `InMemorySagaRepositoryTests` | conflict detection on concurrent update | F14, F15 |
| F14 | Saga state plus outgoing work | `Orchestration/SagaOrchestrator` | `SagaOrchestratorTests` only (no joint transaction) — Open | — | F13, F06 |
| F15 | Saga routing/timeouts | `StateMachine*`, `SagaTimeoutHandler` | `SagaTimeoutHandlerTests`, `StateMachineTests` (with F13/F14 classes: 107 pass) | timeout event raised for the right state, compensation | F13 |
| F16 | Remote request/response | none | none — Open | — | — |
| F17 | Persistent scheduling | `Scheduling/StorageBackedScheduler` (defaults to memory storage) | `StorageBackedSchedulerTests`, `InMemorySchedulerTests` (48) — no restart evidence | due delivery, cancel, restart survival | F03 |
| F18 | Idempotent business effects | `Idempotency/Decorators/IdempotencyDecorator`, stores in PostgreSql/SqlServer | `IdempotencyDecoratorTests`, `IdempotencyPipelineIntegrationTests` (40); `PostgreSqlIdempotencyStoreIntegrationTests` (12) | cached response returned, handler not re-run; whether the pipeline actually inserts the decorator | F03, F08 |
| F19 | Shutdown/backpressure | hosted services, `RabbitMqConsumer` shutdown | `OutboxHostShutdownTests` (2), `InboxHostLifecycleTests`, `RabbitMqShutdownIntegrationTests` (2) | in-flight work finished or recoverable, no hang | F06, F08 |
| F20 | Observability | `src/HeroMessaging.Observability.OpenTelemetry`, `...HealthChecks` | OpenTelemetry.Tests `TraceContextPropagatorTests`, `OpenTelemetryDecoratorTests` (24); HealthChecks.Tests `TransportHealthCheckTests`, `StorageHealthCheckTests` (28) | parent/child trace ids across transport, health status changes | F04 |
| F21 | Serialization/versioning | `src/HeroMessaging.Serialization.*`, `Versioning/` | Serialization.Json.Tests `JsonMessageSerializerTests` (34), MessagePack/Protobuf test projects, `VersionedMessageServiceTests` (22) | round-trip, invalid payload error, size limit | F04 |
| F22 | Migration coexistence | none | none — Open | — | — |
| F23 | Other MassTransit transports | only RabbitMQ and in-memory exist | none — Open | — | — |
| F24 | Consumer testing API | none public (`tests/HeroMessaging.Tests.Shared` is internal test helpers) | none — Open | — | — |
| F25 | Installable release | `docs/release-package-preparation.md`; never run publish workflows | none from here — Open | — | — |
| P01 | In-process command benchmark | `benchmarks/HeroMessaging.CompetitiveBenchmarks` | `dotnet run --project benchmarks/HeroMessaging.CompetitiveBenchmarks -c Release -f net10.0 --no-build -- --verify` (both providers `"Verified": true`); negative controls in its README | verification JSON; measured results only under the acceptance doc's fairness rules | — |
| P02–P05 | Concurrent, RabbitMQ, Outbox→Inbox, fan-out benchmarks | none against MassTransit yet (`tests/HeroMessaging.Benchmarks` is internal regression only) | none — Open | — | — |

## Gate

```bash
dotnet build HeroMessaging.slnx -c Release -warnaserror
for f in net8.0 net9.0 net10.0; do dotnet test --project tests/HeroMessaging.Tests -c Release --no-build --framework $f --filter-not-trait Category=Integration; done
# touched package test projects (RabbitMQ.Tests targets net8.0;net9.0 only), with the containers above for integration
dotnet test --project tests/HeroMessaging.Storage.PostgreSql.Tests -c Release --no-build --framework net10.0
dotnet test --project tests/HeroMessaging.Transport.RabbitMQ.Tests -c Release --no-build --framework net9.0
node --test .github/scripts/check-docs.test.ts && node .github/scripts/check-docs.ts
# NativeAOT consumer when dispatch, generators or trimming-relevant code change (command in F02)
```

## Traps

- Without `PostgreSql__ConnectionString`, `HeroMessaging.Storage.PostgreSql.Tests` classes under `Unit/` (59 tests) fall back to `Host=localhost;Database=test` and fail with connect or "No password has been provided" errors, and `Integration/` classes fall back to Testcontainers (which needs Docker Hub). Expect 59 failures with Docker up, about 127 without. That is missing setup, not a regression.
- `dotnet test --framework net10.0` on `HeroMessaging.Transport.RabbitMQ.Tests` reports "Zero tests ran" (the project targets net8.0;net9.0). Zero tests is not a pass.
- With `RabbitMq__Host` set, three replay/error-queue tests skip (they add a broker user through Testcontainers). Count skips; run them through Testcontainers when the change touches `UserId` handling.
- `PostgreSqlRabbitMqOutboxRecoveryTests` and `PostgreSqlRabbitMqInboxRecoveryTests` rebuild DI hosts inside one process. They are not the two-OS-process kill evidence F06–F08 acceptance requires. `PostgreSqlRabbitMqOutboxProcessCrashTests` (publisher side, queue destination) and `PostgreSqlRabbitMqInboxProcessCrashTests` (consumer side) are; they need `HeroMessaging.ReliabilityHarness.dll` in the test output, which the project reference copies — a missing file fails the test with its path.
- `WithIdempotency` registration alone does not prove deduplication; check that the decorator is in the pipeline for the path being claimed.
- A CompetitiveBenchmarks run on a shared host only validates plumbing; it cannot pass performance acceptance.
- Hosted Actions jobs that end with zero steps and a billing annotation never ran; they are not a pass.

## Last verified

2026-11-06, rows run on commit 07bf0c1 (main), skills added in PR #150: every row above run once with the commands shown; core unit suite 2556 pass on net8.0, net9.0 and net10.0; PostgreSql.Tests 228 pass (net10.0); RabbitMQ.Tests 357 total, 354 pass, 3 skip (net9.0, external broker); check-docs OK, 16 tests; CompetitiveBenchmarks `--verify` both providers verified. Not run: NativeAOT consumer, SQL Server integration, MessagePack/Protobuf suites.

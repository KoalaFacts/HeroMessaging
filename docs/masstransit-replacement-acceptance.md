# MassTransit replacement acceptance

## Product goal

HeroMessaging must combine **better performance**, **lower resource consumption**, and **a friendly public API** for the workloads it claims to support as a MassTransit replacement. A feature name, green unit suite, or fast submission loop does not establish that claim.

This is an acceptance inventory, not a statement that the requirements below have passed. Initial review baseline: `263ee06958eb78d2cdb95c5e38d3d9d545fadd69` (2026-10-24 review). Each release must attach evidence for its own exact source and package versions.

## Status vocabulary

- **Implemented, acceptance open:** code and some tests exist; the complete replacement scenario still needs evidence.
- **Partial:** a useful path exists with the stated scope limitation.
- **Open:** a required path or its acceptance evidence has not been established.
- **Passed:** reserved for a named scenario, exact configuration, executable verification, and retained result. It never means all configurations passed.

## Functional and reliability inventory

| ID | Capability | Initial state | Acceptance requirement |
| --- | --- | --- | --- |
| F01 | In-process commands | Implemented, acceptance open | Exactly one intended handler; awaited completion; exception/cancellation propagation; service lifetime and scope behavior. |
| F02 | In-process queries | Partial | Same contracts as F01 for responses; actual NativeAOT consumer where supported, including errors/cancellation. Unmerged PR evidence is not a main/release result. |
| F03 | In-process publish | Implemented, acceptance open | Multiple handlers, bounded admission, completion receipts, failures, cancellation, and shutdown; document submission versus handler completion. |
| F04 | RabbitMQ queue send | Implemented, acceptance open | Durable queue/message configuration, mandatory routing, publisher confirmation, unroutable/failure outcome and recovery. |
| F05 | RabbitMQ publish/subscribe | Partial | Multiple independent subscribers, topology lifecycle, absent/stopped subscriber behavior, correct routing and schema/type identity. |
| F06 | PostgreSQL external Outbox | Partial | Queue destination path exists. Business-transaction commit/rollback through `PostgreSqlUnitOfWork` and single-host competing claims with lease expiry/fencing have storage-level integration tests (R3). Process death around broker confirmation has two-process evidence for a queue destination: a publisher killed before the publish or after the broker confirmed it is taken over by a second process once the lease expires, every entry ends `Processed`, and the killed-after-confirm message is delivered twice (R6 step 1). Still to prove: competing publisher processes, and the duplicate absorbed by the Inbox across processes (R6). |
| F07 | Transactional external publish | Partial | PostgreSQL Outbox entries with `OutboxOptions.Topic`/`RoutingKey` are published to the RabbitMQ exchange with mandatory routing and publisher confirmation; an unroutable publish stays pending with its route error and follows the entry's retry policy (R4, single host, integration tests). Still to prove: separate processes and process death around broker confirmation (R6). |
| F08 | Durable Inbox | Implemented, acceptance open | Persist before ACK, persisted duplicates, multi-host ownership, crash recovery, actual business handlers, retention and poison-message policy. |
| F09 | Consumer retries/redelivery | Partial | The configured attempt limit and backoff must govern RabbitMQ direct-consumer failure. Prove terminal disposition and retry state across redelivery; distinguish this from Outbox retries. |
| F10 | Dead letters and replay | Partial | Defined retained destination, useful failure context, safe replay and operator procedure. A NACK without a configured DLX is not retained failure evidence. |
| F11 | SQL Server registration | Partial at baseline | Every existing public builder overload works with the real builder; correct providers/options resolve, useful null errors, no database I/O during registration. See registration regression tests in this change. |
| F12 | SQL Server external Outbox | Open | Destination/options persistence, external-delivery contract, recoverable claims, fencing, and process-death recovery equivalent to the claimed PostgreSQL path. |
| F13 | Saga persistence and concurrency | Partial | Both providers retain state. Prove simultaneous updates, conflict detection/retry, duplicate/late/out-of-order events, and restart; sequential stale-object tests alone are insufficient. |
| F14 | Saga state plus outgoing work | Open | Define and prove the transaction boundary joining state transitions, database side effects, Inbox completion, and outgoing Outbox entries. |
| F15 | Saga event routing/timeouts | Partial | Registration-to-consumption path, correlation validation, state-specific timeout events, cancellation, and compensation behavior without hidden application wiring. |
| F16 | Remote request/response | Open | Request/response correlation, response address, success/fault/timeout, cancellation of waiting, late responses and requester restart. Local Query dispatch is a different scenario. |
| F17 | Persistent scheduling | Open | Survive process restart, claim due work safely across instances, cancel/reschedule, avoid unbounded duplicate dispatch. Current storage-backed scheduler defaults to memory storage. |
| F18 | Idempotent business effects | Partial | State the key, retention, concurrency and transaction boundaries; prove crash windows. A cached result alone does not guarantee exactly-once external effects. |
| F19 | Shutdown/backpressure | Implemented, acceptance open | Bounded queues, cancellation-aware handlers, host deadline, safe unfinished-work recovery and stable resource use during overload. |
| F20 | Observability | Implemented, acceptance open | Cross-service trace propagation, useful failure/retry/queue metrics, low-cardinality defaults, health behavior, documented exporter setup and measured overhead. |
| F21 | Serialization/versioning | Implemented, acceptance open | Supported formats, old/new contract compatibility, unknown/invalid payloads, payload limits and documented type/header rules. |
| F22 | Migration coexistence | Open | Explicit adapter or migration procedure for message envelope, headers, type identifiers, endpoints/topology, consumers and outstanding durable work; bidirectional compatibility where claimed. |
| F23 | Other MassTransit transports | Open | Add individually with their own semantic and fault matrix. RabbitMQ acceptance does not establish Azure Service Bus, SQS/SNS, ActiveMQ, SQL transport, Kafka or Event Hubs support. |
| F24 | Consumer testing API | Open | An application author can assert sent/published/consumed/fault outcomes and test request/Saga flows through a supported API without depending on internal implementation classes. |
| F25 | Installable release | Open | Clean external application restores the exact published packages, runs documented examples, and completes the declared reliability scenarios. Source builds and unmerged PRs are separate evidence. |

### First replacement route

Prioritize RabbitMQ plus PostgreSQL and two independent application processes. Cover commit/rollback, broker unavailable, producer process death after confirmation but before Outbox completion, consumer process death after Inbox commit but before ACK, duplicates, finite poison-message retries, and restart. Retain a business ledger proving the declared effects. Existing tests that rebuild a DI Host in one process are useful but do not replace operating-system process-death tests.

## Performance and resource acceptance

### Equivalent-work workloads

| ID | Workload | Completion boundary | Resource and semantic requirements |
| --- | --- | --- | --- |
| P01 | In-process command, single caller | Handler's returned task completes | Same concrete precreated message, work, lifetime, cancellation, diagnostics and runtime; no broker or durability claim. |
| P02 | In-process async and concurrent handlers | All intended handler work completes | Same admitted workload and concurrency contract; expose framework serialization differences instead of hiding them. |
| P03 | RabbitMQ queue send/consume | Required durable receive and business completion | Same broker, queue type, persistence, confirmation, ACK, payload, serializer and prefetch. |
| P04 | PostgreSQL Outbox -> RabbitMQ -> Inbox -> business handler | Declared durable business transaction completes | Include database writes, confirmation, deduplication, retries and recovery on both sides. |
| P05 | Fan-out event | Every required subscriber completes | Same subscription count and work per subscriber; count both events and deliveries. |
| P06 | Stateful Saga | Declared state/outgoing-work commit completes | Same persistence/concurrency/transaction guarantees and contention. |
| P07 | Sustained overload and recovery | Accepted work drains within the declared objective | Observe queue depth, memory growth, latency, error/retry rate and recovery; dropped work must never inflate throughput. |
| P08 | Idle and start/stop | Ready-to-accept and fully stopped boundaries | Record process startup, idle CPU/memory, active services and disposal. |

### Required measurements

Record all of the following for each applicable workload:

- **Useful throughput:** successfully completed messages/second; report accepted, completed, failed, rejected, retried and duplicate counts separately.
- **Latency:** publish/send-to-completion p50, p95 and p99, sample count, sampling method and timeout rate. BenchmarkDotNet iteration percentiles are not per-message latency percentiles.
- **CPU:** process CPU time per completed message and CPU use at matched achieved throughput. For distributed tests, include Broker/database CPU separately.
- **Managed memory:** allocated bytes per completed message, GC counts/time where available, managed heap observations.
- **Process memory:** working set and peak observations, resource limits and observation interval. End-of-run working set is not a sustained-memory profile.
- **Footprint:** startup, idle memory/CPU, worker/task counts, queues and configured bounds.

Use an uninstrumented batch for throughput/CPU/allocation and a separate timestamped batch for per-message latency. Preallocate sample storage before measuring. Both providers pay the same harness cost, which must be stated. A fresh process runs one provider for resource measurements.

### Fairness and evidence rules

1. Pin package and source versions; record SDK/runtime, OS, architecture, processor count/quota, GC mode, payload, handler work/lifetime, concurrency, topology, storage and instrumentation. Verify process metrics identify the actual measured process; fail when reliable process observations are unavailable rather than reporting another process or substituting zero.
2. Measure at the same CPU/memory budget, then compare resources at the same achieved throughput and latency objective. Faster completion alone does not prove lower resource consumption.
3. Warm up; use at least five independent paired runs on an otherwise stable machine, alternating implementation order. Retain per-run values, spread/confidence estimates and raw data. Do not discard valid unfavorable runs.
4. Validate completed-work counts and correctness before accepting any numerical comparison. Retain failure evidence and configuration with the result.
5. Report each mandatory workload separately. An aggregate score cannot conceal a latency, correctness or memory regression in a required workload.
6. Set practical improvement budgets from the measured variance and intended workload before tuning. No fixed speedup or memory-reduction percentage is claimed by this document.
7. Shared-host and short smoke results validate execution/measurement plumbing only. They cannot pass performance acceptance.
8. Maintain existing internal regression guards. Repair a measurement design that fails calibration rather than treating a failed calibration as a win or silently relaxing its validity criteria.

### First reproducible comparator

See [`benchmarks/HeroMessaging.CompetitiveBenchmarks`](../benchmarks/HeroMessaging.CompetitiveBenchmarks/README.md). It pins MassTransit **8.5.12**, uses its scoped mediator, and measures P01 only. This is an explicitly versioned 8.x baseline. Current MassTransit 9.x requires its applicable license and a separately recorded comparison; 8.x results must not be relabeled as 9.x results.

The initial comparator has a verification mode, a managed-allocation microbenchmark, and a single-provider resource smoke mode. Its existence is not evidence that HeroMessaging is faster. Completed competitive results remain **open** until the evidence rules above are met.

## Friendly API acceptance

| ID | Requirement | What proves it |
| --- | --- | --- |
| A01 | Short, working startup | A new external project compiles and executes the exact documented registration and first send/publish without internal service plumbing. |
| A02 | Fluent configuration | Every advertised overload works with the real builder, chains correctly and propagates options. F11 is the first repaired slice. |
| A03 | Explicit semantics | A caller can distinguish command send, event publish, request/response, enqueue, durable acceptance and handler completion from the API and its documentation. |
| A04 | Helpful failures | Invalid configuration fails early with the relevant parameter/setting and an actionable remedy. No cast to an undocumented builder type. |
| A05 | Lifetime safety | Scoped dependencies resolve in the intended scope, consumers are disposed correctly, and singleton APIs do not hide invalid root-scoped resolution. |
| A06 | Sensible bounds | Defaults bound concurrency, queue capacity and memory; optional performance tuning is not required for correct operation. |
| A07 | Cancellation and shutdown | Document and test whether cancellation stops admission, waiting, handler work, or delivery; retries cannot assume cancellation undoes completed work. |
| A08 | Generated code works | Compile and run generated output against the shipped runtime; checking generated text alone does not pass acceptance. |
| A09 | Migration guidance | Demonstrate a complete MassTransit-to-HeroMessaging consumer flow, including wire/topology changes and persisted work. |
| A10 | Compatibility | Publish API changes and upgrade guidance; benchmark helpers introduce no production dependency on the competitor. |

## Roadmap

Work order for the first replacement route. Status: todo, in progress (PR), done (PR), or blocked (reason; owner decision). A row moves an acceptance row's status above only with the evidence that status requires. Per-run notes live in the PR bodies.

| ID | Item | Moves | Status | Evidence |
| --- | --- | --- | --- | --- |
| R0 | Green main: the scheduled Build and Test run failed from 2026-11-04 because the NativeAOT consumer pinned runtime 10.0.12, whose `Microsoft.DotNet.ILCompiler` pack gained advisory GHSA-p3jp-9v83-76w9 (NU1903 as error). CI now passes the newest installed 10.0.x runtime. | F02 evidence job | done (PR #140) | Reproduced NU1903 locally with 10.0.12. PR #140 CI run 42057116267, Unit Tests (ubuntu-latest, net10.0): SDK 10.0.402, runtime 10.0.14 from the runner's installed set, `NativeAOT consumer passed`; also passed locally with SDK 10.0.403 / runtime 10.0.14. |
| D0 | First docs housekeeping: fold finished plans and one-off analyses into their live pages and delete them; fix stale facts; add the docs checker. | — | done (PR #141, a42431d) | Markdown files 59 → 48, bytes 862,027 → about 619,000; `check-docs` 16 tests; 69 problems on the previous main, 0 after. |
| R1 | RabbitMQ direct-consumer retries/redelivery: attempt limit and backoff govern failure; terminal disposition; retry state across redelivery. | F09 | done (PR #142, c55ec4c) | `RabbitMqConsumerRetryPolicyTests` (11), `ConsumerRetryAttemptsTests` (8 methods, 10 cases); RabbitMQ 3.13 integration: classic queue retries 200 ms → 400 ms then dead-letters with `x-death`; quorum queue continues `x-delivery-count` across a consumer restart. Limits: classic-queue count is per consumer instance; quorum count includes every return. F09 stays Partial (two-process/crash evidence is R6). |
| R2 | Dead letters and replay: retained destination with failure context, safe replay, operator procedure. Step 1: confirmed replay API and operator procedure. Step 2: attach the handler exception to the retained message. | F10 | done (step 1: PR #143, b02949a; step 2: PR #144, 8c6cca3) | Step 1: `RabbitMqDeadLetterReplayIntegrationTests` (18, RabbitMQ 3.13): replay to the `x-death` source queue keeps body, IDs, content type, headers and `x-death`; `MaxMessages` bound; `TargetQueue`; a missing explicit target throws `QueueDeliveryException` and leaves every message retained; a deleted source queue or missing history leaves that message retained and reports it while the rest replays; a message that fails again can be replayed again (`hero-replay-count` 2); cancellation before or during a replay keeps unreplayed messages retained; unresolved messages ahead do not use up `MaxMessages`; a missing dead-letter queue gives an actionable error; unresolved messages on a quorum dead-letter queue keep `x-delivery-count` 0 across repeated calls (was 3 when requeued); a priority dead-letter queue whose replayed message fails straight back still replays each message once (was the high-priority one twice); a high-priority unresolved message ahead of replayable ones is examined once and the others still replay; quorum-to-quorum replay after a failed replay starts with retries `[0, 1]` (observed `[1, 2]` with `x-delivery-count` kept). A message published under another broker user (validated `user-id`) is replayed, or kept as unresolved with body and `x-death`, under the replay account (both failed with 406 `PRECONDITION_FAILED` while `UserId` was copied). Unit: `DeadLetterReplayHeadersTests`, `RabbitMqDeadLetterReplayValidationTests`. Step 2: `RabbitMqTransportOptions.ErrorQueueSuffix` moves terminal failures to an error queue with `hero-fault-*` context (exception type/message or reason, retries, time, source queue, consumer) after a confirmed copy; integration `ErrorQueue_KeepsFailureContextAndReplaysToTheSourceQueue`, `ErrorQueue_WhenMissing_FallsBackToTheDeadLetterExchange`, `ErrorQueue_WithoutPublisherConfirms_FallsBackToTheDeadLetterExchange`, `ErrorQueue_MessagePublishedByAnotherBrokerUser_IsMovedWithItsFailureContext` (fell back to the dead-letter exchange while `UserId` was copied); unit `FaultHeadersTests` and 7 consumer cases (including: the copy is persistent without `expiration` or `UserId`; `DeadLetterAsync` after an acknowledgement does not copy; cancelling during the move acknowledges once without a retry — all three failed before the fix). F10 stays Partial until two-process evidence (R6). |
| R3 | PostgreSQL Outbox: commit/rollback with business state, competing workers, lease expiry/fencing, process death around broker confirmation. | F06 | in progress (step 1 done: PR #145, 64f012b; step 1b: PR #148; next: crash around broker confirmation, R6) | Step 1b (PR #148): `PostgreSqlUnitOfWork` queue and message storage no longer open a second connection from the password-stripped connection string (all 4 `PostgreSqlUnitOfWorkSchemaLockTests` failed on main with `No password has been provided`) and skip the schema DDL when the catalog shows the table and indexes exist, so two open units of work insert without waiting (both concurrency tests hit the 10 s budget with the skip disabled). Step 1: `PostgreSqlOutboxBusinessTransactionTests` (9 cases, PostgreSQL 16): business row and outbox entry commit together, roll back together (rollback, failed statement, dispose without commit); a worker polling during the open transaction sees nothing and does not wait; two concurrent business transactions both add entries without waiting (both failed with a 30 s Npgsql read timeout before the catalog check, because the no-op `ALTER TABLE ... IF NOT EXISTS` held an exclusive lock until commit); a table missing the lease columns is still upgraded; a mixed-case custom schema/table and a table name long enough for PostgreSQL to truncate its index names also skip the DDL (the long name failed at the 10 s budget without truncation-aware index names). Existing `PostgreSqlOutboxLeaseTests` cover competing claims and lease fencing. Next: crash around broker confirmation (R6). |
| R4 | Transactional external publish on top of R3. Step 1: confirmed, routed publish to a RabbitMQ topic. Step 2: Outbox entries addressed to a topic delivered with it. | F07 | done (step 1: PR #152, d0e9bf4; step 2: PR #155, c7c4655) | Step 2: `PostgreSqlRabbitMqOutboxPublishTests` (2, PostgreSQL 17 + RabbitMQ 3.13): an entry with `Topic` + `RoutingKey` reaches the exact and wildcard bound queues and not an unrelated one, then is `Processed`; with no binding it stays `Pending` with retries counted, the route error naming the exchange, and `Topic`/`RoutingKey` restored from storage, and is delivered once a binding exists (both timed out or failed before). `PostgreSqlOutboxLeaseTests.ClaimExternalAsync_TopicEntry_RestoresTopicAndRoutingKey` and `AddAsync_InvalidDeliveryAddress_ThrowsAndStoresNothing` (storage used by `PostgreSqlUnitOfWork` stored all four invalid combinations before); unit `ExternalOutboxProcessorTests` (invalid combinations rejected before storing, topic without a publish-capable transport rejected, routing key header, unroutable retry; 6 failed before). Step 1: `RabbitMqConfirmedPublishTests` (4, RabbitMQ 3.13): a matching binding reaches every bound queue; no matching binding, and an exchange without bindings, throw `PublishRouteException` (both passed silently with routing not required, as plain `PublishAsync` behaves); the channel stays usable after a returned message. Unit: confirms disabled gives an actionable error; disconnected throws. F07 Partial after step 2; two-process evidence is R6. |
| R5 | Durable Inbox: persist-before-ACK, duplicates, multi-host ownership, crash recovery, retention/poison policy. Step 1: retention cleanup respects each entry's deduplication window. | F08 | in progress (step 1 done: PR #146; step 2 done: PR #151, 42680df) | Step 1: `CleanupOldEntriesAsync_EntryInsideItsDeduplicationWindow_IsRetainedAndStillRejectsRedelivery` (in-memory unit; PostgreSQL and SQL Server integration), `CleanupOldEntriesAsync_EntryPastBothRetentionAndItsWindow_IsRemoved`, `CleanupOldEntriesAsync_EntryWithWindowShorterThanRetention_IsRemovedAfterRetention`. F08 stays Implemented, acceptance open. Step 2: `PostgreSqlUnitOfWorkInboxTests` (real PostgreSQL): `ConcurrentUnitsOfWork_AdmitToCompleteInboxTableWithoutWaitingForEachOther` failed before the fix (30 s read timeout, second admission waited on the first transaction's index lock), `UnitOfWork_RolledBack_LeavesNoInboxEntry` (entry rolls back with the business transaction and can be admitted again), `CompleteTableWithCustomName_SecondAdmissionDoesNotWaitForAnOpenTransaction` (`Biz.My_Inbox` and a table name long enough to truncate its index names; both failed at the 10 s budget while only the unit-of-work path checked the catalog). |
| R6 | Two-process reliability harness (separate OS processes killed at declared points, business ledger) running R3–R5 in hosted CI. Step 1: PostgreSQL Outbox publisher killed around broker confirmation. | F06–F08 | in progress (step 1: PR #156; step 2: this PR) | Step 1: `tests/HeroMessaging.ReliabilityHarness` is an Outbox publisher run as its own OS process (PostgreSQL Outbox, RabbitMQ queue, one delivery in flight); it stops its first confirmed send at a declared crash point and prints `CRASHPOINT <point> <id>`. `PostgreSqlRabbitMqOutboxProcessCrashTests` (2, PostgreSQL 17 + RabbitMQ 3.13, about 65 s each because the takeover waits for the real 1-minute lease): process A enqueues 3 entries and is killed (SIGKILL) `before-publish` or `after-confirm`; its entry stays `Processing`, the others `Pending`; process B, started separately, delivers every entry and all end `Processed`. Business ledger on the queue: `before-publish` gives each message once; `after-confirm` gives the killed message exactly twice with the same `MessageId` (at-least-once; the duplicate is for the Inbox, R5) and the others once. Negative control: with expired leases no longer reclaimable, both cases fail at the 150 s takeover limit. Step 2: the harness gains an `inbox-consumer` role (RabbitMQ queue admitted through `InboxTransportSubscription` to a PostgreSQL Inbox; the event handler appends a row tagged with its OS process ID to a `business_ledger` table). `PostgreSqlRabbitMqInboxProcessCrashTests` (2, about 3 s each): 3 messages on the queue; consumer process A (prefetch 1) is killed holding the first, unacknowledged delivery `before-inbox` (no Inbox entry exists) or `after-inbox` (entry persisted `Pending`, handler not yet run); consumer process B receives the redelivery, every Inbox entry ends `Processed`, the ledger has exactly one row per message, the killed message's row is B's, and the queue is empty after B stops. Negative controls: acknowledging before the Inbox admission loses the `before-inbox` message; an Inbox processor that does not poll persisted entries leaves the `after-inbox` entry `Pending`; each fails at the 60 s limit. Found: `WithInbox()` without `WithMediator()` cannot dispatch (every entry ends `Failed` with "Unable to resolve service for type ICommandProcessor"); the harness configures both. Next: handler killed mid-run (shows whether the business effect can run twice), competing publisher processes, hosted CI evidence. |
| R7 | Consumer testing API. | F24 | todo | |
| R8 | Remote request/response. | F16 | todo | |
| R9 | Persistent scheduling. | F17 | todo | |
| R10 | Saga state plus outgoing work. | F14 | todo | |
| R11 | SQL Server external Outbox. | F12 | todo | |
| R12 | Equivalent-work benchmarks against MassTransit with resource use; negative results recorded. | P01–P05 | todo | |
| R13 | Idempotency in the pipeline: `WithIdempotency` registers an `IdempotencyDecorator` factory that nothing inserts into processing, so configuring it does not deduplicate (found in D0). Step 1: event bus. Step 2: commands, queries, Inbox and transport consumers. | F18 | in progress (step 1 done: PR #147, b69b2aa) | Step 1 (PR #147): `EventBusIdempotencyTests` (7; 3 failed on main for the missing dedup, the store-outage test failed before the decorator fix), `HandlerScopedIdempotencyPolicyTests` (6). A store outage processes without dedup instead of faulting the bus. Keys are per handler; concurrent duplicates can still both run. F18 stays Partial. |
| C1 | CI capacity: `Build and Test` ran on every branch push and again for the pull request, so each PR commit queued two full matrices and four open PRs waited over an hour for runners. Branch pushes now run only through the pull request. | — | done (PR #149, 1989909) | 2026-11-06 10:33–10:50 UTC: 12 queued `Build and Test`/scan runs across PRs #145–#148 and main, each PR head with duplicate push and pull_request check sets. |
| C2 | Red main: the hosted `Integration Tests` jobs stopped at "Wait for databases" because the runner image no longer has `apt-key` (`sudo: 'apt-key': command not found` while installing `mssql-tools18`), so every PR head and main failed before any test ran. The workflow now installs Microsoft's `packages-microsoft-prod.deb` (signing key and source list). | F06–F08, F12 evidence jobs | in progress (PR #153) | 2026-11-07: main run after #150 and PR #148 head `4bb58cd` failed on net8.0/net9.0/net10.0 with that error. Reproduced the new install path in `ubuntu:24.04`: `sqlcmd` installed from `noble main` with `signed-by=/usr/share/keyrings/microsoft-prod.gpg`. |

## Result record

Each accepted scenario must link to:

1. Exact HeroMessaging commit/package and MassTransit version/license basis.
2. Configuration and runnable command.
3. Correctness/failure-injection outcomes.
4. Raw per-run measurements and environment metadata.
5. Resource/latency/throughput summary and stated limitations.
6. Reviewer and release commit containing the behavior.

Until all required evidence exists, use the status **open** or the specific narrower implemented scope. Product goals remain goals until measured and verified.

## Primary references

- [MassTransit transactional Outbox](https://masstransit.massient.com/concepts/outbox/)
- [MassTransit mediator](https://masstransit.massient.com/configuration/mediator/)
- [MassTransit requests](https://masstransit.massient.com/concepts/requests/)
- [MassTransit scheduling](https://masstransit.massient.com/configuration/schedulers/)
- [MassTransit 8.5.12 package](https://www.nuget.org/packages/MassTransit/8.5.12)
- [MassTransit 8.5.12 license](https://github.com/MassTransit/MassTransit/blob/v8.5.12/LICENSE)

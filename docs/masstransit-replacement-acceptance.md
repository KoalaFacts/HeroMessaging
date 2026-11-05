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
| F06 | PostgreSQL external Outbox | Partial | Queue destination path exists. Prove business transaction commit/rollback, competing workers, lease expiry/fencing, and process death around broker confirmation. |
| F07 | Transactional external publish | Open | Persist an event with business state and deliver to the intended exchange/subscribers with defined routing and retry semantics. Existing external confirmed queue send is narrower. |
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

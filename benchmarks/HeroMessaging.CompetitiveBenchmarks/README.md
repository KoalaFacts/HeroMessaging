# In-process command dispatch comparison

This standalone benchmark compares the public `IHeroMessaging.SendAsync` facade
with MassTransit **8.5.12** `IScopedMediator.Send<CompetitiveCommand>`. Both calls
must await the same handler work. It measures one caller, one retained singleton
handler/consumer, the same precreated concrete command, and the same small CPU
workload. The workload completes synchronously during measurements. No application
retry, outbox, tracing, or other optional middleware is configured.

This is an in-process mediator comparison. It does not measure a broker,
serialization, persistence, durability, request/response, event fan-out, or
concurrency scaling. It does not establish feature equivalence or a replacement
verdict. HeroMessaging currently serializes commands within its command
processor; this single-caller workload does not test that concurrency difference.

## Dependency and lifetime choices

`MassTransit` 8.5.12 and `BenchmarkDotNet` 0.15.8 are pinned only in this executable.
MassTransit 8.5.12 is an explicit Apache-2.0 **8.x baseline**, not the latest major
version. A v9 comparison requires the applicable license and a separately recorded
package version. No licensing checks are bypassed.

The handlers are explicitly registered as singletons on both sides. MassTransit's
`AddConsumer` uses `TryAddScoped`, preserving the preceding singleton registration.
Its scoped mediator is resolved from one explicit scope created during setup, so
per-message DI scope creation is not charged to just one provider. HeroMessaging
uses its public facade with `WithMediator().WithEventBus()` because the facade
also requires an event bus. No events are published.

Primary sources checked for these choices:

- [MassTransit 8.5.12 package](https://www.nuget.org/packages/MassTransit/8.5.12)
- [Tagged 8.5.12 license](https://github.com/MassTransit/MassTransit/blob/v8.5.12/LICENSE)
- [Tagged consumer registration](https://github.com/MassTransit/MassTransit/blob/v8.5.12/src/MassTransit/DependencyInjection/Configuration/DependencyInjectionConsumerRegistrationExtensions.cs)
- [Mediator semantics and scopes](https://masstransit.massient.com/configuration/mediator)
- [v9 license requirements](https://masstransit.massient.com/configuration/license)

## Build and verify correctness

From the repository root, choose a supported target (`net8.0`, `net9.0`, or
`net10.0`) and install its runtime:

```sh
dotnet build benchmarks/HeroMessaging.CompetitiveBenchmarks -c Release -f net10.0 -warnaserror
dotnet run --project benchmarks/HeroMessaging.CompetitiveBenchmarks -c Release -f net10.0 --no-build -- --verify
```

Verification checks both providers' completed count and checksum against a fixed
expected result. A separate gate blocks an asynchronously completing handler:
the returned dispatch task must stay incomplete until the gate is released.
Any failure returns a nonzero exit code. Verification is a correctness check,
not a performance measurement.

These intentional negative controls **must return nonzero** for both providers:

```sh
dotnet run --project benchmarks/HeroMessaging.CompetitiveBenchmarks -c Release -f net10.0 --no-build -- --verify --negative-control missing-work
dotnet run --project benchmarks/HeroMessaging.CompetitiveBenchmarks -c Release -f net10.0 --no-build -- --verify --negative-control early-completion
dotnet run --project benchmarks/HeroMessaging.CompetitiveBenchmarks -c Release -f net10.0 --no-build -- --verify --negative-control missing-handler
```

The first deliberately skips dispatch; the second deliberately substitutes an
already-completed task while the real handler remains gated; the third sends a
command with no registered handler. These injections exist only in verification.

## BenchmarkDotNet dispatch and allocation comparison

```sh
dotnet run --project benchmarks/HeroMessaging.CompetitiveBenchmarks -c Release -f net10.0 --no-build -- --filter '*CommandDispatchBenchmarks*'
```

BenchmarkDotNet awaits each returned task. `MemoryDiagnoser` reports managed
allocated bytes per operation and GC collections. Provider initialization, command
construction, and handler resolution warmup occur outside measurement. Each
method increments an attempt counter; cleanup validates total count/checksum
changes against those attempts so skipped work fails the benchmark. The small
counter cost is included symmetrically in both methods.
Do not interpret BenchmarkDotNet iteration percentiles as individual-message
p95/p99 latency. Its ordinary timing reports are steady-state dispatch costs.

## Separate resource and per-call latency observations

Launch **each command separately**, giving each provider its own fresh process:

```sh
dotnet run --project benchmarks/HeroMessaging.CompetitiveBenchmarks -c Release -f net10.0 --no-build -- --resource hero --count 100000 --latency-count 10000 --warmup 10000
dotnet run --project benchmarks/HeroMessaging.CompetitiveBenchmarks -c Release -f net10.0 --no-build -- --resource masstransit --count 100000 --latency-count 10000 --warmup 10000
```

Only the selected provider is initialized in resource mode. The executable still
references both libraries. Counts are bounded: resource count 1–1,000,000,
latency count 1–100,000, warmup count 0–100,000. A 60-second cooperative
cancellation deadline is passed to dispatch calls; it is not a hard process time
limit. Setup, disposal, and work that ignores cancellation need an external
process timeout. An incomplete count or incorrect checksum fails the run.
On Linux, resource mode also rejects a PID namespace whose `/proc` mount exposes
different process IDs: process CPU/memory APIs can otherwise read a different
process or fail. Run with a matching PID namespace and proc mount.

The JSON report separates two measurement batches:

- `ResourceBatch`: an uninstrumented-per-call sequential batch reports elapsed
  time, completed commands per second, process CPU time and CPU nanoseconds per
  completed command, process-wide managed allocated bytes per completed command,
  and generation 0/1/2 GC collection deltas. Boundary measurement and harness
  overhead are included; these are observations, not isolated framework costs.
- `LatencyBatch`: a separate batch stores one timestamp difference per awaited
  call in a preallocated array. It reports nearest-rank p50/p95/p99. These samples
  include timestamping overhead and represent closed-loop, single-caller latency;
  they do not characterize an externally imposed arrival rate, queuing under
  load, or production tails. They are not used to calculate primary throughput,
  CPU, or allocation metrics.

Both batches report expected and actual counts/checksums. Working-set samples
before and after warmup and both batches, plus the process lifetime peak observed
at the end, are whole-process observations. They are **not** retained heap,
steady-state memory, or memory attributable solely to the selected library.

Metadata records the selected provider, runtime, OS/architectures, process ID,
available processor count, GC mode, clock frequency, build configuration and
assembly informational versions alongside the pinned package versions. Preserve
the raw JSON, git revision/dirty state, machine/CPU/quota details, command line,
and repeated runs when collecting acceptance evidence. Run on a quiet controlled
host and alternate provider order. No automatic superiority verdict is emitted.
Short runs on a shared development host verify the harness only; their timings
are not performance acceptance evidence.

BenchmarkDotNet uses its default out-of-process toolchain. A critical validator
rejects any parsed job using an in-process toolchain: cleanup errors can be logged
without making an in-process benchmark report fail, which would weaken the
work-completion check. This validation runs after option parsing and response-file
expansion, including alternate switch casing and grouped short switches.

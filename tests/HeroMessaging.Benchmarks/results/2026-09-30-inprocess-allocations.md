# In-process allocation attribution (2026-09-30)

## Decision

Keep production behavior unchanged. A local metadata-builder prototype reduced allocation, but did not establish a consistent throughput or tail-latency improvement across workloads. It was removed, not merged. Pipeline and dependency lifetimes, logging, retries, and queue defaults remain unchanged.

The retained work is an allocation diagnostic entry point and five regression test cases for correlation metadata, including default-initialized contexts and concurrent asynchronous isolation. Do not interpret this report as a shipped performance improvement.

## Attribution

`--inprocess-allocations` uses warmed synchronous operations and current-thread allocation counters. One baseline process produced:

| Operation | Allocated bytes | Unit |
| --- | ---: | --- |
| Event object | 80.00 | event |
| Sink arrays, three handlers | 60.02 | event |
| Latency result arrays | 24.01 | event |
| Default pipeline construction | 176.00 | delivery |
| Core success execution | 0.00 | delivery |
| Logging disabled, success | 0.00 | delivery |
| Retry success | 0.00 | delivery |
| Correlation execution | 928.00 | delivery |
| Default pipeline execution | 928.02 | delivery |
| Three individual metadata updates | 696.00 | delivery |
| Local metadata-builder candidate | 352.00 | delivery |

The event object and primary measurement arrays account for about 164 B/event. Correlation metadata is a substantial library allocation source; disabled successful logging is not. Construction is smaller than correlation execution, so caching entire pipelines would target a smaller source while risking dependency lifetimes.

These are component probes, not an exhaustive heap profile. They exclude queueing, publisher/task coordination, asynchronous handlers, and concurrent runtime activity. The diagnostic action delegates and shared inputs are constructed before measurement. Dictionary tree shape varies with per-process randomized string hashing, so metadata byte counts can differ between processes. Compare the two metadata operations within the same process; do not subtract unrelated process samples as exact attribution.

## Controlled Prototype

The only production experiment replaced three `WithMetadata` updates with a local dictionary builder and one immutable snapshot. It preserved existing metadata, key comparers, context fields, null/empty ID semantics, and ambient correlation isolation. No mutable builder was shared across calls and no dependencies were cached.

- Baseline production code: `02eab6f`.
- Host: Windows, AMD Ryzen AI 9 HX PRO 370, 24 logical processors.
- Toolchain: .NET SDK 10.0.401, Release `net10.0`.
- Both binaries used the same benchmark harness. No build or test ran concurrently with the comparisons.
- Each workload used fresh processes in A-B-B-A-A-B order, with A the baseline and B the candidate. Each process warmed 1,000 events, then measured three batches: nine measurements per variant per workload, 54 main measurements total.
- The one-million-event no-op exploratory run was too short for several sustained-rate samples. It was replaced by the two-million-event workload below, not used in the main comparison.

Each table entry is a median of nine per-run values, not a pooled latency percentile. Sustained rate is the median of each run's full-second median rate. Handler completion is recorded inside handlers, before the outer pipeline unwinds. Allocations include the same harness and event costs in both variants.

| Workload | Variant | Complete events/s | Sustained events/s | Accept p95/p99 ms | All-handler p95/p99 ms | B/event | GC gen0/1/2 |
| --- | --- | ---: | ---: | --- | --- | ---: | --- |
| CPU, three handlers | Baseline | 102,312 | 113,867 | 0.52 / 2.17 | 0.44 / 2.09 | 4,427 | 263 / 2 / 1 |
| CPU, three handlers | Candidate | 108,994 | 110,469 | 0.51 / 2.22 | 0.43 / 2.16 | 3,271 | 193 / 1 / 1 |
| No-op, one handler | Baseline | 331,559 | 321,819 | 0.21 / 2.05 | 0.09 / 1.78 | 1,544 | 359 / 2 / 1 |
| No-op, one handler | Candidate | 309,303 | 314,241 | 0.25 / 1.98 | 0.11 / 1.82 | 1,205 | 279 / 2 / 1 |
| Async, three handlers | Baseline | 70,498 | Not compared | 0.69 / 2.42 | 0.84 / 2.65 | 9,024 | 271 / 1 / 0 |
| Async, three handlers | Candidate | 84,402 | Not compared | 0.58 / 2.08 | 0.69 / 2.17 | 7,938 | 240 / 1 / 0 |

Several async batches finished in under three seconds (two baseline and five candidate batches), so their sustained rates were unavailable. Comparing only the slower surviving batches would bias the comparison; no async sustained-rate improvement is claimed.

Allocation medians fell by 26.1% for CPU, 22.0% for no-op, and 12.0% for async. CPU total throughput increased 6.5%, but sustained throughput fell 3.0% and completion p99 rose 3.3%. No-op total throughput fell 6.7%, sustained throughput fell 2.4%, and completion p95/p99 rose. Async total throughput and latency improved, but this does not offset uncertainty on the performance-critical no-op path.

The CPU total-rate ranges overlap (84,424-121,590 baseline versus 90,127-122,382 candidate), as do the no-op ranges (299,596-364,884 versus 297,173-357,283). These local samples do not prove a causal slowdown or statistical significance. They do show that lower allocation alone is insufficient evidence of an across-the-board performance win. Rejecting the prototype is conservative; it avoids shipping an uncertain tradeoff.

## Reproduce

Run attribution and these workloads against the same diagnostic harness built with each production variant:

```bash
dotnet run --project tests/HeroMessaging.Benchmarks -c Release -f net10.0 -- --inprocess-allocations
dotnet run --project tests/HeroMessaging.Benchmarks -c Release -f net10.0 -- --inprocess-concurrent 500000 3 32 16 4 cpu
dotnet run --project tests/HeroMessaging.Benchmarks -c Release -f net10.0 -- --inprocess-concurrent 2000000 1 32 64 24 noop
dotnet run --project tests/HeroMessaging.Benchmarks -c Release -f net10.0 -- --inprocess-concurrent 250000 3 32 16 4 async
```

Keep raw samples, alternate order, and lengthen async batches before evaluating sustained throughput. The next investigation should capture CPU/scheduling evidence on a stable host rather than assume that fewer immutable nodes necessarily make processing faster.

## Verification

The retained diagnostic and tests passed a strict Release solution build with zero warnings and errors. The main test project passed 2,514 tests on each of `net8.0`, `net9.0`, and `net10.0`, both with the prototype and after removing it. The new diagnostic completed against the restored production implementation. Source-content checks confirm that the production decorator matches the baseline.

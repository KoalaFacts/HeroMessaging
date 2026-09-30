# Publish Receipt Ordinary-Publish Guard

Date: 2026-09-30. This is a functional feature, not a throughput optimization.

## Scope And Setup

`PublishAndWaitAsync` is optional and in-process only. It returns per-registration final pipeline outcomes, including retry and validation policies. It preserves transient handler resolution and reports queue rejection or accepted delivery abortion instead of waiting forever after a bus fault.

Cancellation at entry prevents publication. Later cancellation stops only the caller's wait; both pending admission and handler execution continue. These receipts are not durable or broker acknowledgements. Waiting from a handler on the same saturated bus can deadlock.

The ordinary `PublishAsync` method body is unchanged and creates no receipt trackers. The final shared processing path adds nullable completion checks and tracker reset; receipt exception observation runs only in a tracked-delivery wrapper. The ordinary envelope has an additional nullable field, so this is not a claim of zero overhead.

Baseline: commit `91c50a9`, with its Release/net10.0 benchmark output copied before feature edits. Candidate: the uncommitted receipt implementation accompanying this report. Queue options, the retained concurrent harness, and pipeline policy order were unchanged.

The runner uses 1,000 warmup events, then three measured batches per fresh process. Each workload/round has two baseline and two candidate processes, giving six batches per variant. Round 1 and the final round use baseline-candidate-candidate-baseline order. The reverse round uses candidate-baseline-baseline-candidate and repeats only CPU/async.

| Workload | Messages/batch | Handlers | Producers | Capacity | Parallelism |
| --- | ---: | ---: | ---: | ---: | ---: |
| noop | 2,000,000 | 1 | 32 | 64 | 24 |
| cpu | 500,000 | 3 | 32 | 16 | 4 |
| async | 500,000 | 3 | 32 | 16 | 4 |

All 32 processes / 96 measured batches exited successfully with the harness's per-registration delivery checks. No tracing or agent-owned builds/tests overlapped these benchmark processes. The host was not dedicated; absence of external load was not established.

## Retained Results

Values are medians of per-batch values, not pooled latency percentiles. Batches within a process are correlated; six batches are not six independent fresh-process observations.

| Round | Workload | Baseline complete events/s | Candidate complete events/s | Candidate difference |
| --- | --- | ---: | ---: | ---: |
| Initial shared catch | noop | 312488 | 323037 | 3.4% |
| Initial shared catch | cpu | 99401 | 95142 | -4.3% |
| Initial shared catch | async | 73914 | 68518 | -7.3% |
| Reverse shared catch | cpu | 88222 | 109324 | 23.9% |
| Reverse shared catch | async | 76179 | 69417 | -8.9% |
| Final isolated catch | noop | 392698 | 381152 | -2.9% |
| Final isolated catch | cpu | 88057 | 93518 | 6.2% |
| Final isolated catch | async | 66125 | 69460 | 5.0% |

The first implementation added a filtered catch to the shared async processing method. Async throughput fell in both initial rounds (-7.3% and -8.9%). That was treated as a warning, not dismissed or hidden. The final implementation moves receipt exception handling into an opt-in wrapper retaining its tracker separately from the pooled envelope. Normal execution no longer traverses that receipt-specific exception region.

Final implementation latency/throughput guard:

| Workload | Variant | Steady events/s median | Accept p95 / p99 ms | All-handler p95 / p99 ms | Complete events/s batch range |
| --- | --- | ---: | --- | --- | --- |
| noop | baseline | 445427 | 0.130 / 1.605 | 0.080 / 1.340 | 353524-449750 |
| noop | candidate | 425997 | 0.100 / 1.630 | 0.085 / 1.305 | 268813-505419 |
| cpu | baseline | 111194 | 0.745 / 4.150 | 0.495 / 3.755 | 72994-119764 |
| cpu | candidate | 105729 | 0.715 / 3.750 | 0.620 / 3.465 | 40429-112919 |
| async | baseline | 64139 | 1.080 / 4.365 | 1.400 / 4.810 | 52163-90298 |
| async | candidate | 71180 | 1.030 / 3.935 | 1.310 / 4.365 | 53779-80940 |

Final implementation process-wide allocation/CPU/GC guard:

| Workload | Variant | B/event | CPU us/event | GC gen0 / gen1 / gen2 | Contentions/event | Work items/event |
| --- | --- | ---: | ---: | --- | ---: | ---: |
| noop | baseline | 1552.5 | 11.625 | 362.5 / 1.5 / 1.0 | 0.01770 | 0.670 |
| noop | candidate | 1517.0 | 12.655 | 353.5 / 1.5 / 1.0 | 0.02050 | 0.650 |
| cpu | baseline | 4272.0 | 32.420 | 255.0 / 2.0 / 1.0 | 0.02235 | 2.725 |
| cpu | candidate | 4391.0 | 36.920 | 262.5 / 2.0 / 1.0 | 0.02455 | 2.795 |
| async | baseline | 9024.0 | 39.285 | 545.0 / 1.5 / 0.0 | 0.02880 | 8.420 |
| async | candidate | 9210.5 | 42.685 | 557.0 / 2.0 / 0.0 | 0.03425 | 8.375 |

Allocation includes the producer, measurement arrays, event creation, handlers, library, and runtime; it is not isolated library allocation. CPU/contention/work-item deltas bracket publication through observed handler completion. Sorting is outside these runtime deltas, but included in the batch allocation measurement. Final outer pipeline unwinding can overlap the handler-completion measurement boundary.

The small warmup does not eliminate runtime ramp-up during the first measured batch. Wide ranges, correlated batches, and differences between rounds prevent a robust no-regression or improvement claim. In particular, final CPU/event and multi-handler B/event did not improve along with throughput. The change in async throughput sign does not prove exception-region isolation caused the improvement. No scheduling, capacity, thread-pool, queue replacement, or pipeline-lifetime tuning was added.

**Gate: functional verification passed; ordinary-publish performance is not conclusively cleared.** Before merging on a strict performance requirement, repeat on a controlled host with longer warmup and more independent processes. Do not select only the final favorable async round. Opt-in receipt throughput, p95/p99, and B/event have not yet been benchmarked.

## Functional Verification

- Final strict solution build: `dotnet build HeroMessaging.slnx -c Release -warnaserror --no-restore -v:q`; zero warnings/errors.
- Final main test project: 2,533 passed, zero failed/skipped on each of net8.0, net9.0, and net10.0. This includes 19 new receipt tests.
- Final Abstractions tests: 395 passed on net10.0. Final Architecture tests: 33 passed on each of net8.0/net9.0/net10.0, zero failed/skipped.
- Receipt tests cover empty/duplicate handlers, ordered outcomes, retry, validation failure, queue rejection/fault, cancellation during execution/admission, pooled/concurrent isolation, transient resolution, shutdown drain, and facade/decorator forwarding.
- These are pre-CI local results, not full solution integration tests or CI proof. No remote push or merge had been performed at capture time. Subsequent comparisons use the paired CI workflow; these historical samples are retained rather than selected as a performance claim.

## Reproduce And Private Evidence

Build the candidate, retain a baseline from the commit above, then alternate fresh processes running:

```powershell
dotnet tests/HeroMessaging.Benchmarks/bin/Release/net10.0/HeroMessaging.Benchmarks.dll --inprocess-concurrent 2000000 1 32 64 24 noop
dotnet tests/HeroMessaging.Benchmarks/bin/Release/net10.0/HeroMessaging.Benchmarks.dll --inprocess-concurrent 500000 3 32 16 4 cpu
dotnet tests/HeroMessaging.Benchmarks/bin/Release/net10.0/HeroMessaging.Benchmarks.dll --inprocess-concurrent 500000 3 32 16 4 async
```

Private raw logs are retained under ignored `artifacts/receipt-fast-path-20260930-055723/`, `artifacts/receipt-fast-path-reverse-20260930/`, and `artifacts/receipt-fast-path-isolated-20260930/`. Each log names its workload, process order, and variant. The private baseline binaries remain under `artifacts/receipt-baseline/` for reproducibility. Do not commit these binaries or raw logs.

Final measured HeroMessaging.dll SHA-256:
- Baseline: `6867CAB5F3631D3F0A889DD70ECE90B30F88F253AF41B108F175425D2E26D9A2`.
- Candidate: `E59152956DAC6AAE2CB9545B1A071F77F4B539A14ECDB03B23DAD880442C5DDA`.

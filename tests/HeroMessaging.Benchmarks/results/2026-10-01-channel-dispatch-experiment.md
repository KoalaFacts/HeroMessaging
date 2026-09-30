# Bounded channel dispatch experiment (2026-10-01)

## Decision

Withdraw the Channel-backed EventBus dispatcher. Keep the existing ActionBlock implementation, scheduling regression tests, and multi-batch calibration correction. There is no shipped throughput improvement in this change.

The candidate improved some async/receipt measurements but regressed CPU throughput, allocation, and handler tail latency in the same paired run. These results do not justify replacing the queue engine or further tuning it without a new CPU/scheduling diagnosis. They do not show that Channels are universally slower.

## Evidence

The [completed CI comparison](https://github.com/KoalaFacts/HeroMessaging/actions/runs/36737501907) tested experimental head `22e47425fc2dfe786c580cd6c3b3eb1858663ebb`. Its checked-out candidate merge revision was `372ab771b6709496d71c791e0eae21b2cede77e4`; baseline was `783f935a55b0ca6e812b73572abb5fda1e6e4269`. Both sources were clean. This is historical evidence for the withdrawn implementation, not the final PR head.

The same harness/runner alternated baseline-candidate-candidate-baseline, with two fresh processes per variant and three measured batches per process. All 24 processes and 72 measured batches completed; the shortest batch was 11.853 seconds, and every batch had at least 10 full steady windows. All 24 calibration processes also used three batches, excluded from measurement statistics. Raw JSON, binary identities, configuration, logs, and the manifest are retained in the run's comparison artifact.

The Linux runner reported four available processors. Each process warmed 100,000 events and used 32 producers; no-op used one handler, capacity 64, and parallelism 24, while CPU/async used three handlers, capacity 16, and parallelism 4. The earlier 24-processor Windows no-op profile was an investigation lead, not proof of the same bottleneck on this runner or in CPU/async workloads.

Rates below are medians of process batch medians, not pooled samples. This single hosted-runner comparison is not a statistical significance claim or a dedicated-hardware performance target.

| Mode / workload | Baseline events/s | Candidate events/s | Difference | Handler p99 baseline / candidate ms | B/event baseline / candidate |
| --- | ---: | ---: | ---: | --- | --- |
| Publish / no-op | 730204 | 697407 | -4.49% | 0.086 / 0.546 | 1457.47 / 1403.08 |
| Publish / CPU | 219200 | 198840 | -9.29% | 0.101 / 0.581 | 4183.64 / 5476.58 |
| Publish / async | 153687 | 168660 | +9.74% | 0.708 / 0.759 | 9126.39 / 9476.08 |
| Receipt / no-op | 478691 | 533558 | +11.46% | 0.008 / 0.034 | 2381.76 / 2302.12 |
| Receipt / CPU | 189496 | 169827 | -10.38% | 0.105 / 0.660 | 5752.52 / 7058.68 |
| Receipt / async | 128735 | 136910 | +6.35% | 0.868 / 0.890 | 11289.74 / 11723.83 |

CPU/event rose from 17.74 to 19.54 us for publish/CPU, and from 20.31 to 22.72 us for receipt/CPU. Completed thread-pool work items/event rose from 1.880 to 3.751 and from 3.361 to 6.820 respectively. The counters include producer, handler, harness, and runtime work; they suggest scheduling amplification but do not identify exclusive library CPU cost. Raw GC counts cannot be compared across scenarios with different message counts.

## Sampling Correction

The [initial comparison](https://github.com/KoalaFacts/HeroMessaging/actions/runs/36731084870) correctly failed its unchanged 10-second minimum. Receipt/no-op calibration used only one candidate batch at 10.942 seconds, while the three measured batches took 10.870, 9.624, and 9.539 seconds. Calibration did not cover faster later batches within the same process. The underlying runtime mechanism was not established.

Calibration now uses the same warmup and batch count as measurement, chooses the fastest batch across both revisions, and records all calibration durations. The four-attempt limit, 10,000,000-message cap, duration/steady-window checks, paired counts, and process ordering are unchanged. No failed sample is automatically retried or silently discarded. An intentionally insufficient resource-bound test still fails before measurement without creating a success report.

## Reliability Limits

The experimental head passed all nine OS/framework unit jobs, all three integration jobs, and quality/security checks. One earlier local net10.0 process hung after 2,528 tests and was terminated for diagnosis. A private managed sample included RingBuffer processor/wait paths but did not establish the root cause or rule out a scheduling regression. Standalone and subsequent complete three-framework runs passed 2,543 tests per framework. The transient is not claimed fixed; private traces were not uploaded.

The final retained tests are independent of either queue implementation. Do not reuse experimental-head test counts or CI results as validation of later revisions.

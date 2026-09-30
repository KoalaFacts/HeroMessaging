# In-process CPU and scheduling investigation (2026-09-30)

## Decision

Keep production code and defaults unchanged. Investigate Dataflow synchronization and continuation scheduling before further metadata/allocation experiments. This is diagnostic evidence, not a shipped optimization or proof that another queue implementation is better. The subsequent native no-op capture below provides an actual on-CPU hotspot ranking for that scenario only.

The initial investigation collected actual process CPU consumption, managed wait stacks, monitor contention events, and thread-pool counters. Its native recording attempt failed with `0xc5585011` (failed to enable the policy to profile system performance). A subsequent operator-elevated no-op recording succeeded; the initial permissions blocker no longer applies to that capture. Native CPU/async captures remain outstanding.

## Method

- Production baseline: `02eab6f`, unchanged, including the original correlation metadata implementation.
- Host: Windows, AMD Ryzen AI 9 HX PRO 370, 24 logical processors; .NET SDK 10.0.401, Release `net10.0`.
- Same concurrent runner, default pipeline, disabled logging, no optional validation/error/metrics registrations. Synthetic CPU work is 1,024 integer hash rounds per delivery; async work yields once per delivery.
- Eighteen untraced measured batches: no-op in fresh processes with producer counts 32-1-1-32 (three batches each), then three CPU and three async batches. Each process warmed 1,000 events.
- Twelve traced measured batches: three each for no-op, CPU, and async, followed by three no-op batches with explicit processing-window markers. No build or test ran alongside a workload. These sequential local samples are noisy, not a controlled dedicated-host regression gate.
- Traces enabled managed sampled-thread-time, runtime GC/threading/contention events, and one-second `System.Runtime` counters. Analysis reported zero lost events for all four traces.

The retained runner now measures CPU core-seconds with `Process.TotalProcessorTime`, monitor contentions with `Monitor.LockContentionCount`, and completed work items with `ThreadPool.CompletedWorkItemCount`. Deltas bracket publication through observed handler completion, excluding sink allocation and latency-array sorting. They include producer work, event construction, handler work, measurement instrumentation, GC, and runtime work. Final outer pipeline unwinding can overlap the completion boundary.

The `HeroMessaging-InProcessBenchmark` provider emits event IDs 1/2 (`BatchStart`/`BatchStop`; TraceEvent renders them as `Batch/Start` and `Batch/Stop`). Four pairs were verified in the marked trace. Excluding the warmup leaves three measured windows totaling 21.410 seconds. Counter samples are selected by polling timestamp; a one-second counter interval can cross a window boundary.

## Observed Untraced Results

Each value below is a median of per-batch values, not a pooled percentile. Different workloads have different capacity/parallelism and must not be used to isolate a single component's CPU cost.

| Workload | Messages/batch | Handlers / producers / capacity / parallelism | Batches | Complete events/s | CPU us/event | Average utilized cores | Contentions/event | Work items/event | All-handler p99 ms |
| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| No-op | 2,000,000 | 1 / 32 / 64 / 24 | 6 | 259,797 | 12.16 | 3.63 | 0.0172 | 0.62 | 1.51 |
| No-op control | 2,000,000 | 1 / 1 / 64 / 24 | 6 | 194,622 | 8.03 | 1.64 | 0.0067 | 0.57 | 0.49 |
| CPU | 500,000 | 3 / 32 / 16 / 4 | 3 | 56,984 | 34.44 | 2.00 | 0.0264 | 2.63 | 6.15 |
| Async | 500,000 | 3 / 32 / 16 / 4 | 3 | 62,279 | 46.62 | 3.88 | 0.0303 | 8.29 | 3.23 |

The no-op producer control held capacity and processing parallelism fixed. With 32 producers, acceptance p99 was 1.84 ms versus 0.01 ms with one producer. More producers increased throughput as well as CPU/event and contention/event in these samples; reducing producers is not a demonstrated throughput optimization.

Dispersion matters: 32-producer no-op throughput ranged 194,760-397,322 events/s and utilized cores ranged 1.98-4.11. The single-producer range was 127,976-247,387 events/s and 0.72-2.99 cores. Async throughput ranged 33,568-83,651 events/s, with completion p99 ranging 2.88-24.76 ms. Do not convert these results into a reliable performance target, infer thermal throttling without evidence, or claim a causal improvement over the earlier allocation experiment.

## Observed Managed Trace Evidence

For the marked 32-producer no-op trace, processing-window analysis found:

- 144,008 completed monitor contention events, with 514.173 aggregate thread-seconds of reported contention duration. Concurrent waits overlap; this is not 514 seconds of wall time or CPU time. These events alone do not identify the responsible lock.
- 1,045.259 sampled thread-seconds across the three windows. `Monitor.Enter_Slowpath` accounted for 56.58%; thread-pool `LowLevelLifoSemaphore.WaitForSignal` accounted for 22.85%.
- Inspection of sampled monitor-wait callers attributed 31.38% of all sampled thread time to Dataflow consumer paths (`TargetCore.ProcessMessagesLoopCore`, including ActionBlock completion/message acquisition), 25.18% to Dataflow sending (`SendAsync`/`SendAsyncSource`), and 0.01% to other observed monitor-enter paths. This is sampled call-path attribution, not an exact lock-duration breakdown.
- Thread-pool thread count ranged 44-48; queue length ranged 0-33, with a sample average of 24.33. Process CPU counter samples averaged 14.32% of all machine CPU resources. Polling includes interval-boundary effects; the runner's direct per-batch CPU measurements are the better workload-window measure.

The earlier, unfiltered CPU and async traces also showed contention (39,157 and 86,008 completed contention events respectively). Their thread-pool queues peaked at 88 and 12. These captures include initialization and result processing, so their aggregate values are exploratory only; they are not directly comparable to the marked no-op window.

Managed samples include waits and runtime/native transitions. `CPU_TIME`/`UNMANAGED_CODE_TIME` labels in converted output are not OS on-CPU proof. Waiting samples must not be normalized into CPU percentages. This follows the [dotnet-trace documentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace) and [EventPipe scope](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/eventpipe). CPU-counter normalization and thread-pool counter meanings are documented in the [runtime counter reference](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/available-counters).

Profiling can perturb scheduling, allocations, and throughput. The marked trace's batches completed at 330,019, 204,180, and 360,155 events/s, versus the broad untraced range above. Overlap does not establish negligible tracing overhead. Traced rates are not fair optimization comparisons.

## Interpretation And Next Gate

1. Dataflow synchronization is a credible investigation target: monitor waits appear in its send/consumer paths, and actual contention events independently confirm contention exists. This does not establish its share of actual CPU execution or prove it dominates every workload.
2. The async workload completes about 8.29 thread-pool work items/event versus 2.63 for the CPU workload under the same handler/producer/capacity/parallelism settings. Continuation/scheduling amplification merits investigation. This is process-wide accounting, not per-library-method attribution or proof of thread-pool starvation.
3. Low aggregate utilization, waiting stacks, and intermittent queueing do not by themselves distinguish lock convoying, OS ready-time, GC effects, thread-pool adaptation, or external host interference. The subsequent no-op native capture confirms meaningful monitor-spin CPU cost. Extend attribution to CPU/async workloads and stable-host controls before changing scheduling, thread-pool minimums, pipeline lifetimes, or the queue engine.

Do not tune capacity again based on these noisy samples. After native attribution identifies a mechanism, test one bounded change against the unchanged baseline in alternating fresh processes, with throughput, p95/p99, CPU/event, work items/event, allocations, and GC. No stable end-to-end benefit means no production change.

## Subsequent Native No-Op Capture

The operator ran the WPR script elevated with the no-op configuration. The benchmark completed three batches and WPR reported `The trace was successfully saved`. Saving continued for several minutes while the UI initially displayed `0%`; this was not a failed or cancelled recording. The final ETL is 2,859,466,752 bytes. `xperf -a tracestats -timespan` reports zero lost events and zero lost buffers.

The ETL covers 101.727 seconds of system-wide activity. Analysis filters to the benchmark PID recorded in `session.txt`, then uses the three provider-marked measured windows, excluding warmup and sorting gaps:

| Batch | Start ms relative to trace | Stop ms relative to trace | Traced complete events/s | All-handler p99 ms |
| --- | ---: | ---: | ---: | ---: |
| 1 | 926.1144 | 6,820.4589 | 339,309 | 2.15 |
| 2 | 7,537.9955 | 13,040.0679 | 363,500 | 2.32 |
| 3 | 13,385.6837 | 19,198.0045 | 344,097 | 2.34 |

The measured windows total 17.208738 seconds. A private TraceEvent 3.1.21 ETLX analysis resolved the target CoreCLR and kernel PDBs through Microsoft's symbol server with exact recorded PDB identities; unsafe matching was not enabled. The converter emitted image-size consistency warnings for other processes, with no such warning identifying the target PID. Those diagnostics are retained rather than suppressed.

### Actual On-CPU Findings

There are 98,426 target CPU samples after excluding ISR/DPC/non-process samples. Each sample has Count=1; 17 samples lack a call stack, and 6,705 (6.81%) lack a resolved leaf name. Percentages below use all 98,426 samples, not just resolved samples.

| Exclusive leaf | Samples | Share |
| --- | ---: | ---: |
| `ObjHeader::EnterObjMonitorHelperSpin` | 34,357 | 34.91% |
| `ObjectNative::Monitor_TryEnter_FastPath` | 5,809 | 5.90% |
| `ObjectNative::Monitor_Exit_FastPath` | 4,308 | 4.38% |
| `EventBus.PublishAsync` state machine | 3,357 | 3.41% |
| `EventBus.ProcessEventWithPipeline` state machine | 2,405 | 2.44% |
| `CoreMessageProcessor.ProcessAsync` state machine | 2,363 | 2.40% |
| `ThreadNative_SpinWait` | 2,179 | 2.21% |
| `CorrelationContextDecorator.ProcessAsync` state machine | 1,017 | 1.03% |

For monitor-spin samples only, grouping by the first resolved managed caller assigns 32,272 samples (32.79% of all CPU samples) directly to Dataflow send/consumer/completion methods. Leading callers are `TargetCore.ProcessMessagesLoopCore` (8.62%), `DataflowBlock.SendAsync` (6.72%), `ActionBlock.ProcessMessageWithTask` (6.24%), and `SendAsyncSource.OfferToTarget` (4.06%). This is sampled call-path attribution, not an exact lock-object identification or the isolated exclusive CPU cost of those managed methods. Unlike the earlier managed sampled-thread-time numbers, these are actual on-CPU samples.

### Scheduler Evidence And Limits

Context-switch accounting clipped to the same windows gives 98.165 scheduled core-seconds, or 5.704 average scheduled cores. The runner reports 96.84 process CPU core-seconds across the batches; scheduled time can include interrupt execution and has different boundaries, so the two measures are not interchangeable.

An independent `xperf -a cswitch -process -range 926114 6820459` pass over the first window reports 35.399622 scheduled core-seconds for the target PID, versus 35.23 process CPU core-seconds in the runner. Range values are microseconds. This cross-check verifies plausible process/window attribution, not equivalence of the two time definitions or the full ready/blocked accounting.

Indexed thread-lifetime metadata is used for switched-out and awakened thread ownership; relying only on parser `OldProcessID`/`AwakenedProcessID` returned no matching events and was rejected. The observed target has 321,698 switch-outs and 265,510 ready events within the windows. Completed, clipped state intervals account for 12.552 ready thread-seconds and 837.959 blocked thread-seconds. Thread-seconds overlap across threads and are not elapsed wall time or a latency percentile. Blocked totals include idle workers and runtime threads, not just delivery waits. There are 3,579 unmatched measured switch-ins (about 1.11%); intervals without a known ready transition and unclosed intervals are not imputed. These are partial interval totals, not a complete scheduler balance sheet or proof that ready-time is harmless.

Blocked stacks independently include `Monitor_Enter_Slowpath` under Dataflow sending and message acquisition. GC waits and idle thread-pool waits are also present. This pass does not quantify GC pause windows or causally separate tracing/host interference from library synchronization. Native CPU samples themselves include visible stack-unwind work, so recording overhead is material and traced throughput is not an optimization comparison.

The next bounded experiment should reduce measured Dataflow synchronization/continuation overhead while retaining backpressure, handler completion semantics, cancellation, shutdown, and DI lifetimes. First obtain the same native evidence for CPU/async scenarios and an untraced alternating baseline. Do not infer that replacing Dataflow wholesale, caching scoped pipelines, or changing thread-pool minimums is justified by this single no-op capture.

Private evidence is under ignored `artifacts/wpr-inprocess/20260930-044942-noop-1955e6cd/`: `capture.etl`, its ETLX index and managed symbol directory, `benchmark.log`, `wpr.log`, `tracestats.txt`, `cswitch-first-window.txt`, and `native-analysis-final.txt`. Downloaded PDBs stay in ignored `artifacts/wpr-symbols/`. The temporary native-analysis source/project was removed; ignored generated binaries remain under `artifacts/wpr-analysis/` because recursive artifact cleanup was previously blocked by tool policy. No analysis process remains running. Do not commit or upload raw traces, logs, symbols, or ETLX files.

## Reproduce And Evidence

Tracing commands and window-marker semantics are in the benchmark README. Raw local evidence is retained under ignored `artifacts/` paths:

- `cpu-untraced-noop.log`, `cpu-untraced-cpu.log`, `cpu-untraced-async.log`: direct CPU and runtime deltas, throughput, latency, allocation, and GC output.
- `cpu-noop.nettrace`, `cpu-multi.nettrace`, `cpu-async.nettrace`: initial process-targeted traces.
- `cpu-noop-windowed.nettrace`, `cpu-noop-windowed.log`: marked trace and its workload output.
- `cpu-noop-windowed.analysis.txt`, `cpu-noop-windowed.stacks.txt`, and trace-adjacent counter/window CSV files: extracted diagnostic evidence. Speedscope conversion retains navigable call stacks.

Raw traces can contain local environment/process metadata and are not intended for commit or public upload. The temporary operator analysis source/project files were removed after extraction. Cleanup of their ignored generated `bin`/`obj` artifacts was blocked by tool policy; they remain under `artifacts/profile-analysis/` and are not running. No new package dependency, production optimization, push, or PR is part of this investigation. The retained diagnostic tooling, regression tests, and reports are packaged separately from any future production optimization.

## Verification

The strict Release solution build completed with zero warnings and errors. The current main test project passed 2,514 tests on each of `net8.0`, `net9.0`, and `net10.0`, with no failures or skipped tests. The diagnostic workloads all completed their exactly-once handler checks, and the marked trace verified the warmup plus three measured start/stop pairs. This is not a new full integration-suite run or CI result.

The follow-up Windows capture script and profile are available in the benchmark project. WPR schema/profile inspection, the marker provider GUID, all three scenario preflights, named-instance argument forwarding, hidden benchmark process/output capture, invalid-timeout rejection, and non-elevated refusal were verified. The subsequent operator-elevated no-op run verified real recording/save, workload completion, batch markers, zero-loss ETL headers, and usable target runtime symbols. Real cancellation/failure cleanup remains unverified.

The entry point also accepts Windows PowerShell 5.1 and delegates to a verified PowerShell 7 runtime on PATH before executing collector-only APIs. Actual 5.1 launch was tested with both the agent environment and the normal machine/user PATH, including scenario/timeout/validation forwarding and propagation of non-elevated refusal. The save-phase messaging follow-up passed another actual 5.1-to-7 no-op validation launch and PowerShell syntax validation. A fresh strict Release solution build completed with zero warnings and zero errors. PowerShell 7 remains required for collection; no runtime installation, execution-policy change, or automatic elevation was introduced. The earlier test counts above are historical; the native analysis/save-message follow-up is not a new full test or CI run.

Before packaging the diagnostics, the modified correlation test file's existing region directives were replaced with nested behavioral test classes, preserving assertions and test count. A fresh strict Release solution build passed with zero warnings/errors, and the main test project passed 2,514 tests with no failures/skips on each of `net8.0`, `net9.0`, and `net10.0`, both before and after this structural cleanup. Source-content checks found no personal filesystem paths or region directives in the changed files. Production code remains unchanged; this is not a new full integration-suite run or CI result.

The operator-authorized cleanup attempt for the temporary analysis directories and obsolete allocation baseline binaries was rejected by tool policy before deletion. Those ignored directories remain; no alternative deletion mechanism was used. Successful traces, analysis logs, and symbol caches are retained for the next comparison.

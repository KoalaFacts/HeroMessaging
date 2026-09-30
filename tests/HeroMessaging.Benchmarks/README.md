# HeroMessaging Benchmarks

This project contains BenchmarkDotNet microbenchmarks for command, event, query, saga, storage, and ring-buffer paths. Results are local measurements, not end-to-end transport guarantees.

## Run

Use a Release build on a stable machine:

```bash
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --framework net10.0 -- --filter "*CommandProcessor*"
```

Remove the filter to run all benchmarks only after configuring PostgreSQL for the opt-in `PostgreSqlInboxBenchmarks`. CI performance comparison focuses on the in-process workloads below; the other BenchmarkDotNet microbenchmarks remain manually runnable. The database benchmark is not part of this CI job.

The custom configuration reports mean, median, p95, and allocations. These measurements do not include a real broker, sustained load, p99, or an end-to-end publish-to-handler latency distribution. Capture a baseline on fixed hardware before using results as a regression gate or claiming a throughput target.

`--inprocess [message-count] [handler-count] [noop|cpu|async]` is a manual EventBus workload using the default pipeline and distinct handlers (one no-op handler by default). The `cpu` handler performs 1,024 integer hash rounds per delivery; the `async` handler yields once before recording completion. These are controlled synthetic workloads, not representative application business logic. The runner verifies exactly one completion per handler per event, then reports publisher and all-handler-complete rates, publish-to-accept and publish-to-first-handler p95/p99, all-handler-complete p50/p95/p99, and total process allocation per event across three warmed runs. The steady min/median/max rates use only full one-second windows after the first second; use enough messages for at least three seconds per run. Publish acceptance and handler completion can overlap, so the latency columns are not additive. Handler timestamps are taken inside the handlers, not after the outer pipeline unwinds. This is not a competitor comparison or a CI gate. For example:

```bash
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --framework net8.0 -- --inprocess 500000 1
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --framework net8.0 -- --inprocess 250000 3
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --framework net8.0 -- --inprocess 150000 8
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --framework net8.0 -- --inprocess 250000 3 cpu
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --framework net8.0 -- --inprocess 150000 3 async
```

`--inprocess-concurrent <message-count> <handler-count> <producer-count> <capacity> <parallelism> <noop|cpu|async|delay> [publish|receipt] [warmup-count] [runs] [json-output]` measures multiple producers publishing through one EventBus. The last four arguments are optional (defaults: publish, 1,000 warmup events, three runs, no JSON file). Each producer awaits publication return before sending its next event; producers start together and publish disjoint sequence numbers. The configured capacity and parallelism override EventBus defaults. The `delay` handler waits for a nominal 2 ms per delivery to make backpressure observable, but actual delay depends on the OS timer and scheduler. For example:

```bash
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --framework net10.0 -- --inprocess-concurrent 500000 3 32 16 4 cpu
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --framework net10.0 -- --inprocess-concurrent 1000 1 16 8 2 delay
```

`pending-publish` is the share of publication calls whose task was incomplete immediately after invocation. In publish mode it is a backpressure signal, not a direct queue-depth measurement; in receipt mode it also includes waiting for pipeline completion and is not equivalent to publish-mode backpressure. Compare publish and all-handler-complete rates, full-second steady rates, acceptance and completion p95/p99, allocations, and GC counts across identical configurations and modes. The allocation figure includes benchmark harness work and newly created events. Discard runs disrupted by host suspension or competing workloads; a zero steady-rate window or an extreme spread is a warning, not evidence of an EventBus regression. The harness does not tune defaults automatically or prove application-level performance.

The concurrent runner also reports process CPU core-seconds, average utilized cores, CPU microseconds/event, monitor lock contentions/event, and completed thread-pool work items/event. These process-wide deltas cover the publication-to-handler-completion window, excluding sink setup and result sorting. They include the producers, synthetic handlers, harness instrumentation, GC, and runtime activity, not just library code. Average cores is CPU core-seconds divided by wall seconds; it is not a percentage of one core. Completion is still observed inside handlers, so the last outer pipeline unwind can overlap the boundary.

### CI Revision Comparison

The `In-Process Performance Comparison` job runs after successful unit tests on PRs, nightly runs, and manual runs with `run_performance_tests` enabled. It builds baseline and candidate libraries **before** measuring, then uses the same small comparison executable against each revision on the same runner. No artifact from another run is treated as an equivalent baseline.

PRs use the base commit as baseline and the checked-out PR merge revision as candidate. Scheduled/manual runs default to the candidate's first parent; manual runs can set `performance_baseline_ref`. Baseline refs are resolved to a commit before checkout. The performance job has read-only repository permission and does not post PR comments or use `pull_request_target`.

Each noop/CPU/async scenario uses 100,000 warmup events, then three measured batches per fresh process in baseline-candidate-candidate-baseline order (two processes per variant). `-Rounds` can extend the comparison, reversing the order on alternating rounds. Ordinary publication and optional completion receipts are separate modes. If the baseline predates the receipt API, its receipt rows are omitted and the candidate is marked **new API / no baseline**, not compared with ordinary publication. An API removed from a baseline that supports it fails the comparison. If neither revision supports receipts, the report explicitly says no receipt measurements were run.

Before each mode/workload, fresh calibration processes choose a **shared message count** using the fastest batch across both available revisions and initially targeting 25% headroom where the message cap allows. Calibration uses the same warmup and batch count as measurement, so faster later batches within a process participate in count selection; all calibration batch durations are retained in the manifest. Later attempts stop once both revisions meet the minimum instead of chasing optional headroom. Calibration runs are excluded from measured results. `-NoopMessages` and `-MultiHandlerMessages` are starting counts, not final counts. Counts remain fixed for both revisions and all measured processes in that mode/workload. CI requires **every batch to last at least 10 seconds** and provide full steady windows. Calibration is bounded to four attempts and 10,000,000 messages per batch; inability to meet the requested duration fails with a diagnostic rather than silently reporting short-run performance. `-MinimumBatchSeconds` (3-30) and `-MaximumMessages` (up to 10,000,000) allow explicit local sampling/resource overrides. At the maximum count the sink and sorting arrays alone can approach 1 GB; the cap bounds sample storage, not total process memory.

Read the job summary and the `inprocess-comparison` artifact: `report.md`, raw JSON/logs (including calibration), SDK information, manifest with exact commits/dirty-source flags/process order/calibrated counts, binary hashes, and the measured binaries. The report summarizes per-process batch medians and process-median ranges, throughput, steady rates, batch duration and minimum steady-window count, publish-return and all-handler p95/p99, B/event, CPU/event, GC, contention and work items. Percentiles cover the full measured batch; steady rates exclude the first and partial final seconds. It does not pool percentile distributions or treat batches in one process as independent fresh processes. Counts may differ across modes or CI runs: use paired rows for throughput/latency comparisons, and do not compare raw GC collection counts across different batch sizes.

In receipt mode, publication-return latency measures the final receipt, not queue admission. Every receipt is checked for success; reflection binds delegates once before warmup, with no per-event reflection. Receipt validation costs remain included. Handler timestamps still come from inside the handlers, while the receipt mode waits for all final pipeline outcomes before finishing the batch.

Build/execution errors, invalid or missing samples, insufficient sampling duration/steady windows, configuration mismatches, and unsuccessful receipts **fail CI**. Performance deltas are initially informational: same-runner pairing reduces cross-runner differences but does not establish a noise-free or fixed-hardware guarantee. Do not claim zero regression or fastest-in-class performance from a green job. See the [GitHub-hosted runner documentation](https://docs.github.com/en/actions/concepts/runners/github-hosted-runners) for runner infrastructure, and [workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax) for job permissions and summaries.

The [bounded channel dispatch experiment](results/2026-10-01-channel-dispatch-experiment.md) was withdrawn after paired CI measurements showed CPU and tail-latency regressions despite some async gains. EventBus retains ActionBlock; the experiment is evidence, not a shipped optimization.

To smoke-test the orchestration with two existing checkouts (small counts are not performance evidence):

```powershell
pwsh -File tests/HeroMessaging.Benchmarks/Comparison/Report.Tests.ps1
pwsh -File tests/HeroMessaging.Benchmarks/Comparison/Compare-InProcess.ps1 -BaselineRoot ../baseline-checkout -NoopMessages 256 -MultiHandlerMessages 256 -WarmupCount 32 -Runs 1 -MinimumBatchSeconds 0 -OutputDirectory artifacts/comparison-smoke
```

`-MinimumBatchSeconds 0` is an explicit local smoke mode: it skips calibration/duration requirements and marks the report as non-performance evidence. CI does not use this mode. The output directory must be new; the script never deletes or overwrites previous evidence. The comparison executable pins the same DI runtime package for both revisions ([NuGet package](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/10.0.12)); it does not upgrade production dependencies. Other dependency differences between revisions remain part of the comparison and are retained in the measured dependency manifests.

For process-targeted tracing, build first and run the existing DLL, without concurrent builds or tests. The `HeroMessaging-InProcessBenchmark` EventSource emits `BatchStart` (message, handler, and producer counts) and `BatchStop` markers. Exclude the 1,000-event warmup and filter analysis to the measured windows; otherwise initialization and latency-array sorting appear as workload hotspots. For example, with a current `dotnet-trace` installation:

```bash
dotnet-trace collect --profile dotnet-common,dotnet-sampled-thread-time --providers System.Runtime:0:4:EventCounterIntervalSec=1,Microsoft-Windows-DotNETRuntime:0x100003C01D:4,HeroMessaging-InProcessBenchmark:0xFFFFFFFFFFFFFFFF:4 --output artifacts/inprocess.nettrace --show-child-io -- dotnet tests/HeroMessaging.Benchmarks/bin/Release/net10.0/HeroMessaging.Benchmarks.dll --inprocess-concurrent 2000000 1 32 64 24 noop
dotnet-trace report artifacts/inprocess.nettrace topN -n 30
dotnet-trace convert artifacts/inprocess.nettrace --format Speedscope
```

The runtime mask adds contention events to `dotnet-common`. Check lost events and compare against untraced runs to assess measurement overhead. `topN` covers the whole trace, not just the batch markers. Managed sampled-thread-time includes waiting threads and must not be presented as on-CPU percentages; EventPipe does not capture native CPU execution or OS context-switch/ready-time data. Windows CPU and scheduler attribution needs an authorized ETW/WPR recording. See the [official dotnet-trace documentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace) for the distinction and platform requirements.

### CI-Matched Linux CPU And Scheduler Capture

Manually dispatch `Build and Test` on the diagnostic branch with `run_native_profile=true`. This invokes a separate `ubuntu-24.04` runner, not the timed comparison job. It builds before recording and uses the same three-handler CPU workload, 32 producers, capacity 16, parallelism 4, 100,000 warmup events and three batches in both publish and receipt modes. The collector requires four available processors; it does not override the runtime processor count or thread-pool settings to simulate a different host.

`Comparison/Profile-Linux.ps1` calibrates unprofiled batches using the same four-attempt/10-million-message bounds and unchanged ten-second guard. Each mode then runs an unprofiled control, a process-targeted native capture, and another unprofiled control at the same count. The latest installed `dotnet-trace` tool's actual version, SDK/kernel, source revision, binary hashes, PIDs and durations are retained. Profiled throughput is diagnostic only, not an optimization comparison. The controls expose run-to-run variation and observer effects but are not a paired revision comparison.

The native collector uses `collect-linux` with `dotnet-common,cpu-sampling,thread-time`, explicit scheduler switch/wakeup tracepoints, runtime contention events, and the existing benchmark batch markers. A small diagnostic-only TraceEvent executable decodes native traces and writes sample/window/scheduler/symbol-coverage summaries. Missing target CPU or scheduler events, mismatched PID/count, short/missing complete measured windows, decoding errors, or reported event loss fail the lane even if the collector exits successfully. The profile name alone is not evidence: an initial capture contained CPU samples and complete windows but no decoded context-switch events. The explicit tracepoints and data guard address this gap rather than treating it as scheduler proof.

Collection requires root, a kernel supporting `user_events`, and mounted tracefs; missing prerequisites or collection failures fail without a managed-sampling fallback or automatic retry. Privilege is confined to an isolated disposable CI runner. No recording, build, test, or unrelated process is started concurrently in that profiling job. Cleanup terminates only child processes owned by this invocation. Output must be a new directory. The diagnostic reader pins the current [TraceEvent 3.2.8 package](https://www.nuget.org/packages/Microsoft.Diagnostics.Tracing.TraceEvent/3.2.8); production dependencies are unchanged.

The `inprocess-native` artifact contains traces, raw results/logs and provenance; retention is seven days. These artifacts come from the synthetic workload on a disposable runner, not an operator workstation. Do not use this upload path for local workstation traces. The manifest deliberately reports `status=captured` and `analysisStatus=pending`, not validated CPU attribution. Attach can miss early markers: select only complete measured `BatchStart`/`BatchStop` windows with the calibrated message count, excluding warmup, sorting and initialization. Verify event loss, target PID, kernel CPU samples, context-switch events and symbol coverage before attributing cost. Failure to establish complete windows or usable symbols remains an analysis blocker.

Analyze the native trace using a current PerfView version. Native `collect-linux` is a preview format; `dotnet-trace report` and `convert` may not support it. Do not replace native CPU data with managed sampled-thread-time percentages when analysis is unavailable. See the [official collector prerequisites and profile semantics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace#dotnet-trace-collect-linux). This diagnostic lane does not change EventBus, defaults, delivery guarantees, or the normal CI performance gate.

### Windows CPU And Scheduler Capture

`Profile-InProcess.ps1` combines the built-in WPR CPU profile with `InProcess.wprp` for batch markers, GC, threading, and contention. The collector runs in PowerShell 7 on Windows. Windows PowerShell 5.1 can launch the same script: the entry point resolves `pwsh.exe` on PATH, verifies version 7 or later, and forwards the scenario, timeout, and validation switch. It inherits the current elevation and does not install a runtime or request elevation. Missing or older runtimes fail before recording. Build before recording, then validate without elevation or side effects:

```powershell
dotnet build tests/HeroMessaging.Benchmarks/HeroMessaging.Benchmarks.csproj -c Release -f net10.0 -warnaserror
./tests/HeroMessaging.Benchmarks/Profile-InProcess.ps1 -ValidateOnly
```

Run one scenario at a time from an **elevated** PowerShell session (5.1 or 7, with PowerShell 7 installed) at the repository root:

```powershell
./tests/HeroMessaging.Benchmarks/Profile-InProcess.ps1 -Scenario noop
./tests/HeroMessaging.Benchmarks/Profile-InProcess.ps1 -Scenario cpu
./tests/HeroMessaging.Benchmarks/Profile-InProcess.ps1 -Scenario async
```

Each invocation uses a fresh recording instance and writes private output beneath ignored `artifacts/wpr-inprocess/`. The no-op configuration is `2000000 1 32 64 24 noop`; CPU/async use `500000 3 32 16 4` with the corresponding workload. The benchmark process is limited to 90 seconds by default (`-TimeoutSeconds`, 1-180). On timeout/failure, the script terminates only its own benchmark process and attempts to save the partial recording, then reports failure. It stops only its named WPR instance, with named cancellation as a fallback if saving fails. Do not terminate the script host forcibly during capture; `finally` cleanup cannot run after host termination. It does not grant profiling privileges, change registry/policies/thread-pool settings, build during recording, or automatically elevate.

Saving is a separate phase after the benchmark exits. WPR merges the ETL and generates managed symbols; progress can remain at `0%` for several minutes. The benchmark timeout does not cover saving. Wait for `The trace was successfully saved` and the script's `Capture complete` message rather than interrupting or starting another capture. A saved ETL still needs loss, symbol, and window checks before analysis.

WPR captures **system-wide activity**, including other processes and potentially personal paths. Do not commit or publicly upload ETLs or raw logs. Profiling overhead means traced throughput is not an optimization comparison. In WPA, filter to the PID in `session.txt` and the three measured batch windows, excluding the 1,000-event warmup and gaps used for sorting. Verify symbols and lost events from `wpr.log` and ETL statistics; unresolved managed frames or dropped events make attribution incomplete. Examine CPU Usage (Sampled) for actual sampled execution and CPU Usage (Precise) for wait/ready-time, context switches, and readying threads, alongside GC/monitor contention events. Only then choose an optimization mechanism. See the [WPR instance/recording documentation](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/wpr-command-line-options) and [CPU analysis guide](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/cpu-analysis).

Profile/schema validation does not prove a successful elevated capture or usable symbols. Do not claim OS scheduler evidence until an ETL has actually been collected and analyzed.

`--inprocess-allocations` isolates allocation sources on the calling thread with `GC.GetAllocatedBytesForCurrentThread`. It prewarms each operation and reports event objects and the three-handler sink/latency arrays in B/event, and pipeline construction plus synchronous successful decorator execution in B/delivery. The context starts with the same two metadata keys used by EventBus. Loggers are disabled and optional metrics, validation, and error handlers are not registered, matching the manual workload. Three individual metadata updates and a local builder are measured in the same process to avoid cross-process string-hash differences. These rows exclude Dataflow queuing, publisher continuations, async handlers, and concurrent scheduling; they are attribution probes, not additive estimates of every allocation in the concurrent runner or throughput benchmarks. For example:

```bash
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --framework net10.0 -- --inprocess-allocations
```

The PostgreSQL Inbox benchmark uses a real database and is opt-in. Set `PostgreSql__ConnectionString` to a disposable local database, then run `dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --framework net10.0 -- --filter "*PostgreSqlInboxBenchmarks*"`. It creates and removes its own schema. The new-message and duplicate-message cases are reported separately because reducing database round trips can increase duplicate-path serialization work.

## End-to-end pipeline baseline

`--pipeline [message-count] [external-concurrency]` is an opt-in, real-service workload, not a BenchmarkDotNet microbenchmark. Set `PostgreSql__ConnectionString`, `RabbitMq__Host`, and optionally `RabbitMq__Port` to **disposable services bound to loopback**. The runner refuses non-loopback hosts, creates its own PostgreSQL schema and auto-deleting RabbitMQ queue, warms up with 10 events, then sends 100 events by default. External concurrency defaults to 4:

```bash
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --framework net8.0 -- --pipeline 300 4
```

It reports sequential Outbox publish duration, publish-to-handler p50/p95/p99 latency, and throughput to handler and to durable Outbox/Inbox `Processed` states. It checks that both tables contain exactly the expected number of processed messages before reporting success. Compare multiple runs on the same machine and service configuration; this is not a CI performance gate.

Use `--pipeline-direct 300` with the same services to bypass Outbox and measure RabbitMQ-to-Inbox headroom. This mode verifies only the durable Inbox state and does not test Outbox reliability. Do not compare it to the full pipeline as an equivalent delivery guarantee.

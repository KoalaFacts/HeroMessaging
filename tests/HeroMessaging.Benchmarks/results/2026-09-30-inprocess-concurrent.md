# In-process concurrent baseline (2026-09-30)

Manual baseline for the EventBus production code at `fcafa28`. This is not a CI regression threshold or a comparison with another library.

- Host: Windows, AMD Ryzen AI 9 HX PRO 370, `Environment.ProcessorCount = 24`.
- Toolchain: .NET SDK 10.0.401, Release `net10.0`.
- Three measured runs per scenario after a 1,000-event warmup. One EventBus per scenario with the environment-default envelope pool of 64; the configured producers publish distinct event sequences and each awaits acceptance before its next event.
- `pending-publish` means `PublishAsync` did not complete synchronously. It signals backpressure, not measured queue depth. Allocation includes the benchmark harness and event objects.

## CPU, three handlers

The CPU workload performs 1,024 integer hash rounds per delivery. Each run publishes 500,000 events (1,500,000 deliveries) with 32 producers and parallelism 4.

| Capacity | Run | Complete events/s | Full-second min/median/max events/s | Pending publish | Accept p95/p99 | All-handlers p95/p99 | Allocated B/event |
| ---: | ---: | ---: | --- | ---: | ---: | ---: | ---: |
| 16 | 1 | 81,002 | 58,444 / 89,332 / 125,709 | 99.3% | 0.81 / 1.80 ms | 0.94 / 1.93 ms | 4,336 |
| 16 | 2 | 73,656 | 68,516 / 75,197 / 81,965 | 98.7% | 0.69 / 2.31 ms | 0.76 / 2.41 ms | 4,330 |
| 16 | 3 | 67,026 | 54,697 / 58,058 / 98,576 | 93.3% | 0.93 / 4.35 ms | 0.82 / 4.24 ms | 4,288 |
| 64 | 1 | 64,408 | 49,515 / 72,063 / 77,727 | 98.2% | 0.92 / 3.13 ms | 1.53 / 3.79 ms | 4,328 |
| 64 | 2 | 78,188 | 69,116 / 80,813 / 84,250 | 99.1% | 0.60 / 2.28 ms | 0.95 / 2.87 ms | 4,333 |
| 64 | 3 | 73,753 | 60,749 / 71,060 / 79,359 | 97.6% | 0.67 / 2.63 ms | 0.99 / 3.45 ms | 4,322 |

Gen0 collections were 255-259 per CPU run; Gen1 was 0-3 and Gen2 was 0-2. The throughput and latency ranges overlap. These measurements do not establish that either capacity improves sustained throughput or p99. Re-run both configurations in alternating order on an idle machine before changing a default.

```bash
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --framework net10.0 -- --inprocess-concurrent 500000 3 32 16 4 cpu
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --framework net10.0 -- --inprocess-concurrent 500000 3 32 64 4 cpu
```

## Delayed handler, one handler

With 1,000 events, 16 producers, capacity 8, and parallelism 2, a nominal 2 ms delay per delivery produced 154-165 completed events/s and 99.2% pending publishes in all three runs. All-handler p95 was 171.32-224.66 ms and p99 was 181.55-266.41 ms. The OS timer makes this a controlled saturation check, not a precise 2 ms service-time test.

```bash
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --framework net10.0 -- --inprocess-concurrent 1000 1 16 8 2 delay
```

## Excluded observation

A 1,000,000-event no-op run on this host was interrupted by a long host pause. Its second measured iteration reported 556 events/s and zero-completion full-second windows, while the adjacent iterations reported about 212,000-227,000 events/s. The interrupted iteration is not a valid throughput baseline. Do not infer a regression from it.

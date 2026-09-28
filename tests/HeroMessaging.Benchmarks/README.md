# HeroMessaging Benchmarks

This project contains BenchmarkDotNet microbenchmarks for command, event, query, saga, storage, and ring-buffer paths. Results are local measurements, not end-to-end transport guarantees.

## Run

Use a Release build on a stable machine:

```bash
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --framework net10.0 -- --filter "*CommandProcessor*"
```

Remove the filter to run all benchmarks only after configuring PostgreSQL for the opt-in `PostgreSqlInboxBenchmarks`. Other available classes are `CommandProcessorBenchmarks`, `EventBusBenchmarks`, `QueryProcessorBenchmarks`, `SagaOrchestrationBenchmarks`, `StorageBenchmarks`, and `RingBufferBenchmarks`.

The custom configuration reports mean, median, p95, and allocations. These measurements do not include a real broker, sustained load, p99, or an end-to-end publish-to-handler latency distribution. Capture a baseline on fixed hardware before using results as a regression gate or claiming a throughput target.

The PostgreSQL Inbox benchmark uses a real database and is opt-in. Set `PostgreSql__ConnectionString` to a disposable local database, then run `dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --framework net10.0 -- --filter "*PostgreSqlInboxBenchmarks*"`. It creates and removes its own schema. The new-message and duplicate-message cases are reported separately because reducing database round trips can increase duplicate-path serialization work.

# HeroMessaging Benchmarks

This project contains BenchmarkDotNet microbenchmarks for command, event, query, saga, storage, and ring-buffer paths. Results are local measurements, not end-to-end transport guarantees.

## Run

Use a Release build on a stable machine:

```bash
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --framework net10.0 -- --filter "*CommandProcessor*"
```

Remove the filter to run all benchmarks only after configuring PostgreSQL for the opt-in `PostgreSqlInboxBenchmarks`. The scheduled CI benchmark run lists the non-database classes explicitly and does not run this opt-in benchmark.

The custom configuration reports mean, median, p95, and allocations. These measurements do not include a real broker, sustained load, p99, or an end-to-end publish-to-handler latency distribution. Capture a baseline on fixed hardware before using results as a regression gate or claiming a throughput target.

The PostgreSQL Inbox benchmark uses a real database and is opt-in. Set `PostgreSql__ConnectionString` to a disposable local database, then run `dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --framework net10.0 -- --filter "*PostgreSqlInboxBenchmarks*"`. It creates and removes its own schema. The new-message and duplicate-message cases are reported separately because reducing database round trips can increase duplicate-path serialization work.

## End-to-end pipeline baseline

`--pipeline [message-count] [external-concurrency]` is an opt-in, real-service workload, not a BenchmarkDotNet microbenchmark. Set `PostgreSql__ConnectionString`, `RabbitMq__Host`, and optionally `RabbitMq__Port` to **disposable services bound to loopback**. The runner refuses non-loopback hosts, creates its own PostgreSQL schema and auto-deleting RabbitMQ queue, warms up with 10 events, then sends 100 events by default. External concurrency defaults to 4:

```bash
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --framework net8.0 -- --pipeline 300 4
```

It reports sequential Outbox publish duration, publish-to-handler p50/p95/p99 latency, and throughput to handler and to durable Outbox/Inbox `Processed` states. It checks that both tables contain exactly the expected number of processed messages before reporting success. Compare multiple runs on the same machine and service configuration; this is not a CI performance gate.

Use `--pipeline-direct 300` with the same services to bypass Outbox and measure RabbitMQ-to-Inbox headroom. This mode verifies only the durable Inbox state and does not test Outbox reliability. Do not compare it to the full pipeline as an equivalent delivery guarantee.

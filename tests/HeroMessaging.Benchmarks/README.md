# HeroMessaging Benchmarks

This project contains performance benchmarks for HeroMessaging to validate the performance claims outlined in the project's constitutional principles.

## Performance Targets

Based on the project's constitutional principles, we aim to validate:

- **Latency**: <1ms p99 latency for message processing overhead
- **Throughput**: >100K messages/second single-threaded capability
- **Memory**: <1KB allocation per message in steady state

## Benchmark Categories

### 1. CommandProcessorBenchmarks
Validates command processing performance including:
- Single command latency
- Sequential batch processing (100 commands)
- Commands with response values

**Expected Results:**
- Single command: <1ms (p99)
- 100 commands: <100ms total (<1ms per command)
- Memory: <1KB per command

### 2. EventBusBenchmarks
Validates event publishing performance including:
- Single event publishing latency
- Sequential batch processing (100 events)
- Multiple event handlers (fan-out scenarios)

**Expected Results:**
- Single event: <1ms (p99)
- 100 events: <100ms total
- Memory: <1KB per event

### 3. QueryProcessorBenchmarks
Validates query processing performance including:
- Single query latency
- Sequential batch processing (100 queries)

**Expected Results:**
- Single query: <1ms (p99)
- 100 queries: <100ms total
- Memory: <1KB per query

### 4. SagaOrchestrationBenchmarks
Validates saga orchestration performance including:
- Saga creation and first event processing
- Full saga lifecycle (start, transition, complete)
- Sequential batch processing (100 saga events)

**Expected Results:**
- Single saga event: <1ms (p99)
- Full lifecycle: <3ms
- 100 saga events: <100ms total

### 5. StorageBenchmarks
Validates storage operation performance including:
- Message storage
- Message retrieval
- Outbox pattern operations
- Inbox pattern operations (idempotency)
- Duplicate detection

**Expected Results:**
- Store operation: <1ms (p99)
- Retrieve operation: <1ms (p99)
- 100 operations: <100ms total
- Memory: <1KB per operation

## Running the Benchmarks

### Prerequisites
- .NET 8.0 SDK or later
- At least 4GB RAM for benchmark execution
- Stable system state (close other applications)

### Commands

Run all benchmarks:
```bash
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release
```

Run specific benchmark category:
```bash
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --filter "*CommandProcessor*"
```

Run with specific options:
```bash
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release -- --filter "*EventBus*" --memory
```

### Interpreting Results

BenchmarkDotNet provides detailed results including:

- **Mean**: Average execution time
- **Error**: Standard error of the mean
- **StdDev**: Standard deviation
- **Median**: 50th percentile
- **Gen0/Gen1/Gen2**: Garbage collection statistics
- **Allocated**: Memory allocated per operation

### Performance Regression Detection

The CI/CD pipeline should fail if:
- Latency increases by >10% from baseline
- Throughput decreases by >10% from baseline
- Memory allocations increase by >10% from baseline

## Baseline Results

Baseline results should be established on a reference machine and tracked over time. Results will vary based on hardware specifications.

### Reference Configuration
- CPU: [To be determined on first run]
- RAM: [To be determined on first run]
- OS: [To be determined on first run]
- .NET Runtime: [To be determined on first run]

## Troubleshooting

### Benchmarks Running Slowly
- Ensure Release configuration is used (not Debug)
- Close other applications
- Disable CPU throttling
- Ensure adequate cooling

### High Memory Allocations
- Check for boxing/unboxing
- Review LINQ usage (deferred execution)
- Verify object pooling is working correctly

### Inconsistent Results
- Run benchmarks multiple times
- Increase warmup and iteration counts
- Check for background processes
- Verify CPU frequency scaling is disabled

## Contributing

When adding new benchmarks:
1. Follow the naming convention: `{Feature}Benchmarks.cs`
2. Add `[MemoryDiagnoser]` attribute
3. Use `[BenchmarkCategory]` for grouping
4. Document expected performance targets
5. Include in this README

## CI Integration

Benchmarks should run on every PR to detect performance regressions. The CI pipeline should:
1. Run benchmarks on a consistent machine
2. Compare results to baseline
3. Fail if performance degrades >10%
4. Generate performance trend reports

See `.github/workflows/benchmarks.yml` for CI configuration.

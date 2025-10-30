# HeroMessaging Performance Benchmarks

**Comprehensive guide to performance testing, baseline establishment, and regression detection.**

## Overview

HeroMessaging uses BenchmarkDotNet for rigorous performance testing to validate constitutional requirements:

- **Latency Target**: <1ms p99 processing overhead
- **Throughput Target**: >100K messages/second single-threaded
- **Memory Target**: <1KB allocation per message in steady state
- **Regression Detection**: Fail CI if performance drops >10% from baseline

## Running Benchmarks

### Quick Start

```bash
cd tests/HeroMessaging.Benchmarks
dotnet run -c Release
```

### Run Specific Benchmarks

```bash
# Command processor only
dotnet run -c Release --filter "*CommandProcessor*"

# Event bus only
dotnet run -c Release --filter "*EventBus*"

# Saga orchestration only
dotnet run -c Release --filter "*SagaOrchestration*"

# Storage operations only
dotnet run -c Release --filter "*Storage*"
```

### Run with Memory Diagnostics

```bash
dotnet run -c Release -- --memory
```

### Export Results

```bash
# Export to HTML
dotnet run -c Release -- --exporters html

# Export to Markdown
dotnet run -c Release -- --exporters markdown

# Export to JSON for CI
dotnet run -c Release -- --exporters json
```

## Benchmark Categories

### 1. Command Processing

**File**: `CommandProcessorBenchmarks.cs`

**Benchmarks**:
- `ProcessCommand_SingleMessage`: Single command latency
- `ProcessCommand_SequentialBatch`: Sequential throughput (100 commands)
- `ProcessCommand_WithResponse`: Command with response handling

**Targets**:
- Single message: <1ms p99
- Throughput: >100K commands/second
- Memory: <1KB per command

### 2. Query Processing

**File**: `QueryProcessorBenchmarks.cs`

**Benchmarks**:
- `ProcessQuery_SingleQuery`: Single query latency
- `ProcessQuery_SequentialBatch`: Sequential throughput
- `ProcessQuery_WithProjection`: Query with data transformation

**Targets**:
- Single query: <0.5ms p99 (faster than commands)
- Throughput: >150K queries/second
- Memory: <512 bytes per query

### 3. Event Bus

**File**: `EventBusBenchmarks.cs`

**Benchmarks**:
- `PublishEvent_SingleHandler`: Event with one subscriber
- `PublishEvent_MultipleHandlers`: Event with multiple subscribers
- `PublishEvent_SequentialBatch`: Event publishing throughput

**Targets**:
- Single event: <0.3ms p99
- With 5 handlers: <1ms p99
- Throughput: >200K events/second (single handler)

### 4. Saga Orchestration

**File**: `SagaOrchestrationBenchmarks.cs`

**Benchmarks**:
- `SagaTransition_SingleEvent`: Single state transition
- `SagaTransition_WithCompensation`: Transition with compensation action
- `SagaRepository_Save`: Saga persistence
- `SagaRepository_Find`: Saga retrieval

**Targets**:
- State transition: <1ms p99
- With compensation: <1.5ms p99
- Save to in-memory: <0.5ms p99
- Find in memory: <0.2ms p99

### 5. Storage Operations

**File**: `StorageBenchmarks.cs`

**Benchmarks**:
- `InMemoryStorage_Save`: In-memory save operation
- `InMemoryStorage_Retrieve`: In-memory retrieval
- `PostgreSqlStorage_Save`: PostgreSQL save (if available)
- `PostgreSqlStorage_Retrieve`: PostgreSQL retrieval (if available)

**Targets**:
- In-memory save: <0.1ms p99
- In-memory retrieve: <0.05ms p99
- PostgreSQL save: <5ms p99
- PostgreSQL retrieve: <3ms p99

## Establishing Baselines

### Step 1: Run on Reference Hardware

```bash
cd tests/HeroMessaging.Benchmarks
dotnet run -c Release -- --exporters json
```

**Record system information**:
```
CPU: [Your CPU model]
RAM: [Your RAM amount]
OS: [Your OS version]
.NET: [Your .NET version]
Date: [Current date]
```

### Step 2: Document Results

Create `docs/performance-baselines/YYYY-MM-DD-baseline.md`:

```markdown
# Performance Baseline - 2025-10-28

## System Configuration
- **CPU**: AMD Ryzen 9 5950X (16 cores, 32 threads)
- **RAM**: 32GB DDR4 3600MHz
- **OS**: Ubuntu 22.04 LTS
- **Runtime**: .NET 9.0.0
- **Build**: Release

## Results

### Command Processing
| Benchmark | Mean | P99 | Allocated |
|-----------|------|-----|-----------|
| Single Command | 0.45ms | 0.65ms | 856 B |
| Sequential Batch (100) | 42ms | 48ms | 84 KB |
| With Response | 0.52ms | 0.72ms | 912 B |

### Query Processing
| Benchmark | Mean | P99 | Allocated |
|-----------|------|-----|-----------|
| Single Query | 0.32ms | 0.48ms | 512 B |
| Sequential Batch (100) | 28ms | 32ms | 50 KB |

[Continue for all categories...]

## Constitutional Compliance
- ✅ Latency: <1ms p99 achieved
- ✅ Throughput: >100K msg/s achieved
- ✅ Memory: <1KB per message achieved
```

### Step 3: Commit Baseline

```bash
git add docs/performance-baselines/2025-10-28-baseline.md
git commit -m "chore: Establish performance baseline for release 1.0"
```

### Step 4: Configure CI Regression Detection

Update `.github/workflows/ci.yml`:

```yaml
benchmark-validation:
  name: Validate Performance
  runs-on: ubuntu-latest
  if: github.event_name == 'pull_request'

  steps:
    - uses: actions/checkout@v4
      with:
        fetch-depth: 0

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: 9.0.x

    - name: Run Benchmarks
      run: |
        cd tests/HeroMessaging.Benchmarks
        dotnet run -c Release -- --exporters json

    - name: Compare with Baseline
      run: |
        # Compare current results with baseline
        # Fail if regression >10%
        python scripts/compare-benchmarks.py \
          --baseline docs/performance-baselines/latest-baseline.json \
          --current BenchmarkDotNet.Artifacts/results/*.json \
          --threshold 0.10
```

## Regression Detection Script

Create `scripts/compare-benchmarks.py`:

```python
#!/usr/bin/env python3
import json
import sys
import argparse

def compare_benchmarks(baseline_file, current_file, threshold):
    """Compare benchmark results and detect regressions"""

    with open(baseline_file) as f:
        baseline = json.load(f)

    with open(current_file) as f:
        current = json.load(f)

    regressions = []

    for benchmark in current['Benchmarks']:
        name = benchmark['FullName']
        current_mean = benchmark['Statistics']['Mean']

        # Find baseline
        baseline_bench = next(
            (b for b in baseline['Benchmarks'] if b['FullName'] == name),
            None
        )

        if not baseline_bench:
            print(f"Warning: No baseline for {name}")
            continue

        baseline_mean = baseline_bench['Statistics']['Mean']

        # Calculate regression
        regression = (current_mean - baseline_mean) / baseline_mean

        if regression > threshold:
            regressions.append({
                'name': name,
                'baseline': baseline_mean,
                'current': current_mean,
                'regression': regression * 100
            })

    if regressions:
        print(f"\n❌ Performance regressions detected (>{threshold*100}%):\n")
        for r in regressions:
            print(f"  {r['name']}")
            print(f"    Baseline: {r['baseline']:.4f}ns")
            print(f"    Current:  {r['current']:.4f}ns")
            print(f"    Regression: +{r['regression']:.1f}%\n")
        sys.exit(1)
    else:
        print("✅ No performance regressions detected")
        sys.exit(0)

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--baseline', required=True)
    parser.add_argument('--current', required=True)
    parser.add_argument('--threshold', type=float, default=0.10)
    args = parser.parse_args()

    compare_benchmarks(args.baseline, args.current, args.threshold)
```

## Interpreting Results

### Understanding BenchmarkDotNet Output

```
| Method               | Mean     | Error    | StdDev   | Median   | Allocated |
|--------------------- |---------:|---------:|---------:|---------:|----------:|
| ProcessCommand       | 450.2 us | 8.9 us   | 12.4 us  | 448.1 us | 856 B     |
```

**Key Metrics**:
- **Mean**: Average execution time
- **Error**: Half of 99.9% confidence interval
- **StdDev**: Standard deviation
- **Median**: 50th percentile (P50)
- **Allocated**: Memory allocated per operation

### Performance Goals vs Actual

```csharp
// Constitutional requirement: <1ms p99 latency
// How to check: Look at Error + Mean (approximates p99)
// Example: 450μs mean + 12μs error ≈ 462μs p99 ✅

// Constitutional requirement: >100K msg/s throughput
// How to check: 1,000,000 μs / mean time
// Example: 1,000,000 / 450 ≈ 2,222 msg/s per thread ❌
// Need parallel processing to hit target
```

### Memory Analysis

```
Gen0    Gen1    Gen2    Allocated
42      21      5       4.2 MB
```

- **Gen0**: Short-lived objects (frequent collections OK)
- **Gen1**: Medium-lived objects (minimize)
- **Gen2**: Long-lived objects (avoid if possible)
- **Allocated**: Total memory per iteration

**Target**: <1KB allocated per message in steady state

## Continuous Performance Monitoring

### Dashboard Setup (Prometheus + Grafana)

```yaml
# prometheus.yml
scrape_configs:
  - job_name: 'heromessaging-benchmarks'
    static_configs:
      - targets: ['localhost:9090']
```

### Alert Rules

```yaml
# alerts.yml
groups:
  - name: performance
    rules:
      - alert: PerformanceRegression
        expr: heromessaging_command_processing_p99 > 1.0
        for: 5m
        annotations:
          summary: "Command processing p99 exceeded 1ms"
```

## Best Practices

### 1. Run on Dedicated Hardware
- Disable background processes
- Use consistent power settings
- Run multiple iterations for stability

### 2. Use Release Configuration
```bash
# Always benchmark in Release mode
dotnet run -c Release

# Never benchmark in Debug mode
# ❌ dotnet run (uses Debug by default)
```

### 3. Warm-up Iterations
```csharp
[SimpleJob(warmupCount: 3, iterationCount: 10)]
```

### 4. Isolate Benchmarks
```csharp
// Test one thing at a time
[Benchmark]
public void JustTheOperation()
{
    _processor.Process(_message); // No setup/teardown
}
```

### 5. Track Trends Over Time
- Establish baseline per release
- Monitor for gradual degradation
- Document infrastructure changes

## Troubleshooting

### Benchmark Won't Run

**Issue**: `No benchmarks found`

**Solution**:
```csharp
// Ensure [Benchmark] attribute is present
[Benchmark]
public void MyBenchmark() { }
```

### Inconsistent Results

**Issue**: Results vary wildly between runs

**Solution**:
1. Increase warmup iterations
2. Close background applications
3. Check for thermal throttling
4. Use `[SimpleJob(warmupCount: 5)]`

### Memory Leaks

**Issue**: Allocated memory growing

**Solution**:
```csharp
[MemoryDiagnoser]
public class MyBenchmarks
{
    [GlobalCleanup]
    public void Cleanup()
    {
        // Clean up resources
        _disposable?.Dispose();
    }
}
```

## See Also

- [BenchmarkDotNet Documentation](https://benchmarkdotnet.org/)
- [Performance Baselines Directory](../performance-baselines/)
- [CI/CD Configuration](../.github/workflows/ci.yml)
- [Constitutional Requirements](../CLAUDE.md)

## Contributing

When adding new benchmarks:

1. Follow existing naming conventions
2. Include `[BenchmarkCategory]` attribute
3. Document expected performance targets
4. Update this guide with new benchmarks
5. Run full benchmark suite before committing

---

**Last Updated**: 2025-10-28
**Benchmark Version**: 1.0.0
**BenchmarkDotNet Version**: Latest

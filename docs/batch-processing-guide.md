# Batch Processing Guide

**Version**: 1.0
**Status**: In Progress (Phases 1-3 Complete)
**Last Updated**: 2025-11-10

## Overview

The batch processing framework enables high-throughput message processing by accumulating and processing messages together while maintaining full message-level guarantees. This guide covers configuration, usage patterns, and best practices.

## Key Features

- **20-40% throughput improvement** for batch-friendly workloads
- **Full decorator chain** maintained for each message (validation, retry, circuit breaker, idempotency)
- **Pluggable configuration** with predefined profiles
- **Thread-safe** message accumulation
- **Graceful degradation** with fallback to individual processing
- **Cross-platform** support (netstandard2.0, net8.0, net9.0, net10.0)

## Quick Start

### Basic Configuration

```csharp
using HeroMessaging;
using HeroMessaging.Configuration;

services.AddHeroMessaging(builder =>
{
    builder.WithBatchProcessing(batch =>
    {
        batch
            .Enable()  // Enable batch processing
            .UseBalancedProfile();  // Use balanced defaults
    });
});
```

### Using Batch API

```csharp
// Inject IHeroMessaging
private readonly IHeroMessaging _messaging;

// Send multiple commands
var commands = new List<ICommand>
{
    new ProcessOrderCommand { OrderId = 1 },
    new ProcessOrderCommand { OrderId = 2 },
    new ProcessOrderCommand { OrderId = 3 }
};

var results = await _messaging.SendBatchAsync(commands);

// Send commands with responses
var commandsWithResponse = new List<ICommand<OrderResult>>
{
    new ValidateOrderCommand { OrderId = 1 },
    new ValidateOrderCommand { OrderId = 2 }
};

var responses = await _messaging.SendBatchAsync(commandsWithResponse);

// Publish multiple events
var events = new List<IEvent>
{
    new OrderCreatedEvent { OrderId = 1 },
    new OrderCreatedEvent { OrderId = 2 }
};

var publishResults = await _messaging.PublishBatchAsync(events);
```

## Configuration Options

### Predefined Profiles

#### High Throughput Profile
Optimized for maximum throughput in high-volume scenarios:

```csharp
builder.WithBatchProcessing(batch =>
{
    batch.UseHighThroughputProfile();
    // Settings:
    // - MaxBatchSize: 100
    // - BatchTimeout: 500ms
    // - MinBatchSize: 10
    // - MaxDegreeOfParallelism: 4
});
```

**Best For:**
- Event streaming (>100K events/second)
- Bulk data import/export
- Non-time-sensitive workflows

#### Low Latency Profile
Optimized for low latency with smaller batches:

```csharp
builder.WithBatchProcessing(batch =>
{
    batch.UseLowLatencyProfile();
    // Settings:
    // - MaxBatchSize: 20
    // - BatchTimeout: 100ms
    // - MinBatchSize: 5
    // - MaxDegreeOfParallelism: 1 (sequential)
});
```

**Best For:**
- User-facing APIs
- Real-time notifications
- Low-latency requirements (<200ms)

#### Balanced Profile (Default)
Balanced configuration for general use:

```csharp
builder.WithBatchProcessing(batch =>
{
    batch.UseBalancedProfile();
    // Settings:
    // - MaxBatchSize: 50
    // - BatchTimeout: 200ms
    // - MinBatchSize: 2
    // - MaxDegreeOfParallelism: 2
});
```

**Best For:**
- Most scenarios
- Mixed workloads
- Development/testing

### Custom Configuration

```csharp
builder.WithBatchProcessing(batch =>
{
    batch
        .Enable()
        .WithMaxBatchSize(75)                       // Max messages per batch
        .WithBatchTimeout(TimeSpan.FromMilliseconds(300))  // Timeout for partial batches
        .WithMinBatchSize(5)                        // Min size to justify batching
        .WithParallelProcessing(3)                  // Process 3 messages concurrently
        .WithContinueOnFailure(true)                // Continue processing on errors
        .WithFallbackToIndividual(true);            // Retry individually on batch failure
});
```

### Configuration Parameters

| Parameter | Description | Default | Recommended Range |
|-----------|-------------|---------|-------------------|
| `Enabled` | Enable/disable batch processing | `false` | - |
| `MaxBatchSize` | Maximum messages per batch | `50` | 10-100 (typical), 100-1000 (high-throughput) |
| `BatchTimeout` | Max wait time for partial batch | `200ms` | 100ms-1000ms |
| `MinBatchSize` | Minimum size for batching | `2` | 2-10 |
| `MaxDegreeOfParallelism` | Concurrent processing within batch | `1` | 1 (sequential), 2-8 (parallel) |
| `ContinueOnFailure` | Continue processing on message failure | `true` | `true` (recommended) |
| `FallbackToIndividualProcessing` | Retry individually on batch failure | `true` | `true` (recommended) |

## How It Works

### Architecture

The batch processing framework uses a decorator pattern that integrates into the message processing pipeline:

```
Message Received
    ↓
ValidationDecorator ← Validate before batching
    ↓
BatchDecorator ← Accumulate messages (if enabled)
    ↓
[For each message in batch:]
    ↓
IdempotencyDecorator ← Check cache
    ↓
RetryDecorator ← Retry logic
    ↓
CircuitBreakerDecorator ← Circuit breaker
    ↓
Handler Execution
```

### Message Flow

1. **Individual Message**: Message enters pipeline, validated
2. **Accumulation**: BatchDecorator queues message (if batching enabled)
3. **Trigger**: Batch processed when:
   - `MaxBatchSize` reached, OR
   - `BatchTimeout` expires with ≥ `MinBatchSize` messages
4. **Processing**: Each message in batch goes through remaining decorators
5. **Results**: Individual results aggregated and returned

### Thread Safety

- **Concurrent queue**: Thread-safe message accumulation
- **Background processing**: Dedicated thread for batch processing
- **Semaphore-based parallelism**: Controlled concurrent processing within batch

## Usage Patterns

### Pattern 1: Automatic Batching

Let the decorator automatically batch incoming messages:

```csharp
// Configuration
builder.WithBatchProcessing(batch => batch.Enable().UseBalancedProfile());

// Usage - Just send messages normally
foreach (var order in orders)
{
    await messaging.SendAsync(new ProcessOrderCommand { OrderId = order.Id });
}
// BatchDecorator automatically accumulates and processes in batches
```

### Pattern 2: Explicit Batch API

Use batch API methods for explicit batching:

```csharp
// Collect messages
var commands = orders.Select(o => new ProcessOrderCommand { OrderId = o.Id }).ToList();

// Send as batch
var results = await messaging.SendBatchAsync(commands);

// Check results
if (results.All(r => r))
{
    Console.WriteLine("All commands succeeded");
}
```

### Pattern 3: Mixed Workload

Combine automatic and explicit batching:

```csharp
// High-priority: Process immediately (batching disabled for critical path)
await messaging.SendAsync(urgentCommand);

// Bulk operations: Use batch API
var results = await messaging.SendBatchAsync(bulkCommands);
```

### Pattern 4: Event Publishing

Publish events in batches:

```csharp
var events = new List<IEvent>
{
    new UserRegisteredEvent { UserId = 1, Email = "user1@example.com" },
    new UserRegisteredEvent { UserId = 2, Email = "user2@example.com" },
    new UserRegisteredEvent { UserId = 3, Email = "user3@example.com" }
};

var results = await messaging.PublishBatchAsync(events);

// Check failures
var failures = results.Select((success, index) => new { success, index })
                      .Where(r => !r.success)
                      .Select(r => events[r.index]);

foreach (var failedEvent in failures)
{
    // Handle failures
    _logger.LogError("Failed to publish event for user {UserId}", failedEvent.UserId);
}
```

## Performance Optimization

### When to Use Batch Processing

✅ **Use Batching When:**
- Processing >1000 messages/second
- Messages can tolerate 100-500ms additional latency
- Database bulk operations are possible
- Network round-trips are significant
- Messages are independent (no strict ordering)

❌ **Avoid Batching When:**
- Ultra-low latency required (<100ms)
- Messages have strict ordering requirements
- Batch size would typically be 1-2 messages
- Real-time user interactions

### Performance Tuning

**For Maximum Throughput:**
```csharp
batch.UseHighThroughputProfile()
     .WithMaxBatchSize(100)
     .WithParallelProcessing(Environment.ProcessorCount);
```

**For Low Latency:**
```csharp
batch.UseLowLatencyProfile()
     .WithBatchTimeout(TimeSpan.FromMilliseconds(50))
     .WithMaxDegreeOfParallelism(1);
```

**For Memory Efficiency:**
```csharp
batch.WithMaxBatchSize(25)  // Smaller batches
     .WithMinBatchSize(10);  // Higher minimum
```

### Monitoring

Track batch processing metrics:

```csharp
var metrics = messaging.GetMetrics();
Console.WriteLine($"Commands Sent: {metrics.CommandsSent}");
Console.WriteLine($"Events Published: {metrics.EventsPublished}");

// Custom metrics (future enhancement)
// Console.WriteLine($"Batch Hit Rate: {metrics.BatchHitRate}%");
// Console.WriteLine($"Avg Batch Size: {metrics.AverageBatchSize}");
```

## Error Handling

### Individual Message Failures

By default, batch processing continues even if individual messages fail:

```csharp
var commands = new List<ICommand>
{
    validCommand1,
    invalidCommand,  // This will fail
    validCommand2    // This still processes
};

var results = await messaging.SendBatchAsync(commands);
// results[0] = true, results[1] = false, results[2] = true
```

### Batch-Level Failures

If batch processing fails catastrophically:

```csharp
builder.WithBatchProcessing(batch =>
{
    batch.WithFallbackToIndividualProcessing(true);  // Retry individually
});
```

With fallback enabled, on batch failure:
1. Batch processing fails
2. Each message reprocessed individually
3. Individual results returned

### Configuring Failure Behavior

```csharp
batch
    .WithContinueOnFailure(true)      // Continue processing remaining messages
    .WithFallbackToIndividual(true);  // Retry individually on batch failure
```

## Best Practices

### 1. Start with Profiles

Use predefined profiles as starting points:

```csharp
// ✅ Good: Start with a profile
batch.UseBalancedProfile();

// ❌ Avoid: Over-tuning initially
batch.WithMaxBatchSize(73).WithBatchTimeout(TimeSpan.FromMilliseconds(247));
```

### 2. Measure Before Optimizing

Establish baseline metrics before enabling batching:

```csharp
// Before batching
var baseline = BenchmarkRunner.Run<MessageProcessingBenchmark>();

// After batching
var optimized = BenchmarkRunner.Run<MessageProcessingBenchmark>();

// Compare: Should see 20-40% improvement
```

### 3. Consider Latency Requirements

Match batch timeout to latency SLAs:

```csharp
// User-facing API (< 200ms)
batch.UseLowLatencyProfile().WithBatchTimeout(TimeSpan.FromMilliseconds(50));

// Background processing (< 5s acceptable)
batch.UseHighThroughputProfile().WithBatchTimeout(TimeSpan.FromSeconds(1));
```

### 4. Enable Gradual Rollout

Use feature flags for gradual rollout:

```csharp
services.AddHeroMessaging(builder =>
{
    var enableBatching = configuration.GetValue<bool>("Features:BatchProcessing");

    if (enableBatching)
    {
        builder.WithBatchProcessing(batch => batch.Enable().UseBalancedProfile());
    }
});
```

### 5. Monitor and Adjust

Continuously monitor and adjust based on real-world data:

```csharp
// Production monitoring
_logger.LogMetric("batch.size", batchSize);
_logger.LogMetric("batch.latency_ms", latencyMs);
_logger.LogMetric("batch.throughput", messagesPerSecond);
```

## Troubleshooting

### Issue: Batching Not Activating

**Symptoms**: Messages processed individually despite batching enabled

**Solutions**:
1. Verify `Enabled = true` in configuration
2. Check message arrival rate (too slow for batching)
3. Verify `MinBatchSize` isn't too high
4. Check `BatchTimeout` - may be too short

```csharp
// Debug logging
batch.Enable().WithMinBatchSize(2).WithBatchTimeout(TimeSpan.FromSeconds(1));
```

### Issue: High Latency

**Symptoms**: Increased message processing latency

**Solutions**:
1. Reduce `BatchTimeout`
2. Use `UseLowLatencyProfile()`
3. Lower `MaxBatchSize`
4. Consider disabling batching for critical paths

```csharp
batch.UseLowLatencyProfile().WithBatchTimeout(TimeSpan.FromMilliseconds(50));
```

### Issue: Memory Pressure

**Symptoms**: High memory usage, OutOfMemoryException

**Solutions**:
1. Reduce `MaxBatchSize`
2. Increase `MinBatchSize`
3. Monitor queue depth
4. Add backpressure mechanism

```csharp
batch.WithMaxBatchSize(25).WithMinBatchSize(10);  // Smaller batches
```

### Issue: Inconsistent Results

**Symptoms**: Some messages succeed, others fail unpredictably

**Solutions**:
1. Check `ContinueOnFailure` setting
2. Verify message independence (no ordering dependencies)
3. Review handler idempotency
4. Enable `FallbackToIndividualProcessing`

```csharp
batch.WithContinueOnFailure(true).WithFallbackToIndividual(true);
```

## Migration Guide

### From Single Processing

**Before:**
```csharp
services.AddHeroMessaging(builder =>
{
    // No batching
});
```

**After:**
```csharp
services.AddHeroMessaging(builder =>
{
    builder.WithBatchProcessing(batch =>
    {
        batch.Enable().UseBalancedProfile();
    });
});
```

### Gradual Migration

1. **Enable in development** with low volume
2. **Monitor metrics** (latency, throughput, errors)
3. **Enable in staging** with production-like volume
4. **Gradual production rollout** using feature flags
5. **Monitor and adjust** based on real-world data

## Future Enhancements

### Phase 4: RabbitMQ Integration (Planned)
- Transport-level batch acknowledgment
- Consumer prefetch optimization
- Bulk message publishing

### Phase 5: Advanced Features (Future)
- Batch-level idempotency
- Priority-based batching
- Dynamic batch size adjustment
- Enhanced metrics and observability

## Related Documentation

- **[ADR 0007: Batch Processing](adr/0007-batch-processing.md)** - Architecture decisions
- **[Testing Guide](testing-guide.md)** - Testing batch processing
- **[Performance Benchmarks](../tests/HeroMessaging.Benchmarks/)** - Performance validation

## Examples

Complete examples are available in the test project:
- [BatchProcessingTests.cs](../tests/HeroMessaging.Tests/Unit/BatchProcessingTests.cs)
- [BatchDecoratorTests.cs](../tests/HeroMessaging.Tests/Unit/BatchDecoratorTests.cs)
- [BatchProcessingBuilderTests.cs](../tests/HeroMessaging.Tests/Unit/BatchProcessingBuilderTests.cs)

---

**Status**: Documentation complete for Phases 1-3. Will be updated as Phases 4-5 are implemented.

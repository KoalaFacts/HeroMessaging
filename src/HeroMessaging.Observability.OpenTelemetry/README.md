# HeroMessaging.Observability.OpenTelemetry

**OpenTelemetry instrumentation for HeroMessaging providing distributed tracing and metrics.**

## Overview

Comprehensive OpenTelemetry integration for HeroMessaging enabling:

- **Distributed Tracing**: End-to-end trace spans across message flows
- **Metrics**: Message counters, processing duration, queue depth
- **Correlation**: Automatic propagation of trace context
- **Standards**: W3C Trace Context and OpenTelemetry Semantic Conventions

## Installation

```bash
dotnet add package HeroMessaging.Observability.OpenTelemetry
dotnet add package OpenTelemetry.Exporter.Console  # or other exporters
```

### Framework Support

- .NET 6.0
- .NET 7.0
- .NET 8.0
- .NET 9.0

## Quick Start

```csharp
using HeroMessaging;
using HeroMessaging.Observability.OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

// Configure HeroMessaging with OpenTelemetry
services.AddHeroMessaging(builder =>
{
    builder.AddOpenTelemetry();
});

// Configure OpenTelemetry exporters
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("HeroMessaging")
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddMeter("HeroMessaging.Metrics")
        .AddConsoleExporter());
```

### With Jaeger for Distributed Tracing

```csharp
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("HeroMessaging")
        .AddJaegerExporter(options =>
        {
            options.AgentHost = "localhost";
            options.AgentPort = 6831;
        }));
```

### With Prometheus for Metrics

```csharp
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("HeroMessaging.Metrics")
        .AddPrometheusExporter());

// Add Prometheus scraping endpoint
app.MapPrometheusScrapingEndpoint();
```

## Traces

### Automatic Span Creation

The instrumentation automatically creates spans for:

- **Commands**: `HeroMessaging.Command.{CommandName}`
- **Queries**: `HeroMessaging.Query.{QueryName}`
- **Events**: `HeroMessaging.Event.{EventName}`
- **Saga Transitions**: `HeroMessaging.Saga.Transition`
- **Message Send/Receive**: `HeroMessaging.Send`, `HeroMessaging.Receive`

### Span Attributes

Each span includes:

```
messaging.system = "heromessaging"
messaging.destination = [queue/topic name]
messaging.message_id = [message GUID]
messaging.message_type = [type name]
messaging.correlation_id = [correlation ID]
messaging.causation_id = [causation ID]
```

### Example Trace

```
Order Processing Flow:
└─ HeroMessaging.Command.CreateOrder (100ms)
   ├─ HeroMessaging.Saga.Transition (50ms)
   │  └─ Database.SaveSaga (20ms)
   ├─ HeroMessaging.Event.OrderCreated (30ms)
   │  └─ HeroMessaging.Send (10ms)
   └─ HeroMessaging.Command.ProcessPayment (20ms)
```

## Metrics

### Available Meters

| Metric | Type | Description |
|--------|------|-------------|
| `heromessaging_messages_sent_total` | Counter | Total messages sent |
| `heromessaging_messages_received_total` | Counter | Total messages received |
| `heromessaging_messages_failed_total` | Counter | Total failed messages |
| `heromessaging_message_processing_duration_ms` | Histogram | Processing duration in milliseconds |
| `heromessaging_message_size_bytes` | Histogram | Message size in bytes |
| `heromessaging_queue_operations` | Counter | Queue operations count |

### Viewing Metrics

```csharp
// Console output
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("HeroMessaging.Metrics")
        .AddConsoleExporter());

// Output:
// heromessaging_messages_sent_total: 1234
// heromessaging_message_processing_duration_ms: P50=0.5ms, P95=2ms, P99=5ms
```

## Advanced Scenarios

### Custom Span Enrichment

```csharp
// Activity.Current represents the current span
Activity.Current?.SetTag("custom.field", "value");
Activity.Current?.AddEvent(new ActivityEvent("OrderValidated"));
```

### Sampling

```csharp
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetSampler(new TraceIdRatioBasedSampler(0.1)) // Sample 10%
        .AddSource("HeroMessaging"));
```

### Multiple Exporters

```csharp
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("HeroMessaging")
        .AddConsoleExporter()
        .AddJaegerExporter()
        .AddZipkinExporter());
```

## Troubleshooting

### No Traces Appearing

**Check:**
1. Ensure `AddSource("HeroMessaging")` is configured
2. Verify exporter is running (Jaeger, Zipkin, etc.)
3. Check sampling configuration

```csharp
// Enable all traces for debugging
.SetSampler(new AlwaysOnSampler())
```

### Missing Attributes

**Ensure** OpenTelemetry decorator is registered:

```csharp
services.AddHeroMessaging(builder =>
{
    builder.AddOpenTelemetry(); // Required!
});
```

## Integration Examples

### With ASP.NET Core

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeroMessaging(messagingBuilder =>
{
    messagingBuilder.AddOpenTelemetry();
});

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation() // HTTP requests
        .AddSource("HeroMessaging")     // Messages
        .AddJaegerExporter());

var app = builder.Build();
```

### With Background Services

```csharp
public class MessageProcessorService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in _queue.ReadAllAsync(stoppingToken))
        {
            // Each message processing gets a new trace
            using var activity = Activity.Current;
            await _processor.ProcessAsync(message);
        }
    }
}
```

## Performance Impact

- **Overhead**: <0.1ms per operation when tracing is enabled
- **Sampling**: Use sampling in production to reduce overhead
- **Memory**: Minimal - spans are exported asynchronously

## See Also

- [Main Documentation](../../README.md)
- [Health Checks](../HeroMessaging.Observability.HealthChecks/README.md)
- [OpenTelemetry Documentation](https://opentelemetry.io/docs/instrumentation/net/)

## License

This package is part of HeroMessaging and is licensed under the MIT License.

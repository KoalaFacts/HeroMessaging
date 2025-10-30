# HeroMessaging.Observability.HealthChecks

**ASP.NET Core health check integrations for monitoring HeroMessaging components.**

## Overview

Comprehensive health check implementations for all HeroMessaging components including storage, transport, queues, and custom aggregations. Integrates seamlessly with ASP.NET Core health check infrastructure for production monitoring.

**Health Checks Provided**:
- `MessageStorageHealthCheck`: Message persistence health
- `OutboxStorageHealthCheck`: Outbox pattern health
- `InboxStorageHealthCheck`: Inbox pattern health
- `QueueStorageHealthCheck`: Queue depth and health
- `TransportHealthCheck`: Transport connection health
- `CompositeHealthCheck`: Aggregate multiple checks

## Installation

```bash
dotnet add package HeroMessaging.Observability.HealthChecks
dotnet add package Microsoft.Extensions.Diagnostics.HealthChecks
```

### Framework Support

- .NET 6.0
- .NET 7.0
- .NET 8.0
- .NET 9.0

## Quick Start

### Basic Configuration

```csharp
using HeroMessaging.Observability.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Add HeroMessaging with health checks
builder.Services.AddHeroMessaging(messagingBuilder =>
{
    messagingBuilder.UsePostgreSqlStorage(options =>
    {
        options.ConnectionString = "...";
    });
});

// Add health check endpoint
builder.Services.AddHealthChecks()
    .AddHeroMessaging(); // Adds all HeroMessaging health checks

var app = builder.Build();

// Map health check endpoint
app.MapHealthChecks("/health");

app.Run();
```

### Test Health Endpoint

```bash
curl http://localhost:5000/health
```

**Response**:
```json
{
  "status": "Healthy",
  "totalDuration": "00:00:00.0234567",
  "entries": {
    "HeroMessaging": {
      "status": "Healthy",
      "duration": "00:00:00.0123456"
    }
  }
}
```

## Individual Health Checks

### Storage Health Check

Validates message storage operations:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<MessageStorageHealthCheck>(
        "message_storage",
        tags: new[] { "storage", "database" });
```

**What it checks**:
- Can connect to storage
- Can write test message
- Can retrieve test message
- Can delete test message

### Inbox Storage Health Check

Validates inbox pattern health:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<InboxStorageHealthCheck>(
        "inbox_storage",
        tags: new[] { "storage", "inbox" });
```

**What it checks**:
- Can query pending inbox entries
- Storage is responsive
- No connectivity issues

### Outbox Storage Health Check

Validates outbox pattern health:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<OutboxStorageHealthCheck>(
        "outbox_storage",
        tags: new[] { "storage", "outbox" });
```

**What it checks**:
- Can query pending outbox messages
- Storage is operational
- Outbox processor health

### Queue Storage Health Check

Validates queue storage with depth metrics:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<QueueStorageHealthCheck>(
        "queue_storage",
        tags: new[] { "storage", "queue" });
```

**What it checks**:
- Can get queue depth
- Queue is accessible
- Returns depth metrics in health data

### Transport Health Check

Validates transport connection:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<TransportHealthCheck>(
        "transport",
        tags: new[] { "transport", "rabbitmq" });
```

**What it checks**:
- Transport connection is alive
- Can send/receive test message
- Connection pool health

## Advanced Configuration

### Health Check Options

```csharp
builder.Services.AddHealthChecks()
    .AddHeroMessaging(options =>
    {
        options.CheckStorage = true;        // Enable storage checks
        options.CheckTransport = true;      // Enable transport checks
        options.CheckInbox = true;          // Enable inbox checks
        options.CheckOutbox = true;         // Enable outbox checks
        options.CheckQueues = true;         // Enable queue checks
        options.Timeout = TimeSpan.FromSeconds(5); // Health check timeout
    });
```

### Custom Failure Threshold

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<QueueStorageHealthCheck>(
        "orders_queue",
        failureStatus: HealthStatus.Degraded, // Not Unhealthy
        tags: new[] { "queue" },
        timeout: TimeSpan.FromSeconds(3));
```

### Multiple Transports

```csharp
builder.Services.AddHealthChecks()
    .AddCheck("rabbitmq_primary",
        () => CheckTransport("rabbitmq1"),
        tags: new[] { "transport", "primary" })
    .AddCheck("rabbitmq_secondary",
        () => CheckTransport("rabbitmq2"),
        tags: new[] { "transport", "secondary" });
```

## Health Check UI

### Add UI Dashboard

```bash
dotnet add package AspNetCore.HealthChecks.UI
dotnet add package AspNetCore.HealthChecks.UI.InMemory.Storage
```

```csharp
builder.Services
    .AddHealthChecks()
    .AddHeroMessaging();

builder.Services
    .AddHealthChecksUI()
    .AddInMemoryStorage();

var app = builder.Build();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecksUI(); // UI at /healthchecks-ui

app.Run();
```

**Access UI**: http://localhost:5000/healthchecks-ui

## Kubernetes Integration

### Liveness Probe

```yaml
apiVersion: v1
kind: Pod
metadata:
  name: heromessaging-app
spec:
  containers:
  - name: app
    image: myapp:latest
    livenessProbe:
      httpGet:
        path: /health/live
        port: 80
      initialDelaySeconds: 10
      periodSeconds: 10
```

### Readiness Probe

```yaml
readinessProbe:
  httpGet:
    path: /health/ready
    port: 80
  initialDelaySeconds: 5
  periodSeconds: 5
```

### Application Code

```csharp
// Liveness: Am I alive?
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

// Readiness: Am I ready to serve traffic?
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// Configure checks
builder.Services.AddHealthChecks()
    // Always healthy if app is running
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })

    // Dependent services
    .AddHeroMessaging(tags: new[] { "ready" });
```

## Monitoring and Alerting

### Prometheus Exporter

```bash
dotnet add package AspNetCore.HealthChecks.Publisher.Prometheus
```

```csharp
builder.Services.AddHealthChecks()
    .AddHeroMessaging();

builder.Services.Configure<HealthCheckPublisherOptions>(options =>
{
    options.Delay = TimeSpan.FromSeconds(10);
    options.Period = TimeSpan.FromSeconds(10);
});

app.UseHealthChecksPrometheusExporter("/metrics");
```

**Prometheus Scrape Config**:
```yaml
scrape_configs:
  - job_name: 'heromessaging'
    static_configs:
      - targets: ['localhost:5000']
    metrics_path: '/metrics'
```

### Application Insights

```bash
dotnet add package AspNetCore.HealthChecks.ApplicationInsights
```

```csharp
builder.Services.AddHealthChecks()
    .AddHeroMessaging()
    .AddApplicationInsightsPublisher();
```

## Composite Health Checks

Aggregate multiple checks:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<CompositeHealthCheck>("heromessaging_composite",
        tags: new[] { "composite" },
        timeout: TimeSpan.FromSeconds(10));

// CompositeHealthCheck will check all registered HeroMessaging components
```

## Custom Health Checks

### Create Custom Check

```csharp
public class CustomSagaHealthCheck : IHealthCheck
{
    private readonly ISagaRepository<OrderSaga> _repository;

    public CustomSagaHealthCheck(ISagaRepository<OrderSaga> repository)
    {
        _repository = repository;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check for stuck sagas
            var staleSagas = await _repository.FindStaleAsync(
                TimeSpan.FromHours(24),
                cancellationToken);

            var count = staleSagas.Count();

            if (count > 100)
            {
                return HealthCheckResult.Unhealthy(
                    $"Too many stale sagas: {count}",
                    data: new Dictionary<string, object>
                    {
                        ["stale_saga_count"] = count
                    });
            }

            if (count > 10)
            {
                return HealthCheckResult.Degraded(
                    $"Some stale sagas detected: {count}",
                    data: new Dictionary<string, object>
                    {
                        ["stale_saga_count"] = count
                    });
            }

            return HealthCheckResult.Healthy(
                $"No stale sagas: {count}",
                data: new Dictionary<string, object>
                {
                    ["stale_saga_count"] = count
                });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Failed to check saga health",
                ex);
        }
    }
}

// Register
builder.Services.AddHealthChecks()
    .AddCheck<CustomSagaHealthCheck>("saga_health");
```

## Troubleshooting

### Common Issues

#### Issue: Health Check Always Unhealthy

**Symptoms**:
- `/health` returns 503 Service Unavailable

**Solution**:
```csharp
// Check dependencies are registered
builder.Services.AddHeroMessaging(builder =>
{
    builder.UseInMemoryStorage(); // Or PostgreSql/SqlServer
});

// Verify health checks can resolve dependencies
builder.Services.AddHealthChecks()
    .AddHeroMessaging();
```

#### Issue: Timeout Errors

**Symptoms**:
- Health checks timeout frequently

**Solution**:
```csharp
// Increase timeout
builder.Services.AddHealthChecks()
    .AddCheck<MessageStorageHealthCheck>(
        "storage",
        timeout: TimeSpan.FromSeconds(10)); // Increase from default
```

#### Issue: Missing Health Check Data

**Symptoms**:
- Health response lacks metrics (queue depth, etc.)

**Solution**:
```csharp
// Use detailed response writer
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration,
                data = e.Value.Data // Include data!
            })
        });
        await context.Response.WriteAsync(result);
    }
});
```

## Best Practices

1. **Use Tags**: Organize health checks with tags (`storage`, `transport`, `live`, `ready`)
2. **Set Timeouts**: Prevent slow health checks from blocking
3. **Monitor Degraded State**: Not just Healthy/Unhealthy
4. **Include Metrics**: Use `Data` dictionary for diagnostics
5. **Separate Liveness/Readiness**: Different endpoints for Kubernetes
6. **Cache Results**: For expensive health checks
7. **Alert on Failures**: Integrate with monitoring systems

## See Also

- [Main Documentation](../../README.md)
- [OpenTelemetry Integration](../HeroMessaging.Observability.OpenTelemetry/README.md)
- [PostgreSQL Storage](../HeroMessaging.Storage.PostgreSql/README.md)
- [ASP.NET Core Health Checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks)

## License

This package is part of HeroMessaging and is licensed under the MIT License.

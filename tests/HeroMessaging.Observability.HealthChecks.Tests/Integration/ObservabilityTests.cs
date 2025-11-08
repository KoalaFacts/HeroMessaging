using System.Threading;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Tests.TestUtilities;
using Xunit;

namespace HeroMessaging.Observability.HealthChecks.Tests.Integration;

/// <summary>
/// Integration tests for observability plugin implementations
/// Testing health checks, OpenTelemetry metrics/traces, custom metrics, and failure scenarios
/// </summary>
public class ObservabilityTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables = new();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HealthCheckRegistration_RegistersAndExecutesCorrectly()
    {
        // Arrange
        var healthCheckRegistry = new TestHealthCheckRegistry();
        _disposables.Add(healthCheckRegistry);

        var messageProcessorCheck = new TestMessageProcessorHealthCheck();
        var storageHealthCheck = new TestStorageHealthCheck();

        // Act
        healthCheckRegistry.RegisterHealthCheck("MessageProcessor", messageProcessorCheck);
        healthCheckRegistry.RegisterHealthCheck("Storage", storageHealthCheck);

        var healthReport = await healthCheckRegistry.ExecuteHealthChecksAsync();

        // Assert
        Assert.NotNull(healthReport);
        Assert.Equal(2, healthReport.Entries.Count);
        Assert.Contains("MessageProcessor", healthReport.Entries.Keys);
        Assert.Contains("Storage", healthReport.Entries.Keys);
        Assert.Equal(HealthStatus.Healthy, healthReport.OverallStatus);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HealthCheck_WithUnhealthyComponent_ReportsUnhealthy()
    {
        // Arrange
        var healthCheckRegistry = new TestHealthCheckRegistry();
        _disposables.Add(healthCheckRegistry);

        var healthyCheck = new TestMessageProcessorHealthCheck();
        var unhealthyCheck = new TestStorageHealthCheck();
        unhealthyCheck.SimulateFailure("Database connection failed");

        // Act
        healthCheckRegistry.RegisterHealthCheck("Healthy", healthyCheck);
        healthCheckRegistry.RegisterHealthCheck("Unhealthy", unhealthyCheck);

        var healthReport = await healthCheckRegistry.ExecuteHealthChecksAsync();

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, healthReport.OverallStatus);
        Assert.Equal(HealthStatus.Healthy, healthReport.Entries["Healthy"].Status);
        Assert.Equal(HealthStatus.Unhealthy, healthReport.Entries["Unhealthy"].Status);
        Assert.Contains("Database connection failed", healthReport.Entries["Unhealthy"].Description);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task OpenTelemetryMetrics_CollectsMessageProcessingMetrics()
    {
        // Arrange
        var metricsCollector = new TestMetricsCollector();
        _disposables.Add(metricsCollector);

        var messageProcessor = new TestObservableMessageProcessor(metricsCollector);

        var messages = Enumerable.Range(0, 10)
            .Select(i => TestMessageBuilder.CreateValidMessage($"Metrics test {i}"))
            .ToArray();

        // Act
        foreach (var message in messages)
        {
            await messageProcessor.ProcessAsync(message);
        }

        // Simulate processing time
        await Task.Delay(100, TestContext.Current.CancellationToken);

        var metrics = metricsCollector.GetCollectedMetrics();

        // Assert
        Assert.NotEmpty(metrics);

        var messagesProcessedMetric = metrics.FirstOrDefault(m => m.Name == "messages_processed_total");
        Assert.NotNull(messagesProcessedMetric);
        Assert.Equal(10, messagesProcessedMetric.Value);

        var processingDurationMetric = metrics.FirstOrDefault(m => m.Name == "message_processing_duration_ms");
        Assert.NotNull(processingDurationMetric);
        Assert.True(processingDurationMetric.Value > 0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task OpenTelemetryTracing_CreatesTracesForOperations()
    {
        // Arrange
        var tracer = new TestTracer();
        _disposables.Add(tracer);

        var messageProcessor = new TestObservableMessageProcessor(tracer);
        var message = TestMessageBuilder.CreateValidMessage("Tracing test");

        // Act
        await messageProcessor.ProcessAsync(message);

        var traces = tracer.GetCollectedTraces();

        // Assert
        Assert.NotEmpty(traces);

        var processTrace = traces.FirstOrDefault(t => t.OperationName == "ProcessMessage");
        Assert.NotNull(processTrace);
        Assert.Equal("ProcessMessage", processTrace.OperationName);
        Assert.True(processTrace.Duration > TimeSpan.Zero);
        Assert.Contains("message.id", processTrace.Tags.Keys);
        Assert.Equal(message.MessageId.ToString(), processTrace.Tags["message.id"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CustomMetrics_IncrementAndTrackCorrectly()
    {
        // Arrange
        var customMetrics = new TestCustomMetrics();
        _disposables.Add(customMetrics);

        // Act
        customMetrics.IncrementCounter("custom.messages.received");
        customMetrics.IncrementCounter("custom.messages.received");
        customMetrics.IncrementCounter("custom.messages.processed");

        customMetrics.RecordValue("custom.processing.duration", 150.5);
        customMetrics.RecordValue("custom.processing.duration", 200.0);

        customMetrics.SetGauge("custom.active.connections", 5);

        // Assert
        Assert.Equal(2, customMetrics.GetCounterValue("custom.messages.received"));
        Assert.Equal(1, customMetrics.GetCounterValue("custom.messages.processed"));

        var durations = customMetrics.GetHistogramValues("custom.processing.duration");
        Assert.Equal(2, durations.Count);
        Assert.Contains(150.5, durations);
        Assert.Contains(200.0, durations);

        Assert.Equal(5, customMetrics.GetGaugeValue("custom.active.connections"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ObservabilityInFailureScenarios_TracksErrorMetrics()
    {
        // Arrange
        var metricsCollector = new TestMetricsCollector();
        _disposables.Add(metricsCollector);

        var failingProcessor = new TestFailingMessageProcessor(metricsCollector);
        var messages = Enumerable.Range(0, 5)
            .Select(i => TestMessageBuilder.CreateValidMessage($"Failure test {i}"))
            .ToArray();

        // Act
        foreach (var message in messages)
        {
            try
            {
                await failingProcessor.ProcessAsync(message);
            }
            catch (InvalidOperationException)
            {
                // Expected failures
            }
        }

        var metrics = metricsCollector.GetCollectedMetrics();

        // Assert
        var errorMetric = metrics.FirstOrDefault(m => m.Name == "messages_failed_total");
        Assert.NotNull(errorMetric);
        Assert.Equal(5, errorMetric.Value);

        var errorRateMetric = metrics.FirstOrDefault(m => m.Name == "error_rate_percent");
        Assert.NotNull(errorRateMetric);
        Assert.Equal(100.0, errorRateMetric.Value); // All messages failed
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DistributedTracing_PropagatesTraceContext()
    {
        // Arrange
        var tracer = new TestTracer();
        _disposables.Add(tracer);

        var processor1 = new TestObservableMessageProcessor(tracer, "Processor1");
        var processor2 = new TestObservableMessageProcessor(tracer, "Processor2");

        var message = TestMessageBuilder.CreateValidMessage("Distributed tracing test");

        // Act
        var traceContext = tracer.StartTrace("DistributedOperation");

        await processor1.ProcessAsync(message, traceContext);
        await processor2.ProcessAsync(message, traceContext);

        tracer.FinishTrace(traceContext);

        var traces = tracer.GetCollectedTraces();

        // Assert
        Assert.True(traces.Count >= 3); // Parent + 2 child operations

        var parentTrace = traces.FirstOrDefault(t => t.OperationName == "DistributedOperation");
        Assert.NotNull(parentTrace);

        var childTraces = traces.Where(t => t.ParentTraceId == parentTrace.TraceId).ToList();
        Assert.Equal(2, childTraces.Count);

        Assert.Contains(childTraces, t => t.Tags.ContainsValue("Processor1"));
        Assert.Contains(childTraces, t => t.Tags.ContainsValue("Processor2"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MetricsAggregation_AggregatesOverTime()
    {
        // Arrange
        var metricsAggregator = new TestMetricsAggregator();
        _disposables.Add(metricsAggregator);

        // Act - Record metrics over time
        for (int i = 0; i < 10; i++)
        {
            metricsAggregator.RecordProcessingTime(100 + i * 10); // 100, 110, 120, ... 190ms
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        var aggregatedMetrics = await metricsAggregator.GetAggregatedMetricsAsync(TimeSpan.FromSeconds(1));

        // Assert
        Assert.NotNull(aggregatedMetrics);
        Assert.Equal(10, aggregatedMetrics.Count);
        Assert.Equal(100, aggregatedMetrics.Min);
        Assert.Equal(190, aggregatedMetrics.Max);
        Assert.Equal(145, aggregatedMetrics.Average); // (100+190)/2 = 145
        Assert.Equal(1450, aggregatedMetrics.Sum); // Sum of 100+110+...+190
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AlertingIntegration_TriggersAlertsOnThresholds()
    {
        // Arrange
        var alertingSystem = new TestAlertingSystem();
        _disposables.Add(alertingSystem);

        alertingSystem.ConfigureAlert("HighErrorRate", metric => metric.Value > 50.0);
        alertingSystem.ConfigureAlert("HighLatency", metric => metric.Value > 1000.0);

        // Act - Trigger high error rate
        alertingSystem.ReportMetric("error_rate_percent", 75.0);
        alertingSystem.ReportMetric("average_latency_ms", 500.0);

        await Task.Delay(100, TestContext.Current.CancellationToken); // Allow alerts to process

        var triggeredAlerts = alertingSystem.GetTriggeredAlerts();

        // Assert
        Assert.Single(triggeredAlerts);
        Assert.Equal("HighErrorRate", triggeredAlerts[0].Name);
        Assert.Equal(75.0, triggeredAlerts[0].MetricValue);

        // Act - Trigger high latency alert
        alertingSystem.ReportMetric("average_latency_ms", 1500.0);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        triggeredAlerts = alertingSystem.GetTriggeredAlerts();

        // Assert
        Assert.Equal(2, triggeredAlerts.Count);
        Assert.Contains(triggeredAlerts, a => a.Name == "HighLatency");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ObservabilityConfiguration_AppliesCorrectly()
    {
        // Arrange
        var config = new ObservabilityConfiguration
        {
            EnableMetrics = true,
            EnableTracing = true,
            EnableHealthChecks = true,
            MetricsExportInterval = TimeSpan.FromSeconds(5),
            TracesSamplingRate = 0.1, // 10% sampling
            HealthCheckInterval = TimeSpan.FromSeconds(30)
        };

        var observabilityProvider = new TestObservabilityProvider(config);
        _disposables.Add(observabilityProvider);

        // Act
        await observabilityProvider.InitializeAsync();

        // Assert
        Assert.True(observabilityProvider.IsMetricsEnabled);
        Assert.True(observabilityProvider.IsTracingEnabled);
        Assert.True(observabilityProvider.IsHealthChecksEnabled);
        Assert.Equal(TimeSpan.FromSeconds(5), observabilityProvider.MetricsExportInterval);
        Assert.Equal(0.1, observabilityProvider.TracesSamplingRate);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var disposable in _disposables)
        {
            await disposable.DisposeAsync();
        }
    }

    // Test implementation classes
    private class TestHealthCheckRegistry : IAsyncDisposable
    {
        private readonly Dictionary<string, IHealthCheck> _healthChecks = new();

        public void RegisterHealthCheck(string name, IHealthCheck healthCheck)
        {
            _healthChecks[name] = healthCheck;
        }

        public async Task<HealthReport> ExecuteHealthChecksAsync()
        {
            var entries = new Dictionary<string, HealthEntry>();

            foreach (var kvp in _healthChecks)
            {
                var result = await kvp.Value.CheckHealthAsync();
                entries[kvp.Key] = new HealthEntry
                {
                    Status = result.Status,
                    Description = result.Description,
                    Duration = result.Duration
                };
            }

            var overallStatus = entries.Values.Any(e => e.Status == HealthStatus.Unhealthy)
                ? HealthStatus.Unhealthy
                : HealthStatus.Healthy;

            return new HealthReport
            {
                OverallStatus = overallStatus,
                Entries = entries
            };
        }

        public async ValueTask DisposeAsync()
        {
            _healthChecks.Clear();
            await Task.CompletedTask;
        }
    }

    private class TestMessageProcessorHealthCheck : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync()
        {
            await Task.Delay(10, TestContext.Current.CancellationToken); // Simulate health check
            return new HealthCheckResult
            {
                Status = HealthStatus.Healthy,
                Description = "Message processor is healthy",
                Duration = TimeSpan.FromMilliseconds(10)
            };
        }
    }

    private class TestStorageHealthCheck : IHealthCheck
    {
        private bool _simulateFailure = false;
        private string _failureMessage = "";

        public void SimulateFailure(string message)
        {
            _simulateFailure = true;
            _failureMessage = message;
        }

        public async Task<HealthCheckResult> CheckHealthAsync()
        {
            await Task.Delay(15, TestContext.Current.CancellationToken); // Simulate health check

            if (_simulateFailure)
            {
                return new HealthCheckResult
                {
                    Status = HealthStatus.Unhealthy,
                    Description = _failureMessage,
                    Duration = TimeSpan.FromMilliseconds(15)
                };
            }

            return new HealthCheckResult
            {
                Status = HealthStatus.Healthy,
                Description = "Storage is healthy",
                Duration = TimeSpan.FromMilliseconds(15)
            };
        }
    }

    private class TestMetricsCollector : IAsyncDisposable
    {
        private readonly Dictionary<string, Metric> _metrics = new(StringComparer.OrdinalIgnoreCase);
        private readonly Lock _syncRoot = new();

        public void RecordMetric(string name, double value, Dictionary<string, string>? tags = null)
        {
            lock (_syncRoot)
            {
                if (!_metrics.TryGetValue(name, out var metric))
                {
                    metric = new Metric
                    {
                        Name = name,
                        Value = 0,
                        Tags = tags != null ? new Dictionary<string, string>(tags) : new Dictionary<string, string>(),
                        Timestamp = DateTime.UtcNow
                    };
                    _metrics[name] = metric;
                }
                else if (tags != null && tags.Count > 0)
                {
                    foreach (var tag in tags)
                    {
                        metric.Tags[tag.Key] = tag.Value;
                    }
                }

                metric.Timestamp = DateTime.UtcNow;
                metric.Value = IsCounterMetric(name) ? metric.Value + value : value;
            }
        }

        public List<Metric> GetCollectedMetrics()
        {
            lock (_syncRoot)
            {
                return _metrics.Values
                    .Select(metric => new Metric
                    {
                        Name = metric.Name,
                        Value = metric.Value,
                        Tags = new Dictionary<string, string>(metric.Tags),
                        Timestamp = metric.Timestamp
                    })
                    .ToList();
            }
        }

        private static bool IsCounterMetric(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            return name.EndsWith("_total", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".processed", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".failed", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".count", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_count", StringComparison.OrdinalIgnoreCase);
        }

        public async ValueTask DisposeAsync()
        {
            lock (_syncRoot)
            {
                _metrics.Clear();
            }

            await Task.CompletedTask;
        }
    }

    private class TestTracer : IAsyncDisposable
    {
        private readonly List<Trace> _traces = new();

        public TraceContext StartTrace(string operationName)
        {
            var traceId = Guid.NewGuid().ToString();
            return new TraceContext
            {
                TraceId = traceId,
                OperationName = operationName,
                StartTime = DateTime.UtcNow
            };
        }

        public void FinishTrace(TraceContext context)
        {
            _traces.Add(new Trace
            {
                TraceId = context.TraceId,
                ParentTraceId = context.ParentTraceId,
                OperationName = context.OperationName,
                StartTime = context.StartTime,
                Duration = DateTime.UtcNow - context.StartTime,
                Tags = context.Tags
            });
        }

        public List<Trace> GetCollectedTraces() => _traces.ToList();

        public async ValueTask DisposeAsync()
        {
            _traces.Clear();
            await Task.CompletedTask;
        }
    }

    private class TestObservableMessageProcessor
    {
        private readonly TestMetricsCollector? _metricsCollector;
        private readonly TestTracer? _tracer;
        private readonly string _processorName;

        public TestObservableMessageProcessor(TestMetricsCollector metricsCollector, string name = "DefaultProcessor")
        {
            _metricsCollector = metricsCollector;
            _processorName = name;
        }

        public TestObservableMessageProcessor(TestTracer tracer, string name = "DefaultProcessor")
        {
            _tracer = tracer;
            _processorName = name;
        }

        public async Task ProcessAsync(IMessage message, TraceContext? parentContext = null)
        {
            TraceContext? traceContext = null;

            if (_tracer != null)
            {
                traceContext = _tracer.StartTrace("ProcessMessage");
                traceContext.ParentTraceId = parentContext?.TraceId;
                traceContext.Tags["message.id"] = message.MessageId.ToString();
                traceContext.Tags["processor.name"] = _processorName;
            }

            try
            {
                await Task.Delay(50, TestContext.Current.CancellationToken); // Simulate processing

                _metricsCollector?.RecordMetric("messages_processed_total", 1);
                _metricsCollector?.RecordMetric("message_processing_duration_ms", 50);
            }
            finally
            {
                if (_tracer != null && traceContext != null)
                {
                    _tracer.FinishTrace(traceContext);
                }
            }
        }
    }

    private class TestFailingMessageProcessor
    {
        private readonly TestMetricsCollector _metricsCollector;

        public TestFailingMessageProcessor(TestMetricsCollector metricsCollector)
        {
            _metricsCollector = metricsCollector;
        }

        public async Task ProcessAsync(IMessage message)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);

            _metricsCollector.RecordMetric("messages_failed_total", 1);
            _metricsCollector.RecordMetric("error_rate_percent", 100.0);

            throw new InvalidOperationException("Simulated processing failure");
        }
    }

    // Supporting classes and enums
    private interface IHealthCheck
    {
        Task<HealthCheckResult> CheckHealthAsync();
    }

    private enum HealthStatus
    {
        Healthy,
        Unhealthy,
        Degraded
    }

    private class HealthCheckResult
    {
        public HealthStatus Status { get; set; }
        public string Description { get; set; } = "";
        public TimeSpan Duration { get; set; }
    }

    private class HealthReport
    {
        public HealthStatus OverallStatus { get; set; }
        public Dictionary<string, HealthEntry> Entries { get; set; } = new();
    }

    private class HealthEntry
    {
        public HealthStatus Status { get; set; }
        public string Description { get; set; } = "";
        public TimeSpan Duration { get; set; }
    }

    private class Metric
    {
        public string Name { get; set; } = "";
        public double Value { get; set; }
        public Dictionary<string, string> Tags { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }

    private class Trace
    {
        public string TraceId { get; set; } = "";
        public string? ParentTraceId { get; set; }
        public string OperationName { get; set; } = "";
        public DateTime StartTime { get; set; }
        public TimeSpan Duration { get; set; }
        public Dictionary<string, string> Tags { get; set; } = new();
    }

    private class TraceContext
    {
        public string TraceId { get; set; } = "";
        public string? ParentTraceId { get; set; }
        public string OperationName { get; set; } = "";
        public DateTime StartTime { get; set; }
        public Dictionary<string, string> Tags { get; set; } = new();
    }

    // Additional test classes for remaining functionality
    private class TestCustomMetrics : IAsyncDisposable
    {
        private readonly Dictionary<string, long> _counters = new();
        private readonly Dictionary<string, List<double>> _histograms = new();
        private readonly Dictionary<string, double> _gauges = new();

        public void IncrementCounter(string name) => _counters[name] = _counters.GetValueOrDefault(name, 0) + 1;
        public void RecordValue(string name, double value) => _histograms.GetOrAdd(name, []).Add(value);
        public void SetGauge(string name, double value) => _gauges[name] = value;

        public long GetCounterValue(string name) => _counters.GetValueOrDefault(name, 0);
        public List<double> GetHistogramValues(string name) => _histograms.GetValueOrDefault(name, new List<double>());
        public double GetGaugeValue(string name) => _gauges.GetValueOrDefault(name, 0);

        public async ValueTask DisposeAsync() => await Task.CompletedTask;
    }

    private class TestMetricsAggregator : IAsyncDisposable
    {
        private readonly List<double> _values = new();

        public void RecordProcessingTime(double milliseconds) => _values.Add(milliseconds);

        public async Task<AggregatedMetrics> GetAggregatedMetricsAsync(TimeSpan timeWindow)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            return new AggregatedMetrics
            {
                Count = _values.Count,
                Min = _values.DefaultIfEmpty(0).Min(),
                Max = _values.DefaultIfEmpty(0).Max(),
                Average = _values.DefaultIfEmpty(0).Average(),
                Sum = _values.Sum()
            };
        }

        public async ValueTask DisposeAsync() => await Task.CompletedTask;
    }

    private class TestAlertingSystem : IAsyncDisposable
    {
        private readonly Dictionary<string, AlertRule> _alertRules = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<TriggeredAlert> _triggeredAlerts = new();

        private sealed class AlertRule
        {
            public AlertRule(Func<Metric, bool> condition) => Condition = condition;

            public Func<Metric, bool> Condition { get; }
            public HashSet<string> TargetMetricNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        public void ConfigureAlert(string name, Func<Metric, bool> condition) => _alertRules[name] = new AlertRule(condition);

        public void ReportMetric(string metricName, double value)
        {
            var metric = new Metric { Name = metricName, Value = value, Timestamp = DateTime.UtcNow };

            foreach (var ruleEntry in _alertRules)
            {
                var rule = ruleEntry.Value;

                if (rule.TargetMetricNames.Count > 0 && !rule.TargetMetricNames.Contains(metricName))
                {
                    continue;
                }

                if (!rule.Condition(metric))
                {
                    continue;
                }

                if (rule.TargetMetricNames.Count == 0)
                {
                    rule.TargetMetricNames.Add(metricName);
                }

                _triggeredAlerts.Add(new TriggeredAlert
                {
                    Name = ruleEntry.Key,
                    MetricName = metricName,
                    MetricValue = value,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        public List<TriggeredAlert> GetTriggeredAlerts() => _triggeredAlerts.ToList();

        public async ValueTask DisposeAsync() => await Task.CompletedTask;
    }

    private class TestObservabilityProvider : IAsyncDisposable
    {
        private readonly ObservabilityConfiguration _config;

        public TestObservabilityProvider(ObservabilityConfiguration config) => _config = config;

        public async Task InitializeAsync() => await Task.Delay(50, TestContext.Current.CancellationToken);

        public bool IsMetricsEnabled => _config.EnableMetrics;
        public bool IsTracingEnabled => _config.EnableTracing;
        public bool IsHealthChecksEnabled => _config.EnableHealthChecks;
        public TimeSpan MetricsExportInterval => _config.MetricsExportInterval;
        public double TracesSamplingRate => _config.TracesSamplingRate;

        public async ValueTask DisposeAsync() => await Task.CompletedTask;
    }

    private class ObservabilityConfiguration
    {
        public bool EnableMetrics { get; set; }
        public bool EnableTracing { get; set; }
        public bool EnableHealthChecks { get; set; }
        public TimeSpan MetricsExportInterval { get; set; }
        public double TracesSamplingRate { get; set; }
        public TimeSpan HealthCheckInterval { get; set; }
    }

    private class AggregatedMetrics
    {
        public int Count { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double Average { get; set; }
        public double Sum { get; set; }
    }

    private class TriggeredAlert
    {
        public string Name { get; set; } = "";
        public string MetricName { get; set; } = "";
        public double MetricValue { get; set; }
        public DateTime Timestamp { get; set; }
    }
}

// Extension method helper
public static class DictionaryExtensions
{
    public static List<T> GetOrAdd<TKey, T>(this Dictionary<TKey, List<T>> dictionary, TKey key, List<T> defaultValue) where TKey : notnull
    {
        if (!dictionary.TryGetValue(key, out var value))
        {
            value = defaultValue;
            dictionary[key] = value;
        }
        return value;
    }
}








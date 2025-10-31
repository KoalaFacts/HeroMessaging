using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Abstractions.Configuration;

/// <summary>
/// Builder for configuring observability plugins
/// </summary>
public interface IObservabilityBuilder
{
    /// <summary>
    /// Add health checks
    /// </summary>
    IObservabilityBuilder AddHealthChecks(Action<object>? configure = null);

    /// <summary>
    /// Add OpenTelemetry instrumentation
    /// </summary>
    IObservabilityBuilder AddOpenTelemetry(Action<OpenTelemetryOptions>? configure = null);

    /// <summary>
    /// Add metrics collection
    /// </summary>
    IObservabilityBuilder AddMetrics(Action<MetricsOptions>? configure = null);

    /// <summary>
    /// Add distributed tracing
    /// </summary>
    IObservabilityBuilder AddTracing(Action<TracingOptions>? configure = null);

    /// <summary>
    /// Add logging enrichment
    /// </summary>
    IObservabilityBuilder AddLoggingEnrichment(Action<LoggingOptions>? configure = null);

    /// <summary>
    /// Add custom observability provider
    /// </summary>
    IObservabilityBuilder AddCustomProvider<T>(Action<T>? configure = null) where T : class;

    /// <summary>
    /// Enable performance counters
    /// </summary>
    IObservabilityBuilder EnablePerformanceCounters();

    /// <summary>
    /// Enable diagnostic listeners
    /// </summary>
    IObservabilityBuilder EnableDiagnosticListeners();

    /// <summary>
    /// Set sampling rate for tracing
    /// </summary>
    IObservabilityBuilder WithSamplingRate(double rate);

    /// <summary>
    /// Build and return the service collection
    /// </summary>
    IServiceCollection Build();
}

/// <summary>
/// Configuration options for OpenTelemetry instrumentation
/// </summary>
public class OpenTelemetryOptions
{
    /// <summary>
    /// Service name for identification in telemetry data. Default is "HeroMessaging".
    /// </summary>
    public string ServiceName { get; set; } = "HeroMessaging";

    /// <summary>
    /// OTLP (OpenTelemetry Protocol) endpoint URL for exporting telemetry data. Null means use default exporters.
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>
    /// Whether to enable distributed tracing. Default is true.
    /// </summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>
    /// Whether to enable metrics collection. Default is true.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Whether to enable logging integration with OpenTelemetry. Default is false to avoid overhead.
    /// </summary>
    public bool EnableLogging { get; set; } = false;

    /// <summary>
    /// Resource attributes to attach to all telemetry signals for filtering and identification.
    /// </summary>
    public Dictionary<string, string> ResourceAttributes { get; set; } = new();
}

/// <summary>
/// Configuration options for metrics collection
/// </summary>
public class MetricsOptions
{
    /// <summary>
    /// Whether to enable histogram metrics for latency tracking. Default is true.
    /// </summary>
    public bool EnableHistograms { get; set; } = true;

    /// <summary>
    /// Whether to enable counter metrics for event counting. Default is true.
    /// </summary>
    public bool EnableCounters { get; set; } = true;

    /// <summary>
    /// Whether to enable gauge metrics for current values. Default is true.
    /// </summary>
    public bool EnableGauges { get; set; } = true;

    /// <summary>
    /// How frequently to flush metrics to exporters. Default is 1 minute.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Names of custom metrics to collect beyond the standard set.
    /// </summary>
    public IReadOnlyCollection<string> CustomMetrics { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Configuration options for distributed tracing
/// </summary>
public class TracingOptions
{
    /// <summary>
    /// Sampling rate for traces (0.0 to 1.0). Default is 1.0 (100% sampling).
    /// </summary>
    public double SamplingRate { get; set; } = 1.0;

    /// <summary>
    /// Whether to record exception details in traces. Default is true.
    /// </summary>
    public bool RecordExceptions { get; set; } = true;

    /// <summary>
    /// Whether to record span events for significant operations. Default is true.
    /// </summary>
    public bool RecordEvents { get; set; } = true;

    /// <summary>
    /// Operations to exclude from tracing to reduce noise.
    /// </summary>
    public IReadOnlyCollection<string> IgnoredOperations { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Configuration options for logging enrichment
/// </summary>
public class LoggingOptions
{
    /// <summary>
    /// Whether to include logging scopes in output. Default is true.
    /// </summary>
    public bool IncludeScopes { get; set; } = true;

    /// <summary>
    /// Whether to include trace context (TraceId, SpanId) in log entries. Default is true for correlation.
    /// </summary>
    public bool IncludeTraceContext { get; set; } = true;

    /// <summary>
    /// Global properties to attach to all log entries for filtering.
    /// </summary>
    public Dictionary<string, string> GlobalProperties { get; set; } = new();
}
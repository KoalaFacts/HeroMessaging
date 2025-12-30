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
/// Configuration options for OpenTelemetry integration.
/// </summary>
public class OpenTelemetryOptions
{
    /// <summary>
    /// Service name for telemetry. Default: "HeroMessaging".
    /// </summary>
    public string ServiceName { get; set; } = "HeroMessaging";

    /// <summary>
    /// OTLP exporter endpoint URL.
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>
    /// Enable distributed tracing. Default: true.
    /// </summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>
    /// Enable metrics collection. Default: true.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Enable log export. Default: false.
    /// </summary>
    public bool EnableLogging { get; set; } = false;

    /// <summary>
    /// Additional resource attributes for telemetry.
    /// </summary>
    public Dictionary<string, string> ResourceAttributes { get; set; } = [];
}

/// <summary>
/// Configuration options for metrics collection.
/// </summary>
public class MetricsOptions
{
    /// <summary>
    /// Enable histogram metrics. Default: true.
    /// </summary>
    public bool EnableHistograms { get; set; } = true;

    /// <summary>
    /// Enable counter metrics. Default: true.
    /// </summary>
    public bool EnableCounters { get; set; } = true;

    /// <summary>
    /// Enable gauge metrics. Default: true.
    /// </summary>
    public bool EnableGauges { get; set; } = true;

    /// <summary>
    /// Interval for flushing metrics. Default: 1 minute.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Custom metric names to track.
    /// </summary>
    public IReadOnlyCollection<string> CustomMetrics { get; set; } = [];
}

/// <summary>
/// Configuration options for distributed tracing.
/// </summary>
public class TracingOptions
{
    /// <summary>
    /// Sampling rate for traces (0.0 to 1.0). Default: 1.0 (100%).
    /// </summary>
    public double SamplingRate { get; set; } = 1.0;

    /// <summary>
    /// Record exceptions in traces. Default: true.
    /// </summary>
    public bool RecordExceptions { get; set; } = true;

    /// <summary>
    /// Record events in traces. Default: true.
    /// </summary>
    public bool RecordEvents { get; set; } = true;

    /// <summary>
    /// Operation names to exclude from tracing.
    /// </summary>
    public IReadOnlyCollection<string> IgnoredOperations { get; set; } = [];
}

/// <summary>
/// Configuration options for logging enrichment.
/// </summary>
public class LoggingOptions
{
    /// <summary>
    /// Include logging scopes. Default: true.
    /// </summary>
    public bool IncludeScopes { get; set; } = true;

    /// <summary>
    /// Include trace context in logs. Default: true.
    /// </summary>
    public bool IncludeTraceContext { get; set; } = true;

    /// <summary>
    /// Global properties to add to all log entries.
    /// </summary>
    public Dictionary<string, string> GlobalProperties { get; set; } = [];
}

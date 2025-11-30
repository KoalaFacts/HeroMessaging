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

public class OpenTelemetryOptions
{
    public string ServiceName { get; set; } = "HeroMessaging";
    public string? OtlpEndpoint { get; set; }
    public bool EnableTracing { get; set; } = true;
    public bool EnableMetrics { get; set; } = true;
    public bool EnableLogging { get; set; } = false;
    public Dictionary<string, string> ResourceAttributes { get; set; } = [];
}

public class MetricsOptions
{
    public bool EnableHistograms { get; set; } = true;
    public bool EnableCounters { get; set; } = true;
    public bool EnableGauges { get; set; } = true;
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromMinutes(1);
    public IReadOnlyCollection<string> CustomMetrics { get; set; } = [];
}

public class TracingOptions
{
    public double SamplingRate { get; set; } = 1.0;
    public bool RecordExceptions { get; set; } = true;
    public bool RecordEvents { get; set; } = true;
    public IReadOnlyCollection<string> IgnoredOperations { get; set; } = [];
}

public class LoggingOptions
{
    public bool IncludeScopes { get; set; } = true;
    public bool IncludeTraceContext { get; set; } = true;
    public Dictionary<string, string> GlobalProperties { get; set; } = [];
}

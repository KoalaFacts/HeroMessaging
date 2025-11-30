using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace HeroMessaging.Observability.OpenTelemetry;

/// <summary>
/// Configuration options for OpenTelemetry instrumentation integration
/// </summary>
public class OpenTelemetryInstrumentationOptions
{
    /// <summary>
    /// Service name for OpenTelemetry resource attributes
    /// </summary>
    public string ServiceName { get; set; } = "HeroMessaging";

    /// <summary>
    /// Service namespace for OpenTelemetry resource attributes
    /// </summary>
    public string? ServiceNamespace { get; set; }

    /// <summary>
    /// Service version for OpenTelemetry resource attributes
    /// </summary>
    public string ServiceVersion { get; set; } = "1.0.0";

    /// <summary>
    /// Enable tracing instrumentation
    /// </summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>
    /// Enable metrics instrumentation
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Additional tracing configurations (e.g., exporters, samplers)
    /// </summary>
    public List<Action<TracerProviderBuilder>> TracingConfigurations { get; } = [];

    /// <summary>
    /// Additional metrics configurations (e.g., exporters, readers)
    /// </summary>
    public List<Action<MeterProviderBuilder>> MetricsConfigurations { get; } = [];

    /// <summary>
    /// Add a tracing exporter or configuration
    /// </summary>
    public OpenTelemetryInstrumentationOptions ConfigureTracing(Action<TracerProviderBuilder> configure)
    {
        TracingConfigurations.Add(configure);
        return this;
    }

    /// <summary>
    /// Add a metrics exporter or configuration
    /// </summary>
    public OpenTelemetryInstrumentationOptions ConfigureMetrics(Action<MeterProviderBuilder> configure)
    {
        MetricsConfigurations.Add(configure);
        return this;
    }
}

using HeroMessaging.Abstractions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace HeroMessaging.Observability.OpenTelemetry;

/// <summary>
/// Extension methods for registering OpenTelemetry instrumentation with HeroMessaging.
/// Provides distributed tracing and metrics collection for all message processing operations.
/// </summary>
/// <remarks>
/// This class enables OpenTelemetry observability for HeroMessaging, allowing you to:
/// - Track message processing with distributed tracing
/// - Collect performance metrics (throughput, latency, error rates)
/// - Integrate with OpenTelemetry exporters (Jaeger, Zipkin, Prometheus, etc.)
/// - Correlate messages across distributed systems
///
/// The instrumentation is implemented as a decorator in the message processing pipeline,
/// allowing you to control its position relative to other decorators.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds OpenTelemetry instrumentation to HeroMessaging for distributed tracing and metrics collection.
    /// Configures the OpenTelemetry SDK with HeroMessaging-specific activity sources and meters.
    /// </summary>
    /// <param name="builder">The HeroMessaging configuration builder</param>
    /// <param name="configure">Optional action to configure OpenTelemetry options, including service identity and exporters</param>
    /// <returns>The builder instance for fluent chaining</returns>
    /// <remarks>
    /// This method registers OpenTelemetry providers for tracing and metrics based on the configuration.
    /// To use the instrumentation in the processing pipeline, call UseOpenTelemetry() on the pipeline builder.
    ///
    /// Example usage:
    /// <code>
    /// services.AddHeroMessaging(builder =>
    /// {
    ///     builder.AddOpenTelemetry(options =>
    ///     {
    ///         options.ServiceName = "OrderService";
    ///         options.ServiceVersion = "2.0.0";
    ///         options.ConfigureTracing(tracing => tracing.AddOtlpExporter());
    ///         options.ConfigureMetrics(metrics => metrics.AddPrometheusExporter());
    ///     });
    /// });
    /// </code>
    /// </remarks>
    public static IHeroMessagingBuilder AddOpenTelemetry(
        this IHeroMessagingBuilder builder,
        Action<OpenTelemetryOptions>? configure = null)
    {
        var services = builder.Build();

        var options = new OpenTelemetryOptions();
        configure?.Invoke(options);

        // Register OpenTelemetry tracing
        if (options.EnableTracing)
        {
            services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(options.ServiceName, options.ServiceNamespace, options.ServiceVersion))
                .WithTracing(tracing =>
                {
                    tracing.AddSource(HeroMessagingInstrumentation.ActivitySourceName);

                    // Add configured trace providers
                    foreach (var tracingConfig in options.TracingConfigurations)
                    {
                        tracingConfig.Invoke(tracing);
                    }
                });
        }

        // Register OpenTelemetry metrics
        if (options.EnableMetrics)
        {
            services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(options.ServiceName, options.ServiceNamespace, options.ServiceVersion))
                .WithMetrics(metrics =>
                {
                    metrics.AddMeter(HeroMessagingInstrumentation.MeterName);

                    // Add configured metric providers
                    foreach (var metricsConfig in options.MetricsConfigurations)
                    {
                        metricsConfig.Invoke(metrics);
                    }
                });
        }

        // Note: The decorator is applied in the MessageProcessingPipelineBuilder via UseOpenTelemetry()
        // This allows users to control decorator ordering in the pipeline

        return builder;
    }
}

/// <summary>
/// Configuration options for OpenTelemetry integration with HeroMessaging.
/// Controls service identification, instrumentation enablement, and exporter configuration.
/// </summary>
/// <remarks>
/// This class configures how HeroMessaging integrates with OpenTelemetry observability.
/// It controls:
/// - Service resource attributes (name, namespace, version) for identifying telemetry sources
/// - Which instrumentation types are enabled (tracing, metrics)
/// - Additional OpenTelemetry provider configurations (exporters, samplers, processors)
///
/// Example configuration:
/// <code>
/// var options = new OpenTelemetryOptions
/// {
///     ServiceName = "OrderProcessingService",
///     ServiceNamespace = "ECommerce",
///     ServiceVersion = "2.1.0",
///     EnableTracing = true,
///     EnableMetrics = true
/// };
///
/// options.ConfigureTracing(tracing =>
/// {
///     tracing.AddOtlpExporter();
///     tracing.SetSampler(new AlwaysOnSampler());
/// });
///
/// options.ConfigureMetrics(metrics =>
/// {
///     metrics.AddPrometheusExporter();
/// });
/// </code>
/// </remarks>
public class OpenTelemetryOptions
{
    /// <summary>
    /// Gets or sets the service name for OpenTelemetry resource attributes.
    /// This identifies your service in distributed traces and metrics dashboards.
    /// Default: "HeroMessaging"
    /// </summary>
    public string ServiceName { get; set; } = "HeroMessaging";

    /// <summary>
    /// Gets or sets the service namespace for OpenTelemetry resource attributes.
    /// Used to group related services together (e.g., "ECommerce", "PaymentProcessing").
    /// Default: null (no namespace)
    /// </summary>
    public string? ServiceNamespace { get; set; }

    /// <summary>
    /// Gets or sets the service version for OpenTelemetry resource attributes.
    /// Helps track behavior across different versions of your service.
    /// Default: "1.0.0"
    /// </summary>
    public string ServiceVersion { get; set; } = "1.0.0";

    /// <summary>
    /// Gets or sets whether distributed tracing instrumentation is enabled.
    /// When true, creates spans for all message processing operations.
    /// Default: true
    /// </summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>
    /// Gets or sets whether metrics instrumentation is enabled.
    /// When true, records counters and histograms for message processing performance.
    /// Default: true
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Gets the list of additional tracing configurations to apply to the TracerProviderBuilder.
    /// Use this to add exporters (OTLP, Jaeger, Zipkin), configure samplers, or add processors.
    /// Add configurations using <see cref="ConfigureTracing"/>.
    /// </summary>
    public List<Action<TracerProviderBuilder>> TracingConfigurations { get; } = new();

    /// <summary>
    /// Gets the list of additional metrics configurations to apply to the MeterProviderBuilder.
    /// Use this to add exporters (Prometheus, OTLP), configure metric readers, or add views.
    /// Add configurations using <see cref="ConfigureMetrics"/>.
    /// </summary>
    public List<Action<MeterProviderBuilder>> MetricsConfigurations { get; } = new();

    /// <summary>
    /// Adds a configuration action for distributed tracing setup.
    /// Use this to configure exporters, samplers, processors, and other tracing components.
    /// </summary>
    /// <param name="configure">Action to configure the TracerProviderBuilder (e.g., adding exporters)</param>
    /// <returns>This options instance for fluent chaining</returns>
    /// <remarks>
    /// Example:
    /// <code>
    /// options.ConfigureTracing(tracing =>
    /// {
    ///     tracing.AddOtlpExporter(otlp =>
    ///     {
    ///         otlp.Endpoint = new Uri("http://localhost:4317");
    ///     });
    ///     tracing.SetSampler(new TraceIdRatioBasedSampler(0.1)); // Sample 10% of traces
    /// });
    /// </code>
    /// </remarks>
    public OpenTelemetryOptions ConfigureTracing(Action<TracerProviderBuilder> configure)
    {
        TracingConfigurations.Add(configure);
        return this;
    }

    /// <summary>
    /// Adds a configuration action for metrics collection setup.
    /// Use this to configure exporters, metric readers, views, and other metrics components.
    /// </summary>
    /// <param name="configure">Action to configure the MeterProviderBuilder (e.g., adding exporters)</param>
    /// <returns>This options instance for fluent chaining</returns>
    /// <remarks>
    /// Example:
    /// <code>
    /// options.ConfigureMetrics(metrics =>
    /// {
    ///     metrics.AddPrometheusExporter();
    ///     metrics.AddOtlpExporter(otlp =>
    ///     {
    ///         otlp.Endpoint = new Uri("http://localhost:4317");
    ///     });
    /// });
    /// </code>
    /// </remarks>
    public OpenTelemetryOptions ConfigureMetrics(Action<MeterProviderBuilder> configure)
    {
        MetricsConfigurations.Add(configure);
        return this;
    }
}
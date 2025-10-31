using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace HeroMessaging.Observability.OpenTelemetry;

/// <summary>
/// Extension methods for registering OpenTelemetry instrumentation
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add OpenTelemetry instrumentation to HeroMessaging
    /// </summary>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <param name="configure">Optional configuration action for OpenTelemetry</param>
    public static IHeroMessagingBuilder AddOpenTelemetry(
        this IHeroMessagingBuilder builder,
        Action<OpenTelemetryOptions>? configure = null)
    {
        var services = builder.Build();

        var options = new OpenTelemetryOptions();
        configure?.Invoke(options);

        // Register transport instrumentation
        services.TryAddSingleton<ITransportInstrumentation>(OpenTelemetryTransportInstrumentation.Instance);

        // Register OpenTelemetry tracing
        if (options.EnableTracing)
        {
            services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(options.ServiceName, options.ServiceNamespace, options.ServiceVersion))
                .WithTracing(tracing =>
                {
                    tracing.AddSource(HeroMessagingInstrumentation.ActivitySourceName);
                    tracing.AddSource(TransportInstrumentation.ActivitySourceName);

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
                    metrics.AddMeter(TransportInstrumentation.MeterName);

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
/// Configuration options for OpenTelemetry integration
/// </summary>
public class OpenTelemetryOptions
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
    public List<Action<TracerProviderBuilder>> TracingConfigurations { get; } = new();

    /// <summary>
    /// Additional metrics configurations (e.g., exporters, readers)
    /// </summary>
    public List<Action<MeterProviderBuilder>> MetricsConfigurations { get; } = new();

    /// <summary>
    /// Add a tracing exporter or configuration
    /// </summary>
    public OpenTelemetryOptions ConfigureTracing(Action<TracerProviderBuilder> configure)
    {
        TracingConfigurations.Add(configure);
        return this;
    }

    /// <summary>
    /// Add a metrics exporter or configuration
    /// </summary>
    public OpenTelemetryOptions ConfigureMetrics(Action<MeterProviderBuilder> configure)
    {
        MetricsConfigurations.Add(configure);
        return this;
    }
}
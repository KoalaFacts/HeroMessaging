using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Observability;
using HeroMessaging.Observability.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace HeroMessaging.Abstractions.Configuration;

/// <summary>
/// Extension methods for registering OpenTelemetry instrumentation
/// </summary>
// ReSharper disable once CheckNamespace
#pragma warning disable IDE0130 // Namespace does not match folder structure
public static class ExtensionsToIHeroMessagingBuilderForOpenTelemetry
{
    /// <summary>
    /// Add OpenTelemetry instrumentation to HeroMessaging
    /// </summary>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <param name="configure">Optional configuration action for OpenTelemetry</param>
    public static IHeroMessagingBuilder AddOpenTelemetry(
        this IHeroMessagingBuilder builder,
        Action<HeroMessaging.Observability.OpenTelemetry.OpenTelemetryInstrumentationOptions>? configure = null)
    {
        var services = builder.Build();

        var options = new HeroMessaging.Observability.OpenTelemetry.OpenTelemetryInstrumentationOptions();
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

using HeroMessaging.Abstractions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Configuration;

/// <summary>
/// Implementation of observability builder for configuring observability plugins
/// </summary>
public class ObservabilityBuilder : IObservabilityBuilder
{
    private readonly IServiceCollection _services;
    /// <summary>
    /// Initializes a new instance of the <see cref="ObservabilityBuilder"/> class.
    /// </summary>

    public ObservabilityBuilder(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }
    /// <summary>
    /// Executes add health checks.
    /// </summary>

    public IObservabilityBuilder AddHealthChecks(Action<object>? configure = null)
    {
        // Health checks would be registered by the plugin when available
        var options = new object();

        // If configure action is provided, invoke it immediately for consistency
        // with other builder methods, and store for later use by plugins
        configure?.Invoke(options);

        if (configure != null)
        {
            _services.Configure<HealthCheckOptions>(o =>
            {
                o.ConfigureAction = configure;
            });
        }

        return this;
    }
    /// <summary>
    /// Executes add open telemetry.
    /// </summary>

    public IObservabilityBuilder AddOpenTelemetry(Action<OpenTelemetryOptions>? configure = null)
    {
        var options = new OpenTelemetryOptions();
        configure?.Invoke(options);

        _services.AddSingleton(options);

        // When OpenTelemetry plugin is available, it would be registered here
        // This is a placeholder for the actual implementation
        _services.Configure<OpenTelemetryOptions>(o =>
        {
            o.ServiceName = options.ServiceName;
            o.OtlpEndpoint = options.OtlpEndpoint;
            o.EnableTracing = options.EnableTracing;
            o.EnableMetrics = options.EnableMetrics;
            o.EnableLogging = options.EnableLogging;
            o.ResourceAttributes = options.ResourceAttributes;
        });

        return this;
    }
    /// <summary>
    /// Executes add metrics.
    /// </summary>

    public IObservabilityBuilder AddMetrics(Action<MetricsOptions>? configure = null)
    {
        var options = new MetricsOptions();
        configure?.Invoke(options);

        _services.AddSingleton(options);

        // Configure metrics collection
        _services.Configure<MetricsOptions>(o =>
        {
            o.EnableHistograms = options.EnableHistograms;
            o.EnableCounters = options.EnableCounters;
            o.EnableGauges = options.EnableGauges;
            o.FlushInterval = options.FlushInterval;
            o.CustomMetrics = options.CustomMetrics;
        });
        return this;
    }
    /// <summary>
    /// Executes add tracing.
    /// </summary>

    public IObservabilityBuilder AddTracing(Action<TracingOptions>? configure = null)
    {
        var options = new TracingOptions();
        configure?.Invoke(options);

        _services.AddSingleton(options);

        // Configure distributed tracing
        _services.Configure<TracingOptions>(o =>
        {
            o.SamplingRate = options.SamplingRate;
            o.RecordExceptions = options.RecordExceptions;
            o.RecordEvents = options.RecordEvents;
            o.IgnoredOperations = options.IgnoredOperations;
        });
        return this;
    }
    /// <summary>
    /// Executes add logging enrichment.
    /// </summary>

    public IObservabilityBuilder AddLoggingEnrichment(Action<LoggingOptions>? configure = null)
    {
        var options = new LoggingOptions();
        configure?.Invoke(options);

        _services.AddSingleton(options);

        // Configure logging enrichment
        _services.Configure<LoggingOptions>(o =>
        {
            o.IncludeScopes = options.IncludeScopes;
            o.IncludeTraceContext = options.IncludeTraceContext;
            o.GlobalProperties = options.GlobalProperties;
        });

        return this;
    }
    /// <summary>
    /// Executes add custom provider.
    /// </summary>

    public IObservabilityBuilder AddCustomProvider<T>(Action<T>? configure = null) where T : class
    {
        var instance = Activator.CreateInstance<T>();
        configure?.Invoke(instance);

        _services.AddSingleton(instance);

        return this;
    }
    /// <summary>
    /// Executes enable performance counters.
    /// </summary>

    public IObservabilityBuilder EnablePerformanceCounters()
    {
        _services.Configure<ObservabilityOptions>(options =>
        {
            options.EnablePerformanceCounters = true;
        });
        return this;
    }
    /// <summary>
    /// Executes enable diagnostic listeners.
    /// </summary>

    public IObservabilityBuilder EnableDiagnosticListeners()
    {
        _services.Configure<ObservabilityOptions>(options =>
        {
            options.EnableDiagnosticListeners = true;
        });
        return this;
    }
    /// <summary>
    /// Executes with sampling rate.
    /// </summary>

    public IObservabilityBuilder WithSamplingRate(double rate)
    {
        if (rate < 0 || rate > 1)
            throw new ArgumentOutOfRangeException(nameof(rate), "Sampling rate must be between 0 and 1");

        _services.Configure<TracingOptions>(options =>
        {
            options.SamplingRate = rate;
        });
        return this;
    }
    /// <summary>
    /// Executes build.
    /// </summary>

    public IServiceCollection Build()
    {
        return _services;
    }
}
/// <summary>
/// Represents the observability options type.
/// </summary>

// Configuration option classes
public class ObservabilityOptions
{
    /// <summary>
    /// Gets or sets enable performance counters.
    /// </summary>
    public bool EnablePerformanceCounters { get; set; }
    /// <summary>
    /// Gets or sets enable diagnostic listeners.
    /// </summary>
    public bool EnableDiagnosticListeners { get; set; }
}
/// <summary>
/// Represents the health check options type.
/// </summary>

public class HealthCheckOptions
{
    /// <summary>
    /// Gets or sets the callback used to configure a registered health check.
    /// </summary>
    public Action<object>? ConfigureAction { get; set; }
}

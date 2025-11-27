using HeroMessaging.Abstractions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Configuration;

/// <summary>
/// Implementation of observability builder for configuring observability plugins
/// </summary>
public class ObservabilityBuilder : IObservabilityBuilder
{
    private readonly IServiceCollection _services;

    public ObservabilityBuilder(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

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

    public IObservabilityBuilder AddOpenTelemetry(Action<Abstractions.Configuration.OpenTelemetryOptions>? configure = null)
    {
        var options = new Abstractions.Configuration.OpenTelemetryOptions();
        configure?.Invoke(options);

        _services.AddSingleton(options);

        // When OpenTelemetry plugin is available, it would be registered here
        // This is a placeholder for the actual implementation
        _services.Configure<Abstractions.Configuration.OpenTelemetryOptions>(o =>
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

    public IObservabilityBuilder AddMetrics(Action<Abstractions.Configuration.MetricsOptions>? configure = null)
    {
        var options = new Abstractions.Configuration.MetricsOptions();
        configure?.Invoke(options);

        _services.AddSingleton(options);

        // Configure metrics collection
        _services.Configure<Abstractions.Configuration.MetricsOptions>(o =>
        {
            o.EnableHistograms = options.EnableHistograms;
            o.EnableCounters = options.EnableCounters;
            o.EnableGauges = options.EnableGauges;
            o.FlushInterval = options.FlushInterval;
            o.CustomMetrics = options.CustomMetrics;
        });
        return this;
    }

    public IObservabilityBuilder AddTracing(Action<Abstractions.Configuration.TracingOptions>? configure = null)
    {
        var options = new Abstractions.Configuration.TracingOptions();
        configure?.Invoke(options);

        _services.AddSingleton(options);

        // Configure distributed tracing
        _services.Configure<Abstractions.Configuration.TracingOptions>(o =>
        {
            o.SamplingRate = options.SamplingRate;
            o.RecordExceptions = options.RecordExceptions;
            o.RecordEvents = options.RecordEvents;
            o.IgnoredOperations = options.IgnoredOperations;
        });
        return this;
    }

    public IObservabilityBuilder AddLoggingEnrichment(Action<Abstractions.Configuration.LoggingOptions>? configure = null)
    {
        var options = new Abstractions.Configuration.LoggingOptions();
        configure?.Invoke(options);

        _services.AddSingleton(options);

        // Configure logging enrichment
        _services.Configure<Abstractions.Configuration.LoggingOptions>(o =>
        {
            o.IncludeScopes = options.IncludeScopes;
            o.IncludeTraceContext = options.IncludeTraceContext;
            o.GlobalProperties = options.GlobalProperties;
        });

        return this;
    }

    public IObservabilityBuilder AddCustomProvider<T>(Action<T>? configure = null) where T : class
    {
        var instance = Activator.CreateInstance<T>();
        configure?.Invoke(instance);

        _services.AddSingleton(instance);

        return this;
    }

    public IObservabilityBuilder EnablePerformanceCounters()
    {
        _services.Configure<ObservabilityOptions>(options =>
        {
            options.EnablePerformanceCounters = true;
        });
        return this;
    }

    public IObservabilityBuilder EnableDiagnosticListeners()
    {
        _services.Configure<ObservabilityOptions>(options =>
        {
            options.EnableDiagnosticListeners = true;
        });
        return this;
    }

    public IObservabilityBuilder WithSamplingRate(double rate)
    {
        if (rate < 0 || rate > 1)
            throw new ArgumentOutOfRangeException(nameof(rate), "Sampling rate must be between 0 and 1");

        _services.Configure<Abstractions.Configuration.TracingOptions>(options =>
        {
            options.SamplingRate = rate;
        });
        return this;
    }

    public IServiceCollection Build()
    {
        return _services;
    }
}

// Configuration option classes
public class ObservabilityOptions
{
    public bool EnablePerformanceCounters { get; set; }
    public bool EnableDiagnosticListeners { get; set; }
}

public class HealthCheckOptions
{
    public Action<object>? ConfigureAction { get; set; }
}
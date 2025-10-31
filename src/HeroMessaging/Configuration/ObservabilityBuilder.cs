using HeroMessaging.Abstractions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Configuration;

/// <summary>
/// Builder for configuring observability and monitoring features in HeroMessaging.
/// </summary>
/// <remarks>
/// This builder provides a fluent API for configuring:
/// - Health checks (component health monitoring)
/// - OpenTelemetry (distributed tracing, metrics, logging)
/// - Metrics collection (histograms, counters, gauges)
/// - Distributed tracing (sampling, context propagation)
/// - Logging enrichment (trace context, scopes)
/// - Performance counters
///
/// Observability is critical for production systems to understand behavior,
/// diagnose issues, and monitor performance.
///
/// Example:
/// <code>
/// var observabilityBuilder = new ObservabilityBuilder(services);
/// observabilityBuilder
///     .AddHealthChecks()
///     .AddOpenTelemetry(options =>
///     {
///         options.ServiceName = "MyService";
///         options.EnableTracing = true;
///     })
///     .AddMetrics()
///     .WithSamplingRate(0.1)
///     .Build();
/// </code>
/// </remarks>
public class ObservabilityBuilder : IObservabilityBuilder
{
    private readonly IServiceCollection _services;

    /// <summary>
    /// Initializes a new instance of the ObservabilityBuilder class.
    /// </summary>
    /// <param name="services">The service collection to register observability services with</param>
    /// <exception cref="ArgumentNullException">Thrown when services is null</exception>
    public ObservabilityBuilder(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Adds health checks for monitoring HeroMessaging component health.
    /// </summary>
    /// <param name="configure">Optional configuration action for health check options</param>
    /// <returns>The observability builder for method chaining</returns>
    /// <remarks>
    /// Health checks provide readiness and liveness probes for HeroMessaging components.
    ///
    /// Requirements:
    /// - HeroMessaging.Observability.HealthChecks package must be installed
    ///
    /// Example:
    /// <code>
    /// observabilityBuilder.AddHealthChecks(options =>
    /// {
    ///     options.Timeout = TimeSpan.FromSeconds(5);
    /// });
    /// </code>
    /// </remarks>
    public IObservabilityBuilder AddHealthChecks(Action<object>? configure = null)
    {
        // Health checks would be registered by the plugin when available

        // If configure action is provided, it would be passed to the health checks builder
        // when the actual health checks plugin is installed
        if (configure != null)
        {
            _services.Configure<HealthCheckOptions>(options =>
            {
                options.ConfigureAction = configure;
            });
        }

        return this;
    }

    /// <summary>
    /// Adds OpenTelemetry instrumentation for distributed tracing, metrics, and logging.
    /// </summary>
    /// <param name="configure">Optional configuration action for OpenTelemetry options</param>
    /// <returns>The observability builder for method chaining</returns>
    /// <remarks>
    /// OpenTelemetry provides vendor-neutral observability instrumentation.
    ///
    /// Automatically instruments:
    /// - Command and query processing
    /// - Event publishing and handling
    /// - Storage operations
    /// - Queue processing
    ///
    /// Requirements:
    /// - HeroMessaging.Observability.OpenTelemetry package must be installed
    ///
    /// Example:
    /// <code>
    /// observabilityBuilder.AddOpenTelemetry(options =>
    /// {
    ///     options.ServiceName = "OrderService";
    ///     options.OtlpEndpoint = "http://collector:4317";
    ///     options.EnableTracing = true;
    ///     options.EnableMetrics = true;
    /// });
    /// </code>
    /// </remarks>
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

    /// <summary>
    /// Adds metrics collection for monitoring HeroMessaging performance and behavior.
    /// </summary>
    /// <param name="configure">Optional configuration action for metrics options</param>
    /// <returns>The observability builder for method chaining</returns>
    /// <remarks>
    /// Metrics provide quantitative measurements of system behavior:
    /// - Histograms: Distribution of values (latencies, message sizes)
    /// - Counters: Cumulative totals (messages processed, errors)
    /// - Gauges: Point-in-time values (queue depth, active processors)
    ///
    /// Example:
    /// <code>
    /// observabilityBuilder.AddMetrics(options =>
    /// {
    ///     options.EnableHistograms = true;
    ///     options.EnableCounters = true;
    ///     options.FlushInterval = TimeSpan.FromSeconds(10);
    /// });
    /// </code>
    /// </remarks>
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

    /// <summary>
    /// Adds distributed tracing for tracking requests across service boundaries.
    /// </summary>
    /// <param name="configure">Optional configuration action for tracing options</param>
    /// <returns>The observability builder for method chaining</returns>
    /// <remarks>
    /// Distributed tracing tracks requests as they flow through multiple services,
    /// providing visibility into end-to-end latency and dependencies.
    ///
    /// Tracing captures:
    /// - Span context (trace ID, span ID, parent span)
    /// - Operation names and durations
    /// - Attributes (message types, handler names, etc.)
    /// - Events (errors, retries, etc.)
    ///
    /// Example:
    /// <code>
    /// observabilityBuilder.AddTracing(options =>
    /// {
    ///     options.SamplingRate = 0.1; // Sample 10% of traces
    ///     options.RecordExceptions = true;
    ///     options.RecordEvents = true;
    /// });
    /// </code>
    /// </remarks>
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

    /// <summary>
    /// Adds logging enrichment to include trace context and additional metadata in log messages.
    /// </summary>
    /// <param name="configure">Optional configuration action for logging options</param>
    /// <returns>The observability builder for method chaining</returns>
    /// <remarks>
    /// Logging enrichment adds contextual information to log messages:
    /// - Trace ID and span ID (correlate logs with traces)
    /// - Log scopes (structured logging context)
    /// - Global properties (service name, version, environment)
    ///
    /// This enables powerful log correlation and filtering in log aggregation systems.
    ///
    /// Example:
    /// <code>
    /// observabilityBuilder.AddLoggingEnrichment(options =>
    /// {
    ///     options.IncludeScopes = true;
    ///     options.IncludeTraceContext = true;
    ///     options.GlobalProperties = new Dictionary&lt;string, string&gt;
    ///     {
    ///         ["Environment"] = "Production",
    ///         ["Version"] = "1.0.0"
    ///     };
    /// });
    /// </code>
    /// </remarks>
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

    /// <summary>
    /// Adds a custom observability provider.
    /// </summary>
    /// <typeparam name="T">The custom provider type</typeparam>
    /// <param name="configure">Optional configuration action for the provider</param>
    /// <returns>The observability builder for method chaining</returns>
    /// <remarks>
    /// Use this to integrate custom observability providers such as:
    /// - Custom metric collectors
    /// - Proprietary monitoring solutions
    /// - Application-specific instrumentation
    ///
    /// Example:
    /// <code>
    /// observabilityBuilder.AddCustomProvider&lt;MyCustomMetricsProvider&gt;(provider =>
    /// {
    ///     provider.Endpoint = "http://metrics-server:9090";
    /// });
    /// </code>
    /// </remarks>
    public IObservabilityBuilder AddCustomProvider<T>(Action<T>? configure = null) where T : class
    {
        var instance = Activator.CreateInstance<T>();
        configure?.Invoke(instance);

        _services.AddSingleton(instance);

        return this;
    }

    /// <summary>
    /// Enables Windows performance counters for monitoring system-level metrics.
    /// </summary>
    /// <returns>The observability builder for method chaining</returns>
    /// <remarks>
    /// Performance counters provide low-level system metrics:
    /// - CPU usage
    /// - Memory allocation
    /// - Thread pool statistics
    /// - GC statistics
    ///
    /// Note: Only available on Windows. This is a no-op on other platforms.
    ///
    /// Example:
    /// <code>
    /// observabilityBuilder.EnablePerformanceCounters();
    /// </code>
    /// </remarks>
    public IObservabilityBuilder EnablePerformanceCounters()
    {
        _services.Configure<ObservabilityOptions>(options =>
        {
            options.EnablePerformanceCounters = true;
        });
        return this;
    }

    /// <summary>
    /// Enables .NET diagnostic listeners for advanced diagnostics.
    /// </summary>
    /// <returns>The observability builder for method chaining</returns>
    /// <remarks>
    /// Diagnostic listeners provide access to .NET runtime events:
    /// - HttpClient activity
    /// - Entity Framework queries
    /// - Custom DiagnosticSource events
    ///
    /// Example:
    /// <code>
    /// observabilityBuilder.EnableDiagnosticListeners();
    /// </code>
    /// </remarks>
    public IObservabilityBuilder EnableDiagnosticListeners()
    {
        _services.Configure<ObservabilityOptions>(options =>
        {
            options.EnableDiagnosticListeners = true;
        });
        return this;
    }

    /// <summary>
    /// Sets the sampling rate for distributed tracing.
    /// </summary>
    /// <param name="rate">Sampling rate between 0.0 (0%) and 1.0 (100%)</param>
    /// <returns>The observability builder for method chaining</returns>
    /// <remarks>
    /// Sampling reduces the overhead of tracing in high-throughput systems.
    ///
    /// Recommended sampling rates:
    /// - Development: 1.0 (100% - trace everything)
    /// - Low traffic: 0.5-1.0 (50-100%)
    /// - Medium traffic: 0.1-0.5 (10-50%)
    /// - High traffic: 0.01-0.1 (1-10%)
    ///
    /// Example:
    /// <code>
    /// observabilityBuilder.WithSamplingRate(0.1); // Sample 10% of traces
    /// </code>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when rate is not between 0 and 1</exception>
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

    /// <summary>
    /// Completes observability configuration and returns the service collection.
    /// </summary>
    /// <returns>The configured service collection</returns>
    /// <remarks>
    /// This method finalizes observability configuration.
    ///
    /// Example:
    /// <code>
    /// observabilityBuilder
    ///     .AddHealthChecks()
    ///     .AddOpenTelemetry()
    ///     .Build();
    /// </code>
    /// </remarks>
    public IServiceCollection Build()
    {
        return _services;
    }
}

// Configuration option classes

/// <summary>
/// Base configuration options for observability features.
/// </summary>
/// <remarks>
/// Controls platform-specific observability features such as Windows performance counters
/// and .NET diagnostic listeners.
/// </remarks>
public class ObservabilityOptions
{
    /// <summary>
    /// Gets or sets whether Windows performance counters are enabled.
    /// </summary>
    /// <remarks>
    /// Performance counters provide system-level metrics like CPU, memory, and thread pool usage.
    /// Only available on Windows; ignored on other platforms.
    /// Default is false.
    /// </remarks>
    public bool EnablePerformanceCounters { get; set; }

    /// <summary>
    /// Gets or sets whether .NET diagnostic listeners are enabled.
    /// </summary>
    /// <remarks>
    /// Diagnostic listeners provide access to .NET runtime events such as HttpClient activity,
    /// Entity Framework queries, and custom DiagnosticSource events.
    /// Default is false.
    /// </remarks>
    public bool EnableDiagnosticListeners { get; set; }
}

/// <summary>
/// Configuration options for health checks.
/// </summary>
/// <remarks>
/// Stores the configuration action to be applied when the health checks plugin is loaded.
/// </remarks>
public class HealthCheckOptions
{
    /// <summary>
    /// Gets or sets the configuration action to apply to health check settings.
    /// </summary>
    /// <remarks>
    /// This action is invoked when the health checks plugin is initialized,
    /// allowing dynamic configuration of health check behavior.
    /// </remarks>
    public Action<object>? ConfigureAction { get; set; }
}
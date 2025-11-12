using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HeroMessaging.Configuration.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ObservabilityBuilderTests
{
    [Fact]
    public void Constructor_WithNullServices_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new ObservabilityBuilder(null!));
        Assert.Equal("services", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithValidServices_CreatesBuilder()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var builder = new ObservabilityBuilder(services);

        // Assert
        Assert.NotNull(builder);
    }

    [Fact]
    public void AddHealthChecks_WithoutConfiguration_RegistersHealthChecks()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new ObservabilityBuilder(services);

        // Act
        var result = builder.AddHealthChecks();

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void AddHealthChecks_WithConfiguration_RegistersConfigureAction()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new ObservabilityBuilder(services);
        var configCalled = false;

        // Act
        builder.AddHealthChecks(options => { configCalled = true; });
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<IOptions<HealthCheckOptions>>();
        Assert.NotNull(options);
        Assert.NotNull(options.Value.ConfigureAction);
    }

    [Fact]
    public void AddOpenTelemetry_WithoutConfiguration_RegistersOpenTelemetry()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new ObservabilityBuilder(services);

        // Act
        var result = builder.AddOpenTelemetry();

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(OpenTelemetryOptions));
    }

    [Fact]
    public void AddOpenTelemetry_WithConfiguration_AppliesOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new ObservabilityBuilder(services);

        // Act
        builder.AddOpenTelemetry(options =>
        {
            options.ServiceName = "TestService";
            options.OtlpEndpoint = "http://localhost:4317";
            options.EnableTracing = true;
            options.EnableMetrics = true;
            options.EnableLogging = false;
            options.ResourceAttributes["env"] = "test";
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<IOptions<OpenTelemetryOptions>>();
        Assert.NotNull(options);
        Assert.Equal("TestService", options.Value.ServiceName);
        Assert.Equal("http://localhost:4317", options.Value.OtlpEndpoint);
        Assert.True(options.Value.EnableTracing);
        Assert.True(options.Value.EnableMetrics);
        Assert.False(options.Value.EnableLogging);
        Assert.Contains("env", options.Value.ResourceAttributes.Keys);
    }

    [Fact]
    public void AddMetrics_WithoutConfiguration_RegistersMetrics()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new ObservabilityBuilder(services);

        // Act
        var result = builder.AddMetrics();

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(MetricsOptions));
    }

    [Fact]
    public void AddMetrics_WithConfiguration_AppliesOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new ObservabilityBuilder(services);

        // Act
        builder.AddMetrics(options =>
        {
            options.EnableHistograms = true;
            options.EnableCounters = false;
            options.EnableGauges = true;
            options.FlushInterval = TimeSpan.FromSeconds(30);
            options.CustomMetrics = new[] { "test" };
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<IOptions<MetricsOptions>>();
        Assert.NotNull(options);
        Assert.True(options.Value.EnableHistograms);
        Assert.False(options.Value.EnableCounters);
        Assert.True(options.Value.EnableGauges);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Value.FlushInterval);
        Assert.Contains("test", options.Value.CustomMetrics);
    }

    [Fact]
    public void AddTracing_WithoutConfiguration_RegistersTracing()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new ObservabilityBuilder(services);

        // Act
        var result = builder.AddTracing();

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(TracingOptions));
    }

    [Fact]
    public void AddTracing_WithConfiguration_AppliesOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new ObservabilityBuilder(services);

        // Act
        builder.AddTracing(options =>
        {
            options.SamplingRate = 0.5;
            options.RecordExceptions = true;
            options.RecordEvents = false;
            options.IgnoredOperations = new[] { "health-check" };
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<IOptions<TracingOptions>>();
        Assert.NotNull(options);
        Assert.Equal(0.5, options.Value.SamplingRate);
        Assert.True(options.Value.RecordExceptions);
        Assert.False(options.Value.RecordEvents);
        Assert.Contains("health-check", options.Value.IgnoredOperations);
    }

    [Fact]
    public void AddLoggingEnrichment_WithoutConfiguration_RegistersLogging()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new ObservabilityBuilder(services);

        // Act
        var result = builder.AddLoggingEnrichment();

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(LoggingOptions));
    }

    [Fact]
    public void AddLoggingEnrichment_WithConfiguration_AppliesOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new ObservabilityBuilder(services);

        // Act
        builder.AddLoggingEnrichment(options =>
        {
            options.IncludeScopes = true;
            options.IncludeTraceContext = false;
            options.GlobalProperties["app"] = "test";
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<IOptions<LoggingOptions>>();
        Assert.NotNull(options);
        Assert.True(options.Value.IncludeScopes);
        Assert.False(options.Value.IncludeTraceContext);
        Assert.Contains("app", options.Value.GlobalProperties.Keys);
    }

    [Fact]
    public void AddCustomProvider_WithoutConfiguration_RegistersProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new ObservabilityBuilder(services);

        // Act
        var result = builder.AddCustomProvider<TestObservabilityProvider>();

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(TestObservabilityProvider));
    }

    [Fact]
    public void AddCustomProvider_WithConfiguration_AppliesConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new ObservabilityBuilder(services);

        // Act
        builder.AddCustomProvider<TestObservabilityProvider>(provider =>
        {
            provider.Name = "CustomProvider";
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var customProvider = provider.GetService<TestObservabilityProvider>();
        Assert.NotNull(customProvider);
        Assert.Equal("CustomProvider", customProvider.Name);
    }

    [Fact]
    public void EnablePerformanceCounters_EnablesOption()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new ObservabilityBuilder(services);

        // Act
        var result = builder.EnablePerformanceCounters();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        var options = provider.GetService<IOptions<ObservabilityOptions>>();
        Assert.NotNull(options);
        Assert.True(options.Value.EnablePerformanceCounters);
    }

    [Fact]
    public void EnableDiagnosticListeners_EnablesOption()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new ObservabilityBuilder(services);

        // Act
        var result = builder.EnableDiagnosticListeners();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        var options = provider.GetService<IOptions<ObservabilityOptions>>();
        Assert.NotNull(options);
        Assert.True(options.Value.EnableDiagnosticListeners);
    }

    [Fact]
    public void WithSamplingRate_ValidRate_SetsRate()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new ObservabilityBuilder(services);

        // Act
        var result = builder.WithSamplingRate(0.75);
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        var options = provider.GetService<IOptions<TracingOptions>>();
        Assert.NotNull(options);
        Assert.Equal(0.75, options.Value.SamplingRate);
    }

    [Fact]
    public void WithSamplingRate_NegativeRate_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new ObservabilityBuilder(services);

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithSamplingRate(-0.1));
        Assert.Equal("rate", ex.ParamName);
        Assert.Contains("Sampling rate must be between 0 and 1", ex.Message);
    }

    [Fact]
    public void WithSamplingRate_RateGreaterThanOne_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new ObservabilityBuilder(services);

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithSamplingRate(1.1));
        Assert.Equal("rate", ex.ParamName);
        Assert.Contains("Sampling rate must be between 0 and 1", ex.Message);
    }

    [Fact]
    public void WithSamplingRate_BoundaryValues_Accepted()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new ObservabilityBuilder(services);

        // Act & Assert - 0 should be accepted
        builder.WithSamplingRate(0);
        var provider1 = services.BuildServiceProvider();
        var options1 = provider1.GetService<IOptions<TracingOptions>>();
        Assert.NotNull(options1);
        Assert.Equal(0, options1.Value.SamplingRate);

        // Act & Assert - 1 should be accepted
        services = new ServiceCollection();
        builder = new ObservabilityBuilder(services);
        builder.WithSamplingRate(1);
        var provider2 = services.BuildServiceProvider();
        var options2 = provider2.GetService<IOptions<TracingOptions>>();
        Assert.NotNull(options2);
        Assert.Equal(1, options2.Value.SamplingRate);
    }

    [Fact]
    public void Build_ReturnsServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new ObservabilityBuilder(services);

        // Act
        var result = builder.Build();

        // Assert
        Assert.Same(services, result);
    }

    [Fact]
    public void ObservabilityOptions_DefaultValues_AreFalse()
    {
        // Act
        var options = new ObservabilityOptions();

        // Assert
        Assert.False(options.EnablePerformanceCounters);
        Assert.False(options.EnableDiagnosticListeners);
    }

    [Fact]
    public void HealthCheckOptions_ConfigureAction_IsNullByDefault()
    {
        // Act
        var options = new HealthCheckOptions();

        // Assert
        Assert.Null(options.ConfigureAction);
    }

    [Fact]
    public void FluentConfiguration_CanChainMultipleMethods()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new ObservabilityBuilder(services);

        // Act
        var result = builder
            .AddOpenTelemetry(o => o.ServiceName = "Test")
            .AddMetrics(o => o.EnableCounters = true)
            .AddTracing(o => o.SamplingRate = 0.5)
            .AddLoggingEnrichment(o => o.IncludeScopes = true)
            .EnablePerformanceCounters()
            .EnableDiagnosticListeners()
            .WithSamplingRate(0.8)
            .Build();

        // Assert
        Assert.Same(services, result);
    }

    [Fact]
    public void MultipleConfigurations_CanCoexist()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new ObservabilityBuilder(services);

        // Act
        builder
            .AddOpenTelemetry(o => o.ServiceName = "Service1")
            .AddMetrics(o => o.EnableCounters = true)
            .AddTracing(o => o.SamplingRate = 0.5)
            .AddLoggingEnrichment(o => o.IncludeScopes = true);
        var provider = services.BuildServiceProvider();

        // Assert
        var otelOptions = provider.GetService<IOptions<OpenTelemetryOptions>>();
        var metricsOptions = provider.GetService<IOptions<MetricsOptions>>();
        var tracingOptions = provider.GetService<IOptions<TracingOptions>>();
        var loggingOptions = provider.GetService<IOptions<LoggingOptions>>();

        Assert.NotNull(otelOptions);
        Assert.NotNull(metricsOptions);
        Assert.NotNull(tracingOptions);
        Assert.NotNull(loggingOptions);
    }

    // Test helper class
    public sealed class TestObservabilityProvider
    {
        public string Name { get; set; } = string.Empty;
    }
}

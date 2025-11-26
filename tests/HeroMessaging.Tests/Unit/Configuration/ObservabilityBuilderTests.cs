using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HeroMessaging.Tests.Unit.Configuration;

[Trait("Category", "Unit")]
public sealed class ObservabilityBuilderTests
{
    private readonly ServiceCollection _services;

    public ObservabilityBuilderTests()
    {
        _services = new ServiceCollection();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidServices_CreatesInstance()
    {
        // Arrange & Act
        var builder = new ObservabilityBuilder(_services);

        // Assert
        Assert.NotNull(builder);
    }

    [Fact]
    public void Constructor_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ObservabilityBuilder(null!));

        Assert.Equal("services", exception.ParamName);
    }

    #endregion

    #region AddHealthChecks Tests

    [Fact]
    public void AddHealthChecks_WithoutConfiguration_RegistersHealthChecks()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);

        // Act
        var result = builder.AddHealthChecks();

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void AddHealthChecks_WithConfiguration_AppliesConfiguration()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);
        var configureWasCalled = false;

        // Act
        builder.AddHealthChecks(options =>
        {
            configureWasCalled = true;
        });

        // Assert
        Assert.True(configureWasCalled);
    }

    [Fact]
    public void AddHealthChecks_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);

        // Act
        var result = builder.AddHealthChecks();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region AddOpenTelemetry Tests

    [Fact]
    public void AddOpenTelemetry_WithoutConfiguration_RegistersOpenTelemetry()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);

        // Act
        builder.AddOpenTelemetry();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(Abstractions.Configuration.OpenTelemetryOptions));
    }

    [Fact]
    public void AddOpenTelemetry_WithConfiguration_AppliesConfiguration()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);
        var configuredServiceName = string.Empty;

        // Act
        builder.AddOpenTelemetry(options =>
        {
            options.ServiceName = "TestService";
            configuredServiceName = options.ServiceName;
        });

        // Assert
        Assert.Equal("TestService", configuredServiceName);
    }

    [Fact]
    public void AddOpenTelemetry_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);

        // Act
        var result = builder.AddOpenTelemetry();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region AddMetrics Tests

    [Fact]
    public void AddMetrics_WithoutConfiguration_RegistersMetrics()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);

        // Act
        builder.AddMetrics();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(Abstractions.Configuration.MetricsOptions));
    }

    [Fact]
    public void AddMetrics_WithConfiguration_AppliesConfiguration()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);
        var histogramsEnabled = false;

        // Act
        builder.AddMetrics(options =>
        {
            options.EnableHistograms = true;
            histogramsEnabled = options.EnableHistograms;
        });

        // Assert
        Assert.True(histogramsEnabled);
    }

    [Fact]
    public void AddMetrics_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);

        // Act
        var result = builder.AddMetrics();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region AddTracing Tests

    [Fact]
    public void AddTracing_WithoutConfiguration_RegistersTracing()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);

        // Act
        builder.AddTracing();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(Abstractions.Configuration.TracingOptions));
    }

    [Fact]
    public void AddTracing_WithConfiguration_AppliesConfiguration()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);
        var samplingRate = 0.0;

        // Act
        builder.AddTracing(options =>
        {
            options.SamplingRate = 0.5;
            samplingRate = options.SamplingRate;
        });

        // Assert
        Assert.Equal(0.5, samplingRate);
    }

    [Fact]
    public void AddTracing_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);

        // Act
        var result = builder.AddTracing();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region AddLoggingEnrichment Tests

    [Fact]
    public void AddLoggingEnrichment_WithoutConfiguration_RegistersLogging()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);

        // Act
        builder.AddLoggingEnrichment();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(Abstractions.Configuration.LoggingOptions));
    }

    [Fact]
    public void AddLoggingEnrichment_WithConfiguration_AppliesConfiguration()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);
        var scopesIncluded = false;

        // Act
        builder.AddLoggingEnrichment(options =>
        {
            options.IncludeScopes = true;
            scopesIncluded = options.IncludeScopes;
        });

        // Assert
        Assert.True(scopesIncluded);
    }

    [Fact]
    public void AddLoggingEnrichment_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);

        // Act
        var result = builder.AddLoggingEnrichment();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region AddCustomProvider Tests

    [Fact]
    public void AddCustomProvider_WithoutConfiguration_RegistersProvider()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);

        // Act
        builder.AddCustomProvider<TestObservabilityProvider>();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(TestObservabilityProvider));
    }

    [Fact]
    public void AddCustomProvider_WithConfiguration_AppliesConfiguration()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);
        var configuredName = string.Empty;

        // Act
        builder.AddCustomProvider<TestObservabilityProvider>(provider =>
        {
            provider.Name = "CustomProvider";
            configuredName = provider.Name;
        });

        // Assert
        Assert.Equal("CustomProvider", configuredName);
    }

    [Fact]
    public void AddCustomProvider_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);

        // Act
        var result = builder.AddCustomProvider<TestObservabilityProvider>();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region EnablePerformanceCounters Tests

    [Fact]
    public void EnablePerformanceCounters_ConfiguresPerformanceCounters()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);
        builder.EnablePerformanceCounters();
        var provider = _services.BuildServiceProvider();

        // Act
        var options = provider.GetService<IOptions<ObservabilityOptions>>();

        // Assert
        Assert.NotNull(options);
    }

    [Fact]
    public void EnablePerformanceCounters_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);

        // Act
        var result = builder.EnablePerformanceCounters();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region EnableDiagnosticListeners Tests

    [Fact]
    public void EnableDiagnosticListeners_ConfiguresDiagnosticListeners()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);
        builder.EnableDiagnosticListeners();
        var provider = _services.BuildServiceProvider();

        // Act
        var options = provider.GetService<IOptions<ObservabilityOptions>>();

        // Assert
        Assert.NotNull(options);
    }

    [Fact]
    public void EnableDiagnosticListeners_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);

        // Act
        var result = builder.EnableDiagnosticListeners();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region WithSamplingRate Tests

    [Fact]
    public void WithSamplingRate_ValidRate_ConfiguresSamplingRate()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);
        builder.WithSamplingRate(0.75);
        var provider = _services.BuildServiceProvider();

        // Act
        var options = provider.GetService<IOptions<Abstractions.Configuration.TracingOptions>>();

        // Assert
        Assert.NotNull(options);
    }

    [Fact]
    public void WithSamplingRate_NegativeRate_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.WithSamplingRate(-0.1));

        Assert.Equal("rate", exception.ParamName);
        Assert.Contains("must be between 0 and 1", exception.Message);
    }

    [Fact]
    public void WithSamplingRate_RateGreaterThanOne_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.WithSamplingRate(1.1));

        Assert.Equal("rate", exception.ParamName);
        Assert.Contains("must be between 0 and 1", exception.Message);
    }

    [Fact]
    public void WithSamplingRate_ZeroRate_ConfiguresSamplingRate()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);

        // Act
        var result = builder.WithSamplingRate(0.0);

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void WithSamplingRate_OneRate_ConfiguresSamplingRate()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);

        // Act
        var result = builder.WithSamplingRate(1.0);

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void WithSamplingRate_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);

        // Act
        var result = builder.WithSamplingRate(0.5);

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region Build Tests

    [Fact]
    public void Build_ReturnsServiceCollection()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);

        // Act
        var result = builder.Build();

        // Assert
        Assert.Same(_services, result);
    }

    #endregion

    #region Configuration Classes Tests

    [Fact]
    public void ObservabilityOptions_HasDefaultValues()
    {
        // Arrange & Act
        var options = new ObservabilityOptions();

        // Assert
        Assert.False(options.EnablePerformanceCounters);
        Assert.False(options.EnableDiagnosticListeners);
    }

    [Fact]
    public void HealthCheckOptions_HasNullConfigureAction()
    {
        // Arrange & Act
        var options = new HealthCheckOptions();

        // Assert
        Assert.Null(options.ConfigureAction);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void FluentConfiguration_ChainsCorrectly()
    {
        // Arrange
        var builder = new ObservabilityBuilder(_services);

        // Act
        var result = builder
            .AddHealthChecks()
            .AddOpenTelemetry(options => options.ServiceName = "TestService")
            .AddMetrics(options => options.EnableHistograms = true)
            .AddTracing(options => options.SamplingRate = 0.5)
            .AddLoggingEnrichment(options => options.IncludeScopes = true)
            .EnablePerformanceCounters()
            .EnableDiagnosticListeners()
            .WithSamplingRate(0.75)
            .Build();

        // Assert
        Assert.Same(_services, result);
        Assert.Contains(_services, s => s.ServiceType == typeof(Abstractions.Configuration.OpenTelemetryOptions));
        Assert.Contains(_services, s => s.ServiceType == typeof(Abstractions.Configuration.MetricsOptions));
        Assert.Contains(_services, s => s.ServiceType == typeof(Abstractions.Configuration.TracingOptions));
        Assert.Contains(_services, s => s.ServiceType == typeof(Abstractions.Configuration.LoggingOptions));
    }

    #endregion

    #region Test Helper Classes

    public class TestObservabilityProvider
    {
        public string Name { get; set; } = string.Empty;
    }

    #endregion
}

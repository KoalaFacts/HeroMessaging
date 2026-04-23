using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Observability;
using HeroMessaging.Observability.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace HeroMessaging.Observability.OpenTelemetry.Tests.Unit;

/// <summary>
/// Unit tests for ServiceCollectionExtensions
/// Testing OpenTelemetry registration and configuration
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void AddOpenTelemetry_WithDefaultOptions_RegistersTransportInstrumentation()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IHeroMessagingBuilder>();
        mockBuilder.Setup(b => b.Build()).Returns(services);

        // Act
        mockBuilder.Object.AddOpenTelemetry((Action<OpenTelemetryInstrumentationOptions>?)null);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var instrumentation = serviceProvider.GetService<ITransportInstrumentation>();
        Assert.NotNull(instrumentation);
        Assert.IsType<OpenTelemetryTransportInstrumentation>(instrumentation);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddOpenTelemetry_WithNullConfigure_UsesDefaultOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IHeroMessagingBuilder>();
        mockBuilder.Setup(b => b.Build()).Returns(services);

        // Act
        var result = mockBuilder.Object.AddOpenTelemetry(configure: (Action<OpenTelemetryInstrumentationOptions>?)null);

        // Assert
        Assert.NotNull(result);
        var serviceProvider = services.BuildServiceProvider();
        var instrumentation = serviceProvider.GetService<ITransportInstrumentation>();
        Assert.NotNull(instrumentation);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddOpenTelemetry_WithCustomServiceName_ConfiguresOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IHeroMessagingBuilder>();
        mockBuilder.Setup(b => b.Build()).Returns(services);
        var customServiceName = "CustomService";

        // Act
        mockBuilder.Object.AddOpenTelemetry((Action<OpenTelemetryInstrumentationOptions>?)(options =>
        {
            options.ServiceName = customServiceName;
        }));

        // Assert - Options are applied during configuration
        // We verify the registration completed successfully
        var serviceProvider = services.BuildServiceProvider();
        Assert.NotNull(serviceProvider.GetService<ITransportInstrumentation>());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddOpenTelemetry_WithTracingDisabled_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IHeroMessagingBuilder>();
        mockBuilder.Setup(b => b.Build()).Returns(services);

        // Act
        var exception = Record.Exception(() =>
            mockBuilder.Object.AddOpenTelemetry((Action<OpenTelemetryInstrumentationOptions>?)(options =>
            {
                options.EnableTracing = false;
            })));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddOpenTelemetry_WithMetricsDisabled_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IHeroMessagingBuilder>();
        mockBuilder.Setup(b => b.Build()).Returns(services);

        // Act
        var exception = Record.Exception(() =>
            mockBuilder.Object.AddOpenTelemetry((Action<OpenTelemetryInstrumentationOptions>?)(options =>
            {
                options.EnableMetrics = false;
            })));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddOpenTelemetry_WithBothTracingAndMetricsDisabled_RegistersTransportInstrumentation()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IHeroMessagingBuilder>();
        mockBuilder.Setup(b => b.Build()).Returns(services);

        // Act
        mockBuilder.Object.AddOpenTelemetry((Action<OpenTelemetryInstrumentationOptions>?)(options =>
        {
            options.EnableTracing = false;
            options.EnableMetrics = false;
        }));

        // Assert - Transport instrumentation should still be registered
        var serviceProvider = services.BuildServiceProvider();
        var instrumentation = serviceProvider.GetService<ITransportInstrumentation>();
        Assert.NotNull(instrumentation);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddOpenTelemetry_ReturnsOriginalBuilder()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IHeroMessagingBuilder>();
        mockBuilder.Setup(b => b.Build()).Returns(services);

        // Act
        var result = mockBuilder.Object.AddOpenTelemetry((Action<OpenTelemetryInstrumentationOptions>?)null);

        // Assert
        Assert.Same(mockBuilder.Object, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OpenTelemetryOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new OpenTelemetryInstrumentationOptions();

        // Assert
        Assert.Equal("HeroMessaging", options.ServiceName);
        Assert.Null(options.ServiceNamespace);
        Assert.Equal("1.0.0", options.ServiceVersion);
        Assert.True(options.EnableTracing);
        Assert.True(options.EnableMetrics);
        Assert.Empty(options.TracingConfigurations);
        Assert.Empty(options.MetricsConfigurations);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OpenTelemetryOptions_CanModifyProperties()
    {
        // Arrange
        var options = new OpenTelemetryInstrumentationOptions
        {
            ServiceName = "CustomService",
            ServiceNamespace = "MyNamespace",
            ServiceVersion = "2.0.0",
            EnableTracing = false,
            EnableMetrics = false
        };

        // Assert
        Assert.Equal("CustomService", options.ServiceName);
        Assert.Equal("MyNamespace", options.ServiceNamespace);
        Assert.Equal("2.0.0", options.ServiceVersion);
        Assert.False(options.EnableTracing);
        Assert.False(options.EnableMetrics);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OpenTelemetryOptions_ConfigureTracing_AddsConfiguration()
    {
        // Arrange
        var options = new OpenTelemetryInstrumentationOptions();

        static void TracingConfig(TracerProviderBuilder builder) { }

        // Act
        var result = options.ConfigureTracing(TracingConfig);

        // Assert
        Assert.Same(options, result);
        Assert.Single(options.TracingConfigurations);
        Assert.Contains(TracingConfig, options.TracingConfigurations);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OpenTelemetryOptions_ConfigureMetrics_AddsConfiguration()
    {
        // Arrange
        var options = new OpenTelemetryInstrumentationOptions();

        static void MetricsConfig(MeterProviderBuilder builder) { }

        // Act
        var result = options.ConfigureMetrics(MetricsConfig);

        // Assert
        Assert.Same(options, result);
        Assert.Single(options.MetricsConfigurations);
        Assert.Contains(MetricsConfig, options.MetricsConfigurations);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OpenTelemetryOptions_ConfigureTracing_SupportsChaining()
    {
        // Arrange
        var options = new OpenTelemetryInstrumentationOptions();

        static void Config1(TracerProviderBuilder builder) { }
        static void Config2(TracerProviderBuilder builder) { }

        // Act
        options.ConfigureTracing(Config1)
               .ConfigureTracing(Config2);

        // Assert
        Assert.Equal(2, options.TracingConfigurations.Count);
        Assert.Contains(Config1, options.TracingConfigurations);
        Assert.Contains(Config2, options.TracingConfigurations);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OpenTelemetryOptions_ConfigureMetrics_SupportsChaining()
    {
        // Arrange
        var options = new OpenTelemetryInstrumentationOptions();

        static void Config1(MeterProviderBuilder builder) { }
        static void Config2(MeterProviderBuilder builder) { }

        // Act
        options.ConfigureMetrics(Config1)
               .ConfigureMetrics(Config2);

        // Assert
        Assert.Equal(2, options.MetricsConfigurations.Count);
        Assert.Contains(Config1, options.MetricsConfigurations);
        Assert.Contains(Config2, options.MetricsConfigurations);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OpenTelemetryOptions_CombinedConfiguration_WorksTogether()
    {
        // Arrange
        var options = new OpenTelemetryInstrumentationOptions();

        static void TracingConfig(TracerProviderBuilder builder) { }
        static void MetricsConfig(MeterProviderBuilder builder) { }

        // Act
        options.ConfigureTracing(TracingConfig)
               .ConfigureMetrics(MetricsConfig);

        // Assert
        Assert.Single(options.TracingConfigurations);
        Assert.Single(options.MetricsConfigurations);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddOpenTelemetry_WithServiceNamespace_ConfiguresOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IHeroMessagingBuilder>();
        mockBuilder.Setup(b => b.Build()).Returns(services);

        // Act
        mockBuilder.Object.AddOpenTelemetry(options =>
        {
            options.ServiceName = "TestService";
            options.ServiceNamespace = "TestNamespace";
            options.ServiceVersion = "1.2.3";
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        Assert.NotNull(serviceProvider.GetService<ITransportInstrumentation>());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddOpenTelemetry_RegistersSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IHeroMessagingBuilder>();
        mockBuilder.Setup(b => b.Build()).Returns(services);

        // Act
        mockBuilder.Object.AddOpenTelemetry((Action<OpenTelemetryInstrumentationOptions>?)null);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var instrumentation1 = serviceProvider.GetService<ITransportInstrumentation>();
        var instrumentation2 = serviceProvider.GetService<ITransportInstrumentation>();

        Assert.NotNull(instrumentation1);
        Assert.NotNull(instrumentation2);
        Assert.Same(instrumentation1, instrumentation2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddOpenTelemetry_MultipleCallsOnSameBuilder_DoesNotDuplicateRegistration()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockBuilder = new Mock<IHeroMessagingBuilder>();
        mockBuilder.Setup(b => b.Build()).Returns(services);

        // Act
        mockBuilder.Object.AddOpenTelemetry((Action<OpenTelemetryInstrumentationOptions>?)null);
        mockBuilder.Object.AddOpenTelemetry((Action<OpenTelemetryInstrumentationOptions>?)null); // Second call

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var instrumentations = serviceProvider.GetServices<ITransportInstrumentation>().ToList();

        // TryAddSingleton should prevent duplicates
        Assert.Single(instrumentations);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OpenTelemetryOptions_TracingConfigurationsProperty_IsNotNull()
    {
        // Arrange & Act
        var options = new OpenTelemetryInstrumentationOptions();

        // Assert
        Assert.NotNull(options.TracingConfigurations);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OpenTelemetryOptions_MetricsConfigurationsProperty_IsNotNull()
    {
        // Arrange & Act
        var options = new OpenTelemetryInstrumentationOptions();

        // Assert
        Assert.NotNull(options.MetricsConfigurations);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OpenTelemetryOptions_WithNullServiceName_AllowsNull()
    {
        // Arrange
        var options = new OpenTelemetryInstrumentationOptions
        {
            ServiceName = null!
        };

        // Assert - Should allow null (validation would happen at runtime during OpenTelemetry setup)
        Assert.Null(options.ServiceName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OpenTelemetryOptions_WithEmptyServiceName_AllowsEmpty()
    {
        // Arrange
        var options = new OpenTelemetryInstrumentationOptions
        {
            ServiceName = string.Empty
        };

        // Assert
        Assert.Equal(string.Empty, options.ServiceName);
    }
}

using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

[Trait("Category", "Unit")]
public sealed class PipelineConfigurationsTests
{
    private readonly IServiceProvider _serviceProvider;

    public PipelineConfigurationsTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessageProcessingPipeline();
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public void HighThroughput_WithNullServiceProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            PipelineConfigurations.HighThroughput(null!));
        Assert.Equal("serviceProvider", ex.ParamName);
    }

    [Fact]
    public void HighThroughput_WithValidServiceProvider_ReturnsConfiguredPipeline()
    {
        // Act
        var pipeline = PipelineConfigurations.HighThroughput(_serviceProvider);

        // Assert
        Assert.NotNull(pipeline);
        Assert.IsType<MessageProcessingPipelineBuilder>(pipeline);
    }

    [Fact]
    public void CriticalBusiness_WithNullServiceProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            PipelineConfigurations.CriticalBusiness(null!));
        Assert.Equal("serviceProvider", ex.ParamName);
    }

    [Fact]
    public void CriticalBusiness_WithValidServiceProvider_ReturnsConfiguredPipeline()
    {
        // Act
        var pipeline = PipelineConfigurations.CriticalBusiness(_serviceProvider);

        // Assert
        Assert.NotNull(pipeline);
        Assert.IsType<MessageProcessingPipelineBuilder>(pipeline);
    }

    [Fact]
    public void Development_WithNullServiceProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            PipelineConfigurations.Development(null!));
        Assert.Equal("serviceProvider", ex.ParamName);
    }

    [Fact]
    public void Development_WithValidServiceProvider_ReturnsConfiguredPipeline()
    {
        // Act
        var pipeline = PipelineConfigurations.Development(_serviceProvider);

        // Assert
        Assert.NotNull(pipeline);
        Assert.IsType<MessageProcessingPipelineBuilder>(pipeline);
    }

    [Fact]
    public void Integration_WithNullServiceProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            PipelineConfigurations.Integration(null!));
        Assert.Equal("serviceProvider", ex.ParamName);
    }

    [Fact]
    public void Integration_WithValidServiceProvider_ReturnsConfiguredPipeline()
    {
        // Act
        var pipeline = PipelineConfigurations.Integration(_serviceProvider);

        // Assert
        Assert.NotNull(pipeline);
        Assert.IsType<MessageProcessingPipelineBuilder>(pipeline);
    }

    [Fact]
    public void Minimal_WithNullServiceProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            PipelineConfigurations.Minimal(null!));
        Assert.Equal("serviceProvider", ex.ParamName);
    }

    [Fact]
    public void Minimal_WithValidServiceProvider_ReturnsConfiguredPipeline()
    {
        // Act
        var pipeline = PipelineConfigurations.Minimal(_serviceProvider);

        // Assert
        Assert.NotNull(pipeline);
        Assert.IsType<MessageProcessingPipelineBuilder>(pipeline);
    }
}

[Trait("Category", "Unit")]
public sealed class PipelineExtensionsTests
{
    [Fact]
    public void AddMessageProcessingPipeline_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(services.AddMessageProcessingPipeline);
        Assert.Equal("services", ex.ParamName);
    }

    [Fact]
    public void AddMessageProcessingPipeline_WithValidServices_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddMessageProcessingPipeline();

        // Assert
        Assert.Same(services, result);
        var serviceProvider = services.BuildServiceProvider();

        // Verify that MessageProcessingPipelineBuilder can be resolved
        var pipelineBuilder = serviceProvider.GetService<MessageProcessingPipelineBuilder>();
        Assert.NotNull(pipelineBuilder);
    }

    [Fact]
    public void AddMessageProcessingPipeline_ReturnsServiceCollectionForChaining()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddMessageProcessingPipeline();

        // Assert
        Assert.Same(services, result);
    }

    [Fact]
    public void UsePredefinedPipeline_WithHighThroughputProfile_ReturnsHighThroughputPipeline()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessageProcessingPipeline();
        var serviceProvider = services.BuildServiceProvider();
        var builder = new MessageProcessingPipelineBuilder(serviceProvider);

        // Act
        var result = builder.UsePredefinedPipeline(PipelineProfile.HighThroughput, serviceProvider);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<MessageProcessingPipelineBuilder>(result);
    }

    [Fact]
    public void UsePredefinedPipeline_WithCriticalBusinessProfile_ReturnsCriticalBusinessPipeline()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessageProcessingPipeline();
        var serviceProvider = services.BuildServiceProvider();
        var builder = new MessageProcessingPipelineBuilder(serviceProvider);

        // Act
        var result = builder.UsePredefinedPipeline(PipelineProfile.CriticalBusiness, serviceProvider);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<MessageProcessingPipelineBuilder>(result);
    }

    [Fact]
    public void UsePredefinedPipeline_WithDevelopmentProfile_ReturnsDevelopmentPipeline()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessageProcessingPipeline();
        var serviceProvider = services.BuildServiceProvider();
        var builder = new MessageProcessingPipelineBuilder(serviceProvider);

        // Act
        var result = builder.UsePredefinedPipeline(PipelineProfile.Development, serviceProvider);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<MessageProcessingPipelineBuilder>(result);
    }

    [Fact]
    public void UsePredefinedPipeline_WithIntegrationProfile_ReturnsIntegrationPipeline()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessageProcessingPipeline();
        var serviceProvider = services.BuildServiceProvider();
        var builder = new MessageProcessingPipelineBuilder(serviceProvider);

        // Act
        var result = builder.UsePredefinedPipeline(PipelineProfile.Integration, serviceProvider);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<MessageProcessingPipelineBuilder>(result);
    }

    [Fact]
    public void UsePredefinedPipeline_WithMinimalProfile_ReturnsMinimalPipeline()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessageProcessingPipeline();
        var serviceProvider = services.BuildServiceProvider();
        var builder = new MessageProcessingPipelineBuilder(serviceProvider);

        // Act
        var result = builder.UsePredefinedPipeline(PipelineProfile.Minimal, serviceProvider);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<MessageProcessingPipelineBuilder>(result);
    }

    [Fact]
    public void UsePredefinedPipeline_WithInvalidProfile_ReturnsOriginalBuilder()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessageProcessingPipeline();
        var serviceProvider = services.BuildServiceProvider();
        var builder = new MessageProcessingPipelineBuilder(serviceProvider);
        var invalidProfile = (PipelineProfile)999;

        // Act
        var result = builder.UsePredefinedPipeline(invalidProfile, serviceProvider);

        // Assert
        Assert.Same(builder, result);
    }
}

[Trait("Category", "Unit")]
public sealed class PipelineProfileTests
{
    [Fact]
    public void PipelineProfile_HasExpectedValues()
    {
        // Assert - Verify all enum values exist
        Assert.True(Enum.IsDefined(typeof(PipelineProfile), PipelineProfile.HighThroughput));
        Assert.True(Enum.IsDefined(typeof(PipelineProfile), PipelineProfile.CriticalBusiness));
        Assert.True(Enum.IsDefined(typeof(PipelineProfile), PipelineProfile.Development));
        Assert.True(Enum.IsDefined(typeof(PipelineProfile), PipelineProfile.Integration));
        Assert.True(Enum.IsDefined(typeof(PipelineProfile), PipelineProfile.Minimal));
    }

    [Fact]
    public void PipelineProfile_HasExactlyFiveValues()
    {
        // Assert
        var values = Enum.GetValues(typeof(PipelineProfile));
        Assert.Equal(5, values.Length);
    }
}

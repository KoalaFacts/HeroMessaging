using HeroMessaging.Abstractions.Configuration;
using Xunit;

namespace HeroMessaging.Observability.OpenTelemetry.Tests.Unit;

/// <summary>
/// Unit tests for the base <see cref="OpenTelemetryOptions"/> class
/// defined in HeroMessaging.Abstractions.
/// These tests cover properties NOT on OpenTelemetryInstrumentationOptions.
/// </summary>
[Trait("Category", "Unit")]
public class OpenTelemetryOptionsTests
{
    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new OpenTelemetryOptions();

        // Assert
        Assert.Equal("HeroMessaging", options.ServiceName);
        Assert.True(options.EnableTracing);
        Assert.True(options.EnableMetrics);
        Assert.False(options.EnableLogging);
        Assert.Null(options.OtlpEndpoint);
        Assert.NotNull(options.ResourceAttributes);
        Assert.Empty(options.ResourceAttributes);
    }

    [Fact]
    public void ServiceName_CanBeModified()
    {
        // Arrange
        var options = new OpenTelemetryOptions();

        // Act
        options.ServiceName = "CustomService";

        // Assert
        Assert.Equal("CustomService", options.ServiceName);
    }

    [Fact]
    public void EnableTracing_CanBeDisabled()
    {
        // Arrange
        var options = new OpenTelemetryOptions();

        // Act
        options.EnableTracing = false;

        // Assert
        Assert.False(options.EnableTracing);
    }

    [Fact]
    public void EnableMetrics_CanBeDisabled()
    {
        // Arrange
        var options = new OpenTelemetryOptions();

        // Act
        options.EnableMetrics = false;

        // Assert
        Assert.False(options.EnableMetrics);
    }

    [Fact]
    public void EnableLogging_CanBeEnabled()
    {
        // Arrange
        var options = new OpenTelemetryOptions();

        // Act
        options.EnableLogging = true;

        // Assert
        Assert.True(options.EnableLogging);
    }

    [Fact]
    public void OtlpEndpoint_CanBeSet()
    {
        // Arrange
        var options = new OpenTelemetryOptions();

        // Act
        options.OtlpEndpoint = "http://localhost:4317";

        // Assert
        Assert.Equal("http://localhost:4317", options.OtlpEndpoint);
    }

    [Fact]
    public void ResourceAttributes_CanAddEntries()
    {
        // Arrange
        var options = new OpenTelemetryOptions();

        // Act
        options.ResourceAttributes["environment"] = "production";
        options.ResourceAttributes["region"] = "us-east-1";

        // Assert
        Assert.Equal(2, options.ResourceAttributes.Count);
        Assert.Equal("production", options.ResourceAttributes["environment"]);
        Assert.Equal("us-east-1", options.ResourceAttributes["region"]);
    }

    [Fact]
    public void ResourceAttributes_CanBeReplaced()
    {
        // Arrange
        var options = new OpenTelemetryOptions();

        // Act
        options.ResourceAttributes = new Dictionary<string, string>
        {
            ["key1"] = "value1",
            ["key2"] = "value2"
        };

        // Assert
        Assert.Equal(2, options.ResourceAttributes.Count);
        Assert.Equal("value1", options.ResourceAttributes["key1"]);
    }

    [Fact]
    public void AllProperties_CanBeModifiedTogether()
    {
        // Arrange
        var options = new OpenTelemetryOptions();

        // Act
        options.ServiceName = "TestService";
        options.OtlpEndpoint = "http://collector:4317";
        options.EnableTracing = false;
        options.EnableMetrics = false;
        options.EnableLogging = true;
        options.ResourceAttributes["env"] = "test";

        // Assert
        Assert.Equal("TestService", options.ServiceName);
        Assert.Equal("http://collector:4317", options.OtlpEndpoint);
        Assert.False(options.EnableTracing);
        Assert.False(options.EnableMetrics);
        Assert.True(options.EnableLogging);
        Assert.Single(options.ResourceAttributes);
    }
}

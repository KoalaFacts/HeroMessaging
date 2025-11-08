using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace HeroMessaging.Observability.HealthChecks.Tests.Unit;

/// <summary>
/// Unit tests for CompositeHealthCheck
/// Testing aggregation of multiple health check results
/// </summary>
public class CompositeHealthCheckTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task CompositeHealthCheck_WithMultipleCheckNames_ReturnsHealthyWithCorrectData()
    {
        // Arrange
        var checkNames = new[] { "check1", "check2", "check3" };
        var healthCheck = new CompositeHealthCheck(checkNames);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("Composite check for 3 components", result.Description);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.ContainsKey("checks"));
        Assert.True(result.Data.ContainsKey("check_count"));
        Assert.Equal(3, result.Data["check_count"]);

        var returnedChecks = result.Data["checks"] as string[];
        Assert.NotNull(returnedChecks);
        Assert.Equal(checkNames, returnedChecks);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CompositeHealthCheck_WithSingleCheckName_ReturnsHealthyWithCorrectData()
    {
        // Arrange
        var checkNames = new[] { "single_check" };
        var healthCheck = new CompositeHealthCheck(checkNames);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("Composite check for 1 components", result.Description);
        Assert.NotNull(result.Data);
        Assert.Equal(1, result.Data["check_count"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CompositeHealthCheck_WithEmptyCheckNames_ReturnsHealthyWithZeroCount()
    {
        // Arrange
        var checkNames = Array.Empty<string>();
        var healthCheck = new CompositeHealthCheck(checkNames);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("Composite check for 0 components", result.Description);
        Assert.NotNull(result.Data);
        Assert.Equal(0, result.Data["check_count"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CompositeHealthCheck_WithNullCheckNames_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new CompositeHealthCheck(null!));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CompositeHealthCheck_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var checkNames = new[] { "check1", "check2" };
        var healthCheck = new CompositeHealthCheck(checkNames);
        using var cts = new CancellationTokenSource();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), cts.Token);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CompositeHealthCheck_WithDuplicateCheckNames_IncludesAllNames()
    {
        // Arrange
        var checkNames = new[] { "check1", "check1", "check2" };
        var healthCheck = new CompositeHealthCheck(checkNames);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(3, result.Data["check_count"]);

        var returnedChecks = result.Data["checks"] as string[];
        Assert.NotNull(returnedChecks);
        Assert.Equal(3, returnedChecks.Length);
    }
}

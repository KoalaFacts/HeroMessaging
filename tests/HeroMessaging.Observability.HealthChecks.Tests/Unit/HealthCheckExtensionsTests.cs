using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Observability.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Observability.HealthChecks.Tests.Unit;

/// <summary>
/// Unit tests for ServiceCollectionExtensions
/// Testing health check registration and configuration
/// </summary>
public class HealthCheckExtensionsTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddHeroMessagingHealthChecks_WithDefaultOptions_RegistersAllHealthChecks()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Mock.Of<IMessageStorage>());
        services.AddSingleton(Mock.Of<IOutboxStorage>());
        services.AddSingleton(Mock.Of<IInboxStorage>());
        services.AddSingleton(Mock.Of<IQueueStorage>());

        var healthChecksBuilder = services.AddHealthChecks();

        // Act
        healthChecksBuilder.AddHeroMessagingHealthChecks();

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();
        Assert.NotNull(healthCheckService);

        // Execute health checks to verify they're registered and work
        var result = await healthCheckService.CheckHealthAsync();
        Assert.NotNull(result);

        // Verify all expected health checks are present
        Assert.Contains(result.Entries, e => e.Key == "hero_messaging_message_storage");
        Assert.Contains(result.Entries, e => e.Key == "hero_messaging_outbox_storage");
        Assert.Contains(result.Entries, e => e.Key == "hero_messaging_inbox_storage");
        Assert.Contains(result.Entries, e => e.Key == "hero_messaging_queue_storage");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddHeroMessagingHealthChecks_WithStorageDisabled_DoesNotRegisterStorageChecks()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        var healthChecksBuilder = services.AddHealthChecks();

        // Act
        healthChecksBuilder.AddHeroMessagingHealthChecks(options =>
        {
            options.CheckStorage = false;
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        var result = await healthCheckService.CheckHealthAsync();

        // Verify no hero messaging health checks are registered
        Assert.DoesNotContain(result.Entries, e => e.Key.StartsWith("hero_messaging_"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddHeroMessagingHealthChecks_WithSelectiveChecks_RegistersOnlyEnabledChecks()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Mock.Of<IMessageStorage>());
        services.AddSingleton(Mock.Of<IOutboxStorage>());

        var healthChecksBuilder = services.AddHealthChecks();

        // Act
        healthChecksBuilder.AddHeroMessagingHealthChecks(options =>
        {
            options.CheckMessageStorage = true;
            options.CheckOutboxStorage = true;
            options.CheckInboxStorage = false;
            options.CheckQueueStorage = false;
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        var result = await healthCheckService.CheckHealthAsync();

        Assert.Contains(result.Entries, e => e.Key == "hero_messaging_message_storage");
        Assert.Contains(result.Entries, e => e.Key == "hero_messaging_outbox_storage");
        Assert.DoesNotContain(result.Entries, e => e.Key == "hero_messaging_inbox_storage");
        Assert.DoesNotContain(result.Entries, e => e.Key == "hero_messaging_queue_storage");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddHeroMessagingHealthChecks_WithCustomFailureStatus_RegistersSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Mock.Of<IMessageStorage>());

        var healthChecksBuilder = services.AddHealthChecks();

        // Act
        healthChecksBuilder.AddHeroMessagingHealthChecks(options =>
        {
            options.FailureStatus = HealthStatus.Degraded;
            options.CheckOutboxStorage = false;
            options.CheckInboxStorage = false;
            options.CheckQueueStorage = false;
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        var result = await healthCheckService.CheckHealthAsync();

        Assert.Contains(result.Entries, e => e.Key == "hero_messaging_message_storage");
        // Note: We can't easily test the FailureStatus property directly without causing a failure,
        // but we verify the registration succeeded
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddHeroMessagingHealthChecks_WithCustomTags_RegistersSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Mock.Of<IMessageStorage>());

        var healthChecksBuilder = services.AddHealthChecks();
        var customTags = new[] { "hero", "messaging", "storage" };

        // Act
        healthChecksBuilder.AddHeroMessagingHealthChecks(options =>
        {
            options.Tags = customTags;
            options.CheckOutboxStorage = false;
            options.CheckInboxStorage = false;
            options.CheckQueueStorage = false;
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        var result = await healthCheckService.CheckHealthAsync();

        Assert.Contains(result.Entries, e => e.Key == "hero_messaging_message_storage");
        // Note: We can't easily inspect tags after registration, but we verify the health check
        // was registered successfully with the custom tags option
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddHeroMessagingHealthChecks_WithoutStorageRegistered_UsesAlwaysHealthyCheck()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        // Note: Not registering any storage implementations

        var healthChecksBuilder = services.AddHealthChecks();

        // Act
        healthChecksBuilder.AddHeroMessagingHealthChecks();

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        var result = await healthCheckService.CheckHealthAsync();

        Assert.NotNull(result);
        Assert.Equal(HealthStatus.Healthy, result.Status);

        // Verify all checks report as healthy (using AlwaysHealthyCheck)
        Assert.All(result.Entries.Values, entry =>
        {
            Assert.Equal(HealthStatus.Healthy, entry.Status);
            Assert.Contains("not registered", entry.Description, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddCompositeHealthCheck_WithMultipleCheckNames_RegistersCompositeCheck()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var healthChecksBuilder = services.AddHealthChecks();
        var checkNames = new[] { "check1", "check2", "check3" };
        var compositeName = "composite_check";

        // Act
        healthChecksBuilder.AddCompositeHealthCheck(compositeName, checkNames);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        var result = await healthCheckService.CheckHealthAsync();

        Assert.NotNull(result);
        Assert.Contains(result.Entries, e => e.Key == compositeName);
        Assert.Equal(HealthStatus.Healthy, result.Entries[compositeName].Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddCompositeHealthCheck_ExecutesSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var healthChecksBuilder = services.AddHealthChecks();
        var checkNames = new[] { "check1", "check2" };

        // Act
        healthChecksBuilder.AddCompositeHealthCheck("composite", checkNames);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        var result = await healthCheckService.CheckHealthAsync();

        Assert.NotNull(result);
        Assert.Contains(result.Entries, e => e.Key == "composite");
        Assert.Equal(HealthStatus.Healthy, result.Entries["composite"].Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void HeroMessagingHealthCheckOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new HeroMessagingHealthCheckOptions();

        // Assert
        Assert.True(options.CheckStorage);
        Assert.True(options.CheckMessageStorage);
        Assert.True(options.CheckOutboxStorage);
        Assert.True(options.CheckInboxStorage);
        Assert.True(options.CheckQueueStorage);
        Assert.Equal(HealthStatus.Unhealthy, options.FailureStatus);
        Assert.Null(options.Tags);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void HeroMessagingHealthCheckOptions_CanBeModified()
    {
        // Arrange
        var options = new HeroMessagingHealthCheckOptions();

        // Act
        options.CheckStorage = false;
        options.CheckMessageStorage = false;
        options.CheckOutboxStorage = false;
        options.CheckInboxStorage = false;
        options.CheckQueueStorage = false;
        options.FailureStatus = HealthStatus.Degraded;
        options.Tags = new[] { "custom" };

        // Assert
        Assert.False(options.CheckStorage);
        Assert.False(options.CheckMessageStorage);
        Assert.False(options.CheckOutboxStorage);
        Assert.False(options.CheckInboxStorage);
        Assert.False(options.CheckQueueStorage);
        Assert.Equal(HealthStatus.Degraded, options.FailureStatus);
        Assert.Single(options.Tags);
        Assert.Equal("custom", options.Tags.First());
    }
}

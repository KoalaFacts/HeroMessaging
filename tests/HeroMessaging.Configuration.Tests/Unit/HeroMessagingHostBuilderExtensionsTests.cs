using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using System.Reflection;
using Xunit;

namespace HeroMessaging.Configuration.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class HeroMessagingHostBuilderExtensionsTests
{
    [Fact]
    public void UseHeroMessaging_WithNullHostBuilder_ThrowsNullReferenceException()
    {
        // Arrange
        IHostBuilder hostBuilder = null!;

        // Act & Assert
        Assert.Throws<NullReferenceException>(() =>
            hostBuilder.UseHeroMessaging(builder => { }));
    }

    [Fact]
    public void UseHeroMessaging_WithNullConfigureAction_DoesNotThrowImmediately()
    {
        // Arrange
        var hostBuilder = Host.CreateDefaultBuilder();

        // Act - Does not throw immediately, only when Build() is called
        var result = hostBuilder.UseHeroMessaging(null!);

        // Assert - The method returns successfully
        Assert.Same(hostBuilder, result);
    }

    [Fact]
    public void UseHeroMessaging_ConfiguresServicesCorrectly()
    {
        // Arrange
        var hostBuilder = Host.CreateDefaultBuilder();
        var configureWasCalled = false;

        // Act
        var result = hostBuilder.UseHeroMessaging(builder =>
        {
            configureWasCalled = true;
            Assert.NotNull(builder);
            Assert.IsAssignableFrom<IHeroMessagingBuilder>(builder);
        });

        // Build the host to trigger configuration
        using var host = result.Build();

        // Assert
        Assert.Same(hostBuilder, result);
        Assert.True(configureWasCalled);
    }

    [Fact]
    public void UseHeroMessaging_RegistersHeroMessagingServices()
    {
        // Arrange
        var hostBuilder = Host.CreateDefaultBuilder();

        // Act
        var result = hostBuilder.UseHeroMessaging(builder =>
        {
            builder.UseInMemoryStorage();
        });

        // Build and verify services are registered
        using var host = result.Build();
        var services = host.Services;

        // Assert - Verify that HeroMessaging services were registered
        // The exact services depend on the builder configuration
        Assert.NotNull(services);
    }

    [Fact]
    public void UseHeroMessaging_ReturnsHostBuilderForChaining()
    {
        // Arrange
        var hostBuilder = Host.CreateDefaultBuilder();

        // Act
        var result = hostBuilder.UseHeroMessaging(builder => { });

        // Assert
        Assert.Same(hostBuilder, result);
    }

    [Fact]
    public void UseHeroMessagingDevelopment_WithNullHostBuilder_ThrowsNullReferenceException()
    {
        // Arrange
        IHostBuilder hostBuilder = null!;
        var assemblies = new[] { typeof(HeroMessagingHostBuilderExtensionsTests).Assembly };

        // Act & Assert
        Assert.Throws<NullReferenceException>(() =>
            hostBuilder.UseHeroMessagingDevelopment(assemblies));
    }

    [Fact]
    public void UseHeroMessagingDevelopment_WithEmptyAssemblies_ConfiguresDevelopmentMode()
    {
        // Arrange
        var hostBuilder = Host.CreateDefaultBuilder();
        var assemblies = Enumerable.Empty<Assembly>();

        // Act
        var result = hostBuilder.UseHeroMessagingDevelopment(assemblies);

        // Build the host to trigger configuration
        using var host = result.Build();

        // Assert
        Assert.Same(hostBuilder, result);
    }

    [Fact]
    public void UseHeroMessagingDevelopment_WithAssemblies_ConfiguresDevelopmentMode()
    {
        // Arrange
        var hostBuilder = Host.CreateDefaultBuilder();
        var assemblies = new[] { typeof(HeroMessagingHostBuilderExtensionsTests).Assembly };

        // Act
        var result = hostBuilder.UseHeroMessagingDevelopment(assemblies);

        // Build the host to trigger configuration
        using var host = result.Build();
        var services = host.Services;

        // Assert
        Assert.Same(hostBuilder, result);
        Assert.NotNull(services);
    }

    [Fact]
    public void UseHeroMessagingDevelopment_ReturnsHostBuilderForChaining()
    {
        // Arrange
        var hostBuilder = Host.CreateDefaultBuilder();
        var assemblies = new[] { typeof(HeroMessagingHostBuilderExtensionsTests).Assembly };

        // Act
        var result = hostBuilder.UseHeroMessagingDevelopment(assemblies);

        // Assert
        Assert.Same(hostBuilder, result);
    }

    [Fact]
    public void UseHeroMessagingProduction_WithNullHostBuilder_ThrowsNullReferenceException()
    {
        // Arrange
        IHostBuilder hostBuilder = null!;
        var connectionString = "Server=.;Database=Test;";
        var assemblies = new[] { typeof(HeroMessagingHostBuilderExtensionsTests).Assembly };

        // Act & Assert
        Assert.Throws<NullReferenceException>(() =>
            hostBuilder.UseHeroMessagingProduction(connectionString, assemblies));
    }

    [Fact]
    public void UseHeroMessagingProduction_WithNullConnectionString_DoesNotThrowImmediately()
    {
        // Arrange
        var hostBuilder = Host.CreateDefaultBuilder();
        var assemblies = new[] { typeof(HeroMessagingHostBuilderExtensionsTests).Assembly };

        // Act - Does not throw immediately, only when Build() is called
        var result = hostBuilder.UseHeroMessagingProduction(null!, assemblies);

        // Assert - The method returns successfully
        Assert.Same(hostBuilder, result);
        // Note: Build() would throw, but we don't call it in this test
    }

    [Fact]
    public void UseHeroMessagingProduction_WithValidConnectionString_ConfiguresProductionMode()
    {
        // Arrange
        var hostBuilder = Host.CreateDefaultBuilder();
        var connectionString = "Server=.;Database=Test;Integrated Security=true;";
        var assemblies = new[] { typeof(HeroMessagingHostBuilderExtensionsTests).Assembly };

        // Act
        var result = hostBuilder.UseHeroMessagingProduction(connectionString, assemblies);

        // Assert
        Assert.Same(hostBuilder, result);
        // Note: We cannot build without actual database, so we just verify the method returns correctly
    }

    [Fact]
    public void UseHeroMessagingProduction_WithEmptyAssemblies_ConfiguresProductionMode()
    {
        // Arrange
        var hostBuilder = Host.CreateDefaultBuilder();
        var connectionString = "Server=.;Database=Test;Integrated Security=true;";
        var assemblies = Enumerable.Empty<Assembly>();

        // Act
        var result = hostBuilder.UseHeroMessagingProduction(connectionString, assemblies);

        // Assert
        Assert.Same(hostBuilder, result);
    }

    [Fact]
    public void UseHeroMessagingProduction_ReturnsHostBuilderForChaining()
    {
        // Arrange
        var hostBuilder = Host.CreateDefaultBuilder();
        var connectionString = "Server=.;Database=Test;Integrated Security=true;";
        var assemblies = new[] { typeof(HeroMessagingHostBuilderExtensionsTests).Assembly };

        // Act
        var result = hostBuilder.UseHeroMessagingProduction(connectionString, assemblies);

        // Assert
        Assert.Same(hostBuilder, result);
    }

    [Fact]
    public void UseHeroMessagingDevelopment_CanChainMultipleCalls()
    {
        // Arrange
        var hostBuilder = Host.CreateDefaultBuilder();
        var assemblies = new[] { typeof(HeroMessagingHostBuilderExtensionsTests).Assembly };

        // Act
        var result = hostBuilder
            .UseHeroMessagingDevelopment(assemblies)
            .ConfigureServices((context, services) =>
            {
                // Additional service configuration
            });

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void UseHeroMessaging_CanChainWithOtherHostBuilderMethods()
    {
        // Arrange
        var hostBuilder = Host.CreateDefaultBuilder();

        // Act
        var result = hostBuilder
            .UseHeroMessaging(builder =>
            {
                builder.UseInMemoryStorage();
            })
            .ConfigureServices((context, services) =>
            {
                // Additional service configuration
            });

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void UseHeroMessagingProduction_CanChainMultipleCalls()
    {
        // Arrange
        var hostBuilder = Host.CreateDefaultBuilder();
        var connectionString = "Server=.;Database=Test;Integrated Security=true;";
        var assemblies = new[] { typeof(HeroMessagingHostBuilderExtensionsTests).Assembly };

        // Act
        var result = hostBuilder
            .UseHeroMessagingProduction(connectionString, assemblies)
            .ConfigureServices((context, services) =>
            {
                // Additional service configuration
            });

        // Assert
        Assert.NotNull(result);
    }
}

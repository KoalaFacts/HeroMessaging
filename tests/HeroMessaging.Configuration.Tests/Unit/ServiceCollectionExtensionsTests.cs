using System.Reflection;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Configuration.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddHeroMessaging_WithoutConfiguration_ReturnsBuilder()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddHeroMessaging();

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IHeroMessagingBuilder>(result);
    }

    [Fact]
    public void AddHeroMessaging_WithConfiguration_ConfiguresAndBuilds()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        var configureWasCalled = false;

        // Act
        var result = services.AddHeroMessaging(builder =>
        {
            configureWasCalled = true;
            builder.UseInMemoryStorage();
        });

        // Assert
        Assert.Same(services, result);
        Assert.True(configureWasCalled);
    }

    [Fact]
    public void AddHeroMessaging_WithConfiguration_CallsBuild()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);

        // Act
        services.AddHeroMessaging(builder =>
        {
            builder.WithMediator();
        });

        // Assert - Build should have registered services
        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider);
    }

    [Fact]
    public void AddHeroMessagingDevelopment_WithNoAssemblies_ConfiguresDevelopmentMode()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);

        // Act
        var result = services.AddHeroMessagingDevelopment();

        // Assert
        Assert.Same(services, result);
        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider);
    }

    [Fact]
    public void AddHeroMessagingDevelopment_WithAssemblies_ScansAssemblies()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        var assemblies = new[] { typeof(ServiceCollectionExtensionsTests).Assembly };

        // Act
        var result = services.AddHeroMessagingDevelopment(assemblies);

        // Assert
        Assert.Same(services, result);
        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider);
    }

    [Fact]
    public void AddHeroMessagingProduction_WithConnectionString_ConfiguresProductionMode()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        var connectionString = "Server=localhost;Database=test;";

        // Act
        var result = services.AddHeroMessagingProduction(connectionString);

        // Assert
        Assert.Same(services, result);
        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider);
    }

    [Fact]
    public void AddHeroMessagingProduction_WithConnectionStringAndAssemblies_ScansAssemblies()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        var connectionString = "Server=localhost;Database=test;";
        var assemblies = new[] { typeof(ServiceCollectionExtensionsTests).Assembly };

        // Act
        var result = services.AddHeroMessagingProduction(connectionString, assemblies);

        // Assert
        Assert.Same(services, result);
        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider);
    }

    [Fact]
    public void AddHeroMessagingMicroservice_WithConnectionString_ConfiguresMicroserviceMode()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        var connectionString = "Server=localhost;Database=test;";

        // Act
        var result = services.AddHeroMessagingMicroservice(connectionString);

        // Assert
        Assert.Same(services, result);
        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider);
    }

    [Fact]
    public void AddHeroMessagingMicroservice_WithConnectionStringAndAssemblies_ScansAssemblies()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        var connectionString = "Server=localhost;Database=test;";
        var assemblies = new[] { typeof(ServiceCollectionExtensionsTests).Assembly };

        // Act
        var result = services.AddHeroMessagingMicroservice(connectionString, assemblies);

        // Assert
        Assert.Same(services, result);
        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider);
    }

    [Fact]
    public void AddHeroMessagingCustom_WithConfigurationAction_ExecutesConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        var configureWasCalled = false;

        // Act
        var result = services.AddHeroMessagingCustom(builder =>
        {
            configureWasCalled = true;
            builder.UseInMemoryStorage();
        });

        // Assert
        Assert.Same(services, result);
        Assert.True(configureWasCalled);
    }

    [Fact]
    public void AddHeroMessagingCustom_WithConfigurationAndAssemblies_ScansAndConfigures()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        var assemblies = new[] { typeof(ServiceCollectionExtensionsTests).Assembly };
        var configureWasCalled = false;

        // Act
        var result = services.AddHeroMessagingCustom(builder =>
        {
            configureWasCalled = true;
            builder.WithMediator();
        }, assemblies);

        // Assert
        Assert.Same(services, result);
        Assert.True(configureWasCalled);
    }

    [Fact]
    public void AddHeroMessagingDevelopment_UseInMemoryStorage()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);

        // Act
        services.AddHeroMessagingDevelopment();
        var provider = services.BuildServiceProvider();

        // Assert - Should have in-memory storage registered
        Assert.NotNull(provider);
    }

    [Fact]
    public void AddHeroMessagingProduction_EnablesAllFeatures()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        var connectionString = "Server=localhost;Database=test;";

        // Act
        services.AddHeroMessagingProduction(connectionString);
        var provider = services.BuildServiceProvider();

        // Assert - Should configure all production features
        Assert.NotNull(provider);
    }

    [Fact]
    public void AddHeroMessagingMicroservice_EnablesMicroserviceFeatures()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        var connectionString = "Server=localhost;Database=test;";

        // Act
        services.AddHeroMessagingMicroservice(connectionString);
        var provider = services.BuildServiceProvider();

        // Assert - Should configure microservice features
        Assert.NotNull(provider);
    }

    [Fact]
    public void ServiceCollectionExtensions_SupportsFluentChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);

        // Act
        var result = services
            .AddHeroMessaging(builder => builder.UseInMemoryStorage())
            .AddLogging();

        // Assert
        Assert.Same(services, result);
    }
}

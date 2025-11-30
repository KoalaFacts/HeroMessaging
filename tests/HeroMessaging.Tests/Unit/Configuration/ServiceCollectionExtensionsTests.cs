using System.Reflection;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Tests.Unit.Configuration;

[Trait("Category", "Unit")]
public class ServiceCollectionExtensionsTests
{
    #region AddHeroMessaging Tests

    [Fact]
    public void AddHeroMessaging_ReturnsHeroMessagingBuilder()
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
    public void AddHeroMessaging_WithConfigureAction_CallsConfigureAndBuilds()
    {
        // Arrange
        var services = new ServiceCollection();
        var configureCalled = false;

        // Act
        var result = services.AddHeroMessaging(builder =>
        {
            configureCalled = true;
            Assert.NotNull(builder);
        });

        // Assert
        Assert.NotNull(result);
        Assert.True(configureCalled);
    }

    [Fact]
    public void AddHeroMessaging_WithConfigureAction_ReturnsServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddHeroMessaging(builder => { });

        // Assert
        Assert.Same(services, result);
    }

    #endregion

    #region AddHeroMessagingDevelopment Tests

    [Fact]
    public void AddHeroMessagingDevelopment_WithAssemblies_ConfiguresDevelopmentMode()
    {
        // Arrange
        var services = new ServiceCollection();
        var assemblies = new[] { Assembly.GetExecutingAssembly() };

        // Act
        var result = services.AddHeroMessagingDevelopment(assemblies);

        // Assert
        Assert.NotNull(result);
        Assert.Same(services, result);
    }

    [Fact]
    public void AddHeroMessagingDevelopment_WithoutAssemblies_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddHeroMessagingDevelopment([]);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void AddHeroMessagingDevelopment_ConfiguresInMemoryStorage()
    {
        // Arrange
        var services = new ServiceCollection();
        var assemblies = new[] { Assembly.GetExecutingAssembly() };

        // Act
        services.AddHeroMessagingDevelopment(assemblies);

        // Assert
        // Verify services were registered (basic smoke test)
        Assert.True(services.Count > 0);
    }

    #endregion

    #region AddHeroMessagingProduction Tests

    [Fact]
    public void AddHeroMessagingProduction_WithConnectionString_ConfiguresProductionMode()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Server=localhost;Database=test;";
        var assemblies = new[] { Assembly.GetExecutingAssembly() };

        // Act
        var result = services.AddHeroMessagingProduction(connectionString, assemblies);

        // Assert
        Assert.NotNull(result);
        Assert.Same(services, result);
    }

    [Fact]
    public void AddHeroMessagingProduction_WithoutAssemblies_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Server=localhost;Database=test;";

        // Act
        var result = services.AddHeroMessagingProduction(connectionString, []);

        // Assert
        Assert.NotNull(result);
    }

    #endregion

    #region AddHeroMessagingMicroservice Tests

    [Fact]
    public void AddHeroMessagingMicroservice_WithConnectionString_ConfiguresMicroserviceMode()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Server=localhost;Database=test;";
        var assemblies = new[] { Assembly.GetExecutingAssembly() };

        // Act
        var result = services.AddHeroMessagingMicroservice(connectionString, assemblies);

        // Assert
        Assert.NotNull(result);
        Assert.Same(services, result);
    }

    [Fact]
    public void AddHeroMessagingMicroservice_WithoutAssemblies_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Server=localhost;Database=test;";

        // Act
        var result = services.AddHeroMessagingMicroservice(connectionString, []);

        // Assert
        Assert.NotNull(result);
    }

    #endregion

    #region AddHeroMessagingCustom Tests

    [Fact]
    public void AddHeroMessagingCustom_WithConfigureAction_CallsConfigure()
    {
        // Arrange
        var services = new ServiceCollection();
        var configureCalled = false;
        var assemblies = new[] { Assembly.GetExecutingAssembly() };

        // Act
        var result = services.AddHeroMessagingCustom(builder =>
        {
            configureCalled = true;
            Assert.NotNull(builder);
        }, assemblies);

        // Assert
        Assert.NotNull(result);
        Assert.True(configureCalled);
        Assert.Same(services, result);
    }

    [Fact]
    public void AddHeroMessagingCustom_WithAssemblies_ScansAssemblies()
    {
        // Arrange
        var services = new ServiceCollection();
        var assemblies = new[] { Assembly.GetExecutingAssembly() };

        // Act
        var result = services.AddHeroMessagingCustom(builder => { }, assemblies);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void AddHeroMessagingCustom_WithoutAssemblies_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddHeroMessagingCustom(builder => { }, []);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void AddHeroMessagingCustom_ConfigureAction_CanAccessBuilder()
    {
        // Arrange
        var services = new ServiceCollection();
        IHeroMessagingBuilder? capturedBuilder = null;

        // Act
        services.AddHeroMessagingCustom(builder =>
        {
            capturedBuilder = builder;
        }, []);

        // Assert
        Assert.NotNull(capturedBuilder);
    }

    #endregion

    #region Multiple Configurations Tests

    [Fact]
    public void AddHeroMessaging_MultipleConfigurations_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessaging(builder => { });
        services.AddHeroMessaging(builder => { });

        // Assert
        // Should not throw, but may have duplicate services
        Assert.True(services.Count > 0);
    }

    [Fact]
    public void AddHeroMessaging_ChainedConfigurations_AllExecute()
    {
        // Arrange
        var services = new ServiceCollection();
        var call1 = false;
        var call2 = false;

        // Act
        services.AddHeroMessaging(builder =>
        {
            call1 = true;
        });

        services.AddHeroMessaging(builder =>
        {
            call2 = true;
        });

        // Assert
        Assert.True(call1);
        Assert.True(call2);
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public void AddHeroMessaging_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        ServiceCollection? services = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            services!.AddHeroMessaging());
    }

    [Fact]
    public void AddHeroMessaging_WithEmptyServices_ReturnsBuilder()
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
    public void AddHeroMessagingDevelopment_WithNullAssemblies_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        IEnumerable<Assembly>? assemblies = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            services.AddHeroMessagingDevelopment(assemblies!));
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void AddHeroMessagingDevelopment_BuildServiceProvider_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeroMessagingDevelopment([]);

        // Act
        using var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    public void AddHeroMessaging_WithConfiguration_BuildServiceProvider_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeroMessaging(builder =>
        {
            builder.Development()
                   .UseInMemoryStorage();
        });

        // Act
        using var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    public void AddHeroMessagingCustom_WithComplexConfiguration_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddHeroMessagingCustom(builder =>
        {
            builder.Development()
                   .UseInMemoryStorage()
                   .WithErrorHandling();
        }, [Assembly.GetExecutingAssembly()]);

        using var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider);
    }

    #endregion

    #region Parameter Validation Tests

    [Fact]
    public void AddHeroMessaging_WithNullConfigureAction_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            services.AddHeroMessaging(null!));
    }

    [Fact]
    public void AddHeroMessagingCustom_WithNullConfigureAction_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            services.AddHeroMessagingCustom(null!, []));
    }

    #endregion

    #region Service Registration Tests

    [Fact]
    public void AddHeroMessaging_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessaging(builder =>
        {
            builder.Development()
                   .UseInMemoryStorage();
        });

        // Assert
        Assert.True(services.Count > 0, "Services should be registered");
    }

    [Fact]
    public void AddHeroMessagingDevelopment_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingDevelopment([]);

        // Assert
        Assert.True(services.Count > 0, "Development services should be registered");
    }

    [Fact]
    public void AddHeroMessagingProduction_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Server=localhost;Database=test;";

        // Act
        services.AddHeroMessagingProduction(connectionString, []);

        // Assert
        Assert.True(services.Count > 0, "Production services should be registered");
    }

    [Fact]
    public void AddHeroMessagingMicroservice_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Server=localhost;Database=test;";

        // Act
        services.AddHeroMessagingMicroservice(connectionString, []);

        // Assert
        Assert.True(services.Count > 0, "Microservice services should be registered");
    }

    #endregion
}

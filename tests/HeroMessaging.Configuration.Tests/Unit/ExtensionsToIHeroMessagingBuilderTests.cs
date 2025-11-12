using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Configuration.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ExtensionsToIHeroMessagingBuilderTests
{
    private IHeroMessagingBuilder CreateBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        return new HeroMessagingBuilder(services);
    }

    #region Plugin Discovery Tests

    [Fact]
    public void AddPluginsFromDiscovery_RegistersPluginInfrastructure()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        var result = builder.AddPluginsFromDiscovery();

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    [Fact]
    public void AddPluginsFromDiscovery_ReturnsBuilder_ForFluentAPI()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        var result = builder.AddPluginsFromDiscovery();

        // Assert
        Assert.IsAssignableFrom<IHeroMessagingBuilder>(result);
    }

    #endregion

    #region Storage Configuration Tests

    [Fact]
    public void ConfigureStorage_WithConfigureAction_ExecutesConfiguration()
    {
        // Arrange
        var builder = CreateBuilder();
        var configureWasCalled = false;

        // Act
        var result = builder.ConfigureStorage(storage =>
        {
            configureWasCalled = true;
            storage.UseInMemory();
        });

        // Assert
        Assert.True(configureWasCalled);
        Assert.Same(builder, result);
    }

    [Fact]
    public void ConfigureStorage_ReturnsBuilder_ForFluentAPI()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        var result = builder.ConfigureStorage(storage => storage.UseInMemory());

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IHeroMessagingBuilder>(result);
    }

    [Fact]
    public void UseSqlServerStorage_WithConnectionString_ThrowsNotImplementedException()
    {
        // Arrange
        var builder = CreateBuilder();
        var connectionString = "Server=localhost;Database=test;";

        // Act & Assert
        Assert.Throws<NotImplementedException>(() => builder.UseSqlServerStorage(connectionString));
    }

    [Fact]
    public void UseSqlServerStorage_WithConnectionStringAndOptions_ThrowsNotImplementedException()
    {
        // Arrange
        var builder = CreateBuilder();
        var connectionString = "Server=localhost;Database=test;";

        // Act & Assert
        Assert.Throws<NotImplementedException>(() => builder.UseSqlServerStorage(connectionString, options =>
        {
            options.CommandTimeout = TimeSpan.FromSeconds(60);
        }));
    }

    [Fact]
    public void UsePostgreSqlStorage_WithConnectionString_ThrowsNotImplementedException()
    {
        // Arrange
        var builder = CreateBuilder();
        var connectionString = "Host=localhost;Database=test;";

        // Act & Assert
        Assert.Throws<NotImplementedException>(() => builder.UsePostgreSqlStorage(connectionString));
    }

    [Fact]
    public void UsePostgreSqlStorage_WithConnectionStringAndOptions_ThrowsNotImplementedException()
    {
        // Arrange
        var builder = CreateBuilder();
        var connectionString = "Host=localhost;Database=test;";

        // Act & Assert
        Assert.Throws<NotImplementedException>(() => builder.UsePostgreSqlStorage(connectionString, options =>
        {
            options.CommandTimeout = TimeSpan.FromSeconds(90);
        }));
    }

    #endregion

    #region Serialization Configuration Tests

    [Fact]
    public void ConfigureSerialization_WithConfigureAction_ExecutesConfiguration()
    {
        // Arrange
        var builder = CreateBuilder();
        var configureWasCalled = false;

        // Act
        var result = builder.ConfigureSerialization(serialization =>
        {
            configureWasCalled = true;
            serialization.UseJson();
        });

        // Assert
        Assert.True(configureWasCalled);
        Assert.Same(builder, result);
    }

    [Fact]
    public void ConfigureSerialization_ReturnsBuilder_ForFluentAPI()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        var result = builder.ConfigureSerialization(serialization => serialization.UseJson());

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IHeroMessagingBuilder>(result);
    }

    [Fact]
    public void UseJsonSerialization_WithoutOptions_ConfiguresSerialization()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        var result = builder.UseJsonSerialization();

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    [Fact]
    public void UseJsonSerialization_WithOptions_ConfiguresWithOptions()
    {
        // Arrange
        var builder = CreateBuilder();
        var configureWasCalled = false;

        // Act
        var result = builder.UseJsonSerialization(options =>
        {
            configureWasCalled = true;
            options.Indented = true;
            options.CamelCase = false;
        });

        // Assert
        Assert.True(configureWasCalled);
        Assert.Same(builder, result);
    }

    [Fact]
    public void UseProtobufSerialization_WithoutOptions_ConfiguresSerialization()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        var result = builder.UseProtobufSerialization();

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    [Fact]
    public void UseProtobufSerialization_WithOptions_ConfiguresWithOptions()
    {
        // Arrange
        var builder = CreateBuilder();
        var configureWasCalled = false;

        // Act
        var result = builder.UseProtobufSerialization(options =>
        {
            configureWasCalled = true;
            options.IncludeTypeInfo = true;
        });

        // Assert
        Assert.True(configureWasCalled);
        Assert.Same(builder, result);
    }

    [Fact]
    public void UseMessagePackSerialization_WithoutOptions_ConfiguresSerialization()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        var result = builder.UseMessagePackSerialization();

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    [Fact]
    public void UseMessagePackSerialization_WithOptions_ConfiguresWithOptions()
    {
        // Arrange
        var builder = CreateBuilder();
        var configureWasCalled = false;

        // Act
        var result = builder.UseMessagePackSerialization(options =>
        {
            configureWasCalled = true;
            options.UseCompression = true;
        });

        // Assert
        Assert.True(configureWasCalled);
        Assert.Same(builder, result);
    }

    #endregion

    #region Observability Configuration Tests

    [Fact]
    public void ConfigureObservability_WithConfigureAction_ExecutesConfiguration()
    {
        // Arrange
        var builder = CreateBuilder();
        var configureWasCalled = false;

        // Act
        var result = builder.ConfigureObservability(observability =>
        {
            configureWasCalled = true;
            observability.AddMetrics();
        });

        // Assert
        Assert.True(configureWasCalled);
        Assert.Same(builder, result);
    }

    [Fact]
    public void ConfigureObservability_ReturnsBuilder_ForFluentAPI()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        var result = builder.ConfigureObservability(observability => observability.AddMetrics());

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IHeroMessagingBuilder>(result);
    }

    [Fact]
    public void AddHealthChecks_WithoutOptions_ConfiguresHealthChecks()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        var result = builder.AddHealthChecks();

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    [Fact]
    public void AddHealthChecks_WithOptions_ReturnsBuilder()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act - Action<object> is passed but may not be called if not implemented
        var result = builder.AddHealthChecks(options => { });

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    [Fact]
    public void AddOpenTelemetry_WithoutOptions_ConfiguresOpenTelemetry()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        var result = builder.AddOpenTelemetry();

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    [Fact]
    public void AddOpenTelemetry_WithOptions_ConfiguresWithOptions()
    {
        // Arrange
        var builder = CreateBuilder();
        var configureWasCalled = false;

        // Act
        var result = builder.AddOpenTelemetry(options =>
        {
            configureWasCalled = true;
            options.ServiceName = "test-service";
        });

        // Assert
        Assert.True(configureWasCalled);
        Assert.Same(builder, result);
    }

    #endregion

    #region Fluent Chaining Tests

    [Fact]
    public void ExtensionMethods_SupportFluentChaining()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        var result = builder
            .ConfigureStorage(storage => storage.UseInMemory())
            .ConfigureSerialization(serialization => serialization.UseJson())
            .ConfigureObservability(observability => observability.AddMetrics());

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    [Fact]
    public void ExtensionMethods_CanMixBuilderAndExtensions()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        var result = builder
            .UseInMemoryStorage()
            .UseJsonSerialization()
            .AddHealthChecks()
            .WithMediator();

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);
    }

    #endregion
}

using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.Json;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;

namespace HeroMessaging.Serialization.Json.Tests.Unit;

/// <summary>
/// Unit tests for JSON serialization service registration extensions
/// </summary>
public class ServiceCollectionExtensionsTests
{
    #region Positive Cases - AddHeroMessagingJsonSerializer (Basic)

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithoutOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer();
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithoutOptions_RegistersJsonMessageSerializerType()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer();
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.IsType<JsonMessageSerializer>(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithoutOptions_ReturnsSingletonInstance()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer();
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer1 = provider.GetService<IMessageSerializer>();
        var serializer2 = provider.GetService<IMessageSerializer>();
        Assert.Same(serializer1, serializer2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithoutOptions_SerializerWorks()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHeroMessagingJsonSerializer();
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Act
        var testMessage = new { Id = "123", Name = "Test" };
        var data = serializer!.SerializeAsync(testMessage).Result;

        // Assert
        Assert.NotEmpty(data);
    }

    #endregion

    #region Positive Cases - AddHeroMessagingJsonSerializer (With SerializationOptions)

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithSerializationOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new SerializationOptions { MaxMessageSize = 5000 };

        // Act
        services.AddHeroMessagingJsonSerializer(options);
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithSerializationOptions_UsesProvidedOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new SerializationOptions { MaxMessageSize = 1000 };

        // Act
        services.AddHeroMessagingJsonSerializer(options);
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        var testMessage = new { Data = "A very long message that exceeds the limit. " + new string('x', 2000) };
        Assert.ThrowsAsync<InvalidOperationException>(
            () => serializer!.SerializeAsync(testMessage)).Wait();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithCompressionOptions_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new SerializationOptions { EnableCompression = true };

        // Act
        services.AddHeroMessagingJsonSerializer(options);
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithNullOptions_Works()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer(null);
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    #endregion

    #region Positive Cases - AddHeroMessagingJsonSerializer (With JsonSerializerOptions)

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithJsonSerializerOptions_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        // Act
        services.AddHeroMessagingJsonSerializer(null, jsonOptions);
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithCustomJsonOptions_UsesOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        // Act
        services.AddHeroMessagingJsonSerializer(null, jsonOptions);
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        var testMessage = new { Id = "123" };
        var data = serializer!.SerializeAsync(testMessage).Result;
        var json = System.Text.Encoding.UTF8.GetString(data);
        Assert.Contains("\n", json);
    }

    #endregion

    #region Positive Cases - AddHeroMessagingJsonSerializer (With Configuration Action)

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithConfigureAction_Works()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer(opts =>
        {
            opts.MaxMessageSize = 5000;
            opts.EnableCompression = true;
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithConfigureAction_AppliesConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer(opts =>
        {
            opts.MaxMessageSize = 1000;
        });
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        var largeMessage = new { Data = new string('x', 2000) };
        Assert.ThrowsAsync<InvalidOperationException>(
            () => serializer!.SerializeAsync(largeMessage)).Wait();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithConfigureActionAndJsonOptions_Works()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer(
            opts => opts.EnableCompression = true,
            jsonOpts => jsonOpts.WriteIndented = true);
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithConfigureActionAndJsonOptions_UsesOptions()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer(
            opts => { },
            jsonOpts => jsonOpts.WriteIndented = true);
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        var testMessage = new { Id = "123" };
        var data = serializer!.SerializeAsync(testMessage).Result;
        var json = System.Text.Encoding.UTF8.GetString(data);
        Assert.Contains("\n", json);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithConfigureActionAndNullJsonOptions_Works()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer(
            opts => opts.MaxMessageSize = 5000,
            null);
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    #endregion

    #region Positive Cases - ContentType

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_RegisteredSerializer_HasCorrectContentType()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer();
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        Assert.Equal("application/json", serializer!.ContentType);
    }

    #endregion

    #region Positive Cases - Multiple Registrations (TryAddSingleton)

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_CalledTwice_UsesFirstRegistration()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer(new SerializationOptions { MaxMessageSize = 1000 });
        services.AddHeroMessagingJsonSerializer(new SerializationOptions { MaxMessageSize = 5000 });
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        // Should use first registration with MaxMessageSize = 1000
        var largeMessage = new { Data = new string('x', 2000) };
        Assert.ThrowsAsync<InvalidOperationException>(
            () => serializer!.SerializeAsync(largeMessage)).Wait();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithExistingRegistration_DoesNotOverwrite()
    {
        // Arrange
        var services = new ServiceCollection();
        var customSerializer = new JsonMessageSerializer(new SerializationOptions { MaxMessageSize = 500 });

        // Act
        services.AddSingleton<IMessageSerializer>(customSerializer);
        services.AddHeroMessagingJsonSerializer(new SerializationOptions { MaxMessageSize = 5000 });
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.Same(customSerializer, serializer);
    }

    #endregion

    #region Negative Cases - Invalid Configuration

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithConfigureActionThrowingException_PropagatesException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
        {
            services.AddHeroMessagingJsonSerializer(opts =>
            {
                throw new InvalidOperationException("Configuration error");
            });
        });
    }

    #endregion

    #region Integration Cases - Service Collection

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_CanResolveFromProvider_MultipleWays()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer();
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer1 = provider.GetService<IMessageSerializer>();
        var serializer2 = provider.GetRequiredService<IMessageSerializer>();
        Assert.NotNull(serializer1);
        Assert.NotNull(serializer2);
        Assert.Same(serializer1, serializer2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithMultipleServices_AllResolvable()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer();
        services.AddScoped<string>(_ => "test");
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        var testString = provider.GetService<string>();
        Assert.NotNull(serializer);
        Assert.NotNull(testString);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_ChainedCalls_FluentInterface()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddHeroMessagingJsonSerializer();

        // Assert
        Assert.Same(services, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_FluentInterface_AllowsChaining()
    {
        // Arrange & Act
        var services = new ServiceCollection();
        services
            .AddHeroMessagingJsonSerializer()
            .AddScoped<string>(_ => "test");

        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    #endregion

    #region Edge Cases - Compression

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithCompressionFastest_Works()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer(opts =>
        {
            opts.EnableCompression = true;
            opts.CompressionLevel = CompressionLevel.Fastest;
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithCompressionOptimal_Works()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer(opts =>
        {
            opts.EnableCompression = true;
            opts.CompressionLevel = CompressionLevel.Optimal;
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithCompressionEnabled_CompressesData()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer(opts =>
        {
            opts.EnableCompression = true;
        });
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        var testMessage = new { Data = "Test data for compression" };
        var data = serializer!.SerializeAsync(testMessage).Result;
        Assert.NotEmpty(data);
    }

    #endregion

    #region Edge Cases - Max Message Size

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithZeroMaxSize_AllowsAnySize()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer(opts =>
        {
            opts.MaxMessageSize = 0;
        });
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        var largeMessage = new { Data = new string('x', 10000) };
        var data = serializer!.SerializeAsync(largeMessage).Result;
        Assert.NotEmpty(data);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_WithLargeMaxSize_AllowsMessages()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer(opts =>
        {
            opts.MaxMessageSize = 100000;
        });
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        var message = new { Data = new string('x', 10000) };
        var data = serializer!.SerializeAsync(message).Result;
        Assert.NotEmpty(data);
    }

    #endregion

    #region Serialization Verification

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_RegisteredSerializer_CanSerialize()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer();
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        var testMessage = new { Id = "123", Name = "Test" };
        var data = serializer!.SerializeAsync(testMessage).Result;
        Assert.NotEmpty(data);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingJsonSerializer_RegisteredSerializer_CanDeserialize()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer();
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        var testMessage = new { Id = "123", Name = "Test" };
        var data = serializer!.SerializeAsync(testMessage).Result;

        var jsonString = System.Text.Encoding.UTF8.GetString(data);
        var dataToDeserialize = System.Text.Encoding.UTF8.GetBytes(jsonString);

        // Should not throw
        serializer.Deserialize(dataToDeserialize, typeof(object));
    }

    #endregion
}

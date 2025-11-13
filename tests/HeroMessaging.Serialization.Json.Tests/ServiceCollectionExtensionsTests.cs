using System;
using System.Linq;
using System.Text.Json;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Serialization.Json.Tests;

[Trait("Category", "Unit")]
public class ServiceCollectionExtensionsTests
{
    #region AddHeroMessagingJsonSerializer - Basic Tests

    [Fact]
    public void AddHeroMessagingJsonSerializer_WithoutParameters_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer();

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<JsonMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingJsonSerializer_WithNullOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer(options: null, jsonOptions: null);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<JsonMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingJsonSerializer_WithCustomOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new SerializationOptions
        {
            EnableCompression = true,
            MaxMessageSize = 10240
        };

        // Act
        services.AddHeroMessagingJsonSerializer(options);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<JsonMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingJsonSerializer_WithCustomJsonOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        // Act
        services.AddHeroMessagingJsonSerializer(jsonOptions: jsonOptions);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<JsonMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingJsonSerializer_WithBothOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new SerializationOptions
        {
            EnableCompression = true,
            CompressionLevel = CompressionLevel.Maximum
        };
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false
        };

        // Act
        services.AddHeroMessagingJsonSerializer(options, jsonOptions);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<JsonMessageSerializer>(serializer);
    }

    #endregion

    #region AddHeroMessagingJsonSerializer - Action Configuration Tests

    [Fact]
    public void AddHeroMessagingJsonSerializer_WithActionConfiguration_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer(options =>
        {
            options.EnableCompression = true;
            options.MaxMessageSize = 5120;
            options.CompressionLevel = CompressionLevel.Optimal;
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<JsonMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingJsonSerializer_WithBothActionConfigurations_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer(
            configureOptions: options =>
            {
                options.EnableCompression = false;
                options.MaxMessageSize = 2048;
            },
            configureJsonOptions: jsonOptions =>
            {
                jsonOptions.WriteIndented = true;
                jsonOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<JsonMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingJsonSerializer_WithOnlySerializationOptionsAction_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer(
            configureOptions: options =>
            {
                options.EnableCompression = true;
            },
            configureJsonOptions: null);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<JsonMessageSerializer>(serializer);
    }

    #endregion

    #region Service Registration Tests

    [Fact]
    public void AddHeroMessagingJsonSerializer_RegistersAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHeroMessagingJsonSerializer();

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var serializer1 = serviceProvider.GetService<IMessageSerializer>();
        var serializer2 = serviceProvider.GetService<IMessageSerializer>();

        // Assert
        Assert.Same(serializer1, serializer2);
    }

    [Fact]
    public void AddHeroMessagingJsonSerializer_UsesServiceDescriptorTryAdd()
    {
        // Arrange
        var services = new ServiceCollection();
        var existingSerializer = new JsonMessageSerializer();
        services.AddSingleton<IMessageSerializer>(existingSerializer);

        // Act
        services.AddHeroMessagingJsonSerializer();

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.Same(existingSerializer, serializer);
    }

    [Fact]
    public void AddHeroMessagingJsonSerializer_ReturnsServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddHeroMessagingJsonSerializer();

        // Assert
        Assert.Same(services, result);
    }

    #endregion

    #region Functional Tests

    [Fact]
    public void RegisteredSerializer_CanSerializeAndDeserialize()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHeroMessagingJsonSerializer();
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<IMessageSerializer>();
        var message = new TestMessage { Name = "Functional", Value = 123 };

        // Act
        var serialized = serializer.SerializeAsync(message).GetAwaiter().GetResult();
        var deserialized = serializer.DeserializeAsync<TestMessage>(serialized).GetAwaiter().GetResult();

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(message.Name, deserialized.Name);
        Assert.Equal(message.Value, deserialized.Value);
    }

    [Fact]
    public void RegisteredSerializer_WithCompression_CanSerializeAndDeserialize()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new SerializationOptions { EnableCompression = true };
        services.AddHeroMessagingJsonSerializer(options);
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<IMessageSerializer>();
        var message = new TestMessage { Name = "Compressed", Value = 456 };

        // Act
        var serialized = serializer.SerializeAsync(message).GetAwaiter().GetResult();
        var deserialized = serializer.DeserializeAsync<TestMessage>(serialized).GetAwaiter().GetResult();

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(message.Name, deserialized.Name);
        Assert.Equal(message.Value, deserialized.Value);
    }

    [Fact]
    public void RegisteredSerializer_HasCorrectContentType()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHeroMessagingJsonSerializer();
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<IMessageSerializer>();

        // Act
        var contentType = serializer.ContentType;

        // Assert
        Assert.Equal("application/json", contentType);
    }

    #endregion

    #region Multiple Registration Tests

    [Fact]
    public void AddHeroMessagingJsonSerializer_CalledMultipleTimes_DoesNotRegisterMultiple()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingJsonSerializer();
        services.AddHeroMessagingJsonSerializer();
        services.AddHeroMessagingJsonSerializer();

        // Assert
        var descriptors = services.Where(d => d.ServiceType == typeof(IMessageSerializer)).ToList();
        Assert.Single(descriptors);
    }

    #endregion

    #region Chaining Tests

    [Fact]
    public void AddHeroMessagingJsonSerializer_SupportsChaining()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services
            .AddHeroMessagingJsonSerializer()
            .AddLogging();

        // Assert
        Assert.NotNull(result);
        Assert.True(services.Count >= 2);
    }

    #endregion

    #region Test Models

    public class TestMessage
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    #endregion
}

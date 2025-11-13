using System;
using System.Linq;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.MessagePack;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Serialization.MessagePack.Tests;

[Trait("Category", "Unit")]
public class ServiceCollectionExtensionsTests
{
    #region Test Models

    public class TestMessage
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    [MessagePackObject]
    public class ContractTestMessage
    {
        [Key(0)]
        public string Name { get; set; } = string.Empty;

        [Key(1)]
        public int Value { get; set; }
    }

    #endregion

    #region AddHeroMessagingMessagePackSerializer - Basic Tests

    [Fact]
    public void AddHeroMessagingMessagePackSerializer_WithoutParameters_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer();

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<MessagePackMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingMessagePackSerializer_WithNullOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer(options: null, messagePackOptions: null);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<MessagePackMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingMessagePackSerializer_WithCustomOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new SerializationOptions
        {
            EnableCompression = true,
            MaxMessageSize = 10240
        };

        // Act
        services.AddHeroMessagingMessagePackSerializer(options);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<MessagePackMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingMessagePackSerializer_WithCustomMessagePackOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var messagePackOptions = MessagePackSerializerOptions.Standard
            .WithResolver(ContractlessStandardResolver.Instance);

        // Act
        services.AddHeroMessagingMessagePackSerializer(messagePackOptions: messagePackOptions);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<MessagePackMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingMessagePackSerializer_WithBothOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new SerializationOptions
        {
            EnableCompression = true,
            CompressionLevel = CompressionLevel.Maximum
        };
        var messagePackOptions = MessagePackSerializerOptions.Standard
            .WithResolver(ContractlessStandardResolver.Instance);

        // Act
        services.AddHeroMessagingMessagePackSerializer(options, messagePackOptions);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<MessagePackMessageSerializer>(serializer);
    }

    #endregion

    #region AddHeroMessagingMessagePackSerializer - Action Configuration Tests

    [Fact]
    public void AddHeroMessagingMessagePackSerializer_WithActionConfiguration_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer(options =>
        {
            options.EnableCompression = true;
            options.MaxMessageSize = 5120;
            options.CompressionLevel = CompressionLevel.Optimal;
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<MessagePackMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingMessagePackSerializer_WithBothActionConfigurations_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer(
            configureOptions: options =>
            {
                options.EnableCompression = false;
                options.MaxMessageSize = 2048;
            },
            configureMessagePackOptions: messagePackOptions =>
            {
                // Configuration happens but the method signature doesn't allow direct modification
                // This tests that the action is accepted
            });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<MessagePackMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingMessagePackSerializer_WithOnlySerializationOptionsAction_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer(
            configureOptions: options =>
            {
                options.EnableCompression = true;
            },
            configureMessagePackOptions: null);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<MessagePackMessageSerializer>(serializer);
    }

    #endregion

    #region AddHeroMessagingContractMessagePackSerializer Tests

    [Fact]
    public void AddHeroMessagingContractMessagePackSerializer_WithoutParameters_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingContractMessagePackSerializer();

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<ContractMessagePackSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingContractMessagePackSerializer_WithNullOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingContractMessagePackSerializer(options: null, messagePackOptions: null);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<ContractMessagePackSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingContractMessagePackSerializer_WithCustomOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new SerializationOptions
        {
            EnableCompression = true,
            MaxMessageSize = 8192
        };

        // Act
        services.AddHeroMessagingContractMessagePackSerializer(options);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<ContractMessagePackSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingContractMessagePackSerializer_WithCustomMessagePackOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var messagePackOptions = MessagePackSerializerOptions.Standard
            .WithResolver(StandardResolver.Instance);

        // Act
        services.AddHeroMessagingContractMessagePackSerializer(messagePackOptions: messagePackOptions);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<ContractMessagePackSerializer>(serializer);
    }

    #endregion

    #region Service Registration Tests

    [Fact]
    public void AddHeroMessagingMessagePackSerializer_RegistersAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHeroMessagingMessagePackSerializer();

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var serializer1 = serviceProvider.GetService<IMessageSerializer>();
        var serializer2 = serviceProvider.GetService<IMessageSerializer>();

        // Assert
        Assert.Same(serializer1, serializer2);
    }

    [Fact]
    public void AddHeroMessagingMessagePackSerializer_UsesServiceDescriptorTryAdd()
    {
        // Arrange
        var services = new ServiceCollection();
        var existingSerializer = new MessagePackMessageSerializer();
        services.AddSingleton<IMessageSerializer>(existingSerializer);

        // Act
        services.AddHeroMessagingMessagePackSerializer();

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.Same(existingSerializer, serializer);
    }

    [Fact]
    public void AddHeroMessagingContractMessagePackSerializer_RegistersAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHeroMessagingContractMessagePackSerializer();

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var serializer1 = serviceProvider.GetService<IMessageSerializer>();
        var serializer2 = serviceProvider.GetService<IMessageSerializer>();

        // Assert
        Assert.Same(serializer1, serializer2);
    }

    [Fact]
    public void AddHeroMessagingMessagePackSerializer_ReturnsServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddHeroMessagingMessagePackSerializer();

        // Assert
        Assert.Same(services, result);
    }

    [Fact]
    public void AddHeroMessagingContractMessagePackSerializer_ReturnsServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddHeroMessagingContractMessagePackSerializer();

        // Assert
        Assert.Same(services, result);
    }

    #endregion

    #region Functional Tests

    [Fact]
    public void RegisteredMessagePackSerializer_CanSerializeAndDeserialize()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHeroMessagingMessagePackSerializer();
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
    public void RegisteredContractSerializer_CanSerializeAndDeserialize()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHeroMessagingContractMessagePackSerializer();
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<IMessageSerializer>();
        var message = new ContractTestMessage { Name = "ContractFunctional", Value = 456 };

        // Act
        var serialized = serializer.SerializeAsync(message).GetAwaiter().GetResult();
        var deserialized = serializer.DeserializeAsync<ContractTestMessage>(serialized).GetAwaiter().GetResult();

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(message.Name, deserialized.Name);
        Assert.Equal(message.Value, deserialized.Value);
    }

    [Fact]
    public void RegisteredMessagePackSerializer_WithCompression_CanSerializeAndDeserialize()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new SerializationOptions { EnableCompression = true };
        services.AddHeroMessagingMessagePackSerializer(options);
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<IMessageSerializer>();
        var message = new TestMessage { Name = "Compressed", Value = 789 };

        // Act
        var serialized = serializer.SerializeAsync(message).GetAwaiter().GetResult();
        var deserialized = serializer.DeserializeAsync<TestMessage>(serialized).GetAwaiter().GetResult();

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(message.Name, deserialized.Name);
        Assert.Equal(message.Value, deserialized.Value);
    }

    [Fact]
    public void RegisteredMessagePackSerializer_HasCorrectContentType()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHeroMessagingMessagePackSerializer();
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<IMessageSerializer>();

        // Act
        var contentType = serializer.ContentType;

        // Assert
        Assert.Equal("application/x-msgpack", contentType);
    }

    [Fact]
    public void RegisteredContractSerializer_HasCorrectContentType()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHeroMessagingContractMessagePackSerializer();
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<IMessageSerializer>();

        // Act
        var contentType = serializer.ContentType;

        // Assert
        Assert.Equal("application/x-msgpack-contract", contentType);
    }

    #endregion

    #region Multiple Registration Tests

    [Fact]
    public void AddHeroMessagingMessagePackSerializer_CalledMultipleTimes_DoesNotRegisterMultiple()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer();
        services.AddHeroMessagingMessagePackSerializer();
        services.AddHeroMessagingMessagePackSerializer();

        // Assert
        var descriptors = services.Where(d => d.ServiceType == typeof(IMessageSerializer)).ToList();
        Assert.Single(descriptors);
    }

    [Fact]
    public void AddHeroMessagingContractMessagePackSerializer_CalledMultipleTimes_DoesNotRegisterMultiple()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingContractMessagePackSerializer();
        services.AddHeroMessagingContractMessagePackSerializer();
        services.AddHeroMessagingContractMessagePackSerializer();

        // Assert
        var descriptors = services.Where(d => d.ServiceType == typeof(IMessageSerializer)).ToList();
        Assert.Single(descriptors);
    }

    #endregion

    #region Chaining Tests

    [Fact]
    public void AddHeroMessagingMessagePackSerializer_SupportsChaining()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services
            .AddHeroMessagingMessagePackSerializer()
            .AddLogging();

        // Assert
        Assert.NotNull(result);
        Assert.True(services.Count >= 2);
    }

    [Fact]
    public void AddHeroMessagingContractMessagePackSerializer_SupportsChaining()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services
            .AddHeroMessagingContractMessagePackSerializer()
            .AddLogging();

        // Assert
        Assert.NotNull(result);
        Assert.True(services.Count >= 2);
    }

    #endregion

    #region Serializer Type Differentiation Tests

    [Fact]
    public void MessagePackSerializer_AndContractSerializer_AreDifferentTypes()
    {
        // Arrange
        var services1 = new ServiceCollection();
        services1.AddHeroMessagingMessagePackSerializer();
        var serviceProvider1 = services1.BuildServiceProvider();

        var services2 = new ServiceCollection();
        services2.AddHeroMessagingContractMessagePackSerializer();
        var serviceProvider2 = services2.BuildServiceProvider();

        // Act
        var serializer1 = serviceProvider1.GetRequiredService<IMessageSerializer>();
        var serializer2 = serviceProvider2.GetRequiredService<IMessageSerializer>();

        // Assert
        Assert.IsType<MessagePackMessageSerializer>(serializer1);
        Assert.IsType<ContractMessagePackSerializer>(serializer2);
        Assert.NotEqual(serializer1.ContentType, serializer2.ContentType);
    }

    #endregion
}

using System;
using System.Linq;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf;
using ProtoBuf.Meta;
using Xunit;

namespace HeroMessaging.Serialization.Protobuf.Tests;

[Trait("Category", "Unit")]
public class ServiceCollectionExtensionsTests
{
    #region Test Models

    [ProtoContract]
    public class TestMessage
    {
        [ProtoMember(1)]
        public string Name { get; set; } = string.Empty;

        [ProtoMember(2)]
        public int Value { get; set; }
    }

    [ProtoContract]
    public class TypedTestMessage
    {
        [ProtoMember(1)]
        public string Name { get; set; } = string.Empty;

        [ProtoMember(2)]
        public int Value { get; set; }
    }

    #endregion

    #region AddHeroMessagingProtobufSerializer - Basic Tests

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithoutParameters_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer();

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<ProtobufMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithNullOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer(options: null, typeModel: null);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<ProtobufMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithCustomOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new SerializationOptions
        {
            EnableCompression = true,
            MaxMessageSize = 10240
        };

        // Act
        services.AddHeroMessagingProtobufSerializer(options);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<ProtobufMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithCustomTypeModel_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var typeModel = RuntimeTypeModel.Create();

        // Act
        services.AddHeroMessagingProtobufSerializer(typeModel: typeModel);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<ProtobufMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithBothOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new SerializationOptions
        {
            EnableCompression = true,
            CompressionLevel = CompressionLevel.Maximum
        };
        var typeModel = RuntimeTypeModel.Create();

        // Act
        services.AddHeroMessagingProtobufSerializer(options, typeModel);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<ProtobufMessageSerializer>(serializer);
    }

    #endregion

    #region AddHeroMessagingProtobufSerializer - Action Configuration Tests

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithActionConfiguration_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer(options =>
        {
            options.EnableCompression = true;
            options.MaxMessageSize = 5120;
            options.CompressionLevel = CompressionLevel.Optimal;
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<ProtobufMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithBothActionConfigurations_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer(
            configureOptions: options =>
            {
                options.EnableCompression = false;
                options.MaxMessageSize = 2048;
            },
            configureTypeModel: typeModel =>
            {
                // Type model configuration
            });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<ProtobufMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithOnlySerializationOptionsAction_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer(
            configureOptions: options =>
            {
                options.EnableCompression = true;
            },
            configureTypeModel: null);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<ProtobufMessageSerializer>(serializer);
    }

    #endregion

    #region AddHeroMessagingTypedProtobufSerializer Tests

    [Fact]
    public void AddHeroMessagingTypedProtobufSerializer_WithoutParameters_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingTypedProtobufSerializer();

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<TypedProtobufMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingTypedProtobufSerializer_WithNullOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingTypedProtobufSerializer(options: null, typeModel: null);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<TypedProtobufMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingTypedProtobufSerializer_WithCustomOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new SerializationOptions
        {
            EnableCompression = true,
            MaxMessageSize = 8192,
            IncludeTypeInformation = true
        };

        // Act
        services.AddHeroMessagingTypedProtobufSerializer(options);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<TypedProtobufMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingTypedProtobufSerializer_WithCustomTypeModel_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var typeModel = RuntimeTypeModel.Create();

        // Act
        services.AddHeroMessagingTypedProtobufSerializer(typeModel: typeModel);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<TypedProtobufMessageSerializer>(serializer);
    }

    #endregion

    #region Service Registration Tests

    [Fact]
    public void AddHeroMessagingProtobufSerializer_RegistersAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHeroMessagingProtobufSerializer();

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var serializer1 = serviceProvider.GetService<IMessageSerializer>();
        var serializer2 = serviceProvider.GetService<IMessageSerializer>();

        // Assert
        Assert.Same(serializer1, serializer2);
    }

    [Fact]
    public void AddHeroMessagingProtobufSerializer_UsesServiceDescriptorTryAdd()
    {
        // Arrange
        var services = new ServiceCollection();
        var existingSerializer = new ProtobufMessageSerializer();
        services.AddSingleton<IMessageSerializer>(existingSerializer);

        // Act
        services.AddHeroMessagingProtobufSerializer();

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetService<IMessageSerializer>();
        Assert.Same(existingSerializer, serializer);
    }

    [Fact]
    public void AddHeroMessagingTypedProtobufSerializer_RegistersAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHeroMessagingTypedProtobufSerializer();

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var serializer1 = serviceProvider.GetService<IMessageSerializer>();
        var serializer2 = serviceProvider.GetService<IMessageSerializer>();

        // Assert
        Assert.Same(serializer1, serializer2);
    }

    [Fact]
    public void AddHeroMessagingProtobufSerializer_ReturnsServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddHeroMessagingProtobufSerializer();

        // Assert
        Assert.Same(services, result);
    }

    [Fact]
    public void AddHeroMessagingTypedProtobufSerializer_ReturnsServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddHeroMessagingTypedProtobufSerializer();

        // Assert
        Assert.Same(services, result);
    }

    #endregion

    #region Functional Tests

    [Fact]
    public void RegisteredProtobufSerializer_CanSerializeAndDeserialize()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHeroMessagingProtobufSerializer();
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
    public void RegisteredTypedProtobufSerializer_CanSerializeAndDeserialize()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHeroMessagingTypedProtobufSerializer();
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<IMessageSerializer>();
        var message = new TypedTestMessage { Name = "TypedFunctional", Value = 456 };

        // Act
        var serialized = serializer.SerializeAsync(message).GetAwaiter().GetResult();
        var deserialized = serializer.DeserializeAsync<TypedTestMessage>(serialized).GetAwaiter().GetResult();

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(message.Name, deserialized.Name);
        Assert.Equal(message.Value, deserialized.Value);
    }

    [Fact]
    public void RegisteredProtobufSerializer_WithCompression_CanSerializeAndDeserialize()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new SerializationOptions { EnableCompression = true };
        services.AddHeroMessagingProtobufSerializer(options);
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
    public void RegisteredProtobufSerializer_HasCorrectContentType()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHeroMessagingProtobufSerializer();
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<IMessageSerializer>();

        // Act
        var contentType = serializer.ContentType;

        // Assert
        Assert.Equal("application/x-protobuf", contentType);
    }

    [Fact]
    public void RegisteredTypedProtobufSerializer_HasCorrectContentType()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHeroMessagingTypedProtobufSerializer();
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<IMessageSerializer>();

        // Act
        var contentType = serializer.ContentType;

        // Assert
        Assert.Equal("application/x-protobuf-typed", contentType);
    }

    #endregion

    #region Multiple Registration Tests

    [Fact]
    public void AddHeroMessagingProtobufSerializer_CalledMultipleTimes_DoesNotRegisterMultiple()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer();
        services.AddHeroMessagingProtobufSerializer();
        services.AddHeroMessagingProtobufSerializer();

        // Assert
        var descriptors = services.Where(d => d.ServiceType == typeof(IMessageSerializer)).ToList();
        Assert.Single(descriptors);
    }

    [Fact]
    public void AddHeroMessagingTypedProtobufSerializer_CalledMultipleTimes_DoesNotRegisterMultiple()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingTypedProtobufSerializer();
        services.AddHeroMessagingTypedProtobufSerializer();
        services.AddHeroMessagingTypedProtobufSerializer();

        // Assert
        var descriptors = services.Where(d => d.ServiceType == typeof(IMessageSerializer)).ToList();
        Assert.Single(descriptors);
    }

    #endregion

    #region Chaining Tests

    [Fact]
    public void AddHeroMessagingProtobufSerializer_SupportsChaining()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services
            .AddHeroMessagingProtobufSerializer()
            .AddLogging();

        // Assert
        Assert.NotNull(result);
        Assert.True(services.Count >= 2);
    }

    [Fact]
    public void AddHeroMessagingTypedProtobufSerializer_SupportsChaining()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services
            .AddHeroMessagingTypedProtobufSerializer()
            .AddLogging();

        // Assert
        Assert.NotNull(result);
        Assert.True(services.Count >= 2);
    }

    #endregion

    #region Serializer Type Differentiation Tests

    [Fact]
    public void ProtobufSerializer_AndTypedSerializer_AreDifferentTypes()
    {
        // Arrange
        var services1 = new ServiceCollection();
        services1.AddHeroMessagingProtobufSerializer();
        var serviceProvider1 = services1.BuildServiceProvider();

        var services2 = new ServiceCollection();
        services2.AddHeroMessagingTypedProtobufSerializer();
        var serviceProvider2 = services2.BuildServiceProvider();

        // Act
        var serializer1 = serviceProvider1.GetRequiredService<IMessageSerializer>();
        var serializer2 = serviceProvider2.GetRequiredService<IMessageSerializer>();

        // Assert
        Assert.IsType<ProtobufMessageSerializer>(serializer1);
        Assert.IsType<TypedProtobufMessageSerializer>(serializer2);
        Assert.NotEqual(serializer1.ContentType, serializer2.ContentType);
    }

    #endregion

    #region Type Model Configuration Tests

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithTypeModelConfiguration_AppliesConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var configurationApplied = false;

        // Act
        services.AddHeroMessagingProtobufSerializer(
            configureOptions: options => { },
            configureTypeModel: typeModel =>
            {
                configurationApplied = true;
                Assert.NotNull(typeModel);
            });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<IMessageSerializer>();
        Assert.NotNull(serializer);
        Assert.True(configurationApplied);
    }

    #endregion
}

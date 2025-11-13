using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf;
using ProtoBuf.Meta;
using Xunit;

namespace HeroMessaging.Serialization.Protobuf.Tests.Unit;

/// <summary>
/// Unit tests for Protobuf serialization service registration extensions
/// </summary>
[Trait("Category", "Unit")]
public class ServiceCollectionExtensionsTests
{
    [ProtoContract]
    public class TestMessage
    {
        [ProtoMember(1)]
        public string Id { get; set; } = string.Empty;

        [ProtoMember(2)]
        public int Value { get; set; }
    }

    #region Positive Cases - AddHeroMessagingProtobufSerializer

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithoutOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer();
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithoutOptions_RegistersCorrectType()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer();
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.IsType<ProtobufMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithoutOptions_ReturnsSingletonInstance()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer();
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer1 = provider.GetService<IMessageSerializer>();
        var serializer2 = provider.GetService<IMessageSerializer>();
        Assert.Same(serializer1, serializer2);
    }

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithSerializationOptions_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new SerializationOptions { MaxMessageSize = 5000 };

        // Act
        services.AddHeroMessagingProtobufSerializer(options);
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithCompressionEnabled_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new SerializationOptions { EnableCompression = true };

        // Act
        services.AddHeroMessagingProtobufSerializer(options);
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithNullOptions_Works()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer(null);
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    public void AddHeroMessagingProtobufSerializer_ContentType_IsCorrect()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer();
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        Assert.Equal("application/x-protobuf", serializer!.ContentType);
    }

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithConfigureAction_Works()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer(opts =>
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
    public void AddHeroMessagingProtobufSerializer_FluentInterface_AllowsChaining()
    {
        // Arrange & Act
        var services = new ServiceCollection();
        var result = services.AddHeroMessagingProtobufSerializer();

        // Assert
        Assert.Same(services, result);
    }

    #endregion

    #region Positive Cases - AddHeroMessagingTypedProtobufSerializer

    [Fact]
    public void AddHeroMessagingTypedProtobufSerializer_WithoutOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingTypedProtobufSerializer();
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    public void AddHeroMessagingTypedProtobufSerializer_WithoutOptions_RegistersCorrectType()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingTypedProtobufSerializer();
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.IsType<TypedProtobufMessageSerializer>(serializer);
    }

    [Fact]
    public void AddHeroMessagingTypedProtobufSerializer_WithoutOptions_ReturnsSingletonInstance()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingTypedProtobufSerializer();
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer1 = provider.GetService<IMessageSerializer>();
        var serializer2 = provider.GetService<IMessageSerializer>();
        Assert.Same(serializer1, serializer2);
    }

    [Fact]
    public void AddHeroMessagingTypedProtobufSerializer_WithSerializationOptions_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new SerializationOptions { IncludeTypeInformation = true };

        // Act
        services.AddHeroMessagingTypedProtobufSerializer(options);
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    public void AddHeroMessagingTypedProtobufSerializer_ContentType_IsCorrect()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingTypedProtobufSerializer();
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        Assert.Equal("application/x-protobuf-typed", serializer!.ContentType);
    }

    #endregion

    #region Multiple Registrations

    [Fact]
    public void AddHeroMessagingProtobufSerializer_CalledTwice_UsesFirstRegistration()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer(new SerializationOptions { MaxMessageSize = 1000 });
        services.AddHeroMessagingProtobufSerializer(new SerializationOptions { MaxMessageSize = 5000 });
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        var largeMessage = new TestMessage { Id = new string('x', 2000), Value = 42 };
        Assert.ThrowsAsync<InvalidOperationException>(
            () => serializer!.SerializeAsync(largeMessage)).Wait();
    }

    [Fact]
    public void AddHeroMessagingTypedProtobufSerializer_CalledAfterRegular_UsesFirstRegistration()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer();
        services.AddHeroMessagingTypedProtobufSerializer();
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        Assert.IsType<ProtobufMessageSerializer>(serializer);
    }

    #endregion

    #region Integration Cases - Service Collection

    [Fact]
    public void AddHeroMessagingProtobufSerializer_CanResolveFromProvider_MultipleWays()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer();
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer1 = provider.GetService<IMessageSerializer>();
        var serializer2 = provider.GetRequiredService<IMessageSerializer>();
        Assert.NotNull(serializer1);
        Assert.NotNull(serializer2);
        Assert.Same(serializer1, serializer2);
    }

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithMultipleServices_AllResolvable()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer();
        services.AddScoped<string>(_ => "test");
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        var testString = provider.GetService<string>();
        Assert.NotNull(serializer);
        Assert.NotNull(testString);
    }

    [Fact]
    public void AddHeroMessagingProtobufSerializer_FluentInterfaceChaining_Works()
    {
        // Arrange & Act
        var services = new ServiceCollection();
        services
            .AddHeroMessagingProtobufSerializer()
            .AddScoped<string>(_ => "test");

        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    #endregion

    #region Serialization Verification

    [Fact]
    public void AddHeroMessagingProtobufSerializer_RegisteredSerializer_CanSerialize()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer();
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        var testMessage = new TestMessage { Id = "123", Value = 42 };
        var data = serializer!.SerializeAsync(testMessage).Result;
        Assert.NotEmpty(data);
    }

    [Fact]
    public void AddHeroMessagingTypedProtobufSerializer_RegisteredSerializer_CanSerialize()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingTypedProtobufSerializer();
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        var testMessage = new TestMessage { Id = "123", Value = 42 };
        var data = serializer!.SerializeAsync(testMessage).Result;
        Assert.NotEmpty(data);
    }

    [Fact]
    public void AddHeroMessagingProtobufSerializer_RegisteredSerializer_CanDeserialize()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer();
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        var testMessage = new TestMessage { Id = "123", Value = 42 };
        var data = serializer!.SerializeAsync(testMessage).Result;
        var result = serializer.DeserializeAsync<TestMessage>(data).Result;
        Assert.NotNull(result);
        Assert.Equal("123", result.Id);
    }

    #endregion

    #region Compression Configuration

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithCompressionFastest_Works()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer(opts =>
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
    public void AddHeroMessagingProtobufSerializer_WithCompressionOptimal_Works()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer(opts =>
        {
            opts.EnableCompression = true;
            opts.CompressionLevel = CompressionLevel.Optimal;
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    #endregion

    #region Type Model Configuration

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithTypeModelConfiguration_Works()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer(
            opts => { },
            typeModel => { /* Configure type model */ });
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithNullTypeModel_Works()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer(
            opts => { },
            null);
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    #endregion

    #region Max Message Size Configuration

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithZeroMaxSize_AllowsAnySize()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer(opts =>
        {
            opts.MaxMessageSize = 0;
        });
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        var largeMessage = new TestMessage { Id = new string('x', 10000), Value = 42 };
        var data = serializer!.SerializeAsync(largeMessage).Result;
        Assert.NotEmpty(data);
    }

    [Fact]
    public void AddHeroMessagingProtobufSerializer_WithLargeMaxSize_AllowsMessages()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingProtobufSerializer(opts =>
        {
            opts.MaxMessageSize = 100000;
        });
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        var message = new TestMessage { Id = new string('x', 5000), Value = 42 };
        var data = serializer!.SerializeAsync(message).Result;
        Assert.NotEmpty(data);
    }

    #endregion
}

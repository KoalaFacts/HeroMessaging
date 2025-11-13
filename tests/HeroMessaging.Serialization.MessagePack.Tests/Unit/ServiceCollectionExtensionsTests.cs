using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.MessagePack;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Serialization.MessagePack.Tests.Unit;

/// <summary>
/// Unit tests for MessagePack serialization service registration extensions
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [MessagePackObject]
    public class TestMessage
    {
        [Key(0)]
        public string Id { get; set; } = string.Empty;

        [Key(1)]
        public int Value { get; set; }
    }

    #region Positive Cases - AddHeroMessagingMessagePackSerializer

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingMessagePackSerializer_WithoutOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer();
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingMessagePackSerializer_WithoutOptions_RegistersCorrectType()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer();
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.IsType<MessagePackMessageSerializer>(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingMessagePackSerializer_WithoutOptions_ReturnsSingletonInstance()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer();
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer1 = provider.GetService<IMessageSerializer>();
        var serializer2 = provider.GetService<IMessageSerializer>();
        Assert.Same(serializer1, serializer2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingMessagePackSerializer_WithSerializationOptions_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new SerializationOptions { MaxMessageSize = 5000 };

        // Act
        services.AddHeroMessagingMessagePackSerializer(options);
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingMessagePackSerializer_WithCompressionEnabled_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new SerializationOptions { EnableCompression = true };

        // Act
        services.AddHeroMessagingMessagePackSerializer(options);
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingMessagePackSerializer_WithNullOptions_Works()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer(null);
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingMessagePackSerializer_ContentType_IsCorrect()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer();
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        Assert.Equal("application/x-msgpack", serializer!.ContentType);
    }

    #endregion

    #region Positive Cases - AddHeroMessagingMessagePackSerializer (Configuration Action)

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingMessagePackSerializer_WithConfigureAction_Works()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer(opts =>
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
    public void AddHeroMessagingMessagePackSerializer_WithConfigureActionAndMessagePackOptions_Works()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer(
            opts => opts.EnableCompression = true,
            msgPackOpts => msgPackOpts);
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingMessagePackSerializer_FluentInterface_AllowsChaining()
    {
        // Arrange & Act
        var services = new ServiceCollection();
        var result = services.AddHeroMessagingMessagePackSerializer();

        // Assert
        Assert.Same(services, result);
    }

    #endregion

    #region Positive Cases - AddHeroMessagingContractMessagePackSerializer

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingContractMessagePackSerializer_WithoutOptions_RegistersSerializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingContractMessagePackSerializer();
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingContractMessagePackSerializer_WithoutOptions_RegistersCorrectType()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingContractMessagePackSerializer();
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.IsType<ContractMessagePackSerializer>(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingContractMessagePackSerializer_WithoutOptions_ReturnsSingletonInstance()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingContractMessagePackSerializer();
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer1 = provider.GetService<IMessageSerializer>();
        var serializer2 = provider.GetService<IMessageSerializer>();
        Assert.Same(serializer1, serializer2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingContractMessagePackSerializer_WithSerializationOptions_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new SerializationOptions { MaxMessageSize = 5000 };

        // Act
        services.AddHeroMessagingContractMessagePackSerializer(options);
        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingContractMessagePackSerializer_ContentType_IsCorrect()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingContractMessagePackSerializer();
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        Assert.Equal("application/x-msgpack-contract", serializer!.ContentType);
    }

    #endregion

    #region Multiple Registrations

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingMessagePackSerializer_CalledTwice_UsesFirstRegistration()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer(new SerializationOptions { MaxMessageSize = 1000 });
        services.AddHeroMessagingMessagePackSerializer(new SerializationOptions { MaxMessageSize = 5000 });
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        var largeMessage = new TestMessage { Id = new string('x', 2000), Value = 42 };
        Assert.ThrowsAsync<InvalidOperationException>(
            () => serializer!.SerializeAsync(largeMessage)).Wait();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingContractMessagePackSerializer_CalledAfterRegular_UsesFirstRegistration()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer();
        services.AddHeroMessagingContractMessagePackSerializer();
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        Assert.IsType<MessagePackMessageSerializer>(serializer);
    }

    #endregion

    #region Integration Cases - Service Collection

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingMessagePackSerializer_CanResolveFromProvider_MultipleWays()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer();
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
    public void AddHeroMessagingMessagePackSerializer_WithMultipleServices_AllResolvable()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer();
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
    public void AddHeroMessagingMessagePackSerializer_FluentInterfaceChaining_Works()
    {
        // Arrange & Act
        var services = new ServiceCollection();
        services
            .AddHeroMessagingMessagePackSerializer()
            .AddScoped<string>(_ => "test");

        var provider = services.BuildServiceProvider();

        // Assert
        var serializer = provider.GetService<IMessageSerializer>();
        Assert.NotNull(serializer);
    }

    #endregion

    #region Serialization Verification

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingMessagePackSerializer_RegisteredSerializer_CanSerialize()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer();
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        var testMessage = new TestMessage { Id = "123", Value = 42 };
        var data = serializer!.SerializeAsync(testMessage).Result;
        Assert.NotEmpty(data);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingContractMessagePackSerializer_RegisteredSerializer_CanSerialize()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingContractMessagePackSerializer();
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetService<IMessageSerializer>();

        // Assert
        var testMessage = new TestMessage { Id = "123", Value = 42 };
        var data = serializer!.SerializeAsync(testMessage).Result;
        Assert.NotEmpty(data);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingMessagePackSerializer_RegisteredSerializer_CanDeserialize()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer();
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
    [Trait("Category", "Unit")]
    public void AddHeroMessagingMessagePackSerializer_WithCompressionFastest_Works()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer(opts =>
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
    public void AddHeroMessagingMessagePackSerializer_WithCompressionOptimal_Works()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer(opts =>
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

    #region Max Message Size Configuration

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHeroMessagingMessagePackSerializer_WithZeroMaxSize_AllowsAnySize()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer(opts =>
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
    [Trait("Category", "Unit")]
    public void AddHeroMessagingMessagePackSerializer_WithLargeMaxSize_AllowsMessages()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHeroMessagingMessagePackSerializer(opts =>
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

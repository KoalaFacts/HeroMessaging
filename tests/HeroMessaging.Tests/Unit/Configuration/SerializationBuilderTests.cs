using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Configuration;

[Trait("Category", "Unit")]
public sealed class SerializationBuilderTests
{
    private readonly ServiceCollection _services;

    public SerializationBuilderTests()
    {
        _services = new ServiceCollection();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidServices_CreatesInstance()
    {
        // Arrange & Act
        var builder = new SerializationBuilder(_services);

        // Assert
        Assert.NotNull(builder);
    }

    [Fact]
    public void Constructor_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new SerializationBuilder(null!));

        Assert.Equal("services", exception.ParamName);
    }

    #endregion

    #region UseJson Tests

    [Fact]
    public void UseJson_WithoutConfiguration_RegistersJsonSerializer()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);

        // Act
        builder.UseJson();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(Abstractions.Configuration.JsonSerializationOptions));
        Assert.Contains(_services, s => s.ServiceType == typeof(IMessageSerializer));
    }

    [Fact]
    public void UseJson_WithConfiguration_AppliesConfiguration()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);
        var configureWasCalled = false;

        // Act
        builder.UseJson(options =>
        {
            configureWasCalled = true;
        });

        // Assert
        Assert.True(configureWasCalled);
    }

    [Fact]
    public void UseJson_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);

        // Act
        var result = builder.UseJson();

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void UseJson_ThrowsNotImplementedException_WhenResolved()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);
        builder.UseJson();
        var provider = _services.BuildServiceProvider();

        // Act & Assert
        var exception = Assert.Throws<NotImplementedException>(() =>
            provider.GetRequiredService<IMessageSerializer>());

        Assert.Contains("JSON serializer plugin not installed", exception.Message);
    }

    #endregion

    #region UseProtobuf Tests

    [Fact]
    public void UseProtobuf_WithoutConfiguration_RegistersProtobufSerializer()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);

        // Act
        builder.UseProtobuf();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(Abstractions.Configuration.ProtobufSerializationOptions));
        Assert.Contains(_services, s => s.ServiceType == typeof(IMessageSerializer));
    }

    [Fact]
    public void UseProtobuf_WithConfiguration_AppliesConfiguration()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);
        var configureWasCalled = false;

        // Act
        builder.UseProtobuf(options =>
        {
            configureWasCalled = true;
        });

        // Assert
        Assert.True(configureWasCalled);
    }

    [Fact]
    public void UseProtobuf_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);

        // Act
        var result = builder.UseProtobuf();

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void UseProtobuf_ThrowsNotImplementedException_WhenResolved()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);
        builder.UseProtobuf();
        var provider = _services.BuildServiceProvider();

        // Act & Assert
        var exception = Assert.Throws<NotImplementedException>(() =>
            provider.GetRequiredService<IMessageSerializer>());

        Assert.Contains("Protobuf serializer plugin not installed", exception.Message);
    }

    #endregion

    #region UseMessagePack Tests

    [Fact]
    public void UseMessagePack_WithoutConfiguration_RegistersMessagePackSerializer()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);

        // Act
        builder.UseMessagePack();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(Abstractions.Configuration.MessagePackSerializationOptions));
        Assert.Contains(_services, s => s.ServiceType == typeof(IMessageSerializer));
    }

    [Fact]
    public void UseMessagePack_WithConfiguration_AppliesConfiguration()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);
        var configureWasCalled = false;

        // Act
        builder.UseMessagePack(options =>
        {
            configureWasCalled = true;
        });

        // Assert
        Assert.True(configureWasCalled);
    }

    [Fact]
    public void UseMessagePack_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);

        // Act
        var result = builder.UseMessagePack();

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void UseMessagePack_ThrowsNotImplementedException_WhenResolved()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);
        builder.UseMessagePack();
        var provider = _services.BuildServiceProvider();

        // Act & Assert
        var exception = Assert.Throws<NotImplementedException>(() =>
            provider.GetRequiredService<IMessageSerializer>());

        Assert.Contains("MessagePack serializer plugin not installed", exception.Message);
    }

    #endregion

    #region UseCustom Tests

    [Fact]
    public void UseCustom_Generic_RegistersCustomSerializer()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);

        // Act
        builder.UseCustom<TestMessageSerializer>();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IMessageSerializer) &&
                                        s.ImplementationType == typeof(TestMessageSerializer));
    }

    [Fact]
    public void UseCustom_Generic_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);

        // Act
        var result = builder.UseCustom<TestMessageSerializer>();

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void UseCustom_WithInstance_RegistersSerializerInstance()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);
        var serializer = new TestMessageSerializer();

        // Act
        builder.UseCustom(serializer);

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IMessageSerializer) ||
                                        s.ServiceType == typeof(TestMessageSerializer));
    }

    [Fact]
    public void UseCustom_WithInstance_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);
        var serializer = new TestMessageSerializer();

        // Act
        var result = builder.UseCustom(serializer);

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region AddTypeSerializer Tests

    [Fact]
    public void AddTypeSerializer_RegistersSerializerForSpecificType()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);

        // Act
        builder.AddTypeSerializer<TestMessage, TestMessageSerializer>();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(TestMessageSerializer));
    }

    [Fact]
    public void AddTypeSerializer_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);

        // Act
        var result = builder.AddTypeSerializer<TestMessage, TestMessageSerializer>();

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void AddTypeSerializer_ConfiguresTypeMapping()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);
        builder.AddTypeSerializer<TestMessage, TestMessageSerializer>();
        var provider = _services.BuildServiceProvider();

        // Act
        var options = provider.GetService<IOptions<SerializationTypeMapping>>();

        // Assert
        Assert.NotNull(options);
    }

    #endregion

    #region SetDefault Tests

    [Fact]
    public void SetDefault_RegistersDefaultSerializer()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);

        // Act
        builder.SetDefault<TestMessageSerializer>();

        // Assert
        Assert.Contains(_services, s => s.ServiceType == typeof(IMessageSerializer) &&
                                        s.ImplementationType == typeof(TestMessageSerializer));
    }

    [Fact]
    public void SetDefault_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);

        // Act
        var result = builder.SetDefault<TestMessageSerializer>();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region WithCompression Tests

    [Fact]
    public void WithCompression_DefaultLevel_EnablesCompression()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);
        builder.WithCompression();
        var provider = _services.BuildServiceProvider();

        // Act
        var options = provider.GetService<IOptions<SerializationCompressionOptions>>();

        // Assert
        Assert.NotNull(options);
    }

    [Fact]
    public void WithCompression_SpecificLevel_ConfiguresCompressionLevel()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);
        builder.WithCompression(Abstractions.Configuration.CompressionLevel.Fastest);
        var provider = _services.BuildServiceProvider();

        // Act
        var options = provider.GetService<IOptions<SerializationCompressionOptions>>();

        // Assert
        Assert.NotNull(options);
    }

    [Fact]
    public void WithCompression_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);

        // Act
        var result = builder.WithCompression();

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region WithMaxMessageSize Tests

    [Fact]
    public void WithMaxMessageSize_ConfiguresMaxSize()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);
        builder.WithMaxMessageSize(1024 * 1024);
        var provider = _services.BuildServiceProvider();

        // Act
        var options = provider.GetService<IOptions<SerializationOptions>>();

        // Assert
        Assert.NotNull(options);
    }

    [Fact]
    public void WithMaxMessageSize_ReturnsBuilder_ForFluentChaining()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);

        // Act
        var result = builder.WithMaxMessageSize(1024 * 1024);

        // Assert
        Assert.Same(builder, result);
    }

    #endregion

    #region Build Tests

    [Fact]
    public void Build_ReturnsServiceCollection()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);

        // Act
        var result = builder.Build();

        // Assert
        Assert.Same(_services, result);
    }

    [Fact]
    public void Build_DoesNotRegisterDefaultSerializer_WhenNoneConfigured()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);

        // Act
        builder.Build();

        // Assert - No IMessageSerializer should be registered (only abstract ones from UseJson/etc.)
        var concreteSerializers = _services.Where(s =>
            s.ServiceType == typeof(IMessageSerializer) &&
            s.ImplementationType != null).ToList();
        Assert.Empty(concreteSerializers);
    }

    #endregion

    #region Configuration Classes Tests

    [Fact]
    public void SerializationOptions_HasDefaultMaxMessageSize()
    {
        // Arrange & Act
        var options = new SerializationOptions();

        // Assert
        Assert.Equal(1024 * 1024 * 10, options.MaxMessageSize); // 10MB
    }

    [Fact]
    public void SerializationCompressionOptions_HasDefaultValues()
    {
        // Arrange & Act
        var options = new SerializationCompressionOptions();

        // Assert
        Assert.False(options.EnableCompression);
        Assert.Equal(Abstractions.Configuration.CompressionLevel.Optimal, options.CompressionLevel);
        Assert.Equal(1024 * 1024 * 10, options.MaxMessageSize);
    }

    [Fact]
    public void SerializationTypeMapping_InitializesEmptyDictionary()
    {
        // Arrange & Act
        var mapping = new SerializationTypeMapping();

        // Assert
        Assert.NotNull(mapping.TypeSerializers);
        Assert.Empty(mapping.TypeSerializers);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void FluentConfiguration_ChainsCorrectly()
    {
        // Arrange
        var builder = new SerializationBuilder(_services);

        // Act
        var result = builder
            .UseCustom<TestMessageSerializer>()
            .AddTypeSerializer<TestMessage, TestMessageSerializer>()
            .WithCompression(Abstractions.Configuration.CompressionLevel.Fastest)
            .WithMaxMessageSize(5 * 1024 * 1024)
            .Build();

        // Assert
        Assert.Same(_services, result);
        Assert.Contains(_services, s => s.ServiceType == typeof(IMessageSerializer));
    }

    #endregion

    #region Test Helper Classes

    private class TestMessage
    {
        public string? Content { get; set; }
    }

    private class TestMessageSerializer : IMessageSerializer
    {
        public string ContentType => "application/test";

        public ValueTask<byte[]> SerializeAsync<T>(T message, CancellationToken cancellationToken = default)
            => new ValueTask<byte[]>(Array.Empty<byte>());

        public int Serialize<T>(T message, Span<byte> destination)
            => 0;

        public bool TrySerialize<T>(T message, Span<byte> destination, out int bytesWritten)
        {
            bytesWritten = 0;
            return true;
        }

        public int GetRequiredBufferSize<T>(T message)
            => 0;

        public ValueTask<T> DeserializeAsync<T>(byte[] data, CancellationToken cancellationToken = default) where T : class
            => new ValueTask<T>(Activator.CreateInstance<T>());

        public T Deserialize<T>(ReadOnlySpan<byte> data) where T : class
            => Activator.CreateInstance<T>();

        public ValueTask<object?> DeserializeAsync(byte[] data, Type messageType, CancellationToken cancellationToken = default)
            => new ValueTask<object?>(Activator.CreateInstance(messageType));

        public object? Deserialize(ReadOnlySpan<byte> data, Type messageType)
            => Activator.CreateInstance(messageType);
    }

    #endregion
}

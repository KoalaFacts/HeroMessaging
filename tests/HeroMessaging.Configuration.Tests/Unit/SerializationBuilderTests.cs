using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using CompressionLevel = HeroMessaging.Abstractions.Configuration.CompressionLevel;

namespace HeroMessaging.Configuration.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class SerializationBuilderTests
{
    [Fact]
    public void Constructor_WithNullServices_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new SerializationBuilder(null!));
        Assert.Equal("services", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithValidServices_CreatesBuilder()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var builder = new SerializationBuilder(services);

        // Assert
        Assert.NotNull(builder);
    }

    [Fact]
    public void UseJson_WithoutConfiguration_RegistersJsonSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new SerializationBuilder(services);

        // Act
        var result = builder.UseJson();

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(JsonSerializationOptions));
        Assert.Contains(services, sd => sd.ServiceType == typeof(IMessageSerializer));
    }

    [Fact]
    public void UseJson_WithConfiguration_AppliesOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new SerializationBuilder(services);

        // Act
        builder.UseJson(options =>
        {
            options.Indented = true;
            options.CamelCase = true;
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<JsonSerializationOptions>();
        Assert.NotNull(options);
        Assert.True(options.Indented);
        Assert.True(options.CamelCase);
    }

    [Fact]
    public void UseProtobuf_WithoutConfiguration_RegistersProtobufSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new SerializationBuilder(services);

        // Act
        var result = builder.UseProtobuf();

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(ProtobufSerializationOptions));
        Assert.Contains(services, sd => sd.ServiceType == typeof(IMessageSerializer));
    }

    [Fact]
    public void UseProtobuf_WithConfiguration_AppliesOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new SerializationBuilder(services);

        // Act
        builder.UseProtobuf(options =>
        {
            options.IncludeTypeInfo = true;
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<ProtobufSerializationOptions>();
        Assert.NotNull(options);
        Assert.True(options.IncludeTypeInfo);
    }

    [Fact]
    public void UseMessagePack_WithoutConfiguration_RegistersMessagePackSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new SerializationBuilder(services);

        // Act
        var result = builder.UseMessagePack();

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(MessagePackSerializationOptions));
        Assert.Contains(services, sd => sd.ServiceType == typeof(IMessageSerializer));
    }

    [Fact]
    public void UseMessagePack_WithConfiguration_AppliesOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new SerializationBuilder(services);

        // Act
        builder.UseMessagePack(options =>
        {
            options.UseCompression = true;
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<MessagePackSerializationOptions>();
        Assert.NotNull(options);
        Assert.True(options.UseCompression);
    }

    [Fact]
    public void UseCustom_WithTypeParameter_RegistersCustomSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new SerializationBuilder(services);

        // Act
        var result = builder.UseCustom<TestSerializer>();

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(IMessageSerializer) && sd.ImplementationType == typeof(TestSerializer));
    }

    [Fact]
    public void UseCustom_WithInstance_RegistersSerializerInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new SerializationBuilder(services);
        var serializer = new TestSerializer();

        // Act
        var result = builder.UseCustom(serializer);

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(IMessageSerializer) && sd.ImplementationInstance == serializer);
    }

    [Fact]
    public void AddTypeSerializer_RegistersTypeMappingForSpecificMessage()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new SerializationBuilder(services);

        // Act
        var result = builder.AddTypeSerializer<TestMessage, TestSerializer>();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(TestSerializer));
        var options = provider.GetService<IOptions<SerializationTypeMapping>>();
        Assert.NotNull(options);
        Assert.True(options.Value.TypeSerializers.ContainsKey(typeof(TestMessage)));
        Assert.Equal(typeof(TestSerializer), options.Value.TypeSerializers[typeof(TestMessage)]);
    }

    [Fact]
    public void SetDefault_RegistersDefaultSerializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new SerializationBuilder(services);

        // Act
        var result = builder.SetDefault<TestSerializer>();

        // Assert
        Assert.Same(builder, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(IMessageSerializer) && sd.ImplementationType == typeof(TestSerializer));
    }

    [Fact]
    public void WithCompression_EnablesCompressionWithDefaultLevel()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new SerializationBuilder(services);

        // Act
        var result = builder.WithCompression();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        var options = provider.GetService<IOptions<SerializationCompressionOptions>>();
        Assert.NotNull(options);
        Assert.True(options.Value.EnableCompression);
        Assert.Equal(CompressionLevel.Optimal, options.Value.CompressionLevel);
    }

    [Fact]
    public void WithCompression_WithCustomLevel_UsesSpecifiedLevel()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new SerializationBuilder(services);

        // Act
        builder.WithCompression(CompressionLevel.Fastest);
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<IOptions<SerializationCompressionOptions>>();
        Assert.NotNull(options);
        Assert.True(options.Value.EnableCompression);
        Assert.Equal(CompressionLevel.Fastest, options.Value.CompressionLevel);
    }

    [Fact]
    public void WithMaxMessageSize_SetsMaximumMessageSize()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new SerializationBuilder(services);
        var maxSize = 1024 * 1024 * 5; // 5MB

        // Act
        var result = builder.WithMaxMessageSize(maxSize);
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        var options = provider.GetService<IOptions<SerializationOptions>>();
        Assert.NotNull(options);
        Assert.Equal(maxSize, options.Value.MaxMessageSize);
    }

    [Fact]
    public void Build_ReturnsServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new SerializationBuilder(services);

        // Act
        var result = builder.Build();

        // Assert
        Assert.Same(services, result);
    }

    [Fact]
    public void SerializationOptions_DefaultMaxMessageSize_Is10MB()
    {
        // Act
        var options = new SerializationOptions();

        // Assert
        Assert.Equal(1024 * 1024 * 10, options.MaxMessageSize);
    }

    [Fact]
    public void SerializationCompressionOptions_DefaultValues_AreCorrect()
    {
        // Act
        var options = new SerializationCompressionOptions();

        // Assert
        Assert.False(options.EnableCompression);
        Assert.Equal(CompressionLevel.Optimal, options.CompressionLevel);
        Assert.Equal(1024 * 1024 * 10, options.MaxMessageSize); // Inherits from base
    }

    [Fact]
    public void SerializationTypeMapping_TypeSerializers_IsEmptyByDefault()
    {
        // Act
        var mapping = new SerializationTypeMapping();

        // Assert
        Assert.NotNull(mapping.TypeSerializers);
        Assert.Empty(mapping.TypeSerializers);
    }

    [Fact]
    public void FluentConfiguration_CanChainMultipleMethods()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new SerializationBuilder(services);

        // Act
        var result = builder
            .UseCustom<TestSerializer>()
            .WithCompression(CompressionLevel.Fastest)
            .WithMaxMessageSize(1024 * 1024)
            .Build();

        // Assert
        Assert.Same(services, result);
        Assert.Contains(services, sd => sd.ServiceType == typeof(IMessageSerializer));
    }

    [Fact]
    public void MultipleSerializers_CanBeRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new SerializationBuilder(services);

        // Act
        builder.AddTypeSerializer<TestMessage, TestSerializer>();
        builder.AddTypeSerializer<AnotherTestMessage, AnotherTestSerializer>();
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<IOptions<SerializationTypeMapping>>();
        Assert.NotNull(options);
        Assert.Equal(2, options.Value.TypeSerializers.Count);
        Assert.Contains(typeof(TestMessage), options.Value.TypeSerializers.Keys);
        Assert.Contains(typeof(AnotherTestMessage), options.Value.TypeSerializers.Keys);
    }

    [Fact]
    public void AllCompressionLevels_CanBeUsed()
    {
        // Arrange
        var levels = new[]
        {
            CompressionLevel.None,
            CompressionLevel.Fastest,
            CompressionLevel.Optimal,
            CompressionLevel.SmallestSize
        };

        // Act & Assert
        foreach (var level in levels)
        {
            var services = new ServiceCollection();
            var builder = new SerializationBuilder(services);

            builder.WithCompression(level);
            var provider = services.BuildServiceProvider();

            var options = provider.GetService<IOptions<SerializationCompressionOptions>>();
            Assert.NotNull(options);
            Assert.Equal(level, options.Value.CompressionLevel);
        }
    }

    // Test helper classes
    private sealed class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; } = new();
    }

    private sealed class AnotherTestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; } = new();
    }

    private sealed class TestSerializer : IMessageSerializer
    {
        public string ContentType => "application/test";
        public ValueTask<byte[]> SerializeAsync<T>(T message, CancellationToken cancellationToken = default) => ValueTask.FromResult(Array.Empty<byte>());
        public int Serialize<T>(T message, Span<byte> destination) => 0;
        public bool TrySerialize<T>(T message, Span<byte> destination, out int bytesWritten) { bytesWritten = 0; return true; }
        public int GetRequiredBufferSize<T>(T message) => 0;
        public ValueTask<T> DeserializeAsync<T>(byte[] data, CancellationToken cancellationToken = default) where T : class => ValueTask.FromResult<T>(null!);
        public T Deserialize<T>(ReadOnlySpan<byte> data) where T : class => null!;
        public ValueTask<object?> DeserializeAsync(byte[] data, Type messageType, CancellationToken cancellationToken = default) => ValueTask.FromResult<object?>(null);
        public object? Deserialize(ReadOnlySpan<byte> data, Type messageType) => null;
    }

    private sealed class AnotherTestSerializer : IMessageSerializer
    {
        public string ContentType => "application/another";
        public ValueTask<byte[]> SerializeAsync<T>(T message, CancellationToken cancellationToken = default) => ValueTask.FromResult(Array.Empty<byte>());
        public int Serialize<T>(T message, Span<byte> destination) => 0;
        public bool TrySerialize<T>(T message, Span<byte> destination, out int bytesWritten) { bytesWritten = 0; return true; }
        public int GetRequiredBufferSize<T>(T message) => 0;
        public ValueTask<T> DeserializeAsync<T>(byte[] data, CancellationToken cancellationToken = default) where T : class => ValueTask.FromResult<T>(null!);
        public T Deserialize<T>(ReadOnlySpan<byte> data) where T : class => null!;
        public ValueTask<object?> DeserializeAsync(byte[] data, Type messageType, CancellationToken cancellationToken = default) => ValueTask.FromResult<object?>(null);
        public object? Deserialize(ReadOnlySpan<byte> data, Type messageType) => null;
    }
}

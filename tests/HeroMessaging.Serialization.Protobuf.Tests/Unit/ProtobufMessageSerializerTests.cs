<<<<<<< HEAD
using System;
using System.Threading;
using System.Threading.Tasks;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.Protobuf;
using Moq;
using ProtoBuf;
using ProtoBuf.Meta;
=======
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.Protobuf;
using ProtoBuf;
using Moq;
>>>>>>> testing/serialization
using Xunit;

namespace HeroMessaging.Serialization.Protobuf.Tests.Unit;

<<<<<<< HEAD
[Trait("Category", "Unit")]
public class ProtobufMessageSerializerTests
{
    #region Test Models

    [ProtoContract]
    public class TestMessage
    {
        [ProtoMember(1)]
        public string Name { get; set; } = string.Empty;

        [ProtoMember(2)]
        public int Value { get; set; }

        [ProtoMember(3)]
        public DateTime Timestamp { get; set; }
    }

    [ProtoContract]
    public class ComplexMessage
    {
        [ProtoMember(1)]
        public string Id { get; set; } = string.Empty;
=======
/// <summary>
/// Unit tests for ProtobufMessageSerializer class
/// </summary>
public class ProtobufMessageSerializerTests
{
    [ProtoContract]
    private class TestMessage
    {
        [ProtoMember(1)]
        public int Id { get; set; }

        [ProtoMember(2)]
        public string Name { get; set; } = string.Empty;

        [ProtoMember(3)]
        public decimal Value { get; set; }

        [ProtoMember(4)]
        public string[] Tags { get; set; } = Array.Empty<string>();
    }

    [ProtoContract]
    private class ComplexTestMessage
    {
        [ProtoMember(1)]
        public string Id { get; set; } = Guid.NewGuid().ToString();
>>>>>>> testing/serialization

        [ProtoMember(2)]
        public TestMessage? Nested { get; set; }

        [ProtoMember(3)]
<<<<<<< HEAD
        public string[] Tags { get; set; } = Array.Empty<string>();

        [ProtoMember(4)]
        public int[] Numbers { get; set; } = Array.Empty<int>();
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullOptions_UsesDefaults()
    {
        // Arrange & Act
=======
        public List<TestMessage>? Items { get; set; }
    }

    #region Constructor Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithDefaults_CreatesSerializer()
    {
        // Act
>>>>>>> testing/serialization
        var serializer = new ProtobufMessageSerializer();

        // Assert
        Assert.NotNull(serializer);
        Assert.Equal("application/x-protobuf", serializer.ContentType);
    }

    [Fact]
<<<<<<< HEAD
    public void Constructor_WithCustomOptions_UsesProvidedOptions()
    {
        // Arrange
        var options = new SerializationOptions
        {
            EnableCompression = true,
            MaxMessageSize = 2048
        };
=======
    [Trait("Category", "Unit")]
    public void Constructor_WithCustomOptions_SetsOptions()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true, MaxMessageSize = 2048 };
>>>>>>> testing/serialization

        // Act
        var serializer = new ProtobufMessageSerializer(options);

        // Assert
        Assert.NotNull(serializer);
<<<<<<< HEAD
        Assert.Equal("application/x-protobuf", serializer.ContentType);
    }

    [Fact]
    public void Constructor_WithCustomTypeModel_UsesProvidedModel()
    {
        // Arrange
        var typeModel = RuntimeTypeModel.Create();

        // Act
        var serializer = new ProtobufMessageSerializer(typeModel: typeModel);

        // Assert
        Assert.NotNull(serializer);
    }

    [Fact]
    public void Constructor_WithCustomCompressionProvider_UsesProvidedProvider()
    {
        // Arrange
        var mockProvider = new Mock<ICompressionProvider>();
        var options = new SerializationOptions { EnableCompression = true };

        // Act
        var serializer = new ProtobufMessageSerializer(options, compressionProvider: mockProvider.Object);

        // Assert
        Assert.NotNull(serializer);
    }

    [Fact]
    public void Constructor_WithAllParameters_UsesProvidedValues()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var typeModel = RuntimeTypeModel.Create();
        var mockProvider = new Mock<ICompressionProvider>();

        // Act
        var serializer = new ProtobufMessageSerializer(options, typeModel, mockProvider.Object);
=======
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithCompressionProvider_SetsProvider()
    {
        // Arrange
        var provider = new Mock<ICompressionProvider>();

        // Act
        var serializer = new ProtobufMessageSerializer(compressionProvider: provider.Object);
>>>>>>> testing/serialization

        // Assert
        Assert.NotNull(serializer);
    }

    #endregion

    #region ContentType Tests

    [Fact]
<<<<<<< HEAD
    public void ContentType_ReturnsCorrectValue()
=======
    [Trait("Category", "Unit")]
    public void ContentType_ReturnsCorrectType()
>>>>>>> testing/serialization
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();

        // Act
        var contentType = serializer.ContentType;

        // Assert
        Assert.Equal("application/x-protobuf", contentType);
    }

    #endregion

    #region SerializeAsync Tests

    [Fact]
<<<<<<< HEAD
    public async Task SerializeAsync_WithValidMessage_ReturnsSerializedData()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var message = new TestMessage
        {
            Name = "Test",
            Value = 42,
            Timestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
=======
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithValidMessage_ReturnsBytes()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var message = new TestMessage { Id = 1, Name = "Test", Value = 99.99m, Tags = new[] { "tag1", "tag2" } };
>>>>>>> testing/serialization

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotNull(result);
<<<<<<< HEAD
        Assert.True(result.Length > 0);
    }

    [Fact]
=======
        Assert.NotEmpty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
>>>>>>> testing/serialization
    public async Task SerializeAsync_WithNullMessage_ReturnsEmptyArray()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();

        // Act
        var result = await serializer.SerializeAsync<TestMessage>(null!);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
<<<<<<< HEAD
    public async Task SerializeAsync_WithComplexMessage_SerializesCorrectly()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var message = new ComplexMessage
        {
            Id = "complex-123",
            Nested = new TestMessage { Name = "Nested", Value = 99 },
            Tags = new[] { "tag1", "tag2", "tag3" },
            Numbers = new[] { 1, 2, 3, 4, 5 }
=======
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithComplexMessage_ReturnsBytes()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var message = new ComplexTestMessage
        {
            Id = "test-id",
            Nested = new TestMessage { Id = 1, Name = "Nested", Value = 10.5m },
            Items = new List<TestMessage>
            {
                new() { Id = 2, Name = "Item1", Value = 20.0m },
                new() { Id = 3, Name = "Item2", Value = 30.0m }
            }
>>>>>>> testing/serialization
        };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotNull(result);
<<<<<<< HEAD
        Assert.True(result.Length > 0);
    }

    [Fact]
    public async Task SerializeAsync_WithMaxMessageSizeExceeded_ThrowsException()
=======
        Assert.NotEmpty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WhenMaxSizeExceeded_ThrowsInvalidOperationException()
>>>>>>> testing/serialization
    {
        // Arrange
        var options = new SerializationOptions { MaxMessageSize = 10 };
        var serializer = new ProtobufMessageSerializer(options);
<<<<<<< HEAD
        var message = new TestMessage { Name = "This is a very long message name that will exceed the size limit" };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await serializer.SerializeAsync(message));
        Assert.Contains("exceeds maximum allowed size", exception.Message);
    }

    [Fact]
    public async Task SerializeAsync_WithCompressionEnabled_ReturnsCompressedData()
=======
        var message = new TestMessage { Id = 1, Name = "Test message with long name", Value = 99.99m };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await serializer.SerializeAsync(message));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithCompressionEnabled_CompressesData()
>>>>>>> testing/serialization
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var serializer = new ProtobufMessageSerializer(options);
        var message = new TestMessage
        {
<<<<<<< HEAD
            Name = "Test with compression enabled to verify compression works properly",
            Value = 42
=======
            Id = 1,
            Name = "Test message for compression testing with repeated data",
            Value = 99.99m,
            Tags = new[] { "tag1", "tag2", "tag3", "tag4", "tag5" }
>>>>>>> testing/serialization
        };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotNull(result);
<<<<<<< HEAD
        Assert.True(result.Length > 0);
    }

    [Fact]
=======
        Assert.NotEmpty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
>>>>>>> testing/serialization
    public async Task SerializeAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
<<<<<<< HEAD
        var message = new TestMessage { Name = "CancellationTest", Value = 100 };
        var cts = new CancellationTokenSource();
=======
        var message = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };
        using var cts = new CancellationTokenSource();
>>>>>>> testing/serialization

        // Act
        var result = await serializer.SerializeAsync(message, cts.Token);

        // Assert
        Assert.NotNull(result);
<<<<<<< HEAD
        Assert.True(result.Length > 0);
    }

    [Fact]
    public async Task SerializeAsync_ProducesCompactBinaryData()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var message = new ComplexMessage
        {
            Id = "efficiency-test",
            Nested = new TestMessage { Name = "Nested", Value = 999 },
            Tags = new[] { "tag1", "tag2", "tag3", "tag4", "tag5" },
            Numbers = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }
        };

        // Act
        var protobufData = await serializer.SerializeAsync(message);

        // Assert - Protobuf should produce compact binary data
        Assert.NotNull(protobufData);
        Assert.True(protobufData.Length > 0);
        // Typical Protobuf efficiency: very compact
=======
        Assert.NotEmpty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var message = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await serializer.SerializeAsync(message, cts.Token));
>>>>>>> testing/serialization
    }

    #endregion

    #region Serialize (Span) Tests

    [Fact]
<<<<<<< HEAD
=======
    [Trait("Category", "Unit")]
>>>>>>> testing/serialization
    public void Serialize_WithValidMessage_WritesToSpan()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
<<<<<<< HEAD
        var message = new TestMessage { Name = "SpanTest", Value = 200 };
        Span<byte> buffer = new byte[4096];
=======
        var message = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };
        var buffer = new byte[1024];
>>>>>>> testing/serialization

        // Act
        var bytesWritten = serializer.Serialize(message, buffer);

        // Assert
        Assert.True(bytesWritten > 0);
<<<<<<< HEAD
    }

    [Fact]
=======
        Assert.True(bytesWritten <= buffer.Length);
    }

    [Fact]
    [Trait("Category", "Unit")]
>>>>>>> testing/serialization
    public void Serialize_WithNullMessage_ReturnsZero()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
<<<<<<< HEAD
        Span<byte> buffer = new byte[4096];
=======
        var buffer = new byte[1024];
>>>>>>> testing/serialization

        // Act
        var bytesWritten = serializer.Serialize<TestMessage>(null!, buffer);

        // Assert
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
<<<<<<< HEAD
    public void Serialize_WithComplexMessage_SerializesCorrectly()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var message = new ComplexMessage
        {
            Id = "span-complex",
            Nested = new TestMessage { Name = "SpanNested", Value = 300 },
            Tags = new[] { "a", "b", "c" }
        };
        Span<byte> buffer = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(message, buffer);

        // Assert
        Assert.True(bytesWritten > 0);
    }

    [Fact]
    public void Serialize_WithTooSmallBuffer_ThrowsException()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var message = new TestMessage { Name = "SmallBufferTest", Value = 400 };
        Span<byte> buffer = new byte[5]; // Too small

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => serializer.Serialize(message, buffer));
        Assert.Contains("Destination buffer too small", exception.Message);
=======
    [Trait("Category", "Unit")]
    public void Serialize_WithSmallBuffer_ThrowsException()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var message = new TestMessage { Id = 1, Name = "Test message", Value = 99.99m };
        var buffer = new byte[5];

        // Act & Assert
        Assert.Throws<ArgumentException>(() => serializer.Serialize(message, buffer));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_RoundTrip_ProducesIdenticalObject()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var original = new TestMessage { Id = 42, Name = "RoundTrip", Value = 123.45m, Tags = new[] { "a", "b" } };
        var buffer = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(original, buffer);
        var deserialized = serializer.Deserialize<TestMessage>(buffer.AsSpan(0, bytesWritten));

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Value, deserialized.Value);
>>>>>>> testing/serialization
    }

    #endregion

    #region TrySerialize Tests

    [Fact]
<<<<<<< HEAD
=======
    [Trait("Category", "Unit")]
>>>>>>> testing/serialization
    public void TrySerialize_WithValidMessage_ReturnsTrue()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
<<<<<<< HEAD
        var message = new TestMessage { Name = "TrySerialize", Value = 500 };
        Span<byte> buffer = new byte[4096];
=======
        var message = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };
        var buffer = new byte[1024];
>>>>>>> testing/serialization

        // Act
        var success = serializer.TrySerialize(message, buffer, out var bytesWritten);

        // Assert
        Assert.True(success);
        Assert.True(bytesWritten > 0);
    }

    [Fact]
<<<<<<< HEAD
    public void TrySerialize_WithNullMessage_ReturnsTrueWithZeroBytes()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        Span<byte> buffer = new byte[4096];

        // Act
        var success = serializer.TrySerialize<TestMessage>(null!, buffer, out var bytesWritten);

        // Assert
        Assert.True(success);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TrySerialize_WithTooSmallBuffer_ReturnsFalse()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var message = new TestMessage { Name = "TooSmall", Value = 600 };
        Span<byte> buffer = new byte[5];
=======
    [Trait("Category", "Unit")]
    public void TrySerialize_WithSmallBuffer_ReturnsFalse()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var message = new TestMessage { Id = 1, Name = "Test message", Value = 99.99m };
        var buffer = new byte[5];
>>>>>>> testing/serialization

        // Act
        var success = serializer.TrySerialize(message, buffer, out var bytesWritten);

        // Assert
        Assert.False(success);
        Assert.Equal(0, bytesWritten);
    }

    #endregion

    #region GetRequiredBufferSize Tests

    [Fact]
<<<<<<< HEAD
=======
    [Trait("Category", "Unit")]
>>>>>>> testing/serialization
    public void GetRequiredBufferSize_ReturnsPositiveValue()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
<<<<<<< HEAD
        var message = new TestMessage { Name = "BufferSize", Value = 700 };

        // Act
        var bufferSize = serializer.GetRequiredBufferSize(message);

        // Assert
        Assert.True(bufferSize > 0);
        Assert.Equal(2048, bufferSize); // Default estimate for Protobuf
=======
        var message = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };

        // Act
        var size = serializer.GetRequiredBufferSize(message);

        // Assert
        Assert.True(size > 0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRequiredBufferSize_IsSufficientForSerialization()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var message = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };
        var requiredSize = serializer.GetRequiredBufferSize(message);

        // Act
        var buffer = new byte[requiredSize];
        var bytesWritten = serializer.Serialize(message, buffer);

        // Assert
        Assert.True(bytesWritten > 0);
        Assert.True(bytesWritten <= requiredSize);
>>>>>>> testing/serialization
    }

    #endregion

    #region DeserializeAsync Tests

    [Fact]
<<<<<<< HEAD
=======
    [Trait("Category", "Unit")]
>>>>>>> testing/serialization
    public async Task DeserializeAsync_WithValidData_ReturnsMessage()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
<<<<<<< HEAD
        var original = new TestMessage
        {
            Name = "DeserializeTest",
            Value = 800,
            Timestamp = new DateTime(2024, 6, 15, 12, 30, 0, DateTimeKind.Utc)
        };
=======
        var original = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };
>>>>>>> testing/serialization
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync<TestMessage>(data);

        // Assert
        Assert.NotNull(result);
<<<<<<< HEAD
        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Value, result.Value);
    }

    [Fact]
    public async Task DeserializeAsync_WithNullData_ReturnsDefault()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();

        // Act
        var result = await serializer.DeserializeAsync<TestMessage>(null!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DeserializeAsync_WithEmptyData_ReturnsDefault()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();

        // Act
        var result = await serializer.DeserializeAsync<TestMessage>(Array.Empty<byte>());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DeserializeAsync_WithComplexMessage_DeserializesCorrectly()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var original = new ComplexMessage
        {
            Id = "deserialize-complex",
            Nested = new TestMessage { Name = "DeserializeNested", Value = 900 },
            Tags = new[] { "x", "y", "z" },
            Numbers = new[] { 10, 20, 30 }
        };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync<ComplexMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
        Assert.NotNull(result.Nested);
        Assert.Equal(original.Nested.Name, result.Nested.Name);
        Assert.Equal(3, result.Tags.Length);
        Assert.Equal(3, result.Numbers.Length);
    }

    [Fact]
    public async Task DeserializeAsync_WithCompressedData_DecompressesAndDeserializes()
=======
        Assert.Equal(original.Id, result.Id);
        Assert.Equal(original.Name, result.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeserializeAsync_WithEmptyArray_ReturnsNull()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var data = Array.Empty<byte>();

        // Act
        var result = await serializer.DeserializeAsync<TestMessage>(data);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeserializeAsync_WithCompressedData_ReturnsMessage()
>>>>>>> testing/serialization
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var serializer = new ProtobufMessageSerializer(options);
<<<<<<< HEAD
        var original = new TestMessage { Name = "CompressedDeserialize", Value = 1000 };
=======
        var original = new TestMessage { Id = 1, Name = "Compression Test", Value = 99.99m };
>>>>>>> testing/serialization
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync<TestMessage>(data);

        // Assert
        Assert.NotNull(result);
<<<<<<< HEAD
        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Value, result.Value);
    }

    [Fact]
    public async Task DeserializeAsync_TypedOverload_WithValidData_ReturnsObject()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var original = new TestMessage { Name = "TypedDeserialize", Value = 1100 };
=======
        Assert.Equal(original.Id, result.Id);
        Assert.Equal(original.Name, result.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeserializeAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var original = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };
        var data = await serializer.SerializeAsync(original);
        using var cts = new CancellationTokenSource();

        // Act
        var result = await serializer.DeserializeAsync<TestMessage>(data, cts.Token);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeserializeAsync_WithDynamicType_ReturnsObject()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var original = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };
>>>>>>> testing/serialization
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync(data, typeof(TestMessage));

        // Assert
        Assert.NotNull(result);
        Assert.IsType<TestMessage>(result);
<<<<<<< HEAD
        var typedResult = (TestMessage)result;
        Assert.Equal(original.Name, typedResult.Name);
        Assert.Equal(original.Value, typedResult.Value);
    }

    [Fact]
    public async Task DeserializeAsync_TypedOverload_WithNullData_ReturnsNull()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();

        // Act
        var result = await serializer.DeserializeAsync(null!, typeof(TestMessage));

        // Assert
        Assert.Null(result);
=======
>>>>>>> testing/serialization
    }

    #endregion

    #region Deserialize (Span) Tests

    [Fact]
<<<<<<< HEAD
=======
    [Trait("Category", "Unit")]
>>>>>>> testing/serialization
    public void Deserialize_WithValidData_ReturnsMessage()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
<<<<<<< HEAD
        var original = new TestMessage { Name = "SpanDeserialize", Value = 1200 };
        Span<byte> buffer = new byte[4096];
        var bytesWritten = serializer.Serialize(original, buffer);
        var data = buffer.Slice(0, bytesWritten);
=======
        var original = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, original);
        var data = ms.ToArray();
>>>>>>> testing/serialization

        // Act
        var result = serializer.Deserialize<TestMessage>(data);

        // Assert
        Assert.NotNull(result);
<<<<<<< HEAD
        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Value, result.Value);
    }

    [Fact]
    public void Deserialize_WithEmptySpan_ReturnsDefault()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        ReadOnlySpan<byte> data = ReadOnlySpan<byte>.Empty;
=======
        Assert.Equal(original.Id, result.Id);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Deserialize_WithEmptySpan_ReturnsNull()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var data = ReadOnlySpan<byte>.Empty;
>>>>>>> testing/serialization

        // Act
        var result = serializer.Deserialize<TestMessage>(data);

        // Assert
        Assert.Null(result);
    }

    [Fact]
<<<<<<< HEAD
    public void Deserialize_TypedOverload_WithValidData_ReturnsObject()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var original = new TestMessage { Name = "TypedSpanDeserialize", Value = 1300 };
        Span<byte> buffer = new byte[4096];
        var bytesWritten = serializer.Serialize(original, buffer);
        var data = buffer.Slice(0, bytesWritten);
=======
    [Trait("Category", "Unit")]
    public void Deserialize_WithDynamicType_ReturnsObject()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var original = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, original);
        var data = ms.ToArray();
>>>>>>> testing/serialization

        // Act
        var result = serializer.Deserialize(data, typeof(TestMessage));

        // Assert
        Assert.NotNull(result);
        Assert.IsType<TestMessage>(result);
<<<<<<< HEAD
        var typedResult = (TestMessage)result;
        Assert.Equal(original.Name, typedResult.Name);
    }

    [Fact]
    public void Deserialize_TypedOverload_WithEmptySpan_ReturnsNull()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        ReadOnlySpan<byte> data = ReadOnlySpan<byte>.Empty;

        // Act
        var result = serializer.Deserialize(data, typeof(TestMessage));

        // Assert
        Assert.Null(result);
=======
>>>>>>> testing/serialization
    }

    #endregion

<<<<<<< HEAD
    #region Round-Trip Tests

    [Fact]
    public async Task RoundTrip_WithSimpleMessage_PreservesData()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var original = new TestMessage
        {
            Name = "RoundTrip",
            Value = 1400,
            Timestamp = DateTime.UtcNow
        };
=======
    #region RoundTrip Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RoundTrip_AsyncSerialization_PreservesData()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var original = new TestMessage { Id = 42, Name = "RoundTrip", Value = 123.45m, Tags = new[] { "tag1", "tag2" } };
>>>>>>> testing/serialization

        // Act
        var serialized = await serializer.SerializeAsync(original);
        var deserialized = await serializer.DeserializeAsync<TestMessage>(serialized);

        // Assert
        Assert.NotNull(deserialized);
<<<<<<< HEAD
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Value, deserialized.Value);
    }

    [Fact]
    public async Task RoundTrip_WithComplexMessage_PreservesData()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var original = new ComplexMessage
        {
            Id = "roundtrip-complex",
            Nested = new TestMessage { Name = "RoundTripNested", Value = 1500 },
            Tags = new[] { "tag1", "tag2", "tag3", "tag4" },
            Numbers = new[] { 100, 200, 300, 400, 500 }
=======
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Value, deserialized.Value);
        Assert.Equal(original.Tags.Length, deserialized.Tags.Length);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RoundTrip_WithCompression_PreservesData()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var serializer = new ProtobufMessageSerializer(options);
        var original = new TestMessage { Id = 42, Name = "Compressed RoundTrip", Value = 999.99m };

        // Act
        var serialized = await serializer.SerializeAsync(original);
        var deserialized = await serializer.DeserializeAsync<TestMessage>(serialized);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.Name, deserialized.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RoundTrip_WithComplexObject_PreservesData()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var original = new ComplexTestMessage
        {
            Id = "complex-id",
            Nested = new TestMessage { Id = 1, Name = "Nested", Value = 10.5m },
            Items = new List<TestMessage>
            {
                new() { Id = 2, Name = "Item1", Value = 20.0m },
                new() { Id = 3, Name = "Item2", Value = 30.0m }
            }
>>>>>>> testing/serialization
        };

        // Act
        var serialized = await serializer.SerializeAsync(original);
<<<<<<< HEAD
        var deserialized = await serializer.DeserializeAsync<ComplexMessage>(serialized);
=======
        var deserialized = await serializer.DeserializeAsync<ComplexTestMessage>(serialized);
>>>>>>> testing/serialization

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.NotNull(deserialized.Nested);
        Assert.Equal(original.Nested.Name, deserialized.Nested.Name);
<<<<<<< HEAD
        Assert.Equal(original.Tags.Length, deserialized.Tags.Length);
        Assert.Equal(original.Numbers.Length, deserialized.Numbers.Length);
    }

    [Fact]
    public async Task RoundTrip_WithCompression_PreservesData()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var serializer = new ProtobufMessageSerializer(options);
        var original = new TestMessage { Name = "CompressedRoundTrip", Value = 1600 };

        // Act
        var serialized = await serializer.SerializeAsync(original);
        var deserialized = await serializer.DeserializeAsync<TestMessage>(serialized);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Value, deserialized.Value);
    }

    [Fact]
    public void RoundTrip_Span_WithSimpleMessage_PreservesData()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var original = new TestMessage { Name = "SpanRoundTrip", Value = 1700 };
        Span<byte> buffer = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(original, buffer);
        var deserialized = serializer.Deserialize<TestMessage>(buffer.Slice(0, bytesWritten));

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Value, deserialized.Value);
    }

    #endregion

    #region Compression Level Tests

    [Fact]
    public async Task SerializeAsync_WithDifferentCompressionLevels_AllWork()
    {
        // Arrange
        var message = new TestMessage { Name = "CompressionLevelTest", Value = 1800 };
        var compressionLevels = new[]
        {
            CompressionLevel.Fastest,
            CompressionLevel.Optimal,
            CompressionLevel.Maximum
        };

        foreach (var level in compressionLevels)
        {
            var options = new SerializationOptions
            {
                EnableCompression = true,
                CompressionLevel = level
            };
            var serializer = new ProtobufMessageSerializer(options);

            // Act
            var serialized = await serializer.SerializeAsync(message);
            var deserialized = await serializer.DeserializeAsync<TestMessage>(serialized);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(message.Name, deserialized.Name);
            Assert.Equal(message.Value, deserialized.Value);
        }
    }

    #endregion

    #region Custom TypeModel Tests

    [Fact]
    public async Task Serializer_WithCustomTypeModel_WorksCorrectly()
    {
        // Arrange
        var customTypeModel = RuntimeTypeModel.Create();
        customTypeModel.Add(typeof(TestMessage), true);
        var serializer = new ProtobufMessageSerializer(typeModel: customTypeModel);
        var message = new TestMessage { Name = "CustomTypeModel", Value = 1900 };
=======
        Assert.NotNull(deserialized.Items);
        Assert.Equal(2, deserialized.Items.Count);
    }

    #endregion

    #region Edge Cases

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithEmptyMessage_Succeeds()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var message = new TestMessage { Id = 0, Name = "", Value = 0 };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithLargeMessage_Succeeds()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var tags = Enumerable.Range(0, 1000).Select(i => $"tag{i}").ToArray();
        var message = new TestMessage { Id = 1, Name = "Large Message", Value = 99.99m, Tags = tags };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RoundTrip_WithSpecialCharacters_PreservesData()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var message = new TestMessage { Id = 1, Name = "Test\"with'special\\chars/and\"quotes", Value = 99.99m };
>>>>>>> testing/serialization

        // Act
        var serialized = await serializer.SerializeAsync(message);
        var deserialized = await serializer.DeserializeAsync<TestMessage>(serialized);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(message.Name, deserialized.Name);
<<<<<<< HEAD
        Assert.Equal(message.Value, deserialized.Value);
=======
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RoundTrip_WithUnicodeCharacters_PreservesData()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var message = new TestMessage { Id = 1, Name = "Test with emoji 😀 and unicode ñ é", Value = 99.99m };

        // Act
        var serialized = await serializer.SerializeAsync(message);
        var deserialized = await serializer.DeserializeAsync<TestMessage>(serialized);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(message.Name, deserialized.Name);
    }

    #endregion

    #region Compression Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithDifferentCompressionLevels_AllSucceed()
    {
        // Arrange
        var message = new TestMessage { Id = 1, Name = "Compression test", Value = 99.99m };
        var compressionLevels = new[] { CompressionLevel.Fastest, CompressionLevel.Optimal, CompressionLevel.Maximum };

        // Act & Assert
        foreach (var level in compressionLevels)
        {
            var options = new SerializationOptions { EnableCompression = true, CompressionLevel = level };
            var serializer = new ProtobufMessageSerializer(options);
            var result = await serializer.SerializeAsync(message);
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RoundTrip_CompressedData_MatchesUncompressed()
    {
        // Arrange
        var message = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };
        var uncompressedSerializer = new ProtobufMessageSerializer();
        var compressedOptions = new SerializationOptions { EnableCompression = true };
        var compressedSerializer = new ProtobufMessageSerializer(compressedOptions);

        // Act
        var uncompressedData = await uncompressedSerializer.SerializeAsync(message);
        var compressedData = await compressedSerializer.SerializeAsync(message);
        var uncompressedResult = await uncompressedSerializer.DeserializeAsync<TestMessage>(uncompressedData);
        var compressedResult = await compressedSerializer.DeserializeAsync<TestMessage>(compressedData);

        // Assert
        Assert.Equal(uncompressedResult.Id, compressedResult.Id);
        Assert.Equal(uncompressedResult.Name, compressedResult.Name);
        Assert.Equal(uncompressedResult.Value, compressedResult.Value);
>>>>>>> testing/serialization
    }

    #endregion
}

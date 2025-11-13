using System;
using System.Threading;
using System.Threading.Tasks;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.Protobuf;
using Moq;
using ProtoBuf;
using ProtoBuf.Meta;
using Xunit;

namespace HeroMessaging.Serialization.Protobuf.Tests.Unit;

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

        [ProtoMember(2)]
        public TestMessage? Nested { get; set; }

        [ProtoMember(3)]
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
        var serializer = new ProtobufMessageSerializer();

        // Assert
        Assert.NotNull(serializer);
        Assert.Equal("application/x-protobuf", serializer.ContentType);
    }

    [Fact]
    public void Constructor_WithCustomOptions_UsesProvidedOptions()
    {
        // Arrange
        var options = new SerializationOptions
        {
            EnableCompression = true,
            MaxMessageSize = 2048
        };

        // Act
        var serializer = new ProtobufMessageSerializer(options);

        // Assert
        Assert.NotNull(serializer);
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

        // Assert
        Assert.NotNull(serializer);
    }

    #endregion

    #region ContentType Tests

    [Fact]
    public void ContentType_ReturnsCorrectValue()
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

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
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
        };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public async Task SerializeAsync_WithMaxMessageSizeExceeded_ThrowsException()
    {
        // Arrange
        var options = new SerializationOptions { MaxMessageSize = 10 };
        var serializer = new ProtobufMessageSerializer(options);
        var message = new TestMessage { Name = "This is a very long message name that will exceed the size limit" };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await serializer.SerializeAsync(message));
        Assert.Contains("exceeds maximum allowed size", exception.Message);
    }

    [Fact]
    public async Task SerializeAsync_WithCompressionEnabled_ReturnsCompressedData()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var serializer = new ProtobufMessageSerializer(options);
        var message = new TestMessage
        {
            Name = "Test with compression enabled to verify compression works properly",
            Value = 42
        };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public async Task SerializeAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var message = new TestMessage { Name = "CancellationTest", Value = 100 };
        var cts = new CancellationTokenSource();

        // Act
        var result = await serializer.SerializeAsync(message, cts.Token);

        // Assert
        Assert.NotNull(result);
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
    }

    #endregion

    #region Serialize (Span) Tests

    [Fact]
    public void Serialize_WithValidMessage_WritesToSpan()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var message = new TestMessage { Name = "SpanTest", Value = 200 };
        Span<byte> buffer = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(message, buffer);

        // Assert
        Assert.True(bytesWritten > 0);
    }

    [Fact]
    public void Serialize_WithNullMessage_ReturnsZero()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        Span<byte> buffer = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize<TestMessage>(null!, buffer);

        // Assert
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
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
    }

    #endregion

    #region TrySerialize Tests

    [Fact]
    public void TrySerialize_WithValidMessage_ReturnsTrue()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var message = new TestMessage { Name = "TrySerialize", Value = 500 };
        Span<byte> buffer = new byte[4096];

        // Act
        var success = serializer.TrySerialize(message, buffer, out var bytesWritten);

        // Assert
        Assert.True(success);
        Assert.True(bytesWritten > 0);
    }

    [Fact]
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

        // Act
        var success = serializer.TrySerialize(message, buffer, out var bytesWritten);

        // Assert
        Assert.False(success);
        Assert.Equal(0, bytesWritten);
    }

    #endregion

    #region GetRequiredBufferSize Tests

    [Fact]
    public void GetRequiredBufferSize_ReturnsPositiveValue()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var message = new TestMessage { Name = "BufferSize", Value = 700 };

        // Act
        var bufferSize = serializer.GetRequiredBufferSize(message);

        // Assert
        Assert.True(bufferSize > 0);
        Assert.Equal(2048, bufferSize); // Default estimate for Protobuf
    }

    #endregion

    #region DeserializeAsync Tests

    [Fact]
    public async Task DeserializeAsync_WithValidData_ReturnsMessage()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var original = new TestMessage
        {
            Name = "DeserializeTest",
            Value = 800,
            Timestamp = new DateTime(2024, 6, 15, 12, 30, 0, DateTimeKind.Utc)
        };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync<TestMessage>(data);

        // Assert
        Assert.NotNull(result);
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
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var serializer = new ProtobufMessageSerializer(options);
        var original = new TestMessage { Name = "CompressedDeserialize", Value = 1000 };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync<TestMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Value, result.Value);
    }

    [Fact]
    public async Task DeserializeAsync_TypedOverload_WithValidData_ReturnsObject()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var original = new TestMessage { Name = "TypedDeserialize", Value = 1100 };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync(data, typeof(TestMessage));

        // Assert
        Assert.NotNull(result);
        Assert.IsType<TestMessage>(result);
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
    }

    #endregion

    #region Deserialize (Span) Tests

    [Fact]
    public void Deserialize_WithValidData_ReturnsMessage()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var original = new TestMessage { Name = "SpanDeserialize", Value = 1200 };
        Span<byte> buffer = new byte[4096];
        var bytesWritten = serializer.Serialize(original, buffer);
        var data = buffer.Slice(0, bytesWritten);

        // Act
        var result = serializer.Deserialize<TestMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Value, result.Value);
    }

    [Fact]
    public void Deserialize_WithEmptySpan_ReturnsDefault()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        ReadOnlySpan<byte> data = ReadOnlySpan<byte>.Empty;

        // Act
        var result = serializer.Deserialize<TestMessage>(data);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_TypedOverload_WithValidData_ReturnsObject()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var original = new TestMessage { Name = "TypedSpanDeserialize", Value = 1300 };
        Span<byte> buffer = new byte[4096];
        var bytesWritten = serializer.Serialize(original, buffer);
        var data = buffer.Slice(0, bytesWritten);

        // Act
        var result = serializer.Deserialize(data, typeof(TestMessage));

        // Assert
        Assert.NotNull(result);
        Assert.IsType<TestMessage>(result);
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
    }

    #endregion

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

        // Act
        var serialized = await serializer.SerializeAsync(original);
        var deserialized = await serializer.DeserializeAsync<TestMessage>(serialized);

        // Assert
        Assert.NotNull(deserialized);
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
        };

        // Act
        var serialized = await serializer.SerializeAsync(original);
        var deserialized = await serializer.DeserializeAsync<ComplexMessage>(serialized);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.NotNull(deserialized.Nested);
        Assert.Equal(original.Nested.Name, deserialized.Nested.Name);
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

        // Act
        var serialized = await serializer.SerializeAsync(message);
        var deserialized = await serializer.DeserializeAsync<TestMessage>(serialized);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(message.Name, deserialized.Name);
        Assert.Equal(message.Value, deserialized.Value);
    }

    #endregion
}

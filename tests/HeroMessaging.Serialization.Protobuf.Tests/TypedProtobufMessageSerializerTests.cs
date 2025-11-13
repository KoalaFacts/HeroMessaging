using System;
using System.Threading;
using System.Threading.Tasks;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.Protobuf;
using ProtoBuf;
using ProtoBuf.Meta;
using Xunit;

namespace HeroMessaging.Serialization.Protobuf.Tests;

[Trait("Category", "Unit")]
public class TypedProtobufMessageSerializerTests
{
    #region Test Models

    [ProtoContract]
    public class TypedTestMessage
    {
        [ProtoMember(1)]
        public string Name { get; set; } = string.Empty;

        [ProtoMember(2)]
        public int Value { get; set; }

        [ProtoMember(3)]
        public DateTime Timestamp { get; set; }
    }

    [ProtoContract]
    public class TypedComplexMessage
    {
        [ProtoMember(1)]
        public string Id { get; set; } = string.Empty;

        [ProtoMember(2)]
        public TypedTestMessage? Nested { get; set; }

        [ProtoMember(3)]
        public string[] Tags { get; set; } = Array.Empty<string>();
    }

    [ProtoContract]
    [ProtoInclude(100, typeof(DerivedMessage))]
    public class BaseMessage
    {
        [ProtoMember(1)]
        public string BaseProperty { get; set; } = string.Empty;
    }

    [ProtoContract]
    public class DerivedMessage : BaseMessage
    {
        [ProtoMember(1)]
        public string DerivedProperty { get; set; } = string.Empty;
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullOptions_UsesDefaults()
    {
        // Arrange & Act
        var serializer = new TypedProtobufMessageSerializer();

        // Assert
        Assert.NotNull(serializer);
        Assert.Equal("application/x-protobuf-typed", serializer.ContentType);
    }

    [Fact]
    public void Constructor_WithCustomOptions_UsesProvidedOptions()
    {
        // Arrange
        var options = new SerializationOptions
        {
            EnableCompression = true,
            MaxMessageSize = 4096,
            IncludeTypeInformation = true
        };

        // Act
        var serializer = new TypedProtobufMessageSerializer(options);

        // Assert
        Assert.NotNull(serializer);
        Assert.Equal("application/x-protobuf-typed", serializer.ContentType);
    }

    [Fact]
    public void Constructor_WithCustomTypeModel_UsesProvidedTypeModel()
    {
        // Arrange
        var typeModel = RuntimeTypeModel.Create();

        // Act
        var serializer = new TypedProtobufMessageSerializer(typeModel: typeModel);

        // Assert
        Assert.NotNull(serializer);
    }

    [Fact]
    public void Constructor_WithAllParameters_UsesProvidedValues()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true, IncludeTypeInformation = true };
        var typeModel = RuntimeTypeModel.Create();
        var compressionProvider = new GZipCompressionProvider();

        // Act
        var serializer = new TypedProtobufMessageSerializer(options, typeModel, compressionProvider);

        // Assert
        Assert.NotNull(serializer);
    }

    #endregion

    #region ContentType Tests

    [Fact]
    public void ContentType_ReturnsCorrectValue()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();

        // Act
        var contentType = serializer.ContentType;

        // Assert
        Assert.Equal("application/x-protobuf-typed", contentType);
    }

    #endregion

    #region SerializeAsync Tests

    [Fact]
    public async Task SerializeAsync_WithValidMessage_ReturnsSerializedData()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        var message = new TypedTestMessage
        {
            Name = "TypedProtobufTest",
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
        var serializer = new TypedProtobufMessageSerializer();

        // Act
        var result = await serializer.SerializeAsync<TypedTestMessage>(null!);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task SerializeAsync_WithTypeInformationEnabled_IncludesTypeData()
    {
        // Arrange
        var options = new SerializationOptions { IncludeTypeInformation = true };
        var serializer = new TypedProtobufMessageSerializer(options);
        var message = new TypedTestMessage { Name = "WithType", Value = 100 };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        // Result should be larger than without type information due to type prefix
    }

    [Fact]
    public async Task SerializeAsync_WithTypeInformationDisabled_ExcludesTypeData()
    {
        // Arrange
        var options = new SerializationOptions { IncludeTypeInformation = false };
        var serializer = new TypedProtobufMessageSerializer(options);
        var message = new TypedTestMessage { Name = "WithoutType", Value = 200 };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public async Task SerializeAsync_WithComplexMessage_SerializesCorrectly()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        var message = new TypedComplexMessage
        {
            Id = "complex-typed-protobuf",
            Nested = new TypedTestMessage { Name = "Nested", Value = 99 },
            Tags = new[] { "tag1", "tag2", "tag3" }
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
        var serializer = new TypedProtobufMessageSerializer(options);
        var message = new TypedTestMessage { Name = "This is a very long message name" };

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
        var serializer = new TypedProtobufMessageSerializer(options);
        var message = new TypedTestMessage
        {
            Name = "Compression test with typed protobuf serializer",
            Value = 300
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
        var serializer = new TypedProtobufMessageSerializer();
        var message = new TypedTestMessage { Name = "CancellationTest", Value = 400 };
        var cts = new CancellationTokenSource();

        // Act
        var result = await serializer.SerializeAsync(message, cts.Token);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    #endregion

    #region Serialize (Span) Tests

    [Fact]
    public void Serialize_WithValidMessage_WritesToSpan()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        var message = new TypedTestMessage { Name = "SpanTypedProtobuf", Value = 500 };
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
        var serializer = new TypedProtobufMessageSerializer();
        Span<byte> buffer = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize<TypedTestMessage>(null!, buffer);

        // Assert
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void Serialize_WithTypeInformationEnabled_IncludesTypeData()
    {
        // Arrange
        var options = new SerializationOptions { IncludeTypeInformation = true };
        var serializer = new TypedProtobufMessageSerializer(options);
        var message = new TypedTestMessage { Name = "SpanWithType", Value = 600 };
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
        var serializer = new TypedProtobufMessageSerializer();
        var message = new TypedTestMessage { Name = "SmallBuffer", Value = 700 };
        Span<byte> buffer = new byte[5];

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
        var serializer = new TypedProtobufMessageSerializer();
        var message = new TypedTestMessage { Name = "TryTypedProtobuf", Value = 800 };
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
        var serializer = new TypedProtobufMessageSerializer();
        Span<byte> buffer = new byte[4096];

        // Act
        var success = serializer.TrySerialize<TypedTestMessage>(null!, buffer, out var bytesWritten);

        // Assert
        Assert.True(success);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TrySerialize_WithTooSmallBuffer_ReturnsFalse()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        var message = new TypedTestMessage { Name = "TooSmall", Value = 900 };
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
        var serializer = new TypedProtobufMessageSerializer();
        var message = new TypedTestMessage { Name = "BufferSize", Value = 1000 };

        // Act
        var bufferSize = serializer.GetRequiredBufferSize(message);

        // Assert
        Assert.True(bufferSize > 0);
        Assert.Equal(2048 + 256, bufferSize); // Base + type overhead
    }

    #endregion

    #region DeserializeAsync Tests

    [Fact]
    public async Task DeserializeAsync_WithValidData_ReturnsMessage()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        var original = new TypedTestMessage
        {
            Name = "DeserializeTypedProtobuf",
            Value = 1100,
            Timestamp = new DateTime(2024, 6, 15, 12, 30, 0, DateTimeKind.Utc)
        };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync<TypedTestMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Value, result.Value);
    }

    [Fact]
    public async Task DeserializeAsync_WithNullData_ReturnsDefault()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();

        // Act
        var result = await serializer.DeserializeAsync<TypedTestMessage>(null!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DeserializeAsync_WithEmptyData_ReturnsDefault()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();

        // Act
        var result = await serializer.DeserializeAsync<TypedTestMessage>(Array.Empty<byte>());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DeserializeAsync_WithTypeInformation_UsesEmbeddedType()
    {
        // Arrange
        var options = new SerializationOptions { IncludeTypeInformation = true };
        var serializer = new TypedProtobufMessageSerializer(options);
        var original = new TypedTestMessage { Name = "TypeInfo", Value = 1200 };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync<TypedTestMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Value, result.Value);
    }

    [Fact]
    public async Task DeserializeAsync_WithComplexMessage_DeserializesCorrectly()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        var original = new TypedComplexMessage
        {
            Id = "deserialize-complex-typed",
            Nested = new TypedTestMessage { Name = "DeserializeNested", Value = 1300 },
            Tags = new[] { "x", "y", "z" }
        };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync<TypedComplexMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
        Assert.NotNull(result.Nested);
        Assert.Equal(original.Nested.Name, result.Nested.Name);
        Assert.Equal(3, result.Tags.Length);
    }

    [Fact]
    public async Task DeserializeAsync_WithCompressedData_DecompressesAndDeserializes()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var serializer = new TypedProtobufMessageSerializer(options);
        var original = new TypedTestMessage { Name = "CompressedTyped", Value = 1400 };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync<TypedTestMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Value, result.Value);
    }

    [Fact]
    public async Task DeserializeAsync_TypedOverload_WithValidData_ReturnsObject()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        var original = new TypedTestMessage { Name = "TypedOverload", Value = 1500 };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync(data, typeof(TypedTestMessage));

        // Assert
        Assert.NotNull(result);
        Assert.IsType<TypedTestMessage>(result);
        var typedResult = (TypedTestMessage)result;
        Assert.Equal(original.Name, typedResult.Name);
        Assert.Equal(original.Value, typedResult.Value);
    }

    [Fact]
    public async Task DeserializeAsync_TypedOverload_WithTypeInformation_UsesEmbeddedType()
    {
        // Arrange
        var options = new SerializationOptions { IncludeTypeInformation = true };
        var serializer = new TypedProtobufMessageSerializer(options);
        var original = new TypedTestMessage { Name = "EmbeddedType", Value = 1600 };
        var data = await serializer.SerializeAsync(original);

        // Act - Pass base type, should deserialize to actual type
        var result = await serializer.DeserializeAsync(data, typeof(object));

        // Assert
        Assert.NotNull(result);
        Assert.IsType<TypedTestMessage>(result);
        var typedResult = (TypedTestMessage)result;
        Assert.Equal(original.Name, typedResult.Name);
    }

    [Fact]
    public async Task DeserializeAsync_TypedOverload_WithNullData_ReturnsNull()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();

        // Act
        var result = await serializer.DeserializeAsync(null!, typeof(TypedTestMessage));

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Deserialize (Span) Tests

    [Fact]
    public void Deserialize_WithValidData_ReturnsMessage()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        var original = new TypedTestMessage { Name = "SpanDeserializeTyped", Value = 1700 };
        Span<byte> buffer = new byte[4096];
        var bytesWritten = serializer.Serialize(original, buffer);
        var data = buffer.Slice(0, bytesWritten);

        // Act
        var result = serializer.Deserialize<TypedTestMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Value, result.Value);
    }

    [Fact]
    public void Deserialize_WithEmptySpan_ReturnsDefault()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        ReadOnlySpan<byte> data = ReadOnlySpan<byte>.Empty;

        // Act
        var result = serializer.Deserialize<TypedTestMessage>(data);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_TypedOverload_WithValidData_ReturnsObject()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        var original = new TypedTestMessage { Name = "TypedSpanOverload", Value = 1800 };
        Span<byte> buffer = new byte[4096];
        var bytesWritten = serializer.Serialize(original, buffer);
        var data = buffer.Slice(0, bytesWritten);

        // Act
        var result = serializer.Deserialize(data, typeof(TypedTestMessage));

        // Assert
        Assert.NotNull(result);
        Assert.IsType<TypedTestMessage>(result);
        var typedResult = (TypedTestMessage)result;
        Assert.Equal(original.Name, typedResult.Name);
    }

    [Fact]
    public void Deserialize_TypedOverload_WithEmptySpan_ReturnsNull()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        ReadOnlySpan<byte> data = ReadOnlySpan<byte>.Empty;

        // Act
        var result = serializer.Deserialize(data, typeof(TypedTestMessage));

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public async Task RoundTrip_WithSimpleMessage_PreservesData()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        var original = new TypedTestMessage
        {
            Name = "RoundTripTyped",
            Value = 1900,
            Timestamp = DateTime.UtcNow
        };

        // Act
        var serialized = await serializer.SerializeAsync(original);
        var deserialized = await serializer.DeserializeAsync<TypedTestMessage>(serialized);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Value, deserialized.Value);
    }

    [Fact]
    public async Task RoundTrip_WithComplexMessage_PreservesData()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        var original = new TypedComplexMessage
        {
            Id = "roundtrip-complex-typed",
            Nested = new TypedTestMessage { Name = "RoundTripNested", Value = 2000 },
            Tags = new[] { "tag1", "tag2", "tag3", "tag4" }
        };

        // Act
        var serialized = await serializer.SerializeAsync(original);
        var deserialized = await serializer.DeserializeAsync<TypedComplexMessage>(serialized);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.NotNull(deserialized.Nested);
        Assert.Equal(original.Nested.Name, deserialized.Nested.Name);
        Assert.Equal(original.Tags.Length, deserialized.Tags.Length);
    }

    [Fact]
    public async Task RoundTrip_WithTypeInformation_PreservesData()
    {
        // Arrange
        var options = new SerializationOptions { IncludeTypeInformation = true };
        var serializer = new TypedProtobufMessageSerializer(options);
        var original = new TypedTestMessage { Name = "RoundTripWithType", Value = 2100 };

        // Act
        var serialized = await serializer.SerializeAsync(original);
        var deserialized = await serializer.DeserializeAsync<TypedTestMessage>(serialized);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Value, deserialized.Value);
    }

    [Fact]
    public async Task RoundTrip_WithCompression_PreservesData()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var serializer = new TypedProtobufMessageSerializer(options);
        var original = new TypedTestMessage { Name = "CompressedRoundTripTyped", Value = 2200 };

        // Act
        var serialized = await serializer.SerializeAsync(original);
        var deserialized = await serializer.DeserializeAsync<TypedTestMessage>(serialized);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Value, deserialized.Value);
    }

    [Fact]
    public void RoundTrip_Span_WithSimpleMessage_PreservesData()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        var original = new TypedTestMessage { Name = "SpanRoundTripTyped", Value = 2300 };
        Span<byte> buffer = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(original, buffer);
        var deserialized = serializer.Deserialize<TypedTestMessage>(buffer.Slice(0, bytesWritten));

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Value, deserialized.Value);
    }

    #endregion

    #region Polymorphism Tests

    [Fact]
    public async Task RoundTrip_WithPolymorphicMessage_PreservesActualType()
    {
        // Arrange
        var options = new SerializationOptions { IncludeTypeInformation = true };
        var serializer = new TypedProtobufMessageSerializer(options);
        var original = new DerivedMessage
        {
            BaseProperty = "BaseValue",
            DerivedProperty = "DerivedValue"
        };

        // Act
        var serialized = await serializer.SerializeAsync(original);
        var deserialized = await serializer.DeserializeAsync(serialized, typeof(BaseMessage));

        // Assert
        Assert.NotNull(deserialized);
        Assert.IsType<DerivedMessage>(deserialized);
        var derivedResult = (DerivedMessage)deserialized;
        Assert.Equal(original.BaseProperty, derivedResult.BaseProperty);
        Assert.Equal(original.DerivedProperty, derivedResult.DerivedProperty);
    }

    #endregion
}

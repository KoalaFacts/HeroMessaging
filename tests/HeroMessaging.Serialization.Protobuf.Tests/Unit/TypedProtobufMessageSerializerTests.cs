using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.Protobuf;
using ProtoBuf;
using Xunit;

namespace HeroMessaging.Serialization.Protobuf.Tests.Unit;

/// <summary>
/// Unit tests for TypedProtobufMessageSerializer with type information
/// </summary>
[Trait("Category", "Unit")]
public class TypedProtobufMessageSerializerTests
{
    #region Test Models

    [ProtoContract]
    public class TypedMessage
    {
        [ProtoMember(1)]
        public string Id { get; set; } = string.Empty;

        [ProtoMember(2)]
        public string Content { get; set; } = string.Empty;

        [ProtoMember(3)]
        public int Count { get; set; }
    }

    [ProtoContract]
    public class NestedTypedMessage
    {
        [ProtoMember(1)]
        public TypedMessage? Inner { get; set; }

        [ProtoMember(2)]
        public string Label { get; set; } = string.Empty;
    }

    #endregion

    #region Positive Cases - SerializeAsync with Type Information

    [Fact]
    public async Task SerializeAsync_WithTypeInformationEnabled_SerializesSuccessfully()
    {
        // Arrange
        var options = new SerializationOptions { IncludeTypeInformation = true };
        var serializer = new TypedProtobufMessageSerializer(options);
        var message = new TypedMessage { Id = "123", Content = "Test", Count = 42 };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotEmpty(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public async Task SerializeAsync_WithTypeInformationDisabled_SerializesSuccessfully()
    {
        // Arrange
        var options = new SerializationOptions { IncludeTypeInformation = false };
        var serializer = new TypedProtobufMessageSerializer(options);
        var message = new TypedMessage { Id = "123", Content = "Test", Count = 42 };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task SerializeAsync_WithNullMessage_ReturnsEmptyArray()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        TypedMessage? message = null;

        // Act
        var result = await serializer.SerializeAsync(message!);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task SerializeAsync_WithNestedObjects_SerializesSuccessfully()
    {
        // Arrange
        var options = new SerializationOptions { IncludeTypeInformation = true };
        var serializer = new TypedProtobufMessageSerializer(options);
        var inner = new TypedMessage { Id = "inner", Content = "Nested", Count = 99 };
        var message = new NestedTypedMessage { Inner = inner, Label = "test" };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotEmpty(result);
    }

    #endregion

    #region Positive Cases - Serialize (Span)

    [Fact]
    public void Serialize_WithValidMessage_ReturnsBytesWritten()
    {
        // Arrange
        var options = new SerializationOptions { IncludeTypeInformation = true };
        var serializer = new TypedProtobufMessageSerializer(options);
        var message = new TypedMessage { Id = "123", Content = "Test", Count = 42 };
        var destination = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(message, destination);

        // Assert
        Assert.Greater(bytesWritten, 0);
    }

    [Fact]
    public void Serialize_WithNullMessage_ReturnsZero()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        TypedMessage? message = null;
        var destination = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(message!, destination);

        // Assert
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void Serialize_WithSmallBuffer_ThrowsException()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        var message = new TypedMessage { Id = "123", Content = "Test", Count = 42 };
        var destination = new byte[2];

        // Act & Assert
        Assert.Throws<ArgumentException>(() => serializer.Serialize(message, destination));
    }

    #endregion

    #region Positive Cases - TrySerialize

    [Fact]
    public void TrySerialize_WithValidMessage_ReturnsTrue()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        var message = new TypedMessage { Id = "123", Content = "Test", Count = 42 };
        var destination = new byte[4096];

        // Act
        var result = serializer.TrySerialize(message, destination, out var bytesWritten);

        // Assert
        Assert.True(result);
        Assert.Greater(bytesWritten, 0);
    }

    [Fact]
    public void TrySerialize_WithSmallBuffer_ReturnsFalse()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        var message = new TypedMessage { Id = "123", Content = "Test", Count = 42 };
        var destination = new byte[2];

        // Act
        var result = serializer.TrySerialize(message, destination, out var bytesWritten);

        // Assert
        Assert.False(result);
        Assert.Equal(0, bytesWritten);
    }

    #endregion

    #region Positive Cases - Deserialize

    [Fact]
    public async Task DeserializeAsync_WithValidData_ReturnsOriginalMessage()
    {
        // Arrange
        var options = new SerializationOptions { IncludeTypeInformation = true };
        var serializer = new TypedProtobufMessageSerializer(options);
        var original = new TypedMessage { Id = "123", Content = "Test", Count = 42 };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync<TypedMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
        Assert.Equal(original.Content, result.Content);
        Assert.Equal(original.Count, result.Count);
    }

    [Fact]
    public async Task DeserializeAsync_WithEmptyData_ReturnsNull()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();

        // Act
        var result = await serializer.DeserializeAsync<TypedMessage>(Array.Empty<byte>());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_WithValidSpan_ReturnsMessage()
    {
        // Arrange
        var options = new SerializationOptions { IncludeTypeInformation = true };
        var serializer = new TypedProtobufMessageSerializer(options);
        var original = new TypedMessage { Id = "123", Content = "Test", Count = 42 };
        var buffer = new byte[4096];
        var bytesWritten = serializer.Serialize(original, buffer);
        var span = new ReadOnlySpan<byte>(buffer, 0, bytesWritten);

        // Act
        var result = serializer.Deserialize<TypedMessage>(span);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
    }

    [Fact]
    public void Deserialize_WithEmptySpan_ReturnsNull()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();

        // Act
        var result = serializer.Deserialize<TypedMessage>(ReadOnlySpan<byte>.Empty);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Positive Cases - Non-Generic Deserialize

    [Fact]
    public async Task DeserializeAsync_WithTypeParameter_ReturnsObject()
    {
        // Arrange
        var options = new SerializationOptions { IncludeTypeInformation = true };
        var serializer = new TypedProtobufMessageSerializer(options);
        var original = new TypedMessage { Id = "123", Content = "Test", Count = 42 };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync(data, typeof(TypedMessage));

        // Assert
        Assert.NotNull(result);
        Assert.IsType<TypedMessage>(result);
    }

    [Fact]
    public void Deserialize_WithTypeParameter_ReturnsObject()
    {
        // Arrange
        var options = new SerializationOptions { IncludeTypeInformation = true };
        var serializer = new TypedProtobufMessageSerializer(options);
        var original = new TypedMessage { Id = "123", Content = "Test", Count = 42 };
        var buffer = new byte[4096];
        var bytesWritten = serializer.Serialize(original, buffer);
        var span = new ReadOnlySpan<byte>(buffer, 0, bytesWritten);

        // Act
        var result = serializer.Deserialize(span, typeof(TypedMessage));

        // Assert
        Assert.NotNull(result);
        Assert.IsType<TypedMessage>(result);
    }

    #endregion

    #region Positive Cases - Content Type and Buffer Size

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

    [Fact]
    public void GetRequiredBufferSize_ReturnsPositiveValue()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        var message = new TypedMessage { Id = "123", Content = "Test", Count = 42 };

        // Act
        var size = serializer.GetRequiredBufferSize(message);

        // Assert
        Assert.Greater(size, 0);
    }

    [Fact]
    public void GetRequiredBufferSize_WithTypeInfo_IncludesOverhead()
    {
        // Arrange
        var optionsWithType = new SerializationOptions { IncludeTypeInformation = true };
        var optionsWithoutType = new SerializationOptions { IncludeTypeInformation = false };
        var serializerWith = new TypedProtobufMessageSerializer(optionsWithType);
        var serializerWithout = new TypedProtobufMessageSerializer(optionsWithoutType);
        var message = new TypedMessage { Id = "123", Content = "Test", Count = 42 };

        // Act
        var sizeWith = serializerWith.GetRequiredBufferSize(message);
        var sizeWithout = serializerWithout.GetRequiredBufferSize(message);

        // Assert
        // With type info should be larger due to overhead
        Assert.Greater(sizeWith, sizeWithout);
    }

    #endregion

    #region Positive Cases - Compression

    [Fact]
    public async Task SerializeAsync_WithCompressionEnabled_CompressesData()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true, IncludeTypeInformation = true };
        var serializer = new TypedProtobufMessageSerializer(options);
        var message = new TypedMessage { Id = "123", Content = "Test Message", Count = 42 };

        // Act
        var compressedData = await serializer.SerializeAsync(message);
        var uncompressedSerializer = new TypedProtobufMessageSerializer(
            new SerializationOptions { IncludeTypeInformation = true });
        var uncompressedData = await uncompressedSerializer.SerializeAsync(message);

        // Assert
        Assert.True(compressedData.Length <= uncompressedData.Length);
    }

    [Fact]
    public async Task DeserializeAsync_WithCompressedData_ReturnsOriginalMessage()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true, IncludeTypeInformation = true };
        var serializer = new TypedProtobufMessageSerializer(options);
        var original = new TypedMessage { Id = "123", Content = "Test", Count = 42 };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync<TypedMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
    }

    #endregion

    #region Negative Cases - Max Message Size

    [Fact]
    public async Task SerializeAsync_WithExceededMaxSize_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new SerializationOptions { MaxMessageSize = 10 };
        var serializer = new TypedProtobufMessageSerializer(options);
        var message = new TypedMessage { Id = "123456789", Content = "TestMessage", Count = 42 };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => serializer.SerializeAsync(message));
    }

    #endregion

    #region Round Trip Tests

    [Fact]
    public async Task RoundTrip_WithValidMessage_PreservesData()
    {
        // Arrange
        var options = new SerializationOptions { IncludeTypeInformation = true };
        var serializer = new TypedProtobufMessageSerializer(options);
        var original = new TypedMessage { Id = "123", Content = "Test", Count = 42 };

        // Act
        var data = await serializer.SerializeAsync(original);
        var result = await serializer.DeserializeAsync<TypedMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
        Assert.Equal(original.Content, result.Content);
        Assert.Equal(original.Count, result.Count);
    }

    [Fact]
    public async Task RoundTrip_WithNestedObjects_PreservesData()
    {
        // Arrange
        var options = new SerializationOptions { IncludeTypeInformation = true };
        var serializer = new TypedProtobufMessageSerializer(options);
        var inner = new TypedMessage { Id = "inner", Content = "Nested", Count = 99 };
        var original = new NestedTypedMessage { Inner = inner, Label = "test" };

        // Act
        var data = await serializer.SerializeAsync(original);
        var result = await serializer.DeserializeAsync<NestedTypedMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Inner);
        Assert.Equal(original.Inner?.Id, result.Inner.Id);
        Assert.Equal(original.Label, result.Label);
    }

    #endregion

    #region Edge Cases - Cancellation Token

    [Fact]
    public async Task SerializeAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        var message = new TypedMessage { Id = "123", Content = "Test", Count = 42 };
        var cts = new CancellationTokenSource();

        // Act
        var result = await serializer.SerializeAsync(message, cts.Token);

        // Assert
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task DeserializeAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var serializer = new TypedProtobufMessageSerializer();
        var original = new TypedMessage { Id = "123", Content = "Test", Count = 42 };
        var data = await serializer.SerializeAsync(original);
        var cts = new CancellationTokenSource();

        // Act
        var result = await serializer.DeserializeAsync<TypedMessage>(data, cts.Token);

        // Assert
        Assert.NotNull(result);
    }

    #endregion
}

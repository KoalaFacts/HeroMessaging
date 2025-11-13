using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.MessagePack;
using MessagePack;
using Xunit;

namespace HeroMessaging.Serialization.MessagePack.Tests.Unit;

/// <summary>
/// Unit tests for ContractMessagePackSerializer with MessagePack contracts
/// </summary>
public class ContractMessagePackSerializerTests
{
    [MessagePackObject]
    public class ContractMessage
    {
        [Key(0)]
        public string Id { get; set; } = string.Empty;

        [Key(1)]
        public int Count { get; set; }
    }

    [MessagePackObject]
    public class ContractNestedMessage
    {
        [Key(0)]
        public ContractMessage Inner { get; set; } = new();

        [Key(1)]
        public string Label { get; set; } = string.Empty;
    }

    #region Positive Cases - SerializeAsync

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithValidMessage_ReturnsSerializedBytes()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var message = new ContractMessage { Id = "123", Count = 42 };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotEmpty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithNullMessage_ReturnsEmptyArray()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        ContractMessage? message = null;

        // Act
        var result = await serializer.SerializeAsync(message!);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithNestedObjects_SerializesSuccessfully()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var inner = new ContractMessage { Id = "inner", Count = 99 };
        var message = new ContractNestedMessage { Inner = inner, Label = "test" };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotEmpty(result);
    }

    #endregion

    #region Positive Cases - Serialize (Span)

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_WithValidMessage_ReturnsBytesWritten()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var message = new ContractMessage { Id = "123", Count = 42 };
        var destination = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(message, destination);

        // Assert
        Assert.Greater(bytesWritten, 0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_WithNullMessage_ReturnsZero()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        ContractMessage? message = null;
        var destination = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(message!, destination);

        // Assert
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_WithSmallBuffer_ThrowsException()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var message = new ContractMessage { Id = "123", Count = 42 };
        var destination = new byte[2];

        // Act & Assert
        Assert.Throws<ArgumentException>(() => serializer.Serialize(message, destination));
    }

    #endregion

    #region Positive Cases - TrySerialize

    [Fact]
    [Trait("Category", "Unit")]
    public void TrySerialize_WithValidMessage_ReturnsTrue()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var message = new ContractMessage { Id = "123", Count = 42 };
        var destination = new byte[4096];

        // Act
        var result = serializer.TrySerialize(message, destination, out var bytesWritten);

        // Assert
        Assert.True(result);
        Assert.Greater(bytesWritten, 0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TrySerialize_WithSmallBuffer_ReturnsFalse()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var message = new ContractMessage { Id = "123", Count = 42 };
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
    [Trait("Category", "Unit")]
    public async Task DeserializeAsync_WithValidData_ReturnsOriginalMessage()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var original = new ContractMessage { Id = "123", Count = 42 };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync<ContractMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
        Assert.Equal(original.Count, result.Count);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeserializeAsync_WithEmptyData_ReturnsNull()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();

        // Act
        var result = await serializer.DeserializeAsync<ContractMessage>(Array.Empty<byte>());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Deserialize_WithValidSpan_ReturnsMessage()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var original = new ContractMessage { Id = "123", Count = 42 };
        var buffer = new byte[4096];
        var bytesWritten = serializer.Serialize(original, buffer);
        var span = new ReadOnlySpan<byte>(buffer, 0, bytesWritten);

        // Act
        var result = serializer.Deserialize<ContractMessage>(span);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Deserialize_WithEmptySpan_ReturnsNull()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();

        // Act
        var result = serializer.Deserialize<ContractMessage>(ReadOnlySpan<byte>.Empty);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Positive Cases - Non-Generic Deserialize

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeserializeAsync_WithTypeParameter_ReturnsObject()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var original = new ContractMessage { Id = "123", Count = 42 };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync(data, typeof(ContractMessage));

        // Assert
        Assert.NotNull(result);
        Assert.IsType<ContractMessage>(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Deserialize_WithTypeParameter_ReturnsObject()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var original = new ContractMessage { Id = "123", Count = 42 };
        var buffer = new byte[4096];
        var bytesWritten = serializer.Serialize(original, buffer);
        var span = new ReadOnlySpan<byte>(buffer, 0, bytesWritten);

        // Act
        var result = serializer.Deserialize(span, typeof(ContractMessage));

        // Assert
        Assert.NotNull(result);
        Assert.IsType<ContractMessage>(result);
    }

    #endregion

    #region Positive Cases - Content Type and Buffer Size

    [Fact]
    [Trait("Category", "Unit")]
    public void ContentType_ReturnsCorrectValue()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();

        // Act
        var contentType = serializer.ContentType;

        // Assert
        Assert.Equal("application/x-msgpack-contract", contentType);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRequiredBufferSize_ReturnsPositiveValue()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var message = new ContractMessage { Id = "123", Count = 42 };

        // Act
        var size = serializer.GetRequiredBufferSize(message);

        // Assert
        Assert.Greater(size, 0);
    }

    #endregion

    #region Positive Cases - Compression

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithCompressionEnabled_CompressesData()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var serializer = new ContractMessagePackSerializer(options);
        var message = new ContractMessage { Id = "123", Count = 42 };

        // Act
        var compressedData = await serializer.SerializeAsync(message);

        // Serialize without compression
        var uncompressedSerializer = new ContractMessagePackSerializer();
        var uncompressedData = await uncompressedSerializer.SerializeAsync(message);

        // Assert
        Assert.True(compressedData.Length <= uncompressedData.Length);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeserializeAsync_WithCompressedData_ReturnsOriginalMessage()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var serializer = new ContractMessagePackSerializer(options);
        var original = new ContractMessage { Id = "123", Count = 42 };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync<ContractMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
    }

    #endregion

    #region Negative Cases - Max Message Size

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithExceededMaxSize_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new SerializationOptions { MaxMessageSize = 10 };
        var serializer = new ContractMessagePackSerializer(options);
        var message = new ContractMessage { Id = "123456789", Count = 42 };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => serializer.SerializeAsync(message));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithinMaxSize_Succeeds()
    {
        // Arrange
        var options = new SerializationOptions { MaxMessageSize = 10000 };
        var serializer = new ContractMessagePackSerializer(options);
        var message = new ContractMessage { Id = "123", Count = 42 };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotEmpty(result);
    }

    #endregion

    #region Edge Cases - Nested Objects

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RoundTrip_WithNestedObjects_PreservesData()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var inner = new ContractMessage { Id = "inner", Count = 99 };
        var original = new ContractNestedMessage { Inner = inner, Label = "test" };

        // Act
        var data = await serializer.SerializeAsync(original);
        var result = await serializer.DeserializeAsync<ContractNestedMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Inner);
        Assert.Equal(original.Inner.Id, result.Inner.Id);
        Assert.Equal(original.Label, result.Label);
    }

    #endregion

    #region Edge Cases - Cancellation Token

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var message = new ContractMessage { Id = "123", Count = 42 };
        var cts = new CancellationTokenSource();

        // Act
        var result = await serializer.SerializeAsync(message, cts.Token);

        // Assert
        Assert.NotEmpty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeserializeAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var original = new ContractMessage { Id = "123", Count = 42 };
        var data = await serializer.SerializeAsync(original);
        var cts = new CancellationTokenSource();

        // Act
        var result = await serializer.DeserializeAsync<ContractMessage>(data, cts.Token);

        // Assert
        Assert.NotNull(result);
    }

    #endregion

    #region Performance Cases

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithLargePayload_CompletesSuccessfully()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var messages = Enumerable.Range(0, 100)
            .Select(i => new ContractMessage { Id = $"msg-{i}", Count = i })
            .ToList();

        // Act & Assert
        foreach (var msg in messages)
        {
            var result = await serializer.SerializeAsync(msg);
            Assert.NotEmpty(result);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RoundTrip_WithMultipleMessages_PreservesAllData()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var messages = Enumerable.Range(0, 50)
            .Select(i => new ContractMessage { Id = $"msg-{i}", Count = i })
            .ToList();

        // Act & Assert
        foreach (var original in messages)
        {
            var data = await serializer.SerializeAsync(original);
            var result = await serializer.DeserializeAsync<ContractMessage>(data);
            Assert.NotNull(result);
            Assert.Equal(original.Id, result.Id);
            Assert.Equal(original.Count, result.Count);
        }
    }

    #endregion
}

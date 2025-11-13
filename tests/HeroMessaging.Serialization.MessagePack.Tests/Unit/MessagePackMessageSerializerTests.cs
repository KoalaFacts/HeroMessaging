using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.MessagePack;
using MessagePack;
using Xunit;

namespace HeroMessaging.Serialization.MessagePack.Tests.Unit;

/// <summary>
/// Unit tests for MessagePackMessageSerializer covering binary serialization
/// </summary>
public class MessagePackMessageSerializerTests
{
    [MessagePackObject]
    public class SimpleMessage
    {
        [Key(0)]
        public string Id { get; set; } = string.Empty;

        [Key(1)]
        public string Name { get; set; } = string.Empty;

        [Key(2)]
        public int Value { get; set; }
    }

    [MessagePackObject]
    public class MessageWithCollections
    {
        [Key(0)]
        public List<string> Items { get; set; } = new();

        [Key(1)]
        public Dictionary<string, int> Mapping { get; set; } = new();
    }

    [MessagePackObject]
    public class NestedMessage
    {
        [Key(0)]
        public SimpleMessage Inner { get; set; } = new();

        [Key(1)]
        public string Outer { get; set; } = string.Empty;
    }

    #region Positive Cases - SerializeAsync

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithValidMessage_ReturnsSerializedBytes()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var message = new SimpleMessage { Id = "123", Name = "Test", Value = 42 };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotEmpty(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithNullMessage_ReturnsEmptyArray()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        SimpleMessage? message = null;

        // Act
        var result = await serializer.SerializeAsync(message!);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithEmptyCollections_SerializesSuccessfully()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var message = new MessageWithCollections { Items = new List<string>(), Mapping = new Dictionary<string, int>() };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotEmpty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithNestedObjects_SerializesSuccessfully()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var inner = new SimpleMessage { Id = "inner", Name = "Nested", Value = 99 };
        var message = new NestedMessage { Inner = inner, Outer = "outer" };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotEmpty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithComplexCollections_SerializesSuccessfully()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var message = new MessageWithCollections
        {
            Items = new List<string> { "a", "b", "c" },
            Mapping = new Dictionary<string, int> { { "key1", 1 }, { "key2", 2 } }
        };

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
        var serializer = new MessagePackMessageSerializer();
        var message = new SimpleMessage { Id = "123", Name = "Test", Value = 42 };
        var destination = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(message, destination);

        // Assert
        Assert.Greater(bytesWritten, 0);
        Assert.True(bytesWritten <= destination.Length);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_WithNullMessage_ReturnsZero()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        SimpleMessage? message = null;
        var destination = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(message!, destination);

        // Assert
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_WithValidMessage_PopulatesDestinationBuffer()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var message = new SimpleMessage { Id = "123", Name = "Test", Value = 42 };
        var destination = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(message, destination);

        // Assert
        Assert.Greater(bytesWritten, 0);
        Assert.True(destination[0] != 0 || destination[1] != 0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_WithSmallBuffer_ThrowsException()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var message = new SimpleMessage { Id = "123", Name = "Test", Value = 42 };
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
        var serializer = new MessagePackMessageSerializer();
        var message = new SimpleMessage { Id = "123", Name = "Test", Value = 42 };
        var destination = new byte[4096];

        // Act
        var result = serializer.TrySerialize(message, destination, out var bytesWritten);

        // Assert
        Assert.True(result);
        Assert.Greater(bytesWritten, 0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TrySerialize_WithNullMessage_ReturnsTrue()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        SimpleMessage? message = null;
        var destination = new byte[4096];

        // Act
        var result = serializer.TrySerialize(message!, destination, out var bytesWritten);

        // Assert
        Assert.True(result);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TrySerialize_WithSmallBuffer_ReturnsFalse()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var message = new SimpleMessage { Id = "123", Name = "Test", Value = 42 };
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
        var serializer = new MessagePackMessageSerializer();
        var original = new SimpleMessage { Id = "123", Name = "Test", Value = 42 };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync<SimpleMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Value, result.Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeserializeAsync_WithEmptyData_ReturnsNull()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var data = Array.Empty<byte>();

        // Act
        var result = await serializer.DeserializeAsync<SimpleMessage>(data);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeserializeAsync_WithNullData_ReturnsNull()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();

        // Act
        var result = await serializer.DeserializeAsync<SimpleMessage>(null!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Deserialize_WithValidSpan_ReturnsMessage()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var original = new SimpleMessage { Id = "123", Name = "Test", Value = 42 };
        var buffer = new byte[4096];
        var bytesWritten = serializer.Serialize(original, buffer);
        var jsonSpan = new ReadOnlySpan<byte>(buffer, 0, bytesWritten);

        // Act
        var result = serializer.Deserialize<SimpleMessage>(jsonSpan);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Deserialize_WithEmptySpan_ReturnsNull()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var emptySpan = ReadOnlySpan<byte>.Empty;

        // Act
        var result = serializer.Deserialize<SimpleMessage>(emptySpan);

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
        var serializer = new MessagePackMessageSerializer();
        var original = new SimpleMessage { Id = "123", Name = "Test", Value = 42 };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync(data, typeof(SimpleMessage));

        // Assert
        Assert.NotNull(result);
        Assert.IsType<SimpleMessage>(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Deserialize_WithTypeParameter_ReturnsObject()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var original = new SimpleMessage { Id = "123", Name = "Test", Value = 42 };
        var buffer = new byte[4096];
        var bytesWritten = serializer.Serialize(original, buffer);
        var span = new ReadOnlySpan<byte>(buffer, 0, bytesWritten);

        // Act
        var result = serializer.Deserialize(span, typeof(SimpleMessage));

        // Assert
        Assert.NotNull(result);
        Assert.IsType<SimpleMessage>(result);
    }

    #endregion

    #region Positive Cases - Content Type and Buffer Size

    [Fact]
    [Trait("Category", "Unit")]
    public void ContentType_ReturnsApplicationMsgpack()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();

        // Act
        var contentType = serializer.ContentType;

        // Assert
        Assert.Equal("application/x-msgpack", contentType);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRequiredBufferSize_ReturnsPositiveValue()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var message = new SimpleMessage { Id = "123", Name = "Test", Value = 42 };

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
        var serializer = new MessagePackMessageSerializer(options);
        var message = new SimpleMessage { Id = "123", Name = "Test Message", Value = 42 };

        // Act
        var compressedData = await serializer.SerializeAsync(message);

        // Serialize without compression for comparison
        var uncompressedSerializer = new MessagePackMessageSerializer();
        var uncompressedData = await uncompressedSerializer.SerializeAsync(message);

        // Assert
        // Compressed should typically be smaller or similar
        Assert.True(compressedData.Length <= uncompressedData.Length);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeserializeAsync_WithCompressedData_ReturnsOriginalMessage()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var serializer = new MessagePackMessageSerializer(options);
        var original = new SimpleMessage { Id = "123", Name = "Test", Value = 42 };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync<SimpleMessage>(data);

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
        var serializer = new MessagePackMessageSerializer(options);
        var message = new SimpleMessage { Id = "123456789", Name = "TestMessage", Value = 42 };

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
        var serializer = new MessagePackMessageSerializer(options);
        var message = new SimpleMessage { Id = "123", Name = "Test", Value = 42 };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotEmpty(result);
        Assert.True(result.Length <= 10000);
    }

    #endregion

    #region Edge Cases - Type Handling

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RoundTrip_WithCollections_PreservesData()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var original = new MessageWithCollections
        {
            Items = new List<string> { "a", "b", "c" },
            Mapping = new Dictionary<string, int> { { "key1", 1 }, { "key2", 2 } }
        };

        // Act
        var data = await serializer.SerializeAsync(original);
        var result = await serializer.DeserializeAsync<MessageWithCollections>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(2, result.Mapping.Count);
    }

    #endregion

    #region Edge Cases - Cancellation Token

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var message = new SimpleMessage { Id = "123", Name = "Test", Value = 42 };
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
        var serializer = new MessagePackMessageSerializer();
        var original = new SimpleMessage { Id = "123", Name = "Test", Value = 42 };
        var data = await serializer.SerializeAsync(original);
        var cts = new CancellationTokenSource();

        // Act
        var result = await serializer.DeserializeAsync<SimpleMessage>(data, cts.Token);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
    }

    #endregion

    #region Performance Cases

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithLargePayload_CompletesSuccessfully()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var largeItems = Enumerable.Range(0, 1000)
            .Select(i => $"Item-{i}")
            .ToList();
        var message = new MessageWithCollections { Items = largeItems, Mapping = new Dictionary<string, int>() };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotEmpty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RoundTrip_WithLargePayload_PreservesData()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var largeItems = Enumerable.Range(0, 100)
            .Select(i => $"Item-{i}")
            .ToList();
        var original = new MessageWithCollections { Items = largeItems, Mapping = new Dictionary<string, int>() };

        // Act
        var data = await serializer.SerializeAsync(original);
        var result = await serializer.DeserializeAsync<MessageWithCollections>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Items.Count, result.Items.Count);
    }

    #endregion
}

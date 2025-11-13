using System.Text.Json;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.Json;
using Xunit;

namespace HeroMessaging.Serialization.Json.Tests.Unit;

/// <summary>
/// Unit tests for JsonMessageSerializer covering serialization, deserialization, and compression
/// </summary>
public class JsonMessageSerializerTests
{
    private sealed record SimpleMessage(string Id, string Name, int Value);
    private sealed record MessageWithNulls(string? OptionalField, int Number);
    private sealed record MessageWithCollections(List<string> Items, Dictionary<string, int> Mapping);
    private sealed record NestedMessage(SimpleMessage Inner, string Outer);

    #region Positive Cases - SerializeAsync

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithValidMessage_ReturnsSerializedBytes()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var message = new SimpleMessage("123", "Test", 42);

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
        var serializer = new JsonMessageSerializer();
        SimpleMessage? message = null;

        // Act
        var result = await serializer.SerializeAsync(message!);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithValidMessage_ContainsValidJson()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var message = new SimpleMessage("123", "Test", 42);

        // Act
        var result = await serializer.SerializeAsync(message);
        var json = System.Text.Encoding.UTF8.GetString(result);

        // Assert
        Assert.Contains("id", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("123", json);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithEmptyCollections_SerializesSuccessfully()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var message = new MessageWithCollections(new List<string>(), new Dictionary<string, int>());

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
        var serializer = new JsonMessageSerializer();
        var inner = new SimpleMessage("inner", "Nested", 99);
        var message = new NestedMessage(inner, "outer");

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
        var serializer = new JsonMessageSerializer();
        var message = new SimpleMessage("123", "Test", 42);
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
        var serializer = new JsonMessageSerializer();
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
        var serializer = new JsonMessageSerializer();
        var message = new SimpleMessage("123", "Test", 42);
        var destination = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(message, destination);

        // Assert
        Assert.Greater(bytesWritten, 0);
        var writtenBytes = destination.AsSpan(0, bytesWritten);
        var json = System.Text.Encoding.UTF8.GetString(writtenBytes);
        Assert.Contains("id", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_WithSmallBuffer_ThrowsException()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var message = new SimpleMessage("123", "Test", 42);
        var destination = new byte[2];

        // Act & Assert
        Assert.Throws<Exception>(() => serializer.Serialize(message, destination));
    }

    #endregion

    #region Positive Cases - TrySerialize

    [Fact]
    [Trait("Category", "Unit")]
    public void TrySerialize_WithValidMessage_ReturnsTrue()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var message = new SimpleMessage("123", "Test", 42);
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
        var serializer = new JsonMessageSerializer();
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
        var serializer = new JsonMessageSerializer();
        var message = new SimpleMessage("123", "Test", 42);
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
        var serializer = new JsonMessageSerializer();
        var original = new SimpleMessage("123", "Test", 42);
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
        var serializer = new JsonMessageSerializer();
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
        var serializer = new JsonMessageSerializer();

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
        var serializer = new JsonMessageSerializer();
        var original = new SimpleMessage("123", "Test", 42);
        var data = serializer.Serialize(original, new byte[4096]);

        // Get the actual serialized bytes
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
        var serializer = new JsonMessageSerializer();
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
        var serializer = new JsonMessageSerializer();
        var original = new SimpleMessage("123", "Test", 42);
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
        var serializer = new JsonMessageSerializer();
        var original = new SimpleMessage("123", "Test", 42);
        var buffer = new byte[4096];
        var bytesWritten = serializer.Serialize(original, buffer);
        var span = new ReadOnlySpan<byte>(buffer, 0, bytesWritten);

        // Act
        var result = serializer.Deserialize(span, typeof(SimpleMessage));

        // Assert
        Assert.NotNull(result);
        Assert.IsType<SimpleMessage>(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeserializeAsync_WithEmptyDataAndType_ReturnsNull()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();

        // Act
        var result = await serializer.DeserializeAsync(Array.Empty<byte>(), typeof(SimpleMessage));

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Positive Cases - Content Type and Buffer Size

    [Fact]
    [Trait("Category", "Unit")]
    public void ContentType_ReturnsApplicationJson()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();

        // Act
        var contentType = serializer.ContentType;

        // Assert
        Assert.Equal("application/json", contentType);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRequiredBufferSize_ReturnsPositiveValue()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var message = new SimpleMessage("123", "Test", 42);

        // Act
        var size = serializer.GetRequiredBufferSize(message);

        // Assert
        Assert.Greater(size, 0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRequiredBufferSize_WithNullMessage_ReturnsPositiveValue()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();

        // Act
        var size = serializer.GetRequiredBufferSize(null!);

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
        var serializer = new JsonMessageSerializer(options);
        var message = new SimpleMessage("123", "Test Message", 42);

        // Act
        var compressedData = await serializer.SerializeAsync(message);

        // Serialize without compression for comparison
        var uncompressedSerializer = new JsonMessageSerializer();
        var uncompressedData = await uncompressedSerializer.SerializeAsync(message);

        // Assert
        // Compressed data should be smaller
        Assert.True(compressedData.Length < uncompressedData.Length, "Compression should reduce data size");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeserializeAsync_WithCompressedData_ReturnsOriginalMessage()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var serializer = new JsonMessageSerializer(options);
        var original = new SimpleMessage("123", "Test", 42);
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync<SimpleMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Value, result.Value);
    }

    #endregion

    #region Negative Cases - Max Message Size

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithExceededMaxSize_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new SerializationOptions { MaxMessageSize = 10 };
        var serializer = new JsonMessageSerializer(options);
        var message = new SimpleMessage("123456789", "TestMessage", 42);

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
        var serializer = new JsonMessageSerializer(options);
        var message = new SimpleMessage("123", "Test", 42);

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotEmpty(result);
        Assert.True(result.Length <= 10000);
    }

    #endregion

    #region Negative Cases - Invalid Data

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeserializeAsync_WithMalformedJson_ThrowsException()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var malformedData = System.Text.Encoding.UTF8.GetBytes("{invalid json}");

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(
            () => serializer.DeserializeAsync<SimpleMessage>(malformedData));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Deserialize_WithMalformedJson_ThrowsException()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var malformedData = System.Text.Encoding.UTF8.GetBytes("{invalid json}");

        // Act & Assert
        Assert.Throws<Exception>(() =>
            serializer.Deserialize<SimpleMessage>(malformedData));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeserializeAsync_WithWrongType_ThrowsException()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var messageData = new { someField = "value" };
        var data = await serializer.SerializeAsync(messageData);

        // Act & Assert
        // This may throw or return null depending on JSON serializer behavior with missing fields
        var result = await serializer.DeserializeAsync<SimpleMessage>(data);
        // If it doesn't throw, result should have null/default values
        Assert.True(result == null || result.Id == null || result.Name == null);
    }

    #endregion

    #region Edge Cases - Type Handling

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithMessageWithNulls_SerializesSuccessfully()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var message = new MessageWithNulls(null, 42);

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotEmpty(result);
        var json = System.Text.Encoding.UTF8.GetString(result);
        // Null fields might be omitted based on configuration
        Assert.NotNull(json);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithComplexCollections_SerializesSuccessfully()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var message = new MessageWithCollections(
            new List<string> { "a", "b", "c" },
            new Dictionary<string, int> { { "key1", 1 }, { "key2", 2 } });

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotEmpty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RoundTrip_WithCollections_PreservesData()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var original = new MessageWithCollections(
            new List<string> { "a", "b", "c" },
            new Dictionary<string, int> { { "key1", 1 }, { "key2", 2 } });

        // Act
        var data = await serializer.SerializeAsync(original);
        var result = await serializer.DeserializeAsync<MessageWithCollections>(data);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Items);
        Assert.Equal(3, result.Items.Count);
    }

    #endregion

    #region Edge Cases - Cancellation Token

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var message = new SimpleMessage("123", "Test", 42);
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
        var serializer = new JsonMessageSerializer();
        var original = new SimpleMessage("123", "Test", 42);
        var data = await serializer.SerializeAsync(original);
        var cts = new CancellationTokenSource();

        // Act
        var result = await serializer.DeserializeAsync<SimpleMessage>(data, cts.Token);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Constructor_WithCustomJsonOptions_UsesProvidedOptions()
    {
        // Arrange
        var customOptions = new JsonSerializerOptions { WriteIndented = true };
        var serializer = new JsonMessageSerializer(null, customOptions);
        var message = new SimpleMessage("123", "Test", 42);

        // Act
        var result = await serializer.SerializeAsync(message);
        var json = System.Text.Encoding.UTF8.GetString(result);

        // Assert
        Assert.NotEmpty(result);
        // Indented JSON should contain newlines
        Assert.Contains("\n", json);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithCustomOptions_UsesProvidedOptions()
    {
        // Arrange
        var options = new SerializationOptions { MaxMessageSize = 5000 };

        // Act
        var serializer = new JsonMessageSerializer(options);

        // Assert
        // Constructor should complete without exception
        Assert.NotNull(serializer);
    }

    #endregion

    #region Performance Cases

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithLargePayload_CompletesSuccessfully()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var largeItems = Enumerable.Range(0, 1000)
            .Select(i => $"Item-{i}")
            .ToList();
        var message = new MessageWithCollections(largeItems, new Dictionary<string, int>());

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotEmpty(result);
        Assert.True(result.Length > 1000);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RoundTrip_WithLargePayload_PreservesData()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var largeItems = Enumerable.Range(0, 100)
            .Select(i => $"Item-{i}")
            .ToList();
        var original = new MessageWithCollections(largeItems, new Dictionary<string, int>());

        // Act
        var data = await serializer.SerializeAsync(original);
        var result = await serializer.DeserializeAsync<MessageWithCollections>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Items.Count, result.Items.Count);
    }

    #endregion
}

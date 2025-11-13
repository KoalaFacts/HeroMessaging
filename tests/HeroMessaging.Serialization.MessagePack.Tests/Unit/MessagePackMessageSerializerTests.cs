using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.MessagePack;
using MessagePack;
using Moq;
using Xunit;

namespace HeroMessaging.Serialization.MessagePack.Tests.Unit;

/// <summary>
/// Unit tests for MessagePackMessageSerializer class
/// </summary>
public class MessagePackMessageSerializerTests
{
    [MessagePackObject]
    private class TestMessage
    {
        [Key(0)]
        public int Id { get; set; }

        [Key(1)]
        public string Name { get; set; } = string.Empty;

        [Key(2)]
        public decimal Value { get; set; }

        [Key(3)]
        public string[] Tags { get; set; } = Array.Empty<string>();
    }

    [MessagePackObject]
    private class ComplexTestMessage
    {
        [Key(0)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Key(1)]
        public TestMessage? Nested { get; set; }

        [Key(2)]
        public List<TestMessage>? Items { get; set; }
    }

    #region Constructor Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithDefaults_CreatesSerializer()
    {
        // Act
        var serializer = new MessagePackMessageSerializer();

        // Assert
        Assert.NotNull(serializer);
        Assert.Equal("application/x-msgpack", serializer.ContentType);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithCustomOptions_SetsOptions()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true, MaxMessageSize = 2048 };

        // Act
        var serializer = new MessagePackMessageSerializer(options);

        // Assert
        Assert.NotNull(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithMessagePackOptions_SetsOptions()
    {
        // Arrange
        var msgPackOptions = MessagePackSerializerOptions.Standard;

        // Act
        var serializer = new MessagePackMessageSerializer(messagePackOptions: msgPackOptions);

        // Assert
        Assert.NotNull(serializer);
    }

    #endregion

    #region ContentType Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ContentType_ReturnsCorrectType()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();

        // Act
        var contentType = serializer.ContentType;

        // Assert
        Assert.Equal("application/x-msgpack", contentType);
    }

    #endregion

    #region SerializeAsync Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithValidMessage_ReturnsBytes()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var message = new TestMessage { Id = 1, Name = "Test", Value = 99.99m, Tags = new[] { "tag1", "tag2" } };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithNullMessage_ReturnsEmptyArray()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();

        // Act
        var result = await serializer.SerializeAsync<TestMessage>(null!);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithComplexMessage_ReturnsBytes()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var message = new ComplexTestMessage
        {
            Id = "test-id",
            Nested = new TestMessage { Id = 1, Name = "Nested", Value = 10.5m },
            Items = new List<TestMessage>
            {
                new() { Id = 2, Name = "Item1", Value = 20.0m },
                new() { Id = 3, Name = "Item2", Value = 30.0m }
            }
        };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WhenMaxSizeExceeded_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new SerializationOptions { MaxMessageSize = 10 };
        var serializer = new MessagePackMessageSerializer(options);
        var message = new TestMessage { Id = 1, Name = "Test message with long name", Value = 99.99m };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await serializer.SerializeAsync(message));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithCompressionEnabled_CompressesData()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var serializer = new MessagePackMessageSerializer(options);
        var message = new TestMessage
        {
            Id = 1,
            Name = "Test message for compression testing with repeated data",
            Value = 99.99m,
            Tags = new[] { "tag1", "tag2", "tag3", "tag4", "tag5" }
        };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var message = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };
        using var cts = new CancellationTokenSource();

        // Act
        var result = await serializer.SerializeAsync(message, cts.Token);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var message = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await serializer.SerializeAsync(message, cts.Token));
    }

    #endregion

    #region Serialize (Span) Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_WithValidMessage_WritesToSpan()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var message = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };
        var buffer = new byte[1024];

        // Act
        var bytesWritten = serializer.Serialize(message, buffer);

        // Assert
        Assert.True(bytesWritten > 0);
        Assert.True(bytesWritten <= buffer.Length);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_WithNullMessage_ReturnsZero()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var buffer = new byte[1024];

        // Act
        var bytesWritten = serializer.Serialize<TestMessage>(null!, buffer);

        // Assert
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_WithSmallBuffer_ThrowsException()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
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
        var serializer = new MessagePackMessageSerializer();
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
    }

    #endregion

    #region TrySerialize Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void TrySerialize_WithValidMessage_ReturnsTrue()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var message = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };
        var buffer = new byte[1024];

        // Act
        var success = serializer.TrySerialize(message, buffer, out var bytesWritten);

        // Assert
        Assert.True(success);
        Assert.True(bytesWritten > 0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TrySerialize_WithSmallBuffer_ReturnsFalse()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var message = new TestMessage { Id = 1, Name = "Test message", Value = 99.99m };
        var buffer = new byte[5];

        // Act
        var success = serializer.TrySerialize(message, buffer, out var bytesWritten);

        // Assert
        Assert.False(success);
        Assert.Equal(0, bytesWritten);
    }

    #endregion

    #region GetRequiredBufferSize Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRequiredBufferSize_ReturnsPositiveValue()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
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
        var serializer = new MessagePackMessageSerializer();
        var message = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };
        var requiredSize = serializer.GetRequiredBufferSize(message);

        // Act
        var buffer = new byte[requiredSize];
        var bytesWritten = serializer.Serialize(message, buffer);

        // Assert
        Assert.True(bytesWritten > 0);
        Assert.True(bytesWritten <= requiredSize);
    }

    #endregion

    #region DeserializeAsync Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeserializeAsync_WithValidData_ReturnsMessage()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var original = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync<TestMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
        Assert.Equal(original.Name, result.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeserializeAsync_WithEmptyArray_ReturnsNull()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var data = Array.Empty<byte>();

        // Act
        var result = await serializer.DeserializeAsync<TestMessage>(data);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeserializeAsync_WithCompressedData_ReturnsMessage()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var serializer = new MessagePackMessageSerializer(options);
        var original = new TestMessage { Id = 1, Name = "Compression Test", Value = 99.99m };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync<TestMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
        Assert.Equal(original.Name, result.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeserializeAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
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
        var serializer = new MessagePackMessageSerializer();
        var original = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync(data, typeof(TestMessage));

        // Assert
        Assert.NotNull(result);
        Assert.IsType<TestMessage>(result);
    }

    #endregion

    #region Deserialize (Span) Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Deserialize_WithValidData_ReturnsMessage()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var original = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };
        var data = MessagePackSerializer.Serialize(original);

        // Act
        var result = serializer.Deserialize<TestMessage>(data);

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
        var data = ReadOnlySpan<byte>.Empty;

        // Act
        var result = serializer.Deserialize<TestMessage>(data);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Deserialize_WithDynamicType_ReturnsObject()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var original = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };
        var data = MessagePackSerializer.Serialize(original);

        // Act
        var result = serializer.Deserialize(data, typeof(TestMessage));

        // Assert
        Assert.NotNull(result);
        Assert.IsType<TestMessage>(result);
    }

    #endregion

    #region RoundTrip Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RoundTrip_AsyncSerialization_PreservesData()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var original = new TestMessage { Id = 42, Name = "RoundTrip", Value = 123.45m, Tags = new[] { "tag1", "tag2" } };

        // Act
        var serialized = await serializer.SerializeAsync(original);
        var deserialized = await serializer.DeserializeAsync<TestMessage>(serialized);

        // Assert
        Assert.NotNull(deserialized);
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
        var serializer = new MessagePackMessageSerializer(options);
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
        var serializer = new MessagePackMessageSerializer();
        var original = new ComplexTestMessage
        {
            Id = "complex-id",
            Nested = new TestMessage { Id = 1, Name = "Nested", Value = 10.5m },
            Items = new List<TestMessage>
            {
                new() { Id = 2, Name = "Item1", Value = 20.0m },
                new() { Id = 3, Name = "Item2", Value = 30.0m }
            }
        };

        // Act
        var serialized = await serializer.SerializeAsync(original);
        var deserialized = await serializer.DeserializeAsync<ComplexTestMessage>(serialized);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.NotNull(deserialized.Nested);
        Assert.Equal(original.Nested.Name, deserialized.Nested.Name);
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
        var serializer = new MessagePackMessageSerializer();
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
        var serializer = new MessagePackMessageSerializer();
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
        var serializer = new MessagePackMessageSerializer();
        var message = new TestMessage { Id = 1, Name = "Test\"with'special\\chars/and\"quotes", Value = 99.99m };

        // Act
        var serialized = await serializer.SerializeAsync(message);
        var deserialized = await serializer.DeserializeAsync<TestMessage>(serialized);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(message.Name, deserialized.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RoundTrip_WithUnicodeCharacters_PreservesData()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
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
            var serializer = new MessagePackMessageSerializer(options);
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
        var uncompressedSerializer = new MessagePackMessageSerializer();
        var compressedOptions = new SerializationOptions { EnableCompression = true };
        var compressedSerializer = new MessagePackMessageSerializer(compressedOptions);

        // Act
        var uncompressedData = await uncompressedSerializer.SerializeAsync(message);
        var compressedData = await compressedSerializer.SerializeAsync(message);
        var uncompressedResult = await uncompressedSerializer.DeserializeAsync<TestMessage>(uncompressedData);
        var compressedResult = await compressedSerializer.DeserializeAsync<TestMessage>(compressedData);

        // Assert
        Assert.Equal(uncompressedResult.Id, compressedResult.Id);
        Assert.Equal(uncompressedResult.Name, compressedResult.Name);
        Assert.Equal(uncompressedResult.Value, compressedResult.Value);
    }

    #endregion
}

using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.MessagePack;
using MessagePack;
<<<<<<< HEAD
=======
using Moq;
>>>>>>> testing/serialization
using Xunit;

namespace HeroMessaging.Serialization.MessagePack.Tests.Unit;

/// <summary>
<<<<<<< HEAD
/// Unit tests for MessagePackMessageSerializer covering binary serialization
=======
/// Unit tests for MessagePackMessageSerializer class
>>>>>>> testing/serialization
/// </summary>
public class MessagePackMessageSerializerTests
{
    [MessagePackObject]
<<<<<<< HEAD
    public class SimpleMessage
    {
        [Key(0)]
        public string Id { get; set; } = string.Empty;
=======
    internal class TestMessage
    {
        [Key(0)]
        public int Id { get; set; }
>>>>>>> testing/serialization

        [Key(1)]
        public string Name { get; set; } = string.Empty;

        [Key(2)]
<<<<<<< HEAD
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
=======
        public decimal Value { get; set; }

        [Key(3)]
        public string[] Tags { get; set; } = Array.Empty<string>();
    }

    [MessagePackObject]
    internal class ComplexTestMessage
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
>>>>>>> testing/serialization

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
<<<<<<< HEAD
        Assert.NotEmpty(result);
        Assert.True(result.Length > 0);
=======
        Assert.NotNull(result);
        Assert.NotEmpty(result);
>>>>>>> testing/serialization
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithNullMessage_ReturnsEmptyArray()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
<<<<<<< HEAD
        SimpleMessage? message = null;

        // Act
        var result = await serializer.SerializeAsync(message!);

        // Assert
=======

        // Act
        var result = await serializer.SerializeAsync<TestMessage>(null!);

        // Assert
        Assert.NotNull(result);
>>>>>>> testing/serialization
        Assert.Empty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
<<<<<<< HEAD
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
=======
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
>>>>>>> testing/serialization
        };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
<<<<<<< HEAD
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
=======
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
>>>>>>> testing/serialization
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_WithNullMessage_ReturnsZero()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
<<<<<<< HEAD
        SimpleMessage? message = null;
        var destination = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(message!, destination);
=======
        var buffer = new byte[1024];

        // Act
        var bytesWritten = serializer.Serialize<TestMessage>(null!, buffer);
>>>>>>> testing/serialization

        // Assert
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    [Trait("Category", "Unit")]
<<<<<<< HEAD
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
=======
>>>>>>> testing/serialization
    public void Serialize_WithSmallBuffer_ThrowsException()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
<<<<<<< HEAD
        var message = new SimpleMessage { Id = "123", Name = "Test", Value = 42 };
        var destination = new byte[2];

        // Act & Assert
        Assert.Throws<ArgumentException>(() => serializer.Serialize(message, destination));
=======
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
>>>>>>> testing/serialization
    }

    #endregion

<<<<<<< HEAD
    #region Positive Cases - TrySerialize
=======
    #region TrySerialize Tests
>>>>>>> testing/serialization

    [Fact]
    [Trait("Category", "Unit")]
    public void TrySerialize_WithValidMessage_ReturnsTrue()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
<<<<<<< HEAD
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
=======
        var message = new TestMessage { Id = 1, Name = "Test", Value = 99.99m };
        var buffer = new byte[1024];

        // Act
        var success = serializer.TrySerialize(message, buffer, out var bytesWritten);

        // Assert
        Assert.True(success);
        Assert.True(bytesWritten > 0);
>>>>>>> testing/serialization
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TrySerialize_WithSmallBuffer_ReturnsFalse()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
<<<<<<< HEAD
        var message = new SimpleMessage { Id = "123", Name = "Test", Value = 42 };
        var destination = new byte[2];

        // Act
        var result = serializer.TrySerialize(message, destination, out var bytesWritten);

        // Assert
        Assert.False(result);
=======
        var message = new TestMessage { Id = 1, Name = "Test message", Value = 99.99m };
        var buffer = new byte[5];

        // Act
        var success = serializer.TrySerialize(message, buffer, out var bytesWritten);

        // Assert
        Assert.False(success);
>>>>>>> testing/serialization
        Assert.Equal(0, bytesWritten);
    }

    #endregion

<<<<<<< HEAD
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
=======
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
>>>>>>> testing/serialization

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
        Assert.Equal(original.Name, result.Name);
<<<<<<< HEAD
        Assert.Equal(original.Value, result.Value);
=======
>>>>>>> testing/serialization
    }

    [Fact]
    [Trait("Category", "Unit")]
<<<<<<< HEAD
    public async Task DeserializeAsync_WithEmptyData_ReturnsNull()
=======
    public async Task DeserializeAsync_WithEmptyArray_ReturnsNull()
>>>>>>> testing/serialization
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var data = Array.Empty<byte>();

        // Act
<<<<<<< HEAD
        var result = await serializer.DeserializeAsync<SimpleMessage>(data);
=======
        var result = await serializer.DeserializeAsync<TestMessage>(data);
>>>>>>> testing/serialization

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
<<<<<<< HEAD
    public async Task DeserializeAsync_WithNullData_ReturnsNull()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();

        // Act
        var result = await serializer.DeserializeAsync<SimpleMessage>(null!);

        // Assert
        Assert.Null(result);
=======
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
>>>>>>> testing/serialization
    }

    [Fact]
    [Trait("Category", "Unit")]
<<<<<<< HEAD
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
=======
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
>>>>>>> testing/serialization

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
<<<<<<< HEAD
        var emptySpan = ReadOnlySpan<byte>.Empty;

        // Act
        var result = serializer.Deserialize<SimpleMessage>(emptySpan);
=======
        var data = ReadOnlySpan<byte>.Empty;

        // Act
        var result = serializer.Deserialize<TestMessage>(data);
>>>>>>> testing/serialization

        // Assert
        Assert.Null(result);
    }

<<<<<<< HEAD
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
=======
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
>>>>>>> testing/serialization
    }

    #endregion

<<<<<<< HEAD
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
=======
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
>>>>>>> testing/serialization
    }

    [Fact]
    [Trait("Category", "Unit")]
<<<<<<< HEAD
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
=======
    public async Task RoundTrip_WithCompression_PreservesData()
>>>>>>> testing/serialization
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var serializer = new MessagePackMessageSerializer(options);
<<<<<<< HEAD
        var message = new SimpleMessage { Id = "123", Name = "Test Message", Value = 42 };

        // Act
        var compressedData = await serializer.SerializeAsync(message);

        // Serialize without compression for comparison
        var uncompressedSerializer = new MessagePackMessageSerializer();
        var uncompressedData = await uncompressedSerializer.SerializeAsync(message);

        // Assert
        // Compressed should typically be smaller or similar
        Assert.True(compressedData.Length <= uncompressedData.Length);
=======
        var original = new TestMessage { Id = 42, Name = "Compressed RoundTrip", Value = 999.99m };

        // Act
        var serialized = await serializer.SerializeAsync(original);
        var deserialized = await serializer.DeserializeAsync<TestMessage>(serialized);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.Name, deserialized.Name);
>>>>>>> testing/serialization
    }

    [Fact]
    [Trait("Category", "Unit")]
<<<<<<< HEAD
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
=======
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
>>>>>>> testing/serialization
    }

    #endregion

<<<<<<< HEAD
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
=======
    #region Edge Cases

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SerializeAsync_WithEmptyMessage_Succeeds()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var message = new TestMessage { Id = 0, Name = "", Value = 0 };
>>>>>>> testing/serialization

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
<<<<<<< HEAD
=======
        Assert.NotNull(result);
>>>>>>> testing/serialization
        Assert.NotEmpty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
<<<<<<< HEAD
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
=======
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
>>>>>>> testing/serialization
    }

    #endregion
}

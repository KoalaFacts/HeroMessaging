using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.Json;
using Moq;
using Xunit;

namespace HeroMessaging.Serialization.Json.Tests;

[Trait("Category", "Unit")]
public class JsonMessageSerializerTests
{
    #region Test Models

    public class TestMessage
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class ComplexMessage
    {
        public string Id { get; set; } = string.Empty;
        public TestMessage? Nested { get; set; }
        public string[] Tags { get; set; } = Array.Empty<string>();
    }

    public class LargeMessage
    {
        public string Data { get; set; } = string.Empty;
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullOptions_UsesDefaults()
    {
        // Arrange & Act
        var serializer = new JsonMessageSerializer();

        // Assert
        Assert.NotNull(serializer);
        Assert.Equal("application/json", serializer.ContentType);
    }

    [Fact]
    public void Constructor_WithCustomOptions_UsesProvidedOptions()
    {
        // Arrange
        var options = new SerializationOptions
        {
            EnableCompression = true,
            MaxMessageSize = 1024
        };

        // Act
        var serializer = new JsonMessageSerializer(options);

        // Assert
        Assert.NotNull(serializer);
        Assert.Equal("application/json", serializer.ContentType);
    }

    [Fact]
    public void Constructor_WithCustomJsonOptions_UsesProvidedJsonOptions()
    {
        // Arrange
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        // Act
        var serializer = new JsonMessageSerializer(jsonOptions: jsonOptions);

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
        var serializer = new JsonMessageSerializer(options, compressionProvider: mockProvider.Object);

        // Assert
        Assert.NotNull(serializer);
    }

    #endregion

    #region ContentType Tests

    [Fact]
    public void ContentType_ReturnsCorrectValue()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();

        // Act
        var contentType = serializer.ContentType;

        // Assert
        Assert.Equal("application/json", contentType);
    }

    #endregion

    #region SerializeAsync Tests

    [Fact]
    public async Task SerializeAsync_WithValidMessage_ReturnsSerializedData()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
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
        var json = Encoding.UTF8.GetString(result);
        Assert.Contains("\"name\":\"Test\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"value\":42", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SerializeAsync_WithNullMessage_ReturnsEmptyArray()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();

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
        var serializer = new JsonMessageSerializer();
        var message = new ComplexMessage
        {
            Id = "123",
            Nested = new TestMessage { Name = "Nested", Value = 99 },
            Tags = new[] { "tag1", "tag2", "tag3" }
        };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        var json = Encoding.UTF8.GetString(result);
        Assert.Contains("\"id\":\"123\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"nested\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"tags\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SerializeAsync_WithMaxMessageSizeExceeded_ThrowsException()
    {
        // Arrange
        var options = new SerializationOptions { MaxMessageSize = 10 };
        var serializer = new JsonMessageSerializer(options);
        var message = new TestMessage { Name = "This is a long message that will exceed the limit" };

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
        var serializer = new JsonMessageSerializer(options);
        var message = new TestMessage
        {
            Name = "Test with compression enabled to verify compression works",
            Value = 42
        };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        // Compressed data should not be valid UTF-8 JSON
        var isCompressed = !IsValidJson(result);
        Assert.True(isCompressed);
    }

    [Fact]
    public async Task SerializeAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var message = new TestMessage { Name = "Test", Value = 42 };
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
        var serializer = new JsonMessageSerializer();
        var message = new TestMessage { Name = "Test", Value = 42 };
        Span<byte> buffer = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(message, buffer);

        // Assert
        Assert.True(bytesWritten > 0);
        var json = Encoding.UTF8.GetString(buffer.Slice(0, bytesWritten));
        Assert.Contains("\"name\":\"Test\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"value\":42", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_WithNullMessage_ReturnsZero()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
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
        var serializer = new JsonMessageSerializer();
        var message = new ComplexMessage
        {
            Id = "456",
            Nested = new TestMessage { Name = "Inner", Value = 77 },
            Tags = new[] { "a", "b" }
        };
        Span<byte> buffer = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(message, buffer);

        // Assert
        Assert.True(bytesWritten > 0);
        var json = Encoding.UTF8.GetString(buffer.Slice(0, bytesWritten));
        Assert.Contains("\"id\":\"456\"", json, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region TrySerialize Tests

    [Fact]
    public void TrySerialize_WithValidMessage_ReturnsTrue()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var message = new TestMessage { Name = "Test", Value = 42 };
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
        var serializer = new JsonMessageSerializer();
        Span<byte> buffer = new byte[4096];

        // Act
        var success = serializer.TrySerialize<TestMessage>(null!, buffer, out var bytesWritten);

        // Assert
        Assert.True(success);
        Assert.Equal(0, bytesWritten);
    }

    #endregion

    #region GetRequiredBufferSize Tests

    [Fact]
    public void GetRequiredBufferSize_ReturnsPositiveValue()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var message = new TestMessage { Name = "Test", Value = 42 };

        // Act
        var bufferSize = serializer.GetRequiredBufferSize(message);

        // Assert
        Assert.True(bufferSize > 0);
        Assert.Equal(4096, bufferSize); // Default estimate
    }

    #endregion

    #region DeserializeAsync Tests

    [Fact]
    public async Task DeserializeAsync_WithValidData_ReturnsMessage()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var original = new TestMessage
        {
            Name = "Test",
            Value = 42,
            Timestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
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
        var serializer = new JsonMessageSerializer();

        // Act
        var result = await serializer.DeserializeAsync<TestMessage>(null!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DeserializeAsync_WithEmptyData_ReturnsDefault()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();

        // Act
        var result = await serializer.DeserializeAsync<TestMessage>(Array.Empty<byte>());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DeserializeAsync_WithComplexMessage_DeserializesCorrectly()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var original = new ComplexMessage
        {
            Id = "789",
            Nested = new TestMessage { Name = "Child", Value = 33 },
            Tags = new[] { "x", "y", "z" }
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
    }

    [Fact]
    public async Task DeserializeAsync_WithCompressedData_DecompressesAndDeserializes()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var serializer = new JsonMessageSerializer(options);
        var original = new TestMessage { Name = "Compressed Test", Value = 999 };
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
        var serializer = new JsonMessageSerializer();
        var original = new TestMessage { Name = "TypedTest", Value = 123 };
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
        var serializer = new JsonMessageSerializer();

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
        var serializer = new JsonMessageSerializer();
        var original = new TestMessage { Name = "SpanTest", Value = 555 };
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
        var serializer = new JsonMessageSerializer();
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
        var serializer = new JsonMessageSerializer();
        var original = new TestMessage { Name = "TypedSpan", Value = 666 };
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
        var serializer = new JsonMessageSerializer();
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
        var serializer = new JsonMessageSerializer();
        var original = new TestMessage
        {
            Name = "RoundTrip",
            Value = 12345,
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
        var serializer = new JsonMessageSerializer();
        var original = new ComplexMessage
        {
            Id = "complex-1",
            Nested = new TestMessage { Name = "NestedRoundTrip", Value = 888 },
            Tags = new[] { "tag1", "tag2", "tag3", "tag4" }
        };

        // Act
        var serialized = await serializer.SerializeAsync(original);
        var deserialized = await serializer.DeserializeAsync<ComplexMessage>(serialized);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.NotNull(deserialized.Nested);
        Assert.Equal(original.Nested.Name, deserialized.Nested.Name);
        Assert.Equal(original.Nested.Value, deserialized.Nested.Value);
        Assert.Equal(original.Tags.Length, deserialized.Tags.Length);
    }

    [Fact]
    public async Task RoundTrip_WithCompression_PreservesData()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var serializer = new JsonMessageSerializer(options);
        var original = new TestMessage { Name = "CompressedRoundTrip", Value = 777 };

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
        var serializer = new JsonMessageSerializer();
        var original = new TestMessage { Name = "SpanRoundTrip", Value = 333 };
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

    #region Helper Methods

    private static bool IsValidJson(byte[] data)
    {
        try
        {
            var json = Encoding.UTF8.GetString(data);
            JsonDocument.Parse(json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion
}

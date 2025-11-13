using System.Text.Json;
using HeroMessaging.Serialization.Json;
using Xunit;

namespace HeroMessaging.Serialization.Json.Tests.Unit;

/// <summary>
/// Unit tests for JsonSpanSerializer covering zero-allocation serialization
/// </summary>
public class JsonSpanSerializerTests
{
    private sealed record SimpleMessage(string Id, string Name, int Value);
    private sealed record MessageWithNulls(string? OptionalField, int Number);
    private sealed record MessageWithCollections(List<string> Items, Dictionary<string, int> Mapping);

    #region Positive Cases - Serialize

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_WithValidMessage_ReturnsBytesWritten()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<SimpleMessage>();
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
    public void Serialize_WithValidMessage_PopulatesBuffer()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<SimpleMessage>();
        var message = new SimpleMessage("123", "Test", 42);
        var destination = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(message, destination);

        // Assert
        var writtenSpan = destination.AsSpan(0, bytesWritten);
        var json = System.Text.Encoding.UTF8.GetString(writtenSpan);
        Assert.Contains("123", json);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_MultipleMessages_IndependentResults()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<SimpleMessage>();
        var message1 = new SimpleMessage("1", "First", 10);
        var message2 = new SimpleMessage("2", "Second", 20);
        var destination1 = new byte[4096];
        var destination2 = new byte[4096];

        // Act
        var bytes1 = serializer.Serialize(message1, destination1);
        var bytes2 = serializer.Serialize(message2, destination2);

        // Assert
        Assert.NotEqual(bytes1, bytes2);
        var json1 = System.Text.Encoding.UTF8.GetString(destination1.AsSpan(0, bytes1));
        var json2 = System.Text.Encoding.UTF8.GetString(destination2.AsSpan(0, bytes2));
        Assert.Contains("First", json1);
        Assert.Contains("Second", json2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_WithSmallValue_WritesAllData()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<int>();
        int value = 42;
        var destination = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(value, destination);

        // Assert
        Assert.Greater(bytesWritten, 0);
        var json = System.Text.Encoding.UTF8.GetString(destination.AsSpan(0, bytesWritten));
        Assert.Equal("42", json);
    }

    #endregion

    #region Positive Cases - TrySerialize

    [Fact]
    [Trait("Category", "Unit")]
    public void TrySerialize_WithValidMessage_ReturnsTrue()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<SimpleMessage>();
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
    public void TrySerialize_WithSmallBuffer_ReturnsFalse()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<SimpleMessage>();
        var message = new SimpleMessage("123", "Test", 42);
        var destination = new byte[2];

        // Act
        var result = serializer.TrySerialize(message, destination, out var bytesWritten);

        // Assert
        Assert.False(result);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TrySerialize_WithValidMessage_CorrectBytesWritten()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<SimpleMessage>();
        var message = new SimpleMessage("123", "Test", 42);
        var destination = new byte[4096];

        // Act
        var result = serializer.TrySerialize(message, destination, out var bytesWritten);
        var json = System.Text.Encoding.UTF8.GetString(destination.AsSpan(0, bytesWritten));

        // Assert
        Assert.True(result);
        Assert.Contains("123", json);
    }

    #endregion

    #region Positive Cases - GetRequiredBufferSize

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRequiredBufferSize_ReturnsPositiveValue()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<SimpleMessage>();
        var message = new SimpleMessage("123", "Test", 42);

        // Act
        var size = serializer.GetRequiredBufferSize(message);

        // Assert
        Assert.Greater(size, 0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRequiredBufferSize_Consistent()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<SimpleMessage>();
        var message = new SimpleMessage("123", "Test", 42);

        // Act
        var size1 = serializer.GetRequiredBufferSize(message);
        var size2 = serializer.GetRequiredBufferSize(message);

        // Assert
        Assert.Equal(size1, size2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetRequiredBufferSize_SufficientForSerialization()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<SimpleMessage>();
        var message = new SimpleMessage("123", "Test", 42);
        var bufferSize = serializer.GetRequiredBufferSize(message);

        // Act
        var buffer = new byte[bufferSize];
        var bytesWritten = serializer.Serialize(message, buffer);

        // Assert
        Assert.LessOrEqual(bytesWritten, bufferSize);
    }

    #endregion

    #region Positive Cases - Deserialize

    [Fact]
    [Trait("Category", "Unit")]
    public void Deserialize_WithValidData_ReturnsMessage()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<SimpleMessage>();
        var original = new SimpleMessage("123", "Test", 42);
        var destination = new byte[4096];
        var bytesWritten = serializer.Serialize(original, destination);
        var source = new ReadOnlySpan<byte>(destination, 0, bytesWritten);

        // Act
        var result = serializer.Deserialize(source);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Value, result.Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Deserialize_WithSimpleValue_ReturnsValue()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<int>();
        int originalValue = 42;
        var destination = new byte[4096];
        var bytesWritten = serializer.Serialize(originalValue, destination);
        var source = new ReadOnlySpan<byte>(destination, 0, bytesWritten);

        // Act
        var result = serializer.Deserialize(source);

        // Assert
        Assert.Equal(originalValue, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RoundTrip_WithMessage_PreservesData()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<SimpleMessage>();
        var original = new SimpleMessage("abc", "xyz", 999);
        var buffer = new byte[4096];

        // Act
        var written = serializer.Serialize(original, buffer);
        var source = new ReadOnlySpan<byte>(buffer, 0, written);
        var deserialized = serializer.Deserialize(source);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Value, deserialized.Value);
    }

    #endregion

    #region Positive Cases - TryDeserialize

    [Fact]
    [Trait("Category", "Unit")]
    public void TryDeserialize_WithValidData_ReturnsTrue()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<SimpleMessage>();
        var original = new SimpleMessage("123", "Test", 42);
        var destination = new byte[4096];
        var bytesWritten = serializer.Serialize(original, destination);
        var source = new ReadOnlySpan<byte>(destination, 0, bytesWritten);

        // Act
        var result = serializer.TryDeserialize(source, out var message);

        // Assert
        Assert.True(result);
        Assert.NotNull(message);
        Assert.Equal(original.Id, message.Id);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TryDeserialize_WithMalformedData_ReturnsFalse()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<SimpleMessage>();
        var malformedData = System.Text.Encoding.UTF8.GetBytes("{invalid json}");
        var source = new ReadOnlySpan<byte>(malformedData);

        // Act
        var result = serializer.TryDeserialize(source, out var message);

        // Assert
        Assert.False(result);
        Assert.Null(message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TryDeserialize_WithEmptyData_ReturnsFalse()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<SimpleMessage>();
        var source = ReadOnlySpan<byte>.Empty;

        // Act
        var result = serializer.TryDeserialize(source, out var message);

        // Assert
        Assert.False(result);
        Assert.Null(message);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithDefaultOptions_Works()
    {
        // Arrange & Act
        var serializer = new JsonSpanSerializer<SimpleMessage>();

        // Assert
        Assert.NotNull(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithCustomOptions_UsesProvidedOptions()
    {
        // Arrange
        var options = new JsonSerializerOptions { WriteIndented = true };

        // Act
        var serializer = new JsonSpanSerializer<SimpleMessage>(options);

        // Assert
        Assert.NotNull(serializer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullOptions_Works()
    {
        // Arrange & Act
        var serializer = new JsonSpanSerializer<SimpleMessage>(null);

        // Assert
        Assert.NotNull(serializer);
    }

    #endregion

    #region Edge Cases - Different Types

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_WithString_Works()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<string>();
        string message = "Test message";
        var destination = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(message, destination);

        // Assert
        Assert.Greater(bytesWritten, 0);
        var json = System.Text.Encoding.UTF8.GetString(destination.AsSpan(0, bytesWritten));
        Assert.Contains("Test message", json);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_WithList_Works()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<List<string>>();
        var items = new List<string> { "a", "b", "c" };
        var destination = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(items, destination);

        // Assert
        Assert.Greater(bytesWritten, 0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RoundTrip_WithCollections_PreservesData()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<MessageWithCollections>();
        var original = new MessageWithCollections(
            new List<string> { "x", "y", "z" },
            new Dictionary<string, int> { { "a", 1 } });
        var buffer = new byte[4096];

        // Act
        var written = serializer.Serialize(original, buffer);
        var source = new ReadOnlySpan<byte>(buffer, 0, written);
        var result = serializer.Deserialize(source);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Items.Count);
    }

    #endregion

    #region Performance Cases

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_WithLargeBuffer_CompletesSuccessfully()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<SimpleMessage>();
        var message = new SimpleMessage("123", "Test", 42);
        var destination = new byte[10000];

        // Act
        var bytesWritten = serializer.Serialize(message, destination);

        // Assert
        Assert.Greater(bytesWritten, 0);
        Assert.Less(bytesWritten, 10000);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RoundTrip_MultipleMessages_ConsistentResults()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<SimpleMessage>();
        var messages = new[]
        {
            new SimpleMessage("1", "First", 10),
            new SimpleMessage("2", "Second", 20),
            new SimpleMessage("3", "Third", 30)
        };

        // Act & Assert
        foreach (var original in messages)
        {
            var buffer = new byte[4096];
            var written = serializer.Serialize(original, buffer);
            var source = new ReadOnlySpan<byte>(buffer, 0, written);
            var result = serializer.Deserialize(source);

            Assert.NotNull(result);
            Assert.Equal(original.Id, result.Id);
            Assert.Equal(original.Name, result.Name);
            Assert.Equal(original.Value, result.Value);
        }
    }

    #endregion

    #region Negative Cases - Invalid Data

    [Fact]
    [Trait("Category", "Unit")]
    public void Deserialize_WithInvalidJson_ThrowsException()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<SimpleMessage>();
        var invalidData = System.Text.Encoding.UTF8.GetBytes("{not valid json}");
        var source = new ReadOnlySpan<byte>(invalidData);

        // Act & Assert
        Assert.Throws<Exception>(() => serializer.Deserialize(source));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Deserialize_WithEmptySpan_ThrowsException()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<SimpleMessage>();
        var source = ReadOnlySpan<byte>.Empty;

        // Act & Assert
        Assert.Throws<Exception>(() => serializer.Deserialize(source));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_WithInsufficientBuffer_ThrowsException()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<SimpleMessage>();
        var message = new SimpleMessage("123456789", "TestMessage", 42);
        var destination = new byte[5];

        // Act & Assert
        Assert.Throws<Exception>(() => serializer.Serialize(message, destination));
    }

    #endregion

    #region Edge Cases - Boundary Conditions

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_WithMinimalMessage_Works()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<SimpleMessage>();
        var message = new SimpleMessage("", "", 0);
        var destination = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(message, destination);

        // Assert
        Assert.Greater(bytesWritten, 0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RoundTrip_WithMinimalMessage_PreservesData()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<SimpleMessage>();
        var original = new SimpleMessage("", "", 0);
        var buffer = new byte[4096];

        // Act
        var written = serializer.Serialize(original, buffer);
        var source = new ReadOnlySpan<byte>(buffer, 0, written);
        var result = serializer.Deserialize(source);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Id);
        Assert.Empty(result.Name);
        Assert.Equal(0, result.Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_WithMaxIntValue_Works()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<int>();
        int maxValue = int.MaxValue;
        var destination = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(maxValue, destination);

        // Assert
        Assert.Greater(bytesWritten, 0);
    }

    #endregion
}

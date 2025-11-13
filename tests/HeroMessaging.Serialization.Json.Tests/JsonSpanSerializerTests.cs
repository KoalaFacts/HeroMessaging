using System;
using System.Text;
using System.Text.Json;
using HeroMessaging.Serialization.Json;
using Xunit;

namespace HeroMessaging.Serialization.Json.Tests;

[Trait("Category", "Unit")]
public class JsonSpanSerializerTests
{
    #region Test Models

    public class TestMessage
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public class ComplexMessage
    {
        public string Id { get; set; } = string.Empty;
        public TestMessage? Nested { get; set; }
        public int[] Numbers { get; set; } = Array.Empty<int>();
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullOptions_CreatesInstance()
    {
        // Arrange & Act
        var serializer = new JsonSpanSerializer<TestMessage>(null);

        // Assert
        // If we reach here, construction succeeded
        Assert.True(true);
    }

    [Fact]
    public void Constructor_WithCustomOptions_CreatesInstance()
    {
        // Arrange
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        // Act
        var serializer = new JsonSpanSerializer<TestMessage>(options);

        // Assert
        // If we reach here, construction succeeded
        Assert.True(true);
    }

    #endregion

    #region Serialize Tests

    [Fact]
    public void Serialize_WithValidMessage_WritesToSpan()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<TestMessage>();
        var message = new TestMessage { Name = "Test", Value = 42 };
        Span<byte> buffer = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(message, buffer);

        // Assert
        Assert.True(bytesWritten > 0);
        var json = Encoding.UTF8.GetString(buffer.Slice(0, bytesWritten));
        Assert.Contains("\"Name\":\"Test\"", json);
        Assert.Contains("\"Value\":42", json);
    }

    [Fact]
    public void Serialize_WithComplexMessage_SerializesCorrectly()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<ComplexMessage>();
        var message = new ComplexMessage
        {
            Id = "123",
            Nested = new TestMessage { Name = "Nested", Value = 99 },
            Numbers = new[] { 1, 2, 3, 4, 5 }
        };
        Span<byte> buffer = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(message, buffer);

        // Assert
        Assert.True(bytesWritten > 0);
        var json = Encoding.UTF8.GetString(buffer.Slice(0, bytesWritten));
        Assert.Contains("\"Id\":\"123\"", json);
        Assert.Contains("\"Nested\"", json);
        Assert.Contains("\"Numbers\"", json);
    }

    [Fact]
    public void Serialize_WithCustomOptions_UsesOptions()
    {
        // Arrange
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        var serializer = new JsonSpanSerializer<TestMessage>(options);
        var message = new TestMessage { Name = "Indented", Value = 100 };
        Span<byte> buffer = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(message, buffer);

        // Assert
        Assert.True(bytesWritten > 0);
        var json = Encoding.UTF8.GetString(buffer.Slice(0, bytesWritten));
        // Indented JSON should contain newlines
        Assert.Contains("\n", json);
    }

    [Fact]
    public void Serialize_WithLargeBuffer_DoesNotOverflow()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<TestMessage>();
        var message = new TestMessage { Name = "LargeBuffer", Value = 200 };
        Span<byte> buffer = new byte[10240]; // Large buffer

        // Act
        var bytesWritten = serializer.Serialize(message, buffer);

        // Assert
        Assert.True(bytesWritten > 0);
        Assert.True(bytesWritten < buffer.Length);
    }

    #endregion

    #region TrySerialize Tests

    [Fact]
    public void TrySerialize_WithValidMessage_ReturnsTrue()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<TestMessage>();
        var message = new TestMessage { Name = "TryTest", Value = 50 };
        Span<byte> buffer = new byte[4096];

        // Act
        var success = serializer.TrySerialize(message, buffer, out var bytesWritten);

        // Assert
        Assert.True(success);
        Assert.True(bytesWritten > 0);
    }

    [Fact]
    public void TrySerialize_WithValidComplexMessage_ReturnsTrue()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<ComplexMessage>();
        var message = new ComplexMessage
        {
            Id = "complex-try",
            Nested = new TestMessage { Name = "Try", Value = 75 },
            Numbers = new[] { 10, 20, 30 }
        };
        Span<byte> buffer = new byte[4096];

        // Act
        var success = serializer.TrySerialize(message, buffer, out var bytesWritten);

        // Assert
        Assert.True(success);
        Assert.True(bytesWritten > 0);
    }

    [Fact]
    public void TrySerialize_WithTooSmallBuffer_ReturnsFalse()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<TestMessage>();
        var message = new TestMessage { Name = "SmallBuffer", Value = 300 };
        Span<byte> buffer = new byte[10]; // Too small

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
        var serializer = new JsonSpanSerializer<TestMessage>();
        var message = new TestMessage { Name = "BufferSize", Value = 400 };

        // Act
        var bufferSize = serializer.GetRequiredBufferSize(message);

        // Assert
        Assert.True(bufferSize > 0);
        Assert.Equal(4096, bufferSize); // Default estimate
    }

    [Fact]
    public void GetRequiredBufferSize_ForComplexMessage_ReturnsPositiveValue()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<ComplexMessage>();
        var message = new ComplexMessage
        {
            Id = "buffer-complex",
            Numbers = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }
        };

        // Act
        var bufferSize = serializer.GetRequiredBufferSize(message);

        // Assert
        Assert.True(bufferSize > 0);
    }

    #endregion

    #region Deserialize Tests

    [Fact]
    public void Deserialize_WithValidData_ReturnsMessage()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<TestMessage>();
        var original = new TestMessage { Name = "Deserialize", Value = 500 };
        Span<byte> buffer = new byte[4096];
        var bytesWritten = serializer.Serialize(original, buffer);

        // Act
        var result = serializer.Deserialize(buffer.Slice(0, bytesWritten));

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Value, result.Value);
    }

    [Fact]
    public void Deserialize_WithComplexMessage_DeserializesCorrectly()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<ComplexMessage>();
        var original = new ComplexMessage
        {
            Id = "deserialize-complex",
            Nested = new TestMessage { Name = "DeserializeNested", Value = 600 },
            Numbers = new[] { 1, 2, 3 }
        };
        Span<byte> buffer = new byte[4096];
        var bytesWritten = serializer.Serialize(original, buffer);

        // Act
        var result = serializer.Deserialize(buffer.Slice(0, bytesWritten));

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
        Assert.NotNull(result.Nested);
        Assert.Equal(original.Nested.Name, result.Nested.Name);
        Assert.Equal(original.Numbers.Length, result.Numbers.Length);
    }

    [Fact]
    public void Deserialize_WithEmptySpan_ReturnsDefault()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<TestMessage>();
        ReadOnlySpan<byte> data = ReadOnlySpan<byte>.Empty;

        // Act
        var result = serializer.Deserialize(data);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region TryDeserialize Tests

    [Fact]
    public void TryDeserialize_WithValidData_ReturnsTrue()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<TestMessage>();
        var original = new TestMessage { Name = "TryDeserialize", Value = 700 };
        Span<byte> buffer = new byte[4096];
        var bytesWritten = serializer.Serialize(original, buffer);

        // Act
        var success = serializer.TryDeserialize(buffer.Slice(0, bytesWritten), out var result);

        // Assert
        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Value, result.Value);
    }

    [Fact]
    public void TryDeserialize_WithComplexMessage_ReturnsTrue()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<ComplexMessage>();
        var original = new ComplexMessage
        {
            Id = "try-complex",
            Nested = new TestMessage { Name = "TryNested", Value = 800 },
            Numbers = new[] { 5, 10, 15 }
        };
        Span<byte> buffer = new byte[4096];
        var bytesWritten = serializer.Serialize(original, buffer);

        // Act
        var success = serializer.TryDeserialize(buffer.Slice(0, bytesWritten), out var result);

        // Assert
        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
    }

    [Fact]
    public void TryDeserialize_WithInvalidData_ReturnsFalse()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<TestMessage>();
        var invalidData = Encoding.UTF8.GetBytes("invalid json data {{{");

        // Act
        var success = serializer.TryDeserialize(invalidData, out var result);

        // Assert
        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void TryDeserialize_WithEmptySpan_ReturnsFalse()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<TestMessage>();
        ReadOnlySpan<byte> data = ReadOnlySpan<byte>.Empty;

        // Act
        var success = serializer.TryDeserialize(data, out var result);

        // Assert
        Assert.False(success);
        Assert.Null(result);
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void RoundTrip_WithSimpleMessage_PreservesData()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<TestMessage>();
        var original = new TestMessage { Name = "RoundTrip", Value = 900 };
        Span<byte> buffer = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(original, buffer);
        var deserialized = serializer.Deserialize(buffer.Slice(0, bytesWritten));

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Value, deserialized.Value);
    }

    [Fact]
    public void RoundTrip_WithComplexMessage_PreservesData()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<ComplexMessage>();
        var original = new ComplexMessage
        {
            Id = "roundtrip-complex",
            Nested = new TestMessage { Name = "RoundTripNested", Value = 1000 },
            Numbers = new[] { 100, 200, 300, 400 }
        };
        Span<byte> buffer = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(original, buffer);
        var deserialized = serializer.Deserialize(buffer.Slice(0, bytesWritten));

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.NotNull(deserialized.Nested);
        Assert.Equal(original.Nested.Name, deserialized.Nested.Name);
        Assert.Equal(original.Nested.Value, deserialized.Nested.Value);
        Assert.Equal(original.Numbers.Length, deserialized.Numbers.Length);
    }

    [Fact]
    public void RoundTrip_TryMethods_PreservesData()
    {
        // Arrange
        var serializer = new JsonSpanSerializer<TestMessage>();
        var original = new TestMessage { Name = "TryRoundTrip", Value = 1100 };
        Span<byte> buffer = new byte[4096];

        // Act
        var serializeSuccess = serializer.TrySerialize(original, buffer, out var bytesWritten);
        var deserializeSuccess = serializer.TryDeserialize(buffer.Slice(0, bytesWritten), out var deserialized);

        // Assert
        Assert.True(serializeSuccess);
        Assert.True(deserializeSuccess);
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Value, deserialized.Value);
    }

    #endregion
}

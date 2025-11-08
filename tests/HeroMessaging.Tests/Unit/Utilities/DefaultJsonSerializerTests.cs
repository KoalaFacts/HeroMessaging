using HeroMessaging.Utilities;
using System.Text.Json;
using Xunit;

namespace HeroMessaging.Tests.Unit.Utilities;

/// <summary>
/// Unit tests for <see cref="DefaultJsonSerializer"/> implementation.
/// Tests cover serialization, deserialization, byte counting, and buffer management.
/// Target: 100% coverage for public APIs (constitutional requirement).
/// </summary>
public class DefaultJsonSerializerTests
{
    private readonly IBufferPoolManager _bufferPool;
    private readonly IJsonSerializer _serializer;

    public DefaultJsonSerializerTests()
    {
        _bufferPool = new DefaultBufferPoolManager();
        _serializer = new DefaultJsonSerializer(_bufferPool);
    }

    #region Constructor Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullBufferPool_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new DefaultJsonSerializer(null!));

        Assert.Equal("bufferPool", exception.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidBufferPool_CreatesInstance()
    {
        // Arrange
        var bufferPool = new DefaultBufferPoolManager();

        // Act
        var serializer = new DefaultJsonSerializer(bufferPool);

        // Assert
        Assert.NotNull(serializer);
    }

    #endregion

    #region SerializeToString Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void SerializeToString_WithSimpleObject_ReturnsJsonString()
    {
        // Arrange
        var obj = new { Name = "Test", Value = 42 };

        // Act
        var result = _serializer.SerializeToString(obj);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("\"Name\"", result);
        Assert.Contains("\"Test\"", result);
        Assert.Contains("\"Value\"", result);
        Assert.Contains("42", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SerializeToString_Generic_WithSimpleObject_ReturnsJsonString()
    {
        // Arrange
        var obj = new TestClass { Name = "Test", Value = 42 };

        // Act
        var result = _serializer.SerializeToString(obj);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("\"Name\"", result);
        Assert.Contains("\"Test\"", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SerializeToString_WithType_ReturnsJsonString()
    {
        // Arrange
        var obj = new TestClass { Name = "Test", Value = 42 };

        // Act
        var result = _serializer.SerializeToString(obj, typeof(TestClass));

        // Assert
        Assert.NotNull(result);
        Assert.Contains("\"Name\"", result);
        Assert.Contains("\"Test\"", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SerializeToString_WithCustomOptions_UsesProvidedOptions()
    {
        // Arrange
        var obj = new TestClass { Name = "Test", Value = 42 };
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Act
        var result = _serializer.SerializeToString(obj, options);

        // Assert
        Assert.Contains("\"name\"", result); // camelCase instead of PascalCase
        Assert.Contains("\"value\"", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SerializeToString_WithComplexObject_HandlesNesting()
    {
        // Arrange
        var obj = new ComplexClass
        {
            Name = "Parent",
            Child = new TestClass { Name = "Child", Value = 100 }
        };

        // Act
        var result = _serializer.SerializeToString(obj);

        // Assert
        Assert.Contains("\"Name\"", result);
        Assert.Contains("\"Parent\"", result);
        Assert.Contains("\"Child\"", result);
        Assert.Contains("100", result);
    }

    #endregion

    #region DeserializeFromString Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void DeserializeFromString_WithValidJson_ReturnsObject()
    {
        // Arrange
        var json = "{\"Name\":\"Test\",\"Value\":42}";

        // Act
        var result = _serializer.DeserializeFromString<TestClass>(json);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DeserializeFromString_WithNullJson_ReturnsDefault()
    {
        // Arrange & Act
        var result = _serializer.DeserializeFromString<TestClass>(null!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DeserializeFromString_WithEmptyJson_ReturnsDefault()
    {
        // Arrange & Act
        var result = _serializer.DeserializeFromString<TestClass>(string.Empty);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DeserializeFromString_WithSmallJson_UsesStackAllocation()
    {
        // Arrange: Small JSON that fits in 1KB stack allocation threshold
        var json = "{\"Name\":\"Small\",\"Value\":1}";

        // Act
        var result = _serializer.DeserializeFromString<TestClass>(json);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Small", result.Name);
        Assert.Equal(1, result.Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DeserializeFromString_WithLargeJson_UsesPooledBuffer()
    {
        // Arrange: Large JSON that exceeds stack allocation threshold
        var largeString = new string('x', 2000);
        var json = $"{{\"Name\":\"{largeString}\",\"Value\":999}}";

        // Act
        var result = _serializer.DeserializeFromString<TestClass>(json);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(largeString, result.Name);
        Assert.Equal(999, result.Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DeserializeFromString_WithType_ReturnsObject()
    {
        // Arrange
        var json = "{\"Name\":\"Test\",\"Value\":42}";

        // Act
        var result = _serializer.DeserializeFromString(json, typeof(TestClass));

        // Assert
        Assert.NotNull(result);
        var testObj = Assert.IsType<TestClass>(result);
        Assert.Equal("Test", testObj.Name);
        Assert.Equal(42, testObj.Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DeserializeFromString_WithType_NullJson_ReturnsNull()
    {
        // Arrange & Act
        var result = _serializer.DeserializeFromString(null!, typeof(TestClass));

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DeserializeFromString_WithCustomOptions_UsesProvidedOptions()
    {
        // Arrange
        var json = "{\"name\":\"Test\",\"value\":42}"; // camelCase
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        // Act
        var result = _serializer.DeserializeFromString<TestClass>(json, options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test", result.Name);
        Assert.Equal(42, result.Value);
    }

    #endregion

    #region GetJsonByteCount Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void GetJsonByteCount_WithSimpleObject_ReturnsCorrectCount()
    {
        // Arrange
        var obj = new TestClass { Name = "Test", Value = 42 };

        // Act
        var byteCount = _serializer.GetJsonByteCount(obj);

        // Assert: Verify byte count matches actual serialization
        var json = _serializer.SerializeToString(obj);
        var expectedCount = System.Text.Encoding.UTF8.GetByteCount(json);
        Assert.Equal(expectedCount, byteCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetJsonByteCount_WithComplexObject_ReturnsCorrectCount()
    {
        // Arrange
        var obj = new ComplexClass
        {
            Name = "Parent",
            Child = new TestClass { Name = "Child", Value = 100 }
        };

        // Act
        var byteCount = _serializer.GetJsonByteCount(obj);

        // Assert: Verify byte count matches actual serialization
        var json = _serializer.SerializeToString(obj);
        var expectedCount = System.Text.Encoding.UTF8.GetByteCount(json);
        Assert.Equal(expectedCount, byteCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetJsonByteCount_WithCustomOptions_UsesProvidedOptions()
    {
        // Arrange
        var obj = new TestClass { Name = "Test", Value = 42 };
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Act
        var byteCount = _serializer.GetJsonByteCount(obj, options);

        // Assert: Verify byte count matches actual serialization with options
        var json = _serializer.SerializeToString(obj, options);
        var expectedCount = System.Text.Encoding.UTF8.GetByteCount(json);
        Assert.Equal(expectedCount, byteCount);
    }

    #endregion

    #region SerializeToBuffer Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void SerializeToBuffer_WithSimpleObject_WritesToBuffer()
    {
        // Arrange
        var obj = new TestClass { Name = "Test", Value = 42 };
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();

        // Act
        _serializer.SerializeToBuffer(obj, buffer);

        // Assert
        var writtenBytes = buffer.WrittenSpan;
        Assert.True(writtenBytes.Length > 0);

        var json = System.Text.Encoding.UTF8.GetString(writtenBytes);
        Assert.Contains("\"Name\"", json);
        Assert.Contains("\"Test\"", json);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SerializeToBuffer_WithCustomOptions_UsesProvidedOptions()
    {
        // Arrange
        var obj = new TestClass { Name = "Test", Value = 42 };
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Act
        _serializer.SerializeToBuffer(obj, buffer, options);

        // Assert
        var json = System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
        Assert.Contains("\"name\"", json); // camelCase
    }

    #endregion

    #region Round-trip Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void RoundTrip_SerializeAndDeserialize_PreservesData()
    {
        // Arrange
        var original = new TestClass { Name = "RoundTrip", Value = 123 };

        // Act: Serialize then deserialize
        var json = _serializer.SerializeToString(original);
        var deserialized = _serializer.DeserializeFromString<TestClass>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Value, deserialized.Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RoundTrip_ComplexObject_PreservesNestedData()
    {
        // Arrange
        var original = new ComplexClass
        {
            Name = "Parent",
            Child = new TestClass { Name = "NestedChild", Value = 999 }
        };

        // Act: Serialize then deserialize
        var json = _serializer.SerializeToString(original);
        var deserialized = _serializer.DeserializeFromString<ComplexClass>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.NotNull(deserialized.Child);
        Assert.Equal(original.Child.Name, deserialized.Child.Name);
        Assert.Equal(original.Child.Value, deserialized.Child.Value);
    }

    #endregion

    #region Test Helper Classes

    public class TestClass
    {
        public string? Name { get; set; }
        public int Value { get; set; }
    }

    public class ComplexClass
    {
        public string? Name { get; set; }
        public TestClass? Child { get; set; }
    }

    #endregion
}

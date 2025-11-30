using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using HeroMessaging.Utilities;
using Xunit;

namespace HeroMessaging.Tests.Unit.Utilities;

public class DefaultJsonSerializerTests
{
    private readonly DefaultBufferPoolManager _bufferPool;
    private readonly DefaultJsonSerializer _serializer;

    public DefaultJsonSerializerTests()
    {
        _bufferPool = new DefaultBufferPoolManager();
        _serializer = new DefaultJsonSerializer(_bufferPool);
    }

    // Test model classes
    public class TestModel
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    public class ComplexModel
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public List<string>? Tags { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    [Trait("Category", "Unit")]
    public class Constructor
    {
        [Fact]
        public void ThrowsArgumentNullException_WhenBufferPoolIsNull()
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() =>
                new DefaultJsonSerializer(null!));

            Assert.Contains("bufferPool", exception.Message);
        }

        [Fact]
        public void CreatesInstance_WithValidBufferPool()
        {
            // Arrange
            var bufferPool = new DefaultBufferPoolManager();

            // Act
            var serializer = new DefaultJsonSerializer(bufferPool);

            // Assert
            Assert.NotNull(serializer);
        }
    }

    [Trait("Category", "Unit")]
    public class SerializeToString_Object
    {
        private readonly DefaultJsonSerializer _serializer;

        public SerializeToString_Object()
        {
            var bufferPool = new DefaultBufferPoolManager();
            _serializer = new DefaultJsonSerializer(bufferPool);
        }

        [Fact]
        public void SerializesSimpleObject_Successfully()
        {
            // Arrange
            var obj = new TestModel { Id = 1, Name = "Test" };

            // Act
            var json = _serializer.SerializeToString(obj);

            // Assert
            Assert.Contains("\"Id\":1", json);
            Assert.Contains("\"Name\":\"Test\"", json);
        }

        [Fact]
        public void SerializesComplexObject_Successfully()
        {
            // Arrange
            var obj = new ComplexModel
            {
                Id = 1,
                Name = "Complex",
                Tags = ["tag1", "tag2"],
                Metadata = new Dictionary<string, object> { { "key1", "value1" } }
            };

            // Act
            var json = _serializer.SerializeToString(obj);

            // Assert
            Assert.Contains("\"Id\":1", json);
            Assert.Contains("\"Name\":\"Complex\"", json);
            Assert.Contains("\"Tags\"", json);
            Assert.Contains("\"Metadata\"", json);
        }

        [Fact]
        public void SerializesWithCustomOptions()
        {
            // Arrange
            var obj = new TestModel { Id = 1, Name = "Test" };
            var options = new JsonSerializerOptions { WriteIndented = true };

            // Act
            var json = _serializer.SerializeToString(obj, options);

            // Assert
            Assert.Contains("\n", json); // Indented JSON contains newlines
        }

        [Fact]
        public void SerializesNullProperties_Correctly()
        {
            // Arrange
            var obj = new TestModel { Id = 1, Name = null };

            // Act
            var json = _serializer.SerializeToString(obj);

            // Assert
            Assert.Contains("\"Id\":1", json);
            Assert.Contains("\"Name\":null", json);
        }

        [Fact]
        public void SerializesEmptyObject()
        {
            // Arrange
            var obj = new TestModel();

            // Act
            var json = _serializer.SerializeToString(obj);

            // Assert
            Assert.Contains("\"Id\":0", json);
            Assert.Contains("\"Name\":null", json);
        }
    }

    [Trait("Category", "Unit")]
    public class SerializeToString_Generic
    {
        private readonly DefaultJsonSerializer _serializer;

        public SerializeToString_Generic()
        {
            var bufferPool = new DefaultBufferPoolManager();
            _serializer = new DefaultJsonSerializer(bufferPool);
        }

        [Fact]
        public void SerializesGenericObject_Successfully()
        {
            // Arrange
            var obj = new TestModel { Id = 42, Name = "Generic Test" };

            // Act
            var json = _serializer.SerializeToString<TestModel>(obj);

            // Assert
            Assert.Contains("\"Id\":42", json);
            Assert.Contains("\"Name\":\"Generic Test\"", json);
        }

        [Fact]
        public void SerializesPrimitiveTypes()
        {
            // Act
            var intJson = _serializer.SerializeToString<int>(123);
            var stringJson = _serializer.SerializeToString<string>("test");
            var boolJson = _serializer.SerializeToString<bool>(true);

            // Assert
            Assert.Equal("123", intJson);
            Assert.Equal("\"test\"", stringJson);
            Assert.Equal("true", boolJson);
        }

        [Fact]
        public void SerializesCollections()
        {
            // Arrange
            var list = new List<int> { 1, 2, 3 };

            // Act
            var json = _serializer.SerializeToString<List<int>>(list);

            // Assert
            Assert.Equal("[1,2,3]", json);
        }

        [Fact]
        public void SerializesDictionaries()
        {
            // Arrange
            var dict = new Dictionary<string, int> { { "one", 1 }, { "two", 2 } };

            // Act
            var json = _serializer.SerializeToString<Dictionary<string, int>>(dict);

            // Assert
            Assert.Contains("\"one\":1", json);
            Assert.Contains("\"two\":2", json);
        }

        [Fact]
        public void SerializesWithCustomOptions()
        {
            // Arrange
            var obj = new TestModel { Id = 1, Name = "Test" };
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

            // Act
            var json = _serializer.SerializeToString<TestModel>(obj, options);

            // Assert
            Assert.Contains("\"id\":1", json);
            Assert.Contains("\"name\":\"Test\"", json);
        }
    }

    [Trait("Category", "Unit")]
    public class SerializeToString_WithType
    {
        private readonly DefaultJsonSerializer _serializer;

        public SerializeToString_WithType()
        {
            var bufferPool = new DefaultBufferPoolManager();
            _serializer = new DefaultJsonSerializer(bufferPool);
        }

        [Fact]
        public void SerializesWithRuntimeType_Successfully()
        {
            // Arrange
            object obj = new TestModel { Id = 1, Name = "Test" };
            var type = typeof(TestModel);

            // Act
            var json = _serializer.SerializeToString(obj, type);

            // Assert
            Assert.Contains("\"Id\":1", json);
            Assert.Contains("\"Name\":\"Test\"", json);
        }

        [Fact]
        public void HandlesPolymorphicSerialization()
        {
            // Arrange
            object obj = new TestModel { Id = 1, Name = "Test" };
            var type = typeof(object);

            // Act
            var json = _serializer.SerializeToString(obj, type);

            // Assert - Should still serialize the actual properties
            Assert.Contains("\"Id\":1", json);
            Assert.Contains("\"Name\":\"Test\"", json);
        }

        [Fact]
        public void SerializesWithCustomOptions()
        {
            // Arrange
            object obj = new TestModel { Id = 1, Name = "Test" };
            var type = typeof(TestModel);
            var options = new JsonSerializerOptions { WriteIndented = true };

            // Act
            var json = _serializer.SerializeToString(obj, type, options);

            // Assert
            Assert.Contains("\n", json);
        }
    }

    [Trait("Category", "Unit")]
    public class SerializeToBuffer
    {
        private readonly DefaultJsonSerializer _serializer;

        public SerializeToBuffer()
        {
            var bufferPool = new DefaultBufferPoolManager();
            _serializer = new DefaultJsonSerializer(bufferPool);
        }

        [Fact]
        public void WritesToBuffer_Successfully()
        {
            // Arrange
            var obj = new TestModel { Id = 1, Name = "Test" };
            var buffer = new ArrayBufferWriter<byte>();

            // Act
            _serializer.SerializeToBuffer(obj, buffer);

            // Assert
            var json = Encoding.UTF8.GetString(buffer.WrittenSpan);
            Assert.Contains("\"Id\":1", json);
            Assert.Contains("\"Name\":\"Test\"", json);
        }

        [Fact]
        public void WritesMultipleObjects_ToSameBuffer()
        {
            // Arrange
            var obj1 = new TestModel { Id = 1, Name = "First" };
            var obj2 = new TestModel { Id = 2, Name = "Second" };
            var buffer = new ArrayBufferWriter<byte>();

            // Act
            _serializer.SerializeToBuffer(obj1, buffer);
            var firstLength = buffer.WrittenCount;
            _serializer.SerializeToBuffer(obj2, buffer);

            // Assert
            Assert.True(buffer.WrittenCount > firstLength);
        }

        [Fact]
        public void WritesToBuffer_WithCustomOptions()
        {
            // Arrange
            var obj = new TestModel { Id = 1, Name = "Test" };
            var buffer = new ArrayBufferWriter<byte>();
            var options = new JsonSerializerOptions { WriteIndented = true };

            // Act
            _serializer.SerializeToBuffer(obj, buffer, options);

            // Assert
            var json = Encoding.UTF8.GetString(buffer.WrittenSpan);
            Assert.Contains("\n", json);
        }
    }

    [Trait("Category", "Unit")]
    public class DeserializeFromString_Generic
    {
        private readonly DefaultJsonSerializer _serializer;

        public DeserializeFromString_Generic()
        {
            var bufferPool = new DefaultBufferPoolManager();
            _serializer = new DefaultJsonSerializer(bufferPool);
        }

        [Fact]
        public void DeserializesSimpleObject_Successfully()
        {
            // Arrange
            var json = "{\"Id\":1,\"Name\":\"Test\"}";

            // Act
            var obj = _serializer.DeserializeFromString<TestModel>(json);

            // Assert
            Assert.NotNull(obj);
            Assert.Equal(1, obj.Id);
            Assert.Equal("Test", obj.Name);
        }

        [Fact]
        public void DeserializesComplexObject_Successfully()
        {
            // Arrange
            var json = "{\"Id\":1,\"Name\":\"Complex\",\"Tags\":[\"tag1\",\"tag2\"],\"Metadata\":{\"key1\":\"value1\"}}";

            // Act
            var obj = _serializer.DeserializeFromString<ComplexModel>(json);

            // Assert
            Assert.NotNull(obj);
            Assert.Equal(1, obj.Id);
            Assert.Equal("Complex", obj.Name);
            Assert.NotNull(obj.Tags);
            Assert.Equal(2, obj.Tags.Count);
        }

        [Fact]
        public void ReturnsDefault_WhenJsonIsNull()
        {
            // Act
            var obj = _serializer.DeserializeFromString<TestModel>(null!);

            // Assert
            Assert.Null(obj);
        }

        [Fact]
        public void ReturnsDefault_WhenJsonIsEmpty()
        {
            // Act
            var obj = _serializer.DeserializeFromString<TestModel>(string.Empty);

            // Assert
            Assert.Null(obj);
        }

        [Fact]
        public void DeserializesPrimitiveTypes()
        {
            // Act
            var intValue = _serializer.DeserializeFromString<int>("123");
            var stringValue = _serializer.DeserializeFromString<string>("\"test\"");
            var boolValue = _serializer.DeserializeFromString<bool>("true");

            // Assert
            Assert.Equal(123, intValue);
            Assert.Equal("test", stringValue);
            Assert.True(boolValue);
        }

        [Fact]
        public void DeserializesCollections()
        {
            // Arrange
            var json = "[1,2,3]";

            // Act
            var list = _serializer.DeserializeFromString<List<int>>(json);

            // Assert
            Assert.NotNull(list);
            Assert.Equal(3, list.Count);
            Assert.Equal(new[] { 1, 2, 3 }, list);
        }

        [Fact]
        public void DeserializesWithCustomOptions()
        {
            // Arrange
            var json = "{\"id\":1,\"name\":\"Test\"}";
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            // Act
            var obj = _serializer.DeserializeFromString<TestModel>(json, options);

            // Assert
            Assert.NotNull(obj);
            Assert.Equal(1, obj.Id);
            Assert.Equal("Test", obj.Name);
        }

        [Fact]
        public void HandlesLargeJson_UsingPooledBuffers()
        {
            // Arrange - Create a large JSON string (>1KB to trigger pooling)
            var largeObject = new ComplexModel
            {
                Id = 1,
                Name = new string('X', 2000),
                Tags = Enumerable.Range(0, 100).Select(i => $"tag{i}").ToList()
            };
            var json = _serializer.SerializeToString(largeObject);

            // Act
            var obj = _serializer.DeserializeFromString<ComplexModel>(json);

            // Assert
            Assert.NotNull(obj);
            Assert.Equal(1, obj.Id);
            Assert.NotNull(obj.Tags);
            Assert.Equal(100, obj.Tags.Count);
        }
    }

    [Trait("Category", "Unit")]
    public class DeserializeFromString_WithType
    {
        private readonly DefaultJsonSerializer _serializer;

        public DeserializeFromString_WithType()
        {
            var bufferPool = new DefaultBufferPoolManager();
            _serializer = new DefaultJsonSerializer(bufferPool);
        }

        [Fact]
        public void DeserializesWithRuntimeType_Successfully()
        {
            // Arrange
            var json = "{\"Id\":1,\"Name\":\"Test\"}";
            var type = typeof(TestModel);

            // Act
            var obj = _serializer.DeserializeFromString(json, type);

            // Assert
            Assert.NotNull(obj);
            Assert.IsType<TestModel>(obj);
            var testModel = (TestModel)obj;
            Assert.Equal(1, testModel.Id);
            Assert.Equal("Test", testModel.Name);
        }

        [Fact]
        public void ReturnsNull_WhenJsonIsNull()
        {
            // Arrange
            var type = typeof(TestModel);

            // Act
            var obj = _serializer.DeserializeFromString(null!, type);

            // Assert
            Assert.Null(obj);
        }

        [Fact]
        public void ReturnsNull_WhenJsonIsEmpty()
        {
            // Arrange
            var type = typeof(TestModel);

            // Act
            var obj = _serializer.DeserializeFromString(string.Empty, type);

            // Assert
            Assert.Null(obj);
        }

        [Fact]
        public void DeserializesWithCustomOptions()
        {
            // Arrange
            var json = "{\"id\":1,\"name\":\"Test\"}";
            var type = typeof(TestModel);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            // Act
            var obj = _serializer.DeserializeFromString(json, type, options);

            // Assert
            Assert.NotNull(obj);
            var testModel = (TestModel)obj;
            Assert.Equal(1, testModel.Id);
            Assert.Equal("Test", testModel.Name);
        }
    }

    [Trait("Category", "Unit")]
    public class GetJsonByteCount
    {
        private readonly DefaultJsonSerializer _serializer;

        public GetJsonByteCount()
        {
            var bufferPool = new DefaultBufferPoolManager();
            _serializer = new DefaultJsonSerializer(bufferPool);
        }

        [Fact]
        public void ReturnsCorrectByteCount_ForSimpleObject()
        {
            // Arrange
            var obj = new TestModel { Id = 1, Name = "Test" };

            // Act
            var byteCount = _serializer.GetJsonByteCount(obj);
            var json = _serializer.SerializeToString(obj);
            var actualByteCount = Encoding.UTF8.GetByteCount(json);

            // Assert
            Assert.Equal(actualByteCount, byteCount);
        }

        [Fact]
        public void ReturnsCorrectByteCount_ForComplexObject()
        {
            // Arrange
            var obj = new ComplexModel
            {
                Id = 1,
                Name = "Complex",
                Tags = ["tag1", "tag2"],
                Metadata = new Dictionary<string, object> { { "key1", "value1" } }
            };

            // Act
            var byteCount = _serializer.GetJsonByteCount(obj);
            var json = _serializer.SerializeToString(obj);
            var actualByteCount = Encoding.UTF8.GetByteCount(json);

            // Assert
            Assert.Equal(actualByteCount, byteCount);
        }

        [Fact]
        public void ReturnsCorrectByteCount_WithCustomOptions()
        {
            // Arrange
            var obj = new TestModel { Id = 1, Name = "Test" };
            var options = new JsonSerializerOptions { WriteIndented = true };

            // Act
            var byteCount = _serializer.GetJsonByteCount(obj, options);
            var json = _serializer.SerializeToString(obj, options);
            var actualByteCount = Encoding.UTF8.GetByteCount(json);

            // Assert
            Assert.Equal(actualByteCount, byteCount);
        }

        [Fact]
        public void ReturnsCorrectByteCount_ForPrimitives()
        {
            // Act
            var intCount = _serializer.GetJsonByteCount(123);
            var stringCount = _serializer.GetJsonByteCount("test");
            var boolCount = _serializer.GetJsonByteCount(true);

            // Assert
            Assert.Equal(3, intCount); // "123"
            Assert.Equal(6, stringCount); // "\"test\""
            Assert.Equal(4, boolCount); // "true"
        }

        [Fact]
        public void ReturnsCorrectByteCount_ForLargeObject()
        {
            // Arrange
            var largeObject = new ComplexModel
            {
                Id = 1,
                Name = new string('X', 1000),
                Tags = Enumerable.Range(0, 50).Select(i => $"tag{i}").ToList()
            };

            // Act
            var byteCount = _serializer.GetJsonByteCount(largeObject);
            var json = _serializer.SerializeToString(largeObject);
            var actualByteCount = Encoding.UTF8.GetByteCount(json);

            // Assert
            Assert.Equal(actualByteCount, byteCount);
        }
    }

    [Trait("Category", "Unit")]
    public class RoundTripSerialization
    {
        private readonly DefaultJsonSerializer _serializer;

        public RoundTripSerialization()
        {
            var bufferPool = new DefaultBufferPoolManager();
            _serializer = new DefaultJsonSerializer(bufferPool);
        }

        [Fact]
        public void RoundTrip_PreservesObjectState()
        {
            // Arrange
            var original = new TestModel { Id = 42, Name = "RoundTrip Test" };

            // Act
            var json = _serializer.SerializeToString(original);
            var deserialized = _serializer.DeserializeFromString<TestModel>(json);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Id, deserialized.Id);
            Assert.Equal(original.Name, deserialized.Name);
        }

        [Fact]
        public void RoundTrip_PreservesComplexObjectState()
        {
            // Arrange
            var original = new ComplexModel
            {
                Id = 1,
                Name = "Complex",
                Tags = ["tag1", "tag2", "tag3"],
                Metadata = new Dictionary<string, object> { { "key1", "value1" }, { "key2", 42 } }
            };

            // Act
            var json = _serializer.SerializeToString(original);
            var deserialized = _serializer.DeserializeFromString<ComplexModel>(json);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Id, deserialized.Id);
            Assert.Equal(original.Name, deserialized.Name);
            Assert.NotNull(deserialized.Tags);
            Assert.Equal(original.Tags.Count, deserialized.Tags.Count);
        }
    }
}

using System;
using System.Text;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.Json;
using Moq;
using Xunit;

namespace HeroMessaging.Serialization.Json.Tests;

[Trait("Category", "Unit")]
public class MessageSpanTests
{
    #region Test Models

    public class TestMessage
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidData_CreatesInstance()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("{\"Name\":\"Test\",\"Value\":42}");
        var mockDeserializer = new Mock<ISpanDeserializer<TestMessage>>();

        // Act
        var messageSpan = new MessageSpan<TestMessage>(data, mockDeserializer.Object);

        // Assert
        Assert.Equal(data.Length, messageSpan.Size);
    }

    [Fact]
    public void Constructor_WithEmptyData_CreatesInstance()
    {
        // Arrange
        ReadOnlySpan<byte> data = ReadOnlySpan<byte>.Empty;
        var mockDeserializer = new Mock<ISpanDeserializer<TestMessage>>();

        // Act
        var messageSpan = new MessageSpan<TestMessage>(data, mockDeserializer.Object);

        // Assert
        Assert.Equal(0, messageSpan.Size);
        Assert.True(messageSpan.IsEmpty);
    }

    #endregion

    #region Data Property Tests

    [Fact]
    public void Data_ReturnsOriginalSpan()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("{\"Name\":\"DataTest\",\"Value\":100}");
        var mockDeserializer = new Mock<ISpanDeserializer<TestMessage>>();
        var messageSpan = new MessageSpan<TestMessage>(data, mockDeserializer.Object);

        // Act
        var result = messageSpan.Data;

        // Assert
        Assert.Equal(data.Length, result.Length);
        Assert.True(result.SequenceEqual(data));
    }

    #endregion

    #region Size Property Tests

    [Fact]
    public void Size_ReturnsCorrectLength()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("{\"Name\":\"SizeTest\",\"Value\":200}");
        var mockDeserializer = new Mock<ISpanDeserializer<TestMessage>>();
        var messageSpan = new MessageSpan<TestMessage>(data, mockDeserializer.Object);

        // Act
        var size = messageSpan.Size;

        // Assert
        Assert.Equal(data.Length, size);
    }

    [Fact]
    public void Size_ForEmptyData_ReturnsZero()
    {
        // Arrange
        ReadOnlySpan<byte> data = ReadOnlySpan<byte>.Empty;
        var mockDeserializer = new Mock<ISpanDeserializer<TestMessage>>();
        var messageSpan = new MessageSpan<TestMessage>(data, mockDeserializer.Object);

        // Act
        var size = messageSpan.Size;

        // Assert
        Assert.Equal(0, size);
    }

    #endregion

    #region IsEmpty Property Tests

    [Fact]
    public void IsEmpty_ForEmptyData_ReturnsTrue()
    {
        // Arrange
        ReadOnlySpan<byte> data = ReadOnlySpan<byte>.Empty;
        var mockDeserializer = new Mock<ISpanDeserializer<TestMessage>>();
        var messageSpan = new MessageSpan<TestMessage>(data, mockDeserializer.Object);

        // Act
        var isEmpty = messageSpan.IsEmpty;

        // Assert
        Assert.True(isEmpty);
    }

    [Fact]
    public void IsEmpty_ForNonEmptyData_ReturnsFalse()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("{\"Name\":\"NotEmpty\",\"Value\":300}");
        var mockDeserializer = new Mock<ISpanDeserializer<TestMessage>>();
        var messageSpan = new MessageSpan<TestMessage>(data, mockDeserializer.Object);

        // Act
        var isEmpty = messageSpan.IsEmpty;

        // Assert
        Assert.False(isEmpty);
    }

    #endregion

    #region GetMessage Tests

    [Fact]
    public void GetMessage_CallsDeserializer()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("{\"Name\":\"GetTest\",\"Value\":400}");
        var expectedMessage = new TestMessage { Name = "GetTest", Value = 400 };
        var mockDeserializer = new Mock<ISpanDeserializer<TestMessage>>();
        mockDeserializer.Setup(d => d.Deserialize(It.IsAny<ReadOnlySpan<byte>>()))
            .Returns(expectedMessage);
        var messageSpan = new MessageSpan<TestMessage>(data, mockDeserializer.Object);

        // Act
        var result = messageSpan.GetMessage();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedMessage.Name, result.Name);
        Assert.Equal(expectedMessage.Value, result.Value);
        mockDeserializer.Verify(d => d.Deserialize(It.IsAny<ReadOnlySpan<byte>>()), Times.Once);
    }

    [Fact]
    public void GetMessage_PassesCorrectDataToDeserializer()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("{\"Name\":\"PassTest\",\"Value\":500}");
        ReadOnlySpan<byte> capturedSpan = default;
        var mockDeserializer = new Mock<ISpanDeserializer<TestMessage>>();
        mockDeserializer.Setup(d => d.Deserialize(It.IsAny<ReadOnlySpan<byte>>()))
            .Callback<ReadOnlySpan<byte>>(span => capturedSpan = span.ToArray())
            .Returns(new TestMessage());
        var messageSpan = new MessageSpan<TestMessage>(data, mockDeserializer.Object);

        // Act
        messageSpan.GetMessage();

        // Assert
        Assert.True(capturedSpan.SequenceEqual(data));
    }

    #endregion

    #region TryGetMessage Tests

    [Fact]
    public void TryGetMessage_WithSuccessfulDeserialization_ReturnsTrue()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("{\"Name\":\"TryTest\",\"Value\":600}");
        var expectedMessage = new TestMessage { Name = "TryTest", Value = 600 };
        var mockDeserializer = new Mock<ISpanDeserializer<TestMessage>>();
        mockDeserializer.Setup(d => d.TryDeserialize(It.IsAny<ReadOnlySpan<byte>>(), out It.Ref<TestMessage>.IsAny))
            .Returns((ReadOnlySpan<byte> _, out TestMessage msg) =>
            {
                msg = expectedMessage;
                return true;
            });
        var messageSpan = new MessageSpan<TestMessage>(data, mockDeserializer.Object);

        // Act
        var success = messageSpan.TryGetMessage(out var result);

        // Assert
        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal(expectedMessage.Name, result.Name);
        Assert.Equal(expectedMessage.Value, result.Value);
    }

    [Fact]
    public void TryGetMessage_WithFailedDeserialization_ReturnsFalse()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("invalid data");
        var mockDeserializer = new Mock<ISpanDeserializer<TestMessage>>();
        mockDeserializer.Setup(d => d.TryDeserialize(It.IsAny<ReadOnlySpan<byte>>(), out It.Ref<TestMessage>.IsAny))
            .Returns((ReadOnlySpan<byte> _, out TestMessage msg) =>
            {
                msg = default!;
                return false;
            });
        var messageSpan = new MessageSpan<TestMessage>(data, mockDeserializer.Object);

        // Act
        var success = messageSpan.TryGetMessage(out var result);

        // Assert
        Assert.False(success);
    }

    #endregion

    #region Slice Tests

    [Fact]
    public void Slice_ReturnsCorrectSubspan()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("{\"Name\":\"SliceTest\",\"Value\":700}");
        var mockDeserializer = new Mock<ISpanDeserializer<TestMessage>>();
        var messageSpan = new MessageSpan<TestMessage>(data, mockDeserializer.Object);

        // Act
        var slice = messageSpan.Slice(0, 10);

        // Assert
        Assert.Equal(10, slice.Length);
        Assert.True(slice.SequenceEqual(data.AsSpan(0, 10)));
    }

    [Fact]
    public void Slice_WithOffset_ReturnsCorrectSubspan()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("{\"Name\":\"OffsetSlice\",\"Value\":800}");
        var mockDeserializer = new Mock<ISpanDeserializer<TestMessage>>();
        var messageSpan = new MessageSpan<TestMessage>(data, mockDeserializer.Object);

        // Act
        var slice = messageSpan.Slice(5, 10);

        // Assert
        Assert.Equal(10, slice.Length);
        Assert.True(slice.SequenceEqual(data.AsSpan(5, 10)));
    }

    [Fact]
    public void Slice_ToEndOfData_ReturnsCorrectSubspan()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("{\"Name\":\"EndSlice\"}");
        var mockDeserializer = new Mock<ISpanDeserializer<TestMessage>>();
        var messageSpan = new MessageSpan<TestMessage>(data, mockDeserializer.Object);
        var startIndex = 10;
        var length = data.Length - startIndex;

        // Act
        var slice = messageSpan.Slice(startIndex, length);

        // Assert
        Assert.Equal(length, slice.Length);
        Assert.True(slice.SequenceEqual(data.AsSpan(startIndex, length)));
    }

    #endregion

    #region Integration Tests with JsonSpanSerializer

    [Fact]
    public void Integration_WithJsonSpanSerializer_WorksCorrectly()
    {
        // Arrange
        var original = new TestMessage { Name = "Integration", Value = 999 };
        var jsonSerializer = new JsonSpanSerializer<TestMessage>();
        Span<byte> buffer = new byte[4096];
        var bytesWritten = jsonSerializer.Serialize(original, buffer);
        var data = buffer.Slice(0, bytesWritten).ToArray();

        // Create a wrapper that implements ISpanDeserializer
        var deserializer = new JsonSpanDeserializerWrapper<TestMessage>(jsonSerializer);
        var messageSpan = new MessageSpan<TestMessage>(data, deserializer);

        // Act
        var result = messageSpan.GetMessage();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Value, result.Value);
        Assert.Equal(data.Length, messageSpan.Size);
        Assert.False(messageSpan.IsEmpty);
    }

    #endregion

    #region Helper Classes

    private class JsonSpanDeserializerWrapper<T> : ISpanDeserializer<T>
    {
        private readonly JsonSpanSerializer<T> _serializer;

        public JsonSpanDeserializerWrapper(JsonSpanSerializer<T> serializer)
        {
            _serializer = serializer;
        }

        public T Deserialize(ReadOnlySpan<byte> source)
        {
            return _serializer.Deserialize(source);
        }

        public bool TryDeserialize(ReadOnlySpan<byte> source, out T message)
        {
            return _serializer.TryDeserialize(source, out message);
        }
    }

    #endregion
}

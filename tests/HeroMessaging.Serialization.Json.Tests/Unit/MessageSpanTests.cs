using HeroMessaging.Serialization.Json;
using Xunit;

namespace HeroMessaging.Serialization.Json.Tests.Unit;

/// <summary>
/// Unit tests for MessageSpan ref struct covering zero-copy message access
/// </summary>
public class MessageSpanTests
{
    private sealed record SimpleMessage(string Id, string Name, int Value);

    private sealed class MockDeserializer : ISpanDeserializer<SimpleMessage>
    {
        private readonly SimpleMessage _messageToReturn;

        public MockDeserializer(SimpleMessage? messageToReturn = null)
        {
            _messageToReturn = messageToReturn ?? new SimpleMessage("default", "Default", 0);
        }

        public SimpleMessage Deserialize(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty) throw new ArgumentException("Empty data");
            return _messageToReturn;
        }

        public bool TryDeserialize(ReadOnlySpan<byte> data, out SimpleMessage message)
        {
            if (data.IsEmpty)
            {
                message = null!;
                return false;
            }
            message = _messageToReturn;
            return true;
        }
    }

    #region Positive Cases - Data Access

    [Fact]
    [Trait("Category", "Unit")]
    public void Data_ReturnsSpan()
    {
        // Arrange
        var rawData = new byte[] { 1, 2, 3, 4, 5 };
        var deserializer = new MockDeserializer();

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);
        var data = messageSpan.Data;

        // Assert
        Assert.Equal(rawData.Length, data.Length);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Data_PreservesContent()
    {
        // Arrange
        var rawData = new byte[] { 1, 2, 3, 4, 5 };
        var deserializer = new MockDeserializer();

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);
        var data = messageSpan.Data;

        // Assert
        for (int i = 0; i < rawData.Length; i++)
        {
            Assert.Equal(rawData[i], data[i]);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Data_ZeroCopy()
    {
        // Arrange
        var rawData = new byte[] { 1, 2, 3, 4, 5 };
        var deserializer = new MockDeserializer();

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);
        var data1 = messageSpan.Data;
        var data2 = messageSpan.Data;

        // Assert
        Assert.True(System.MemoryExtensions.SequenceEqual(data1, data2));
    }

    #endregion

    #region Positive Cases - Size

    [Fact]
    [Trait("Category", "Unit")]
    public void Size_ReturnsDataLength()
    {
        // Arrange
        var rawData = new byte[] { 1, 2, 3, 4, 5 };
        var deserializer = new MockDeserializer();

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);
        var size = messageSpan.Size;

        // Assert
        Assert.Equal(5, size);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Size_WithEmptyData_ReturnsZero()
    {
        // Arrange
        var rawData = ReadOnlySpan<byte>.Empty;
        var deserializer = new MockDeserializer();

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);
        var size = messageSpan.Size;

        // Assert
        Assert.Equal(0, size);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Size_ConsistentWithDataLength()
    {
        // Arrange
        var rawData = new byte[] { 1, 2, 3, 4, 5, 6, 7 };
        var deserializer = new MockDeserializer();

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);

        // Assert
        Assert.Equal(messageSpan.Data.Length, messageSpan.Size);
    }

    #endregion

    #region Positive Cases - GetMessage

    [Fact]
    [Trait("Category", "Unit")]
    public void GetMessage_ReturnsDeserializedMessage()
    {
        // Arrange
        var rawData = new byte[] { 1, 2, 3, 4, 5 };
        var expectedMessage = new SimpleMessage("test", "Test", 42);
        var deserializer = new MockDeserializer(expectedMessage);

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);
        var result = messageSpan.GetMessage();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedMessage.Id, result.Id);
        Assert.Equal(expectedMessage.Name, result.Name);
        Assert.Equal(expectedMessage.Value, result.Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetMessage_WithDifferentData_DeserializesCorrectly()
    {
        // Arrange
        var data1 = new byte[] { 1, 2, 3 };
        var data2 = new byte[] { 4, 5, 6, 7 };
        var message1 = new SimpleMessage("msg1", "Message 1", 1);
        var message2 = new SimpleMessage("msg2", "Message 2", 2);

        var deserializer1 = new MockDeserializer(message1);
        var deserializer2 = new MockDeserializer(message2);

        // Act
        var span1 = new MessageSpan<SimpleMessage>(data1, deserializer1);
        var span2 = new MessageSpan<SimpleMessage>(data2, deserializer2);

        var result1 = span1.GetMessage();
        var result2 = span2.GetMessage();

        // Assert
        Assert.Equal("msg1", result1.Id);
        Assert.Equal("msg2", result2.Id);
    }

    #endregion

    #region Positive Cases - TryGetMessage

    [Fact]
    [Trait("Category", "Unit")]
    public void TryGetMessage_WithValidData_ReturnsTrue()
    {
        // Arrange
        var rawData = new byte[] { 1, 2, 3, 4, 5 };
        var expectedMessage = new SimpleMessage("test", "Test", 42);
        var deserializer = new MockDeserializer(expectedMessage);

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);
        var result = messageSpan.TryGetMessage(out var message);

        // Assert
        Assert.True(result);
        Assert.NotNull(message);
        Assert.Equal(expectedMessage.Id, message.Id);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TryGetMessage_WithEmptyData_ReturnsFalse()
    {
        // Arrange
        var rawData = ReadOnlySpan<byte>.Empty;
        var deserializer = new MockDeserializer();

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);
        var result = messageSpan.TryGetMessage(out var message);

        // Assert
        Assert.False(result);
        Assert.Null(message);
    }

    #endregion

    #region Positive Cases - IsEmpty

    [Fact]
    [Trait("Category", "Unit")]
    public void IsEmpty_WithEmptyData_ReturnsTrue()
    {
        // Arrange
        var rawData = ReadOnlySpan<byte>.Empty;
        var deserializer = new MockDeserializer();

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);
        var isEmpty = messageSpan.IsEmpty;

        // Assert
        Assert.True(isEmpty);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsEmpty_WithNonEmptyData_ReturnsFalse()
    {
        // Arrange
        var rawData = new byte[] { 1, 2, 3 };
        var deserializer = new MockDeserializer();

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);
        var isEmpty = messageSpan.IsEmpty;

        // Assert
        Assert.False(isEmpty);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsEmpty_MatchesDataIsEmpty()
    {
        // Arrange
        var rawData = new byte[] { 1 };
        var deserializer = new MockDeserializer();

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);

        // Assert
        Assert.Equal(messageSpan.Data.IsEmpty, messageSpan.IsEmpty);
    }

    #endregion

    #region Positive Cases - Slice

    [Fact]
    [Trait("Category", "Unit")]
    public void Slice_WithValidRange_ReturnsCorrectData()
    {
        // Arrange
        var rawData = new byte[] { 1, 2, 3, 4, 5 };
        var deserializer = new MockDeserializer();

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);
        var sliced = messageSpan.Slice(1, 3);

        // Assert
        Assert.Equal(3, sliced.Length);
        Assert.Equal(2, sliced[0]);
        Assert.Equal(3, sliced[1]);
        Assert.Equal(4, sliced[2]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Slice_WithStartAtZero_ReturnsFullData()
    {
        // Arrange
        var rawData = new byte[] { 1, 2, 3, 4, 5 };
        var deserializer = new MockDeserializer();

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);
        var sliced = messageSpan.Slice(0, 5);

        // Assert
        Assert.Equal(5, sliced.Length);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Slice_WithMiddleRange_ReturnsCorrectSubset()
    {
        // Arrange
        var rawData = new byte[] { 10, 20, 30, 40, 50, 60 };
        var deserializer = new MockDeserializer();

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);
        var sliced = messageSpan.Slice(2, 2);

        // Assert
        Assert.Equal(2, sliced.Length);
        Assert.Equal(30, sliced[0]);
        Assert.Equal(40, sliced[1]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Slice_WithZeroLength_ReturnsEmpty()
    {
        // Arrange
        var rawData = new byte[] { 1, 2, 3 };
        var deserializer = new MockDeserializer();

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);
        var sliced = messageSpan.Slice(1, 0);

        // Assert
        Assert.True(sliced.IsEmpty);
    }

    #endregion

    #region Negative Cases - Invalid Ranges

    [Fact]
    [Trait("Category", "Unit")]
    public void Slice_WithInvalidRange_ThrowsException()
    {
        // Arrange
        var rawData = new byte[] { 1, 2, 3 };
        var deserializer = new MockDeserializer();

        // Act & Assert
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            messageSpan.Slice(10, 5));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Slice_WithNegativeStart_ThrowsException()
    {
        // Arrange
        var rawData = new byte[] { 1, 2, 3 };
        var deserializer = new MockDeserializer();

        // Act & Assert
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            messageSpan.Slice(-1, 2));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Slice_WithExceedingLength_ThrowsException()
    {
        // Arrange
        var rawData = new byte[] { 1, 2, 3 };
        var deserializer = new MockDeserializer();

        // Act & Assert
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            messageSpan.Slice(1, 10));
    }

    #endregion

    #region Edge Cases - Empty Data

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithEmptyData_Works()
    {
        // Arrange
        var rawData = ReadOnlySpan<byte>.Empty;
        var deserializer = new MockDeserializer();

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);

        // Assert
        Assert.True(messageSpan.IsEmpty);
        Assert.Equal(0, messageSpan.Size);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MultipleOperations_OnEmptyData_Consistent()
    {
        // Arrange
        var rawData = ReadOnlySpan<byte>.Empty;
        var deserializer = new MockDeserializer();

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);

        // Assert
        Assert.True(messageSpan.IsEmpty);
        Assert.Equal(0, messageSpan.Size);
        Assert.Equal(0, messageSpan.Data.Length);
    }

    #endregion

    #region Edge Cases - Large Data

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithLargeData_Works()
    {
        // Arrange
        var rawData = new byte[10000];
        for (int i = 0; i < rawData.Length; i++)
        {
            rawData[i] = (byte)(i % 256);
        }
        var deserializer = new MockDeserializer();

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);

        // Assert
        Assert.Equal(10000, messageSpan.Size);
        Assert.False(messageSpan.IsEmpty);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Slice_WithLargeData_Works()
    {
        // Arrange
        var rawData = new byte[5000];
        var deserializer = new MockDeserializer();

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);
        var sliced = messageSpan.Slice(1000, 2000);

        // Assert
        Assert.Equal(2000, sliced.Length);
    }

    #endregion

    #region Integration Cases - Data Flow

    [Fact]
    [Trait("Category", "Unit")]
    public void FullWorkflow_DataAccess_ZeroCopy()
    {
        // Arrange
        var rawData = new byte[] { 1, 2, 3, 4, 5 };
        var expectedMessage = new SimpleMessage("id", "name", 42);
        var deserializer = new MockDeserializer(expectedMessage);

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);

        // Access data without deserialization
        var dataSize = messageSpan.Size;
        var slicedData = messageSpan.Slice(0, 3);

        // Deserialize when needed
        var message = messageSpan.GetMessage();

        // Assert
        Assert.Equal(5, dataSize);
        Assert.Equal(3, slicedData.Length);
        Assert.NotNull(message);
        Assert.Equal("id", message.Id);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FullWorkflow_OptionalDeserialization_WithTry()
    {
        // Arrange
        var rawData = new byte[] { 1, 2, 3 };
        var expectedMessage = new SimpleMessage("test", "Test", 10);
        var deserializer = new MockDeserializer(expectedMessage);

        // Act
        var messageSpan = new MessageSpan<SimpleMessage>(rawData, deserializer);

        // Try deserialize
        var success = messageSpan.TryGetMessage(out var message);

        // Assert
        Assert.True(success);
        Assert.NotNull(message);
        Assert.Equal("test", message.Id);
    }

    #endregion
}

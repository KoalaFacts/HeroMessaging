using Xunit;

namespace HeroMessaging.Serialization.Protobuf.Tests.Integration;

/// <summary>
/// Integration tests for Protocol Buffers serialization implementation
/// Tests round-trip consistency and compact binary format
/// Note: Uses ProtobufTestMessage instead of TestMessage because Protobuf cannot serialize Dictionary&lt;string, object&gt;
/// </summary>
[Trait("Category", "Integration")]
public class ProtobufSerializationIntegrationTests
{
    [Fact]
    public async Task ProtobufSerialization_RoundTrip_MaintainsDataIntegrity()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var originalMessage = ProtobufTestMessageBuilder.CreateValidMessage("Protocol Buffers serialization test");

        // Act
        var serializedData = await serializer.SerializeAsync(originalMessage);
        var deserializedMessage = await serializer.DeserializeAsync<ProtobufTestMessage>(serializedData);

        // Assert
        Assert.NotNull(deserializedMessage);
        Assert.Equal(originalMessage.MessageId, deserializedMessage.MessageId);
        ProtobufTestMessageBuilder.AssertSameContent(originalMessage, deserializedMessage);
        Assert.Equal(originalMessage.Timestamp, deserializedMessage.Timestamp);
    }

    [Fact]
    public async Task ProtobufSerialization_ProducesCompactBinary()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var message = ProtobufTestMessageBuilder.CreateValidMessage("Compact test");

        // Act
        var serializedData = await serializer.SerializeAsync(message);

        // Assert
        Assert.True(serializedData.Length > 0);
        Assert.True(serializedData.Length < 1000, "Should be reasonably small for test message");
    }

    [Fact]
    public async Task ProtobufSerialization_WithLargeMessage_HandlesEfficiently()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var largeMessage = ProtobufTestMessageBuilder.CreateLargeMessage(50000);

        // Act
        var serializedData = await serializer.SerializeAsync(largeMessage);
        var deserializedMessage = await serializer.DeserializeAsync<ProtobufTestMessage>(serializedData);

        // Assert
        Assert.NotNull(deserializedMessage);
        Assert.Equal(largeMessage.MessageId, deserializedMessage.MessageId);
    }

    [Fact]
    public async Task ProtobufSerialization_Concurrency_HandlesMultipleOperations()
    {
        // Arrange
        var serializer = new ProtobufMessageSerializer();
        var messages = Enumerable.Range(0, 50)
            .Select(i => ProtobufTestMessageBuilder.CreateValidMessage($"Concurrent message {i}"))
            .ToList();

        // Act
        var serializeTasks = messages.Select(m => serializer.SerializeAsync(m).AsTask()).ToArray();
        var serializedData = await Task.WhenAll(serializeTasks);

        var deserializeTasks = serializedData.Select(d => serializer.DeserializeAsync<ProtobufTestMessage>(d).AsTask()).ToArray();
        var deserializedMessages = await Task.WhenAll(deserializeTasks);

        // Assert
        Assert.Equal(messages.Count, deserializedMessages.Length);
        for (int i = 0; i < messages.Count; i++)
        {
            Assert.Equal(messages[i].MessageId, deserializedMessages[i].MessageId);
            ProtobufTestMessageBuilder.AssertSameContent(messages[i], deserializedMessages[i]);
        }
    }
}

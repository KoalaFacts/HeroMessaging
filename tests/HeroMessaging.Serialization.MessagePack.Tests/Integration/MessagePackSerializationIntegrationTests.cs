using HeroMessaging.Serialization.Json;
using HeroMessaging.Tests.TestUtilities;
using Xunit;

namespace HeroMessaging.Serialization.MessagePack.Tests.Integration;

/// <summary>
/// Integration tests for MessagePack serialization implementation
/// Tests round-trip consistency and compact binary format
/// </summary>
[Trait("Category", "Integration")]
public class MessagePackSerializationIntegrationTests
{
    [Fact]
    public async Task MessagePackSerialization_RoundTrip_MaintainsDataIntegrity()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var originalMessage = TestMessageBuilder.CreateValidMessage("MessagePack serialization test");

        // Act
        var serializedData = await serializer.SerializeAsync(originalMessage, TestContext.Current.CancellationToken);
        var deserializedMessage = await serializer.DeserializeAsync<TestMessage>(serializedData, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(deserializedMessage);
        Assert.Equal(originalMessage.MessageId, deserializedMessage.MessageId);
        TestMessageExtensions.AssertSameContent(originalMessage, deserializedMessage);
        Assert.Equal(originalMessage.Timestamp, deserializedMessage.Timestamp);
    }

    [Fact]
    public async Task MessagePackSerialization_IsMoreCompactThanJson()
    {
        // Arrange
        var messagePackSerializer = new MessagePackMessageSerializer();
        var jsonSerializer = new JsonMessageSerializer();
        var message = TestMessageBuilder.CreateValidMessage("Compact test");

        // Act
        var messagePackData = await messagePackSerializer.SerializeAsync(message, TestContext.Current.CancellationToken);
        var jsonData = await jsonSerializer.SerializeAsync(message, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(messagePackData.Length < jsonData.Length,
            $"MessagePack ({messagePackData.Length} bytes) should be more compact than JSON ({jsonData.Length} bytes)");
    }

    [Fact]
    public async Task MessagePackSerialization_WithLargeMessage_HandlesEfficiently()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var largeMessage = TestMessageBuilder.CreateLargeMessage(50000);

        // Act
        var serializedData = await serializer.SerializeAsync(largeMessage, TestContext.Current.CancellationToken);
        var deserializedMessage = await serializer.DeserializeAsync<TestMessage>(serializedData, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(deserializedMessage);
        Assert.Equal(largeMessage.MessageId, deserializedMessage.MessageId);
        Assert.Equal(largeMessage.GetTestContent()?.Length, deserializedMessage.GetTestContent()?.Length);
    }

    [Fact]
    public async Task MessagePackSerialization_Concurrency_HandlesMultipleOperations()
    {
        // Arrange
        var serializer = new MessagePackMessageSerializer();
        var messages = Enumerable.Range(0, 50)
            .Select(i => TestMessageBuilder.CreateValidMessage($"Concurrent message {i}"))
            .ToList();

        // Act
        var serializeTasks = messages.Select(m => serializer.SerializeAsync(m, TestContext.Current.CancellationToken).AsTask()).ToArray();
        var serializedData = await Task.WhenAll(serializeTasks);

        var deserializeTasks = serializedData.Select(d => serializer.DeserializeAsync<TestMessage>(d, TestContext.Current.CancellationToken).AsTask()).ToArray();
        var deserializedMessages = await Task.WhenAll(deserializeTasks);

        // Assert
        Assert.Equal(messages.Count, deserializedMessages.Length);
        for (int i = 0; i < messages.Count; i++)
        {
            Assert.Equal(messages[i].MessageId, deserializedMessages[i].MessageId);
            TestMessageExtensions.AssertSameContent(messages[i], deserializedMessages[i]);
        }
    }
}

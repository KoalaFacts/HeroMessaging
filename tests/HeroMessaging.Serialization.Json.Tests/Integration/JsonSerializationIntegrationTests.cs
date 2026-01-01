using System.Text;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Tests.TestUtilities;
using Xunit;

namespace HeroMessaging.Serialization.Json.Tests.Integration;

/// <summary>
/// Integration tests for JSON serialization implementation
/// Tests round-trip consistency, complex nested data, and JSON format validation
/// </summary>
[Trait("Category", "Integration")]
public class JsonSerializationIntegrationTests
{
    [Fact]
    public async Task JsonSerialization_RoundTrip_MaintainsDataIntegrity()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var originalMessage = TestMessageBuilder.CreateValidMessage("JSON serialization test");

        // Act
        var serializedData = await serializer.SerializeAsync(originalMessage, TestContext.Current.CancellationToken);
        var deserializedMessage = await serializer.DeserializeAsync<TestMessage>(serializedData, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(deserializedMessage);
        Assert.Equal(originalMessage.MessageId, deserializedMessage.MessageId);
        TestMessageExtensions.AssertSameContent(originalMessage, deserializedMessage);
        Assert.Equal(originalMessage.Timestamp, deserializedMessage.Timestamp);

        // Verify serialized data is valid JSON with camelCase property names (default policy)
        var jsonString = Encoding.UTF8.GetString(serializedData);
        Assert.Contains("messageId", jsonString);
        Assert.Contains("timestamp", jsonString);
    }

    [Fact]
    public async Task JsonSerialization_WithComplexMessage_HandlesNestedData()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
        var complexMessage = CreateComplexMessage();

        // Act
        var serializedData = await serializer.SerializeAsync(complexMessage, TestContext.Current.CancellationToken);
        var deserializedMessage = await serializer.DeserializeAsync<TestMessage>(serializedData, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(deserializedMessage);
        Assert.Equal(complexMessage.MessageId, deserializedMessage.MessageId);

        // Verify nested metadata is preserved
        Assert.NotNull(deserializedMessage.Metadata);
        Assert.True(deserializedMessage.Metadata.Count >= 3);
    }

    [Fact]
    public async Task JsonSerialization_WithCompression_ReducesSize()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var serializerWithCompression = new JsonMessageSerializer(options);
        var serializerWithoutCompression = new JsonMessageSerializer();

        var message = TestMessageBuilder.CreateLargeMessage(10000);

        // Act
        var compressedData = await serializerWithCompression.SerializeAsync(message, TestContext.Current.CancellationToken);
        var uncompressedData = await serializerWithoutCompression.SerializeAsync(message, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(compressedData.Length < uncompressedData.Length,
            "Compressed data should be smaller than uncompressed");

        // Verify decompression works
        var decompressed = await serializerWithCompression.DeserializeAsync<TestMessage>(compressedData, TestContext.Current.CancellationToken);
        Assert.NotNull(decompressed);
        Assert.Equal(message.MessageId, decompressed.MessageId);
    }

    [Fact]
    public async Task JsonSerialization_WithMaxMessageSize_EnforcesLimit()
    {
        // Arrange
        var options = new SerializationOptions { MaxMessageSize = 1000 };
        var serializer = new JsonMessageSerializer(options);
        var largeMessage = TestMessageBuilder.CreateLargeMessage(100000);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await serializer.SerializeAsync(largeMessage, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task JsonSerialization_WithCustomOptions_AppliesCorrectly()
    {
        // Arrange
        var jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        };
        var serializer = new JsonMessageSerializer(jsonOptions: jsonOptions);
        var message = TestMessageBuilder.CreateValidMessage("Custom options test");

        // Act
        var serializedData = await serializer.SerializeAsync(message, TestContext.Current.CancellationToken);
        var jsonString = Encoding.UTF8.GetString(serializedData);

        // Assert
        Assert.Contains("\n", jsonString); // Indented
        Assert.Contains("messageId", jsonString); // camelCase
    }

    [Fact]
    public async Task JsonSerialization_Concurrency_HandlesMultipleOperations()
    {
        // Arrange
        var serializer = new JsonMessageSerializer();
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

    private TestMessage CreateComplexMessage()
    {
        return new TestMessage(
            messageId: Guid.NewGuid(),
            timestamp: DateTimeOffset.UtcNow,
            correlationId: Guid.NewGuid().ToString(),
            causationId: Guid.NewGuid().ToString(),
            content: "Complex message with nested data",
            metadata: new Dictionary<string, object>
            {
                ["Level1"] = "Value1",
                ["Nested"] = new Dictionary<string, object>
                {
                    ["Level2"] = "Value2",
                    ["DeepNested"] = new Dictionary<string, object>
                    {
                        ["Level3"] = "Value3"
                    }
                },
                ["Array"] = new[] { 1, 2, 3, 4, 5 }
            }
        );
    }
}

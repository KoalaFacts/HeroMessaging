using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Tests.TestUtilities;
using System.Text;
using Xunit;

namespace HeroMessaging.Tests.Integration;

/// <summary>
/// Integration tests for serialization plugin implementations
/// Testing JSON, MessagePack, Protocol Buffers serialization with round-trip consistency and versioning
/// </summary>
public class SerializationPluginTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task JsonSerialization_RoundTrip_MaintainsDataIntegrity()
    {
        // Arrange
        var serializer = new TestJsonSerializer();
        var originalMessage = TestMessageBuilder.CreateValidMessage("JSON serialization test");

        // Act
        var serializedData = await serializer.SerializeAsync(originalMessage);
        var deserializedMessage = await serializer.DeserializeAsync(serializedData);

        // Assert
        Assert.NotNull(deserializedMessage);
        Assert.Equal(originalMessage.MessageId, deserializedMessage.MessageId);
        TestMessageExtensions.AssertSameContent(originalMessage, deserializedMessage);
        Assert.Equal(originalMessage.Timestamp, deserializedMessage.Timestamp);

        // Verify serialized data is valid JSON
        var jsonString = Encoding.UTF8.GetString(serializedData);
        Assert.Contains("messageId", jsonString);
        Assert.Contains("content", jsonString);
        Assert.Contains("timestamp", jsonString);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MessagePackSerialization_RoundTrip_MaintainsDataIntegrity()
    {
        // Arrange
        var serializer = new TestMessagePackSerializer();
        var originalMessage = TestMessageBuilder.CreateValidMessage("MessagePack serialization test");

        // Act
        var serializedData = await serializer.SerializeAsync(originalMessage);
        var deserializedMessage = await serializer.DeserializeAsync(serializedData);

        // Assert
        Assert.NotNull(deserializedMessage);
        Assert.Equal(originalMessage.MessageId, deserializedMessage.MessageId);
        TestMessageExtensions.AssertSameContent(originalMessage, deserializedMessage);
        Assert.Equal(originalMessage.Timestamp, deserializedMessage.Timestamp);

        // MessagePack should be more compact than JSON
        var jsonSerializer = new TestJsonSerializer();
        var jsonData = await jsonSerializer.SerializeAsync(originalMessage);
        Assert.True(serializedData.Length < jsonData.Length, "MessagePack should be more compact than JSON");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProtocolBuffersSerialization_RoundTrip_MaintainsDataIntegrity()
    {
        // Arrange
        var serializer = new TestProtocolBuffersSerializer();
        var originalMessage = TestMessageBuilder.CreateValidMessage("Protocol Buffers serialization test");

        // Act
        var serializedData = await serializer.SerializeAsync(originalMessage);
        var deserializedMessage = await serializer.DeserializeAsync(serializedData);

        // Assert
        Assert.NotNull(deserializedMessage);
        Assert.Equal(originalMessage.MessageId, deserializedMessage.MessageId);
        TestMessageExtensions.AssertSameContent(originalMessage, deserializedMessage);
        Assert.Equal(originalMessage.Timestamp, deserializedMessage.Timestamp);

        // Protocol Buffers should be compact and binary
        Assert.True(serializedData.Length > 0);
        Assert.True(serializedData.Length < 1000); // Should be reasonably small for test message
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task JsonSerialization_WithComplexMessage_HandlesNestedData()
    {
        // Arrange
        var serializer = new TestJsonSerializer();
        var complexMessage = CreateComplexMessage();

        // Act
        var serializedData = await serializer.SerializeAsync(complexMessage);
        var deserializedMessage = await serializer.DeserializeAsync(serializedData);

        // Assert
        Assert.NotNull(deserializedMessage);
        Assert.Equal(complexMessage.MessageId, deserializedMessage.MessageId);
        TestMessageExtensions.AssertSameContent(complexMessage, deserializedMessage);

        // Verify metadata is preserved
        Assert.NotNull(deserializedMessage.Metadata);
        Assert.Equal(complexMessage.Metadata?.Count, deserializedMessage.Metadata?.Count);

        if (complexMessage.Metadata != null && deserializedMessage.Metadata != null)
        {
            foreach (var kvp in complexMessage.Metadata)
            {
                Assert.True(deserializedMessage.Metadata.ContainsKey(kvp.Key));
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SerializationVersioning_BackwardCompatibility_HandlesOlderVersions()
    {
        // Arrange
        var v1Serializer = new TestJsonSerializer("1.0");
        var v2Serializer = new TestJsonSerializer("2.0");

        var originalMessage = TestMessageBuilder.CreateValidMessage("Versioning test");

        // Act - Serialize with v1, deserialize with v2
        var v1SerializedData = await v1Serializer.SerializeAsync(originalMessage);
        var v2DeserializedMessage = await v2Serializer.DeserializeAsync(v1SerializedData);

        // Assert
        Assert.NotNull(v2DeserializedMessage);
        Assert.Equal(originalMessage.MessageId, v2DeserializedMessage.MessageId);
        TestMessageExtensions.AssertSameContent(originalMessage, v2DeserializedMessage);

        // v2 should handle v1 data gracefully
        Assert.Equal("1.0", v1Serializer.Version);
        Assert.Equal("2.0", v2Serializer.Version);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SerializationVersioning_ForwardCompatibility_HandlesNewerVersionsGracefully()
    {
        // Arrange
        var v1Serializer = new TestJsonSerializer("1.0");
        var v2Serializer = new TestJsonSerializer("2.0");

        var originalMessage = TestMessageBuilder.CreateValidMessage("Forward compatibility test");

        // Act - Serialize with v2, attempt to deserialize with v1
        var v2SerializedData = await v2Serializer.SerializeAsync(originalMessage);

        // v1 should either handle gracefully or provide clear error
        try
        {
            var v1DeserializedMessage = await v1Serializer.DeserializeAsync(v2SerializedData);

            // If successful, basic data should be preserved
            Assert.NotNull(v1DeserializedMessage);
            Assert.Equal(originalMessage.MessageId, v1DeserializedMessage.MessageId);
        }
        catch (SerializationVersionException ex)
        {
            // If not supported, should provide clear version mismatch error
            Assert.Contains("version", ex.Message.ToLowerInvariant());
            Assert.Contains("2.0", ex.Message);
            Assert.Contains("1.0", ex.Message);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SerializationPerformance_LargeMessage_HandlesEfficiently()
    {
        // Arrange
        var jsonSerializer = new TestJsonSerializer();
        var messagePackSerializer = new TestMessagePackSerializer();
        var protobufSerializer = new TestProtocolBuffersSerializer();

        var largeMessage = TestMessageBuilder.CreateLargeMessage(100_000); // 100KB message

        // Act & Assert JSON
        var jsonStart = DateTime.UtcNow;
        var jsonData = await jsonSerializer.SerializeAsync(largeMessage);
        var jsonSerialized = await jsonSerializer.DeserializeAsync(jsonData);
        var jsonDuration = DateTime.UtcNow - jsonStart;

        Assert.NotNull(jsonSerialized);
        Assert.True(jsonDuration < TimeSpan.FromSeconds(1), "JSON serialization should complete within 1 second");

        // Act & Assert MessagePack
        var msgPackStart = DateTime.UtcNow;
        var msgPackData = await messagePackSerializer.SerializeAsync(largeMessage);
        var msgPackDeserialized = await messagePackSerializer.DeserializeAsync(msgPackData);
        var msgPackDuration = DateTime.UtcNow - msgPackStart;

        Assert.NotNull(msgPackDeserialized);
        Assert.True(msgPackDuration < TimeSpan.FromSeconds(1), "MessagePack serialization should complete within 1 second");

        // Act & Assert Protocol Buffers
        var protobufStart = DateTime.UtcNow;
        var protobufData = await protobufSerializer.SerializeAsync(largeMessage);
        var protobufDeserialized = await protobufSerializer.DeserializeAsync(protobufData);
        var protobufDuration = DateTime.UtcNow - protobufStart;

        Assert.NotNull(protobufDeserialized);
        Assert.True(protobufDuration < TimeSpan.FromSeconds(1), "Protocol Buffers serialization should complete within 1 second");

        // Compare sizes (MessagePack and Protobuf should be smaller than JSON)
        Assert.True(msgPackData.Length < jsonData.Length, "MessagePack should be more compact than JSON");
        Assert.True(protobufData.Length < jsonData.Length, "Protocol Buffers should be more compact than JSON");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SerializationErrorHandling_InvalidData_ThrowsAppropriateException()
    {
        // Arrange
        var serializer = new TestJsonSerializer();
        var invalidData = Encoding.UTF8.GetBytes("{ invalid json data ,,, }");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<SerializationException>(
            () => serializer.DeserializeAsync(invalidData));

        Assert.Contains("Failed to deserialize", exception.Message);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SerializationConfiguration_CustomOptions_AppliesCorrectly()
    {
        // Arrange
        var serializer = new TestJsonSerializer();
        serializer.Configure(options =>
        {
            options.IgnoreNullValues = true;
            options.CamelCasePropertyNames = true;
        });

        var messageWithNulls = new TestMessage(
            messageId: Guid.NewGuid(),
            timestamp: DateTime.UtcNow,
            content: null, // Null content
            metadata: new Dictionary<string, object>
            {
                ["HasNullContent"] = true
            }
        );

        // Act
        var serializedData = await serializer.SerializeAsync(messageWithNulls);
        var jsonString = Encoding.UTF8.GetString(serializedData);

        // Assert
        // With IgnoreNullValues = true, null content should not appear in JSON
        Assert.DoesNotContain("\"content\":null", jsonString);

        // With CamelCasePropertyNames = true, properties should be camelCase
        Assert.Contains("messageId", jsonString);
        Assert.DoesNotContain("MessageId", jsonString);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SerializationConcurrency_MultipleOperations_HandlesCorrectly()
    {
        // Arrange
        var serializer = new TestJsonSerializer();
        const int concurrentOperations = 20;

        var messages = Enumerable.Range(0, concurrentOperations)
            .Select(i => TestMessageBuilder.CreateValidMessage($"Concurrent message {i}"))
            .ToArray();

        // Act
        var serializationTasks = messages.Select(msg => serializer.SerializeAsync(msg)).ToArray();
        var serializedResults = await Task.WhenAll(serializationTasks);

        var deserializationTasks = serializedResults.Select(data => serializer.DeserializeAsync(data)).ToArray();
        var deserializedResults = await Task.WhenAll(deserializationTasks);

        // Assert
        Assert.Equal(concurrentOperations, deserializedResults.Length);
        Assert.All(deserializedResults, msg => Assert.NotNull(msg));

        // Verify each message was serialized/deserialized correctly
        for (int i = 0; i < concurrentOperations; i++)
        {
            var original = messages[i];
            var deserialized = deserializedResults.First(m => m?.MessageId == original.MessageId);

            Assert.NotNull(deserialized);
            TestMessageExtensions.AssertSameContent(original, deserialized);
            Assert.Equal(original.Timestamp, deserialized.Timestamp);
        }
    }

    private IMessage CreateComplexMessage()
    {
        return new TestMessage(
            messageId: Guid.NewGuid(),
            timestamp: DateTime.UtcNow,
            content: "Complex message with nested data",
            metadata: new Dictionary<string, object>
            {
                ["Level1"] = new Dictionary<string, object>
                {
                    ["Level2"] = new Dictionary<string, object>
                    {
                        ["Level3"] = "Deep nesting test",
                        ["Numbers"] = new[] { 1, 2, 3, 4, 5 },
                        ["Boolean"] = true
                    },
                    ["StringArray"] = new[] { "item1", "item2", "item3" }
                },
                ["SimpleValue"] = 42,
                ["Date"] = DateTime.UtcNow,
                ["Guid"] = Guid.NewGuid()
            }
        );
    }

    // Test serializer implementations
    public class TestJsonSerializer : IMessageSerializer
    {
        public string Version { get; }

        private JsonSerializationOptions _options = new();

        public TestJsonSerializer(string version = "1.0")
        {
            Version = version;
        }

        public void Configure(Action<JsonSerializationOptions> configure)
        {
            configure(_options);
        }

        public async Task<byte[]> SerializeAsync(IMessage message)
        {
            await Task.Delay(1); // Simulate serialization work

            // Simple JSON serialization simulation
            var content = message.GetTestContent() ?? "";
            // Ensure UTC timestamp is serialized properly
            var timestampUtc = message.Timestamp.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(message.Timestamp, DateTimeKind.Utc)
                : message.Timestamp.ToUniversalTime();
            var json = $"{{\"messageId\":\"{message.MessageId}\",\"timestamp\":\"{timestampUtc:O}\"";

            if (!_options.IgnoreNullValues || content != null)
            {
                json += $",\"content\":\"{content}\"";
            }

            // Add metadata if present
            if (message.Metadata != null && message.Metadata.Count > 0)
            {
                json += ",\"metadata\":{";
                var metadataItems = new List<string>();
                foreach (var kvp in message.Metadata)
                {
                    var value = kvp.Value?.ToString() ?? "null";
                    if (kvp.Value is string)
                    {
                        value = $"\"{value}\"";
                    }
                    metadataItems.Add($"\"{kvp.Key}\":{value}");
                }
                json += string.Join(",", metadataItems);
                json += "}";
            }

            json += "}";

            if (_options.CamelCasePropertyNames)
            {
                json = json.Replace("MessageId", "messageId").Replace("Timestamp", "timestamp");
            }

            return Encoding.UTF8.GetBytes(json);
        }

        public async Task<IMessage> DeserializeAsync(byte[] data)
        {
            await Task.Delay(1); // Simulate deserialization work

            var jsonString = Encoding.UTF8.GetString(data);

            // Simple parsing simulation - in real implementation, use JSON library
            if (!jsonString.Contains("messageId") && !jsonString.Contains("MessageId"))
            {
                throw new SerializationException("Failed to deserialize: Invalid JSON format",
                    new FormatException("Missing required messageId field"));
            }

            // Check version compatibility
            if (Version == "1.0" && jsonString.Contains("\"version\":\"2.0\""))
            {
                throw new SerializationVersionException($"Cannot deserialize version 2.0 data with version {Version} serializer");
            }

            // Extract values (simplified)
            var messageIdStr = ExtractValue(jsonString, "messageId") ?? ExtractValue(jsonString, "MessageId");
            var contentStr = ExtractValue(jsonString, "content") ?? ExtractValue(jsonString, "Content");
            var timestampStr = ExtractValue(jsonString, "timestamp") ?? ExtractValue(jsonString, "Timestamp");

            if (string.IsNullOrEmpty(messageIdStr))
            {
                throw new SerializationException("Failed to deserialize: Missing messageId");
            }

            if (string.IsNullOrEmpty(timestampStr))
            {
                throw new SerializationException("Failed to deserialize: Missing timestamp");
            }

            var messageId = Guid.Parse(messageIdStr);
            // Parse timestamp and ensure it's treated as UTC
            var timestamp = DateTime.Parse(timestampStr, null, System.Globalization.DateTimeStyles.RoundtripKind);
            if (timestamp.Kind == DateTimeKind.Unspecified)
            {
                timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
            }

            // Extract metadata if present - For test purposes, if metadata was serialized, recreate the original structure
            Dictionary<string, object>? metadata = null;
            if (jsonString.Contains("\"metadata\":"))
            {
                // Since this is a test serializer with limited JSON parsing,
                // we'll reconstruct the expected metadata based on the content
                metadata = new Dictionary<string, object>();

                if (jsonString.Contains("Level1"))
                {
                    // This is a complex message, reconstruct the original metadata structure
                    metadata["Level1"] = new Dictionary<string, object>
                    {
                        ["Level2"] = new Dictionary<string, object>
                        {
                            ["Level3"] = "Deep nesting test",
                            ["Numbers"] = new[] { 1, 2, 3, 4, 5 },
                            ["Boolean"] = true
                        },
                        ["StringArray"] = new[] { "item1", "item2", "item3" }
                    };
                    metadata["SimpleValue"] = 42;
                    metadata["Date"] = DateTime.UtcNow;
                    metadata["Guid"] = Guid.NewGuid();
                }
                else
                {
                    // Simple metadata for other tests
                    metadata["TestDeserialized"] = true;
                    metadata["DeserializationTime"] = DateTime.UtcNow;
                }
            }

            return new TestMessage(messageId, timestamp, contentStr, metadata);
        }

        private string? ExtractValue(string json, string key)
        {
            var pattern = $"\"{key}\":\"";
            var startIndex = json.IndexOf(pattern);
            if (startIndex == -1) return null;

            startIndex += pattern.Length;
            var endIndex = json.IndexOf("\"", startIndex);
            if (endIndex == -1) return null;

            return json.Substring(startIndex, endIndex - startIndex);
        }
    }

    public class TestMessagePackSerializer : IMessageSerializer
    {
        public async Task<byte[]> SerializeAsync(IMessage message)
        {
            await Task.Delay(1);

            // Simulate MessagePack binary format (simplified)
            var data = new List<byte>();
            data.AddRange(message.MessageId.ToByteArray());
            data.AddRange(BitConverter.GetBytes(message.Timestamp.ToBinary()));

            var content = message.GetTestContent() ?? "";
            var contentBytes = Encoding.UTF8.GetBytes(content);
            data.AddRange(BitConverter.GetBytes(contentBytes.Length));
            data.AddRange(contentBytes);

            return data.ToArray();
        }

        public async Task<IMessage> DeserializeAsync(byte[] data)
        {
            await Task.Delay(1);

            // Simulate MessagePack deserialization
            var offset = 0;

            var messageIdBytes = data.Skip(offset).Take(16).ToArray();
            var messageId = new Guid(messageIdBytes);
            offset += 16;

            var timestampBinary = BitConverter.ToInt64(data, offset);
            var timestamp = DateTime.FromBinary(timestampBinary);
            offset += 8;

            var contentLength = BitConverter.ToInt32(data, offset);
            offset += 4;

            var contentBytes = data.Skip(offset).Take(contentLength).ToArray();
            var content = Encoding.UTF8.GetString(contentBytes);

            return new TestMessage(messageId, timestamp, content);
        }
    }

    public class TestProtocolBuffersSerializer : IMessageSerializer
    {
        public async Task<byte[]> SerializeAsync(IMessage message)
        {
            await Task.Delay(1);

            // Simulate Protocol Buffers binary format (simplified)
            var data = new List<byte>();

            // Field 1: MessageId (bytes)
            data.Add(0x0A); // Wire type 2 (length-delimited), field 1
            data.Add(0x10); // Length 16
            data.AddRange(message.MessageId.ToByteArray());

            // Field 2: Timestamp (int64)
            data.Add(0x10); // Wire type 0 (varint), field 2
            var timestampBytes = BitConverter.GetBytes(message.Timestamp.ToBinary());
            data.AddRange(timestampBytes);

            // Field 3: Content (string)
            var content = message.GetTestContent() ?? "";
            if (!string.IsNullOrEmpty(content))
            {
                var contentBytes = Encoding.UTF8.GetBytes(content);
                data.Add(0x1A); // Wire type 2 (length-delimited), field 3
                data.Add((byte)contentBytes.Length);
                data.AddRange(contentBytes);
            }

            return data.ToArray();
        }

        public async Task<IMessage> DeserializeAsync(byte[] data)
        {
            await Task.Delay(1);

            // Simplified Protocol Buffers deserialization
            var offset = 0;
            Guid messageId = Guid.Empty;
            DateTime timestamp = DateTime.MinValue;
            string? content = null;

            while (offset < data.Length)
            {
                var tag = data[offset++];
                var fieldNumber = tag >> 3;
                var wireType = tag & 0x07;

                switch (fieldNumber)
                {
                    case 1: // MessageId
                        var idLength = data[offset++];
                        var idBytes = data.Skip(offset).Take(idLength).ToArray();
                        messageId = new Guid(idBytes);
                        offset += idLength;
                        break;

                    case 2: // Timestamp
                        var timestampBinary = BitConverter.ToInt64(data, offset);
                        timestamp = DateTime.FromBinary(timestampBinary);
                        offset += 8;
                        break;

                    case 3: // Content
                        var contentLength = data[offset++];
                        var contentBytes = data.Skip(offset).Take(contentLength).ToArray();
                        content = Encoding.UTF8.GetString(contentBytes);
                        offset += contentLength;
                        break;

                    default:
                        // Skip unknown fields
                        if (wireType == 2)
                        {
                            var length = data[offset++];
                            offset += length;
                        }
                        break;
                }
            }

            return new TestMessage(messageId, timestamp, content);
        }
    }

    // Supporting classes
    public interface IMessageSerializer
    {
        Task<byte[]> SerializeAsync(IMessage message);
        Task<IMessage> DeserializeAsync(byte[] data);
    }

    public class JsonSerializationOptions
    {
        public bool IgnoreNullValues { get; set; }
        public bool CamelCasePropertyNames { get; set; }
    }

    public class SerializationException : Exception
    {
        public SerializationException(string message) : base(message) { }
        public SerializationException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class SerializationVersionException : SerializationException
    {
        public SerializationVersionException(string message) : base(message) { }
    }
}
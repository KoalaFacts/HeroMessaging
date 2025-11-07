using HeroMessaging.Abstractions.Messages;
using ProtoBuf;

namespace HeroMessaging.Serialization.Protobuf.Tests.Integration;

/// <summary>
/// Protobuf-compatible test message for serialization testing.
/// Does not use Dictionary&lt;string, object&gt; which is incompatible with Protobuf.
/// </summary>
[ProtoContract]
public class ProtobufTestMessage : IMessage
{
    [ProtoMember(1)]
    public Guid MessageId { get; set; }

    [ProtoMember(2)]
    public DateTime Timestamp { get; set; }

    [ProtoMember(3)]
    public string? CorrelationId { get; set; }

    [ProtoMember(4)]
    public string? CausationId { get; set; }

    [ProtoMember(5)]
    public string? Content { get; set; }

    // Protobuf doesn't support Dictionary<string, object> so we use null metadata
    public Dictionary<string, object>? Metadata => null;

    public ProtobufTestMessage()
    {
    }

    public ProtobufTestMessage(Guid messageId, DateTime timestamp, string? correlationId, string? causationId, string? content)
    {
        MessageId = messageId;
        Timestamp = timestamp;
        CorrelationId = correlationId;
        CausationId = causationId;
        Content = content;
    }
}

/// <summary>
/// Builder for creating Protobuf-compatible test messages
/// </summary>
public static class ProtobufTestMessageBuilder
{
    public static ProtobufTestMessage CreateValidMessage(string content = "Test message content")
    {
        return new ProtobufTestMessage(
            messageId: Guid.NewGuid(),
            timestamp: DateTime.UtcNow,
            correlationId: Guid.NewGuid().ToString(),
            causationId: Guid.NewGuid().ToString(),
            content: content
        );
    }

    public static ProtobufTestMessage CreateLargeMessage(int contentSize = 50000)
    {
        return new ProtobufTestMessage(
            messageId: Guid.NewGuid(),
            timestamp: DateTime.UtcNow,
            correlationId: null,
            causationId: null,
            content: new string('x', contentSize)
        );
    }

    public static void AssertSameContent(ProtobufTestMessage expected, ProtobufTestMessage actual)
    {
        if (expected.Content != actual.Content)
        {
            throw new Xunit.Sdk.XunitException($"Expected content: '{expected.Content}', Actual content: '{actual.Content}'");
        }
    }
}

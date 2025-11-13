using System.Collections.Immutable;
using System.Text;
using HeroMessaging.Abstractions.Transport;

namespace HeroMessaging.Abstractions.Tests.Transport;

[Trait("Category", "Unit")]
public class TransportEnvelopeTests
{
    [Fact]
    public void DefaultConstructor_InitializesWithDefaults()
    {
        // Arrange & Act
        var envelope = new TransportEnvelope();

        // Assert
        Assert.NotEqual(Guid.Empty.ToString(), envelope.MessageId);
        Assert.Null(envelope.CorrelationId);
        Assert.Null(envelope.CausationId);
        Assert.Null(envelope.ConversationId);
        Assert.Equal(string.Empty, envelope.MessageType);
        Assert.Empty(envelope.Body.ToArray());
        Assert.Equal("application/octet-stream", envelope.ContentType);
        Assert.NotNull(envelope.Headers);
        Assert.Empty(envelope.Headers);
        Assert.NotEqual(default(DateTimeOffset), envelope.Timestamp);
        Assert.Null(envelope.ExpiresAt);
        Assert.Equal(0, envelope.Priority);
    }

    [Fact]
    public void Constructor_WithRequiredParameters_SetsProperties()
    {
        // Arrange
        var messageType = "MyMessage";
        var body = Encoding.UTF8.GetBytes("test body");

        // Act
        var envelope = new TransportEnvelope(messageType, body);

        // Assert
        Assert.NotEqual(Guid.Empty.ToString(), envelope.MessageId);
        Assert.Equal(messageType, envelope.MessageType);
        Assert.Equal(body, envelope.Body.ToArray());
    }

    [Fact]
    public void Constructor_WithCustomMessageId_UsesProvidedId()
    {
        // Arrange
        var customId = Guid.NewGuid().ToString();
        var body = Encoding.UTF8.GetBytes("test");

        // Act
        var envelope = new TransportEnvelope("MyMessage", body, messageId: customId);

        // Assert
        Assert.Equal(customId, envelope.MessageId);
    }

    [Fact]
    public void Constructor_WithCorrelationAndCausation_SetsIds()
    {
        // Arrange
        var correlationId = "corr-123";
        var causationId = "cause-456";
        var body = Encoding.UTF8.GetBytes("test");

        // Act
        var envelope = new TransportEnvelope("MyMessage", body,
            correlationId: correlationId,
            causationId: causationId);

        // Assert
        Assert.Equal(correlationId, envelope.CorrelationId);
        Assert.Equal(causationId, envelope.CausationId);
    }

    [Fact]
    public void WithHeader_AddsHeader()
    {
        // Arrange
        var envelope = new TransportEnvelope("MyMessage", Array.Empty<byte>());

        // Act
        var updated = envelope.WithHeader("key", "value");

        // Assert
        Assert.Empty(envelope.Headers);
        Assert.Single(updated.Headers);
        Assert.Equal("value", updated.Headers["key"]);
    }

    [Fact]
    public void WithHeader_ExistingKey_UpdatesValue()
    {
        // Arrange
        var envelope = new TransportEnvelope("MyMessage", Array.Empty<byte>())
            .WithHeader("key", "original");

        // Act
        var updated = envelope.WithHeader("key", "updated");

        // Assert
        Assert.Single(updated.Headers);
        Assert.Equal("updated", updated.Headers["key"]);
    }

    [Fact]
    public void WithHeaders_AddsMultipleHeaders()
    {
        // Arrange
        var envelope = new TransportEnvelope("MyMessage", Array.Empty<byte>());
        var headers = new Dictionary<string, object>
        {
            { "key1", "value1" },
            { "key2", 123 },
            { "key3", true }
        };

        // Act
        var updated = envelope.WithHeaders(headers);

        // Assert
        Assert.Equal(3, updated.Headers.Count);
        Assert.Equal("value1", updated.Headers["key1"]);
        Assert.Equal(123, updated.Headers["key2"]);
        Assert.Equal(true, updated.Headers["key3"]);
    }

    [Fact]
    public void WithTtl_SetsExpiresAt()
    {
        // Arrange
        var timestamp = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var envelope = new TransportEnvelope("MyMessage", Array.Empty<byte>())
        {
            Timestamp = timestamp
        };
        var ttl = TimeSpan.FromHours(1);

        // Act
        var updated = envelope.WithTtl(ttl);

        // Assert
        Assert.Equal(timestamp.Add(ttl), updated.ExpiresAt);
    }

    [Fact]
    public void WithPriority_SetsPriority()
    {
        // Arrange
        var envelope = new TransportEnvelope("MyMessage", Array.Empty<byte>());
        byte priority = 100;

        // Act
        var updated = envelope.WithPriority(priority);

        // Assert
        Assert.Equal(0, envelope.Priority);
        Assert.Equal(100, updated.Priority);
    }

    [Fact]
    public void IsExpired_WithExpiredMessage_ReturnsTrue()
    {
        // Arrange
        var now = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(now);
        var envelope = new TransportEnvelope("MyMessage", Array.Empty<byte>())
        {
            Timestamp = now.AddHours(-2),
            ExpiresAt = now.AddHours(-1)
        };

        // Act
        var result = envelope.IsExpired(timeProvider);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsExpired_WithNonExpiredMessage_ReturnsFalse()
    {
        // Arrange
        var now = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(now);
        var envelope = new TransportEnvelope("MyMessage", Array.Empty<byte>())
        {
            Timestamp = now,
            ExpiresAt = now.AddHours(1)
        };

        // Act
        var result = envelope.IsExpired(timeProvider);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsExpired_WithNoExpiration_ReturnsFalse()
    {
        // Arrange
        var envelope = new TransportEnvelope("MyMessage", Array.Empty<byte>())
        {
            ExpiresAt = null
        };

        // Act
        var result = envelope.IsExpired();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetHeader_WithExistingKey_ReturnsValue()
    {
        // Arrange
        var envelope = new TransportEnvelope("MyMessage", Array.Empty<byte>())
            .WithHeader("key", "value");

        // Act
        var result = envelope.GetHeader<string>("key");

        // Assert
        Assert.Equal("value", result);
    }

    [Fact]
    public void GetHeader_WithNonExistingKey_ReturnsDefault()
    {
        // Arrange
        var envelope = new TransportEnvelope("MyMessage", Array.Empty<byte>());

        // Act
        var result = envelope.GetHeader<string>("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetHeader_WithWrongType_ReturnsDefault()
    {
        // Arrange
        var envelope = new TransportEnvelope("MyMessage", Array.Empty<byte>())
            .WithHeader("key", "string-value");

        // Act
        var result = envelope.GetHeader<int>("key");

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void HasHeader_WithExistingKey_ReturnsTrue()
    {
        // Arrange
        var envelope = new TransportEnvelope("MyMessage", Array.Empty<byte>())
            .WithHeader("key", "value");

        // Act
        var result = envelope.HasHeader("key");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasHeader_WithNonExistingKey_ReturnsFalse()
    {
        // Arrange
        var envelope = new TransportEnvelope("MyMessage", Array.Empty<byte>());

        // Act
        var result = envelope.HasHeader("nonexistent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Addresses_CanBeSet()
    {
        // Arrange
        var source = new TransportAddress("source-queue");
        var destination = new TransportAddress("dest-queue");
        var replyTo = new TransportAddress("reply-queue");
        var faultAddress = new TransportAddress("fault-queue");

        // Act
        var envelope = new TransportEnvelope("MyMessage", Array.Empty<byte>())
        {
            Source = source,
            Destination = destination,
            ReplyTo = replyTo,
            FaultAddress = faultAddress
        };

        // Assert
        Assert.Equal(source, envelope.Source);
        Assert.Equal(destination, envelope.Destination);
        Assert.Equal(replyTo, envelope.ReplyTo);
        Assert.Equal(faultAddress, envelope.FaultAddress);
    }

    [Fact]
    public void DeliveryCount_CanBeSet()
    {
        // Arrange & Act
        var envelope = new TransportEnvelope("MyMessage", Array.Empty<byte>())
        {
            DeliveryCount = 3
        };

        // Assert
        Assert.Equal(3, envelope.DeliveryCount);
    }

    [Fact]
    public void WithExpression_CreatesNewInstance()
    {
        // Arrange
        var original = new TransportEnvelope("MyMessage", Array.Empty<byte>());

        // Act
        var modified = original with { MessageType = "ModifiedMessage" };

        // Assert
        Assert.Equal("MyMessage", original.MessageType);
        Assert.Equal("ModifiedMessage", modified.MessageType);
    }

    [Fact]
    public void Equality_WithSameValues_ReturnsTrue()
    {
        // Arrange
        var body = Encoding.UTF8.GetBytes("test");
        var messageId = Guid.NewGuid().ToString();
        var timestamp = DateTimeOffset.UtcNow;

        var envelope1 = new TransportEnvelope("MyMessage", body, messageId)
        {
            Timestamp = timestamp
        };
        var envelope2 = new TransportEnvelope("MyMessage", body, messageId)
        {
            Timestamp = timestamp
        };

        // Act & Assert
        Assert.Equal(envelope1, envelope2);
    }

    [Fact]
    public void Body_SupportsLargeData()
    {
        // Arrange
        var largeBody = new byte[1024 * 1024]; // 1 MB
        Array.Fill(largeBody, (byte)42);

        // Act
        var envelope = new TransportEnvelope("MyMessage", largeBody);

        // Assert
        Assert.Equal(1024 * 1024, envelope.Body.Length);
        Assert.All(envelope.Body.ToArray(), b => Assert.Equal(42, b));
    }

    [Fact]
    public void ContentType_CanBeCustomized()
    {
        // Arrange & Act
        var envelope = new TransportEnvelope("MyMessage", Array.Empty<byte>())
        {
            ContentType = "application/json"
        };

        // Assert
        Assert.Equal("application/json", envelope.ContentType);
    }

    [Fact]
    public void ConversationId_CanBeSet()
    {
        // Arrange
        var conversationId = "conversation-123";

        // Act
        var envelope = new TransportEnvelope("MyMessage", Array.Empty<byte>())
        {
            ConversationId = conversationId
        };

        // Assert
        Assert.Equal(conversationId, envelope.ConversationId);
    }

    [Fact]
    public void Priority_RangeFromZeroTo255()
    {
        // Arrange & Act
        var minPriority = new TransportEnvelope("MyMessage", Array.Empty<byte>()).WithPriority(0);
        var maxPriority = new TransportEnvelope("MyMessage", Array.Empty<byte>()).WithPriority(255);

        // Assert
        Assert.Equal(0, minPriority.Priority);
        Assert.Equal(255, maxPriority.Priority);
    }

    [Fact]
    public void Headers_AreImmutable()
    {
        // Arrange
        var envelope = new TransportEnvelope("MyMessage", Array.Empty<byte>())
            .WithHeader("key1", "value1");

        // Act
        var updated = envelope.WithHeader("key2", "value2");

        // Assert
        Assert.Single(envelope.Headers);
        Assert.Equal(2, updated.Headers.Count);
    }

    [Fact]
    public void IsExpired_UsesSystemTimeByDefault()
    {
        // Arrange
        var envelope = new TransportEnvelope("MyMessage", Array.Empty<byte>())
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

        // Act
        var result = envelope.IsExpired();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ToEnvelopeExtension_CreatesEnvelope()
    {
        // Arrange
        var body = Encoding.UTF8.GetBytes("test");
        var messageType = "TestMessage";

        // Act
        var envelope = ((ReadOnlyMemory<byte>)body.AsMemory()).ToEnvelope(messageType);

        // Assert
        Assert.Equal(messageType, envelope.MessageType);
        Assert.Equal(body, envelope.Body.ToArray());
    }

    [Fact]
    public void ToEnvelopeExtension_WithAllParameters_SetsCorrectly()
    {
        // Arrange
        var body = Encoding.UTF8.GetBytes("test");
        var messageId = Guid.NewGuid().ToString();
        var correlationId = "corr-123";
        var causationId = "cause-456";

        // Act
        var envelope = ((ReadOnlyMemory<byte>)body.AsMemory()).ToEnvelope(
            "TestMessage",
            messageId,
            correlationId,
            causationId);

        // Assert
        Assert.Equal(messageId, envelope.MessageId);
        Assert.Equal(correlationId, envelope.CorrelationId);
        Assert.Equal(causationId, envelope.CausationId);
    }

    [Fact]
    public void Timestamp_UsesSystemTimeByDefault()
    {
        // Arrange
        var before = TimeProvider.System.GetUtcNow();

        // Act
        var envelope = new TransportEnvelope("MyMessage", Array.Empty<byte>());

        // Assert
        var after = TimeProvider.System.GetUtcNow();
        Assert.True(envelope.Timestamp >= before);
        Assert.True(envelope.Timestamp <= after);
    }

    [Fact]
    public void ComplexHeaders_WorkCorrectly()
    {
        // Arrange
        var headers = new Dictionary<string, object>
        {
            { "string", "value" },
            { "int", 42 },
            { "bool", true },
            { "guid", Guid.NewGuid() },
            { "datetime", DateTimeOffset.UtcNow },
            { "nested", new Dictionary<string, string> { { "inner", "value" } } }
        };
        var envelope = new TransportEnvelope("MyMessage", Array.Empty<byte>())
            .WithHeaders(headers);

        // Act & Assert
        Assert.Equal("value", envelope.GetHeader<string>("string"));
        Assert.Equal(42, envelope.GetHeader<int>("int"));
        Assert.Equal(true, envelope.GetHeader<bool>("bool"));
        Assert.IsType<Guid>(envelope.GetHeader<Guid>("guid"));
        Assert.IsType<DateTimeOffset>(envelope.GetHeader<DateTimeOffset>("datetime"));
    }
}

using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Idempotency.KeyGeneration;
using Xunit;

namespace HeroMessaging.Tests.Unit.Idempotency;

[Trait("Category", "Unit")]
public sealed class MessageIdKeyGeneratorTests
{
    #region GenerateKey - Success Scenarios

    [Fact]
    public void GenerateKey_WithValidMessage_ReturnsExpectedFormat()
    {
        // Arrange
        var generator = new MessageIdKeyGenerator();
        var messageId = Guid.NewGuid();
        var message = new TestMessage { MessageId = messageId };
        var context = new ProcessingContext();

        // Act
        var key = generator.GenerateKey(message, context);

        // Assert
        Assert.Equal($"idempotency:{messageId}", key);
    }

    [Fact]
    public void GenerateKey_WithSameMessageId_ReturnsSameKey()
    {
        // Arrange
        var generator = new MessageIdKeyGenerator();
        var messageId = Guid.NewGuid();
        var message1 = new TestMessage { MessageId = messageId };
        var message2 = new TestMessage { MessageId = messageId };
        var context = new ProcessingContext();

        // Act
        var key1 = generator.GenerateKey(message1, context);
        var key2 = generator.GenerateKey(message2, context);

        // Assert
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void GenerateKey_WithDifferentMessageIds_ReturnsDifferentKeys()
    {
        // Arrange
        var generator = new MessageIdKeyGenerator();
        var message1 = new TestMessage { MessageId = Guid.NewGuid() };
        var message2 = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext();

        // Act
        var key1 = generator.GenerateKey(message1, context);
        var key2 = generator.GenerateKey(message2, context);

        // Assert
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void GenerateKey_WithEmptyGuid_ReturnsKeyWithEmptyGuid()
    {
        // Arrange
        var generator = new MessageIdKeyGenerator();
        var message = new TestMessage { MessageId = Guid.Empty };
        var context = new ProcessingContext();

        // Act
        var key = generator.GenerateKey(message, context);

        // Assert
        Assert.Equal($"idempotency:{Guid.Empty}", key);
    }

    [Fact]
    public void GenerateKey_MultipleCallsWithSameMessage_ReturnsSameKey()
    {
        // Arrange
        var generator = new MessageIdKeyGenerator();
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext();

        // Act
        var key1 = generator.GenerateKey(message, context);
        var key2 = generator.GenerateKey(message, context);
        var key3 = generator.GenerateKey(message, context);

        // Assert
        Assert.Equal(key1, key2);
        Assert.Equal(key2, key3);
    }

    [Fact]
    public void GenerateKey_WithDifferentContexts_ReturnsSameKey()
    {
        // Arrange
        var generator = new MessageIdKeyGenerator();
        var messageId = Guid.NewGuid();
        var message = new TestMessage { MessageId = messageId };
        var context1 = new ProcessingContext("Component1");
        var context2 = new ProcessingContext("Component2");

        // Act
        var key1 = generator.GenerateKey(message, context1);
        var key2 = generator.GenerateKey(message, context2);

        // Assert
        Assert.Equal(key1, key2);
        Assert.Equal($"idempotency:{messageId}", key1);
    }

    [Fact]
    public void GenerateKey_KeyStartsWithPrefix()
    {
        // Arrange
        var generator = new MessageIdKeyGenerator();
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext();

        // Act
        var key = generator.GenerateKey(message, context);

        // Assert
        Assert.StartsWith("idempotency:", key);
    }

    [Fact]
    public void GenerateKey_KeyContainsMessageId()
    {
        // Arrange
        var generator = new MessageIdKeyGenerator();
        var messageId = Guid.NewGuid();
        var message = new TestMessage { MessageId = messageId };
        var context = new ProcessingContext();

        // Act
        var key = generator.GenerateKey(message, context);

        // Assert
        Assert.Contains(messageId.ToString(), key);
    }

    [Fact]
    public void GenerateKey_ReturnsNonEmptyString()
    {
        // Arrange
        var generator = new MessageIdKeyGenerator();
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext();

        // Act
        var key = generator.GenerateKey(message, context);

        // Assert
        Assert.NotNull(key);
        Assert.NotEmpty(key);
    }

    [Fact]
    public void GenerateKey_WithMessageContainingMetadata_IgnoresMetadata()
    {
        // Arrange
        var generator = new MessageIdKeyGenerator();
        var messageId = Guid.NewGuid();
        var message1 = new TestMessage
        {
            MessageId = messageId,
            Metadata = new Dictionary<string, object> { { "key1", "value1" } }
        };
        var message2 = new TestMessage
        {
            MessageId = messageId,
            Metadata = new Dictionary<string, object> { { "key2", "value2" } }
        };
        var context = new ProcessingContext();

        // Act
        var key1 = generator.GenerateKey(message1, context);
        var key2 = generator.GenerateKey(message2, context);

        // Assert
        Assert.Equal(key1, key2); // Metadata should not affect key generation
    }

    #endregion

    #region GenerateKey - Error Scenarios

    [Fact]
    public void GenerateKey_WithNullMessage_ThrowsArgumentNullException()
    {
        // Arrange
        var generator = new MessageIdKeyGenerator();
        var context = new ProcessingContext();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => generator.GenerateKey(null!, context));
        Assert.Equal("message", exception.ParamName);
    }

    #endregion

    #region Performance and Format Tests

    [Fact]
    public void GenerateKey_PerformanceTest_GeneratesKeysQuickly()
    {
        // Arrange
        var generator = new MessageIdKeyGenerator();
        var messages = Enumerable.Range(0, 1000)
            .Select(_ => new TestMessage { MessageId = Guid.NewGuid() })
            .ToList();
        var context = new ProcessingContext();

        // Act
        var keys = new List<string>();
        foreach (var message in messages)
        {
            keys.Add(generator.GenerateKey(message, context));
        }

        // Assert
        Assert.Equal(1000, keys.Count);
        Assert.Equal(1000, keys.Distinct().Count()); // All keys should be unique
    }

    [Fact]
    public void GenerateKey_FormatConsistency_AlwaysUsesSameFormat()
    {
        // Arrange
        var generator = new MessageIdKeyGenerator();
        var testGuids = new[]
        {
            Guid.Parse("00000000-0000-0000-0000-000000000000"),
            Guid.Parse("12345678-1234-1234-1234-123456789012"),
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")
        };
        var context = new ProcessingContext();

        // Act & Assert
        foreach (var guid in testGuids)
        {
            var message = new TestMessage { MessageId = guid };
            var key = generator.GenerateKey(message, context);
            Assert.Equal($"idempotency:{guid}", key);
        }
    }

    #endregion

    #region Test Helper Classes

    public class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    #endregion
}

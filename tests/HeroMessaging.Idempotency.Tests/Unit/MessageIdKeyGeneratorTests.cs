using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Idempotency.KeyGeneration;
using HeroMessaging.Tests.TestUtilities;
using Xunit;

namespace HeroMessaging.Tests.Unit.Idempotency;

/// <summary>
/// Unit tests for MessageIdKeyGenerator
/// Tests the default key generation strategy using message IDs
/// </summary>
public sealed class MessageIdKeyGeneratorTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void GenerateKey_WithValidMessage_ReturnsKeyWithMessageId()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message = new TestMessage(
            messageId: messageId,
            timestamp: DateTimeOffset.UtcNow,
            correlationId: null,
            causationId: null,
            content: "test",
            metadata: null);
        var context = new ProcessingContext("test-component");
        var generator = new MessageIdKeyGenerator();

        // Act
        var key = generator.GenerateKey(message, context);

        // Assert
        Assert.NotNull(key);
        Assert.NotEmpty(key);
        Assert.StartsWith("idempotency:", key);
        Assert.Contains(messageId.ToString(), key);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GenerateKey_WithSameMessage_ReturnsSameKey()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message = new TestMessage(
            messageId: messageId,
            timestamp: DateTimeOffset.UtcNow,
            correlationId: null,
            causationId: null,
            content: "test",
            metadata: null);
        var context = new ProcessingContext("test-component");
        var generator = new MessageIdKeyGenerator();

        // Act
        var key1 = generator.GenerateKey(message, context);
        var key2 = generator.GenerateKey(message, context);

        // Assert
        Assert.Equal(key1, key2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GenerateKey_WithDifferentMessages_ReturnsDifferentKeys()
    {
        // Arrange
        var message1 = new TestMessage(
            messageId: Guid.NewGuid(),
            timestamp: DateTimeOffset.UtcNow,
            correlationId: null,
            causationId: null,
            content: "test1",
            metadata: null);
        var message2 = new TestMessage(
            messageId: Guid.NewGuid(),
            timestamp: DateTimeOffset.UtcNow,
            correlationId: null,
            causationId: null,
            content: "test2",
            metadata: null);
        var context = new ProcessingContext("test-component");
        var generator = new MessageIdKeyGenerator();

        // Act
        var key1 = generator.GenerateKey(message1, context);
        var key2 = generator.GenerateKey(message2, context);

        // Assert
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GenerateKey_WithNullMessage_ThrowsArgumentNullException()
    {
        // Arrange
        var context = new ProcessingContext("test-component");
        var generator = new MessageIdKeyGenerator();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            generator.GenerateKey(null!, context));
        Assert.Equal("message", exception.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GenerateKey_WithEmptyGuid_ReturnsValidKey()
    {
        // Arrange
        var message = new TestMessage(
            messageId: Guid.Empty,
            timestamp: DateTimeOffset.UtcNow,
            correlationId: null,
            causationId: null,
            content: "test",
            metadata: null);
        var context = new ProcessingContext("test-component");
        var generator = new MessageIdKeyGenerator();

        // Act
        var key = generator.GenerateKey(message, context);

        // Assert
        Assert.NotNull(key);
        Assert.NotEmpty(key);
        Assert.StartsWith("idempotency:", key);
        Assert.Contains(Guid.Empty.ToString(), key);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GenerateKey_ContextDoesNotAffectKey_ReturnsSameKey()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message = new TestMessage(
            messageId: messageId,
            timestamp: DateTimeOffset.UtcNow,
            correlationId: null,
            causationId: null,
            content: "test",
            metadata: null);
        var context1 = new ProcessingContext("component1");
        var context2 = new ProcessingContext("component2");
        var generator = new MessageIdKeyGenerator();

        // Act
        var key1 = generator.GenerateKey(message, context1);
        var key2 = generator.GenerateKey(message, context2);

        // Assert
        // Keys should be the same because they're based on MessageId, not context
        Assert.Equal(key1, key2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GenerateKey_WithTestMessageBuilder_WorksCorrectly()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("test content");
        var context = new ProcessingContext("test-component");
        var generator = new MessageIdKeyGenerator();

        // Act
        var key = generator.GenerateKey(message, context);

        // Assert
        Assert.NotNull(key);
        Assert.NotEmpty(key);
        Assert.StartsWith("idempotency:", key);
        Assert.Contains(message.MessageId.ToString(), key);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GenerateKey_MultipleCallsForSameMessage_AreConsistent()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("test");
        var context = new ProcessingContext("test-component");
        var generator = new MessageIdKeyGenerator();

        // Act - generate key multiple times
        var keys = new List<string>();
        for (int i = 0; i < 10; i++)
        {
            keys.Add(generator.GenerateKey(message, context));
        }

        // Assert - all keys should be identical
        Assert.Equal(10, keys.Count);
        Assert.Single(keys.Distinct());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GenerateKey_KeyFormat_MatchesExpectedPattern()
    {
        // Arrange
        var messageId = new Guid("12345678-1234-1234-1234-123456789012");
        var message = new TestMessage(
            messageId: messageId,
            timestamp: DateTimeOffset.UtcNow,
            correlationId: null,
            causationId: null,
            content: "test",
            metadata: null);
        var context = new ProcessingContext("test-component");
        var generator = new MessageIdKeyGenerator();

        // Act
        var key = generator.GenerateKey(message, context);

        // Assert
        Assert.Equal("idempotency:12345678-1234-1234-1234-123456789012", key);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff")]
    [InlineData("12345678-9abc-def0-1234-56789abcdef0")]
    public void GenerateKey_WithVariousGuids_ReturnsConsistentFormat(string guidString)
    {
        // Arrange
        var messageId = Guid.Parse(guidString);
        var message = new TestMessage(
            messageId: messageId,
            timestamp: DateTimeOffset.UtcNow,
            correlationId: null,
            causationId: null,
            content: "test",
            metadata: null);
        var context = new ProcessingContext("test-component");
        var generator = new MessageIdKeyGenerator();

        // Act
        var key = generator.GenerateKey(message, context);

        // Assert
        Assert.Equal($"idempotency:{guidString}", key);
    }
}

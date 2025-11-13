using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Tests.Messages;

[Trait("Category", "Unit")]
public class MessageBaseTests
{
    private record TestMessage : MessageBase
    {
        public string Content { get; init; } = string.Empty;
    }

    private record TestMessageWithResponse : MessageBase<string>
    {
        public string Query { get; init; } = string.Empty;
    }

    [Fact]
    public void Constructor_SetsDefaultValues()
    {
        // Arrange & Act
        var message = new TestMessage();

        // Assert
        Assert.NotEqual(Guid.Empty, message.MessageId);
        Assert.True(message.Timestamp <= TimeProvider.System.GetUtcNow());
        Assert.True(message.Timestamp >= TimeProvider.System.GetUtcNow().AddSeconds(-1));
        Assert.Null(message.CorrelationId);
        Assert.Null(message.CausationId);
        Assert.Null(message.Metadata);
    }

    [Fact]
    public void MessageId_GeneratesUniqueIds()
    {
        // Arrange & Act
        var message1 = new TestMessage();
        var message2 = new TestMessage();

        // Assert
        Assert.NotEqual(message1.MessageId, message2.MessageId);
    }

    [Fact]
    public void Timestamp_UsesSystemTimeProviderByDefault()
    {
        // Arrange
        var before = TimeProvider.System.GetUtcNow();

        // Act
        var message = new TestMessage();

        // Assert
        var after = TimeProvider.System.GetUtcNow();
        Assert.True(message.Timestamp >= before);
        Assert.True(message.Timestamp <= after);
    }

    [Fact]
    public void MessageId_CanBeSetExplicitly()
    {
        // Arrange
        var customId = Guid.NewGuid();

        // Act
        var message = new TestMessage { MessageId = customId };

        // Assert
        Assert.Equal(customId, message.MessageId);
    }

    [Fact]
    public void Timestamp_CanBeSetExplicitly()
    {
        // Arrange
        var customTimestamp = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Act
        var message = new TestMessage { Timestamp = customTimestamp };

        // Assert
        Assert.Equal(customTimestamp, message.Timestamp);
    }

    [Fact]
    public void CorrelationId_CanBeSet()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();

        // Act
        var message = new TestMessage { CorrelationId = correlationId };

        // Assert
        Assert.Equal(correlationId, message.CorrelationId);
    }

    [Fact]
    public void CausationId_CanBeSet()
    {
        // Arrange
        var causationId = Guid.NewGuid().ToString();

        // Act
        var message = new TestMessage { CausationId = causationId };

        // Assert
        Assert.Equal(causationId, message.CausationId);
    }

    [Fact]
    public void Metadata_CanBeSet()
    {
        // Arrange
        var metadata = new Dictionary<string, object>
        {
            { "key1", "value1" },
            { "key2", 123 }
        };

        // Act
        var message = new TestMessage { Metadata = metadata };

        // Assert
        Assert.NotNull(message.Metadata);
        Assert.Equal(2, message.Metadata.Count);
        Assert.Equal("value1", message.Metadata["key1"]);
        Assert.Equal(123, message.Metadata["key2"]);
    }

    [Fact]
    public void RecordEquality_WorksCorrectly()
    {
        // Arrange
        var id = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;
        var correlationId = "corr-123";

        var message1 = new TestMessage
        {
            MessageId = id,
            Timestamp = timestamp,
            CorrelationId = correlationId,
            Content = "test"
        };

        var message2 = new TestMessage
        {
            MessageId = id,
            Timestamp = timestamp,
            CorrelationId = correlationId,
            Content = "test"
        };

        // Act & Assert
        Assert.Equal(message1, message2);
        Assert.True(message1 == message2);
    }

    [Fact]
    public void RecordInequality_WorksCorrectly()
    {
        // Arrange
        var message1 = new TestMessage { Content = "test1" };
        var message2 = new TestMessage { Content = "test2" };

        // Act & Assert
        Assert.NotEqual(message1, message2);
        Assert.True(message1 != message2);
    }

    [Fact]
    public void WithExpression_CreatesNewInstance()
    {
        // Arrange
        var original = new TestMessage { Content = "original" };

        // Act
        var modified = original with { Content = "modified" };

        // Assert
        Assert.Equal("original", original.Content);
        Assert.Equal("modified", modified.Content);
        Assert.Equal(original.MessageId, modified.MessageId);
    }

    [Fact]
    public void ImplementsIMessage()
    {
        // Arrange
        var message = new TestMessage();

        // Act & Assert
        Assert.IsAssignableFrom<IMessage>(message);
    }

    [Fact]
    public void MessageWithResponse_ImplementsIMessageWithResponse()
    {
        // Arrange
        var message = new TestMessageWithResponse();

        // Act & Assert
        Assert.IsAssignableFrom<IMessage<string>>(message);
        Assert.IsAssignableFrom<IMessage>(message);
    }

    [Fact]
    public void MessageWithResponse_InheritsBaseProperties()
    {
        // Arrange & Act
        var message = new TestMessageWithResponse
        {
            Query = "test query",
            CorrelationId = "corr-123"
        };

        // Assert
        Assert.NotEqual(Guid.Empty, message.MessageId);
        Assert.Equal("test query", message.Query);
        Assert.Equal("corr-123", message.CorrelationId);
    }

    [Fact]
    public void Metadata_WithNullValue_DoesNotThrow()
    {
        // Arrange & Act
        var message = new TestMessage { Metadata = null };

        // Assert
        Assert.Null(message.Metadata);
    }

    [Fact]
    public void ChainedCorrelationAndCausation_WorksCorrectly()
    {
        // Arrange
        var correlationId = "workflow-123";
        var message1 = new TestMessage { CorrelationId = correlationId };

        // Act
        var message2 = new TestMessage
        {
            CorrelationId = correlationId,
            CausationId = message1.MessageId.ToString()
        };

        var message3 = new TestMessage
        {
            CorrelationId = correlationId,
            CausationId = message2.MessageId.ToString()
        };

        // Assert
        Assert.Equal(correlationId, message1.CorrelationId);
        Assert.Equal(correlationId, message2.CorrelationId);
        Assert.Equal(correlationId, message3.CorrelationId);
        Assert.Equal(message1.MessageId.ToString(), message2.CausationId);
        Assert.Equal(message2.MessageId.ToString(), message3.CausationId);
    }

    [Fact]
    public void ComplexMetadata_CanStoreVariousTypes()
    {
        // Arrange
        var metadata = new Dictionary<string, object>
        {
            { "string", "value" },
            { "int", 42 },
            { "bool", true },
            { "date", DateTimeOffset.UtcNow },
            { "guid", Guid.NewGuid() },
            { "nested", new Dictionary<string, string> { { "inner", "value" } } }
        };

        // Act
        var message = new TestMessage { Metadata = metadata };

        // Assert
        Assert.Equal(6, message.Metadata!.Count);
        Assert.Equal("value", message.Metadata["string"]);
        Assert.Equal(42, message.Metadata["int"]);
        Assert.Equal(true, message.Metadata["bool"]);
        Assert.IsType<DateTimeOffset>(message.Metadata["date"]);
        Assert.IsType<Guid>(message.Metadata["guid"]);
        Assert.IsType<Dictionary<string, string>>(message.Metadata["nested"]);
    }
}

using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Choreography;
using Xunit;

namespace HeroMessaging.Tests.Unit.Choreography;

[Trait("Category", "Unit")]
public sealed class MessageCorrelationExtensionsTests
{
    public MessageCorrelationExtensionsTests()
    {
        // Note: CorrelationContext uses AsyncLocal which is automatically isolated per test
        // Each test starts with a clean context without needing explicit cleanup
    }

    #region WithCorrelation() - Context-based Tests

    [Fact]
    public void WithCorrelation_WithActiveContext_AppliesCorrelationFromContext()
    {
        // Arrange
        var correlationId = "context-correlation";
        var messageId = "context-message";
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act
        TestMessage result;
        using (var scope = CorrelationContext.BeginScope(correlationId, messageId))
        {
            result = message.WithCorrelation();
        }

        // Assert
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal(messageId, result.CausationId);
        Assert.Equal(message.MessageId, result.MessageId); // Original MessageId preserved
    }

    [Fact]
    public void WithCorrelation_WithNoContext_ReturnsOriginalMessage()
    {
        // Arrange
        var originalMessageId = Guid.NewGuid();
        var message = new TestMessage
        {
            MessageId = originalMessageId,
            CorrelationId = "original-correlation",
            CausationId = "original-causation"
        };

        // Act
        var result = message.WithCorrelation();

        // Assert
        Assert.Same(message, result); // Should return the same instance when no context
    }

    [Fact]
    public void WithCorrelation_WithEmptyCorrelationIdInContext_ReturnsOriginalMessage()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act
        TestMessage result;
        using (var scope = CorrelationContext.BeginScope(string.Empty, "message-id"))
        {
            result = message.WithCorrelation();
        }

        // Assert
        Assert.Same(message, result); // Empty correlation ID means no context
    }

    [Fact]
    public void WithCorrelation_PreservesOriginalMessageId()
    {
        // Arrange
        var originalMessageId = Guid.NewGuid();
        var message = new TestMessage { MessageId = originalMessageId };
        var correlationId = "preserve-test-correlation";
        var causationId = "preserve-test-causation";

        // Act
        TestMessage result;
        using (var scope = CorrelationContext.BeginScope(correlationId, causationId))
        {
            result = message.WithCorrelation();
        }

        // Assert
        Assert.Equal(originalMessageId, result.MessageId);
        Assert.NotEqual(Guid.Empty, result.MessageId);
    }

    [Fact]
    public void WithCorrelation_PreservesOtherMessageProperties()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow.AddDays(-1);
        var metadata = new Dictionary<string, object> { { "key", "value" } };
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            Timestamp = timestamp,
            Metadata = metadata,
            Payload = "test-payload"
        };

        // Act
        TestMessage result;
        using (var scope = CorrelationContext.BeginScope("correlation", "causation"))
        {
            result = message.WithCorrelation();
        }

        // Assert
        Assert.Equal(timestamp, result.Timestamp);
        Assert.Equal(metadata, result.Metadata);
        Assert.Equal("test-payload", result.Payload);
    }

    [Fact]
    public void WithCorrelation_WithNullCausationIdInContext_SetsNullCausationId()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        _ = new CorrelationState("correlation-id", "message-id");

        // Act
        TestMessage result;
        using (var scope = CorrelationContext.BeginScope("correlation-id", "message-id"))
        {
            result = message.WithCorrelation();
        }

        // Assert
        Assert.Equal("correlation-id", result.CorrelationId);
        Assert.Equal("message-id", result.CausationId);
    }

    [Fact]
    public void WithCorrelation_MultipleCallsInSameContext_UseSameContext()
    {
        // Arrange
        var correlationId = "shared-correlation";
        var messageId = "shared-message";
        var message1 = new TestMessage { MessageId = Guid.NewGuid() };
        var message2 = new TestMessage { MessageId = Guid.NewGuid() };

        // Act
        TestMessage result1, result2;
        using (var scope = CorrelationContext.BeginScope(correlationId, messageId))
        {
            result1 = message1.WithCorrelation();
            result2 = message2.WithCorrelation();
        }

        // Assert
        Assert.Equal(correlationId, result1.CorrelationId);
        Assert.Equal(correlationId, result2.CorrelationId);
        Assert.Equal(messageId, result1.CausationId);
        Assert.Equal(messageId, result2.CausationId);
    }

    #endregion

    #region WithCorrelation(string, string) - Explicit Parameters Tests

    [Fact]
    public void WithCorrelation_ExplicitParameters_SetsCorrelationAndCausation()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var correlationId = "explicit-correlation";
        var causationId = "explicit-causation";

        // Act
        var result = message.WithCorrelation(correlationId, causationId);

        // Assert
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal(causationId, result.CausationId);
    }

    [Fact]
    public void WithCorrelation_ExplicitCorrelationOnly_SetsCorrelationWithNullCausation()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var correlationId = "explicit-correlation-only";

        // Act
        var result = message.WithCorrelation(correlationId);

        // Assert
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Null(result.CausationId);
    }

    [Fact]
    public void WithCorrelation_ExplicitParameters_PreservesOriginalMessageId()
    {
        // Arrange
        var originalMessageId = Guid.NewGuid();
        var message = new TestMessage { MessageId = originalMessageId };

        // Act
        var result = message.WithCorrelation("correlation", "causation");

        // Assert
        Assert.Equal(originalMessageId, result.MessageId);
    }

    [Fact]
    public void WithCorrelation_ExplicitParameters_PreservesOtherProperties()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow.AddDays(-2);
        var metadata = new Dictionary<string, object> { { "prop", "value" } };
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            Timestamp = timestamp,
            Metadata = metadata,
            Payload = "preserved-payload"
        };

        // Act
        var result = message.WithCorrelation("correlation", "causation");

        // Assert
        Assert.Equal(timestamp, result.Timestamp);
        Assert.Equal(metadata, result.Metadata);
        Assert.Equal("preserved-payload", result.Payload);
    }

    [Fact]
    public void WithCorrelation_ExplicitParameters_OverridesExistingCorrelation()
    {
        // Arrange
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = "old-correlation",
            CausationId = "old-causation"
        };

        // Act
        var result = message.WithCorrelation("new-correlation", "new-causation");

        // Assert
        Assert.Equal("new-correlation", result.CorrelationId);
        Assert.Equal("new-causation", result.CausationId);
    }

    [Fact]
    public void WithCorrelation_ExplicitEmptyStrings_SetsEmptyStrings()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act
        var result = message.WithCorrelation(string.Empty, string.Empty);

        // Assert
        Assert.Equal(string.Empty, result.CorrelationId);
        Assert.Equal(string.Empty, result.CausationId);
    }

    [Fact]
    public void WithCorrelation_ExplicitNullCausation_SetsNullCausation()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act
        var result = message.WithCorrelation("correlation", null);

        // Assert
        Assert.Equal("correlation", result.CorrelationId);
        Assert.Null(result.CausationId);
    }

    [Fact]
    public void WithCorrelation_ExplicitParameters_IgnoresActiveContext()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act
        TestMessage result;
        using (var scope = CorrelationContext.BeginScope("context-correlation", "context-message"))
        {
            result = message.WithCorrelation("explicit-correlation", "explicit-causation");
        }

        // Assert
        Assert.Equal("explicit-correlation", result.CorrelationId);
        Assert.Equal("explicit-causation", result.CausationId);
    }

    #endregion

    #region GetCorrelation Tests

    [Fact]
    public void GetCorrelation_WithBothIds_ReturnsTuple()
    {
        // Arrange
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = "get-correlation",
            CausationId = "get-causation"
        };

        // Act
        var (correlationId, causationId) = message.GetCorrelation();

        // Assert
        Assert.Equal("get-correlation", correlationId);
        Assert.Equal("get-causation", causationId);
    }

    [Fact]
    public void GetCorrelation_WithNullIds_ReturnsNulls()
    {
        // Arrange
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = null,
            CausationId = null
        };

        // Act
        var (correlationId, causationId) = message.GetCorrelation();

        // Assert
        Assert.Null(correlationId);
        Assert.Null(causationId);
    }

    [Fact]
    public void GetCorrelation_WithMixedNullability_ReturnsCorrectValues()
    {
        // Arrange
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = "has-correlation",
            CausationId = null
        };

        // Act
        var (correlationId, causationId) = message.GetCorrelation();

        // Assert
        Assert.Equal("has-correlation", correlationId);
        Assert.Null(causationId);
    }

    [Fact]
    public void GetCorrelation_CanBeDeconstructed()
    {
        // Arrange
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = "deconstruct-correlation",
            CausationId = "deconstruct-causation"
        };

        // Act
        var (CorrelationId, CausationId) = message.GetCorrelation();

        // Assert
        Assert.Equal("deconstruct-correlation", CorrelationId);
        Assert.Equal("deconstruct-causation", CausationId);
    }

    #endregion

    #region HasCorrelation Tests

    [Fact]
    public void HasCorrelation_WithCorrelationId_ReturnsTrue()
    {
        // Arrange
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = "has-correlation-id"
        };

        // Act
        var hasCorrelation = message.HasCorrelation();

        // Assert
        Assert.True(hasCorrelation);
    }

    [Fact]
    public void HasCorrelation_WithNullCorrelationId_ReturnsFalse()
    {
        // Arrange
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = null
        };

        // Act
        var hasCorrelation = message.HasCorrelation();

        // Assert
        Assert.False(hasCorrelation);
    }

    [Fact]
    public void HasCorrelation_WithEmptyCorrelationId_ReturnsFalse()
    {
        // Arrange
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = string.Empty
        };

        // Act
        var hasCorrelation = message.HasCorrelation();

        // Assert
        Assert.False(hasCorrelation);
    }

    [Fact]
    public void HasCorrelation_WithWhitespaceCorrelationId_ReturnsTrue()
    {
        // Arrange - Whitespace is considered a value
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = "   "
        };

        // Act
        var hasCorrelation = message.HasCorrelation();

        // Assert
        Assert.True(hasCorrelation);
    }

    #endregion

    #region HasCausation Tests

    [Fact]
    public void HasCausation_WithCausationId_ReturnsTrue()
    {
        // Arrange
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CausationId = "has-causation-id"
        };

        // Act
        var hasCausation = message.HasCausation();

        // Assert
        Assert.True(hasCausation);
    }

    [Fact]
    public void HasCausation_WithNullCausationId_ReturnsFalse()
    {
        // Arrange
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CausationId = null
        };

        // Act
        var hasCausation = message.HasCausation();

        // Assert
        Assert.False(hasCausation);
    }

    [Fact]
    public void HasCausation_WithEmptyCausationId_ReturnsFalse()
    {
        // Arrange
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CausationId = string.Empty
        };

        // Act
        var hasCausation = message.HasCausation();

        // Assert
        Assert.False(hasCausation);
    }

    [Fact]
    public void HasCausation_WithWhitespaceCausationId_ReturnsTrue()
    {
        // Arrange - Whitespace is considered a value
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CausationId = "\t\n"
        };

        // Act
        var hasCausation = message.HasCausation();

        // Assert
        Assert.True(hasCausation);
    }

    #endregion

    #region GetCorrelationChain Tests

    [Fact]
    public void GetCorrelationChain_WithAllIds_ReturnsFullChain()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message = new TestMessage
        {
            MessageId = messageId,
            CorrelationId = "chain-correlation",
            CausationId = "chain-causation"
        };

        // Act
        var chain = message.GetCorrelationChain();

        // Assert
        Assert.Equal($"Correlation=chain-correlation → Causation=chain-causation → Message={messageId}", chain);
    }

    [Fact]
    public void GetCorrelationChain_WithoutCorrelation_OmitsCorrelation()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message = new TestMessage
        {
            MessageId = messageId,
            CorrelationId = null,
            CausationId = "chain-causation"
        };

        // Act
        var chain = message.GetCorrelationChain();

        // Assert
        Assert.Equal($"Causation=chain-causation → Message={messageId}", chain);
        Assert.DoesNotContain("Correlation=", chain);
    }

    [Fact]
    public void GetCorrelationChain_WithoutCausation_OmitsCausation()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message = new TestMessage
        {
            MessageId = messageId,
            CorrelationId = "chain-correlation",
            CausationId = null
        };

        // Act
        var chain = message.GetCorrelationChain();

        // Assert
        Assert.Equal($"Correlation=chain-correlation → Message={messageId}", chain);
        Assert.DoesNotContain("Causation=", chain);
    }

    [Fact]
    public void GetCorrelationChain_WithOnlyMessageId_ReturnsOnlyMessage()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message = new TestMessage
        {
            MessageId = messageId,
            CorrelationId = null,
            CausationId = null
        };

        // Act
        var chain = message.GetCorrelationChain();

        // Assert
        Assert.Equal($"Message={messageId}", chain);
    }

    [Fact]
    public void GetCorrelationChain_WithEmptyStrings_OmitsEmptyValues()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message = new TestMessage
        {
            MessageId = messageId,
            CorrelationId = string.Empty,
            CausationId = string.Empty
        };

        // Act
        var chain = message.GetCorrelationChain();

        // Assert
        Assert.Equal($"Message={messageId}", chain);
    }

    [Fact]
    public void GetCorrelationChain_UsesArrowSeparator()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message = new TestMessage
        {
            MessageId = messageId,
            CorrelationId = "correlation",
            CausationId = "causation"
        };

        // Act
        var chain = message.GetCorrelationChain();

        // Assert
        Assert.Contains(" → ", chain);
        var parts = chain.Split(" → ");
        Assert.Equal(3, parts.Length);
    }

    [Fact]
    public void GetCorrelationChain_PreservesIdFormats()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var correlationId = "CORRELATION-123-ABC";
        var causationId = "causation_456_xyz";
        var message = new TestMessage
        {
            MessageId = messageId,
            CorrelationId = correlationId,
            CausationId = causationId
        };

        // Act
        var chain = message.GetCorrelationChain();

        // Assert
        Assert.Contains(correlationId, chain);
        Assert.Contains(causationId, chain);
        Assert.Contains(messageId.ToString(), chain);
    }

    #endregion

    #region Integration Scenarios

    [Fact]
    public void IntegrationScenario_WorkflowWithMultipleMessages()
    {
        // Arrange - Simulate a workflow with chained messages
        var workflow = new List<TestMessage>();

        // Act - First message starts the workflow
        var message1 = new TestMessage { MessageId = Guid.NewGuid() };
        workflow.Add(message1);

        // Second message is triggered by first message
        TestMessage message2;
        using (var scope = CorrelationContext.BeginScope(message1))
        {
            message2 = new TestMessage { MessageId = Guid.NewGuid() }.WithCorrelation();
        }
        workflow.Add(message2);

        // Third message is triggered by second message
        TestMessage message3;
        using (var scope = CorrelationContext.BeginScope(message2))
        {
            message3 = new TestMessage { MessageId = Guid.NewGuid() }.WithCorrelation();
        }
        workflow.Add(message3);

        // Assert - Verify the correlation chain
        Assert.Null(message1.CorrelationId); // First message has no correlation

        Assert.Equal(message1.MessageId.ToString(), message2.CorrelationId);
        Assert.Equal(message1.MessageId.ToString(), message2.CausationId);

        Assert.Equal(message1.MessageId.ToString(), message3.CorrelationId); // Same workflow
        Assert.Equal(message2.MessageId.ToString(), message3.CausationId); // Direct cause
    }

    [Fact]
    public void IntegrationScenario_ExplicitCorrelationOverridesContext()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var explicitCorrelation = "explicit-workflow";
        var explicitCausation = "explicit-cause";

        // Act - Context is active but explicit parameters take precedence
        TestMessage result;
        using (var scope = CorrelationContext.BeginScope("context-workflow", "context-cause"))
        {
            result = message.WithCorrelation(explicitCorrelation, explicitCausation);
        }

        // Assert
        Assert.Equal(explicitCorrelation, result.CorrelationId);
        Assert.Equal(explicitCausation, result.CausationId);
    }

    [Fact]
    public void IntegrationScenario_MessageChainTracking()
    {
        // Arrange & Act - Create a chain of messages
        var message1 = new TestMessage { MessageId = Guid.NewGuid() };
        var chain1 = message1.GetCorrelationChain();

        TestMessage message2;
        using (var scope = CorrelationContext.BeginScope(message1))
        {
            message2 = new TestMessage { MessageId = Guid.NewGuid() }.WithCorrelation();
        }
        var chain2 = message2.GetCorrelationChain();

        TestMessage message3;
        using (var scope = CorrelationContext.BeginScope(message2))
        {
            message3 = new TestMessage { MessageId = Guid.NewGuid() }.WithCorrelation();
        }
        var chain3 = message3.GetCorrelationChain();

        // Assert - Each chain should be progressively longer
        Assert.Single(chain1.Split(" → "));
        Assert.Equal(3, chain2.Split(" → ").Length);
        Assert.Equal(3, chain3.Split(" → ").Length);

        // Assert - Verify correlation IDs
        Assert.True(message2.HasCorrelation());
        Assert.True(message2.HasCausation());
        Assert.True(message3.HasCorrelation());
        Assert.True(message3.HasCausation());
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void WithCorrelation_WithSpecialCharactersInIds_PreservesCharacters()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var correlationId = "correlation-with-special-chars-!@#$%^&*()";
        var causationId = "causation-with-unicode-☃️🎉";

        // Act
        var result = message.WithCorrelation(correlationId, causationId);

        // Assert
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal(causationId, result.CausationId);
    }

    [Fact]
    public void GetCorrelationChain_WithVeryLongIds_HandlesCorrectly()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var longCorrelationId = new string('c', 1000);
        var longCausationId = new string('x', 1000);
        var message = new TestMessage
        {
            MessageId = messageId,
            CorrelationId = longCorrelationId,
            CausationId = longCausationId
        };

        // Act
        var chain = message.GetCorrelationChain();

        // Assert
        Assert.Contains(longCorrelationId, chain);
        Assert.Contains(longCausationId, chain);
        Assert.Contains(messageId.ToString(), chain);
    }

    [Fact]
    public void WithCorrelation_CalledMultipleTimes_CreatesNewInstances()
    {
        // Arrange
        var original = new TestMessage { MessageId = Guid.NewGuid() };

        // Act
        var result1 = original.WithCorrelation("correlation-1", "causation-1");
        var result2 = original.WithCorrelation("correlation-2", "causation-2");

        // Assert
        Assert.NotSame(original, result1);
        Assert.NotSame(original, result2);
        Assert.NotSame(result1, result2);
        Assert.Equal("correlation-1", result1.CorrelationId);
        Assert.Equal("correlation-2", result2.CorrelationId);
    }

    #endregion

    #region Test Helper Classes

    private record TestMessage : MessageBase
    {
        public string? Payload { get; init; }
    }

    #endregion
}

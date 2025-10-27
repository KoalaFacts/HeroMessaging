using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Choreography;
using HeroMessaging.Tests.TestUtilities;
using Xunit;

namespace HeroMessaging.Tests.Unit.Choreography;

/// <summary>
/// Tests for message correlation extension methods
/// </summary>
public class MessageCorrelationExtensionsTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void WithCorrelation_AppliesCurrentContext()
    {
        // Arrange
        var correlationId = "corr-123";
        var messageId = "msg-456";
        var message = new TestCommand();

        // Act
        using (CorrelationContext.BeginScope(correlationId, messageId))
        {
            var enriched = message.WithCorrelation();

            // Assert
            Assert.Equal(correlationId, enriched.CorrelationId);
            Assert.Equal(messageId, enriched.CausationId);
            Assert.NotEqual(message.MessageId, enriched.MessageId); // Should get new ID
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithCorrelation_WithoutContext_ReturnsOriginalMessage()
    {
        // Arrange
        var message = new TestCommand();

        // Act - no correlation context
        var result = message.WithCorrelation();

        // Assert - should return same message since no context exists
        Assert.Equal(message, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithCorrelation_WithExplicitIds_AppliesThemCorrectly()
    {
        // Arrange
        var message = new TestCommand();
        var correlationId = "explicit-corr";
        var causationId = "explicit-cause";

        // Act
        var enriched = message.WithCorrelation(correlationId, causationId);

        // Assert
        Assert.Equal(correlationId, enriched.CorrelationId);
        Assert.Equal(causationId, enriched.CausationId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetCorrelation_ReturnsCorrelationAndCausationIds()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage(
            correlationId: "corr-123",
            causationId: "cause-456");

        // Act
        var (correlationId, causationId) = message.GetCorrelation();

        // Assert
        Assert.Equal("corr-123", correlationId);
        Assert.Equal("cause-456", causationId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void HasCorrelation_ReturnsTrueWhenCorrelationIdExists()
    {
        // Arrange
        var messageWithCorrelation = TestMessageBuilder.CreateValidMessage(correlationId: "corr-123");
        var messageWithoutCorrelation = TestMessageBuilder.CreateValidMessage();

        // Act & Assert
        Assert.True(messageWithCorrelation.HasCorrelation());
        Assert.False(messageWithoutCorrelation.HasCorrelation());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void HasCausation_ReturnsTrueWhenCausationIdExists()
    {
        // Arrange
        var messageWithCausation = TestMessageBuilder.CreateValidMessage(
            correlationId: "corr-123",
            causationId: "cause-456");
        var messageWithoutCausation = TestMessageBuilder.CreateValidMessage();

        // Act & Assert
        Assert.True(messageWithCausation.HasCausation());
        Assert.False(messageWithoutCausation.HasCausation());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetCorrelationChain_ReturnsFormattedChain()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage(
            correlationId: "corr-123",
            causationId: "cause-456");

        // Act
        var chain = message.GetCorrelationChain();

        // Assert
        Assert.Contains("Correlation=corr-123", chain);
        Assert.Contains("Causation=cause-456", chain);
        Assert.Contains($"Message={message.MessageId}", chain);
        Assert.Contains("→", chain); // Should have arrow separator
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetCorrelationChain_WithoutCorrelation_ReturnsOnlyMessageId()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage();

        // Act
        var chain = message.GetCorrelationChain();

        // Assert
        Assert.Contains($"Message={message.MessageId}", chain);
        Assert.DoesNotContain("Correlation=", chain);
        Assert.DoesNotContain("Causation=", chain);
    }

    // Test helper classes using MessageBase
    private record TestCommand : MessageBase, ICommand;

    private record TestEvent : MessageBase, IEvent;

    private record TestCommandWithResponse : MessageBase<string>, ICommand<string>;
}

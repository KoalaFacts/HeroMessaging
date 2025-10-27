using HeroMessaging.Choreography;
using HeroMessaging.Tests.TestUtilities;
using Xunit;

namespace HeroMessaging.Tests.Unit.Choreography;

/// <summary>
/// Tests for correlation context tracking
/// </summary>
public class CorrelationContextTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void BeginScope_SetsCorrelationContext()
    {
        // Arrange
        var correlationId = "corr-123";
        var messageId = "msg-456";

        // Act
        using (CorrelationContext.BeginScope(correlationId, messageId))
        {
            // Assert
            Assert.NotNull(CorrelationContext.Current);
            Assert.Equal(correlationId, CorrelationContext.CurrentCorrelationId);
            Assert.Equal(messageId, CorrelationContext.CurrentMessageId);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BeginScope_ClearsContextOnDispose()
    {
        // Arrange
        var correlationId = "corr-123";
        var messageId = "msg-456";

        // Act
        using (CorrelationContext.BeginScope(correlationId, messageId))
        {
            Assert.NotNull(CorrelationContext.Current);
        }

        // Assert - context should be cleared after scope disposal
        Assert.Null(CorrelationContext.Current);
        Assert.Null(CorrelationContext.CurrentCorrelationId);
        Assert.Null(CorrelationContext.CurrentMessageId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BeginScope_RestoresPreviousContext()
    {
        // Arrange
        var outerCorrelationId = "outer-corr";
        var outerMessageId = "outer-msg";
        var innerCorrelationId = "inner-corr";
        var innerMessageId = "inner-msg";

        // Act & Assert
        using (CorrelationContext.BeginScope(outerCorrelationId, outerMessageId))
        {
            Assert.Equal(outerCorrelationId, CorrelationContext.CurrentCorrelationId);

            using (CorrelationContext.BeginScope(innerCorrelationId, innerMessageId))
            {
                Assert.Equal(innerCorrelationId, CorrelationContext.CurrentCorrelationId);
            }

            // Outer context should be restored
            Assert.Equal(outerCorrelationId, CorrelationContext.CurrentCorrelationId);
            Assert.Equal(outerMessageId, CorrelationContext.CurrentMessageId);
        }

        // All contexts should be cleared
        Assert.Null(CorrelationContext.Current);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BeginScope_FlowsThroughAsyncOperations()
    {
        // Arrange
        var correlationId = "corr-123";
        var messageId = "msg-456";

        // Act & Assert
        using (CorrelationContext.BeginScope(correlationId, messageId))
        {
            await Task.Delay(10); // Simulate async work

            // Context should still be available after await
            Assert.Equal(correlationId, CorrelationContext.CurrentCorrelationId);
            Assert.Equal(messageId, CorrelationContext.CurrentMessageId);

            await HelperMethodAsync();

            Assert.Equal(correlationId, CorrelationContext.CurrentCorrelationId);
        }

        async Task HelperMethodAsync()
        {
            await Task.Delay(10);
            // Context should flow through nested async calls
            Assert.Equal(correlationId, CorrelationContext.CurrentCorrelationId);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BeginScope_WithMessage_ExtractsCorrelationAndMessageId()
    {
        // Arrange
        var correlationId = "corr-123";
        var causationId = "cause-456";
        var message = TestMessageBuilder.CreateValidMessage(
            correlationId: correlationId,
            causationId: causationId);

        // Act
        using (CorrelationContext.BeginScope(message))
        {
            // Assert - should use message's CorrelationId and MessageId
            Assert.Equal(correlationId, CorrelationContext.CurrentCorrelationId);
            Assert.Equal(message.MessageId.ToString(), CorrelationContext.CurrentMessageId);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BeginScope_WithMessageWithoutCorrelation_UsesMessageIdAsCorrelation()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage();

        // Act
        using (CorrelationContext.BeginScope(message))
        {
            // Assert - should use message's MessageId as CorrelationId when not set
            Assert.Equal(message.MessageId.ToString(), CorrelationContext.CurrentCorrelationId);
            Assert.Equal(message.MessageId.ToString(), CorrelationContext.CurrentMessageId);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CorrelationState_IsImmutable()
    {
        // Arrange & Act
        var state = new CorrelationState("corr-123", "msg-456");

        // Assert
        Assert.Equal("corr-123", state.CorrelationId);
        Assert.Equal("msg-456", state.MessageId);

        // CorrelationState is a record, so it should support with expressions
        var newState = state with { CorrelationId = "new-corr" };
        Assert.Equal("new-corr", newState.CorrelationId);
        Assert.Equal("msg-456", newState.MessageId);

        // Original should be unchanged
        Assert.Equal("corr-123", state.CorrelationId);
    }
}

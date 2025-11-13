using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Choreography;
using Xunit;

namespace HeroMessaging.Tests.Unit.Choreography;

[Trait("Category", "Unit")]
public sealed class CorrelationContextTests
{
    public CorrelationContextTests()
    {
        // Note: CorrelationContext uses AsyncLocal which is automatically isolated per test
        // Each test starts with a clean context without needing explicit cleanup
    }

    #region Current Property Tests

    [Fact]
    public void Current_WithNoContext_ReturnsNull()
    {
        // Arrange & Act
        var current = CorrelationContext.Current;

        // Assert
        Assert.Null(current);
    }

    [Fact]
    public void Current_AfterBeginScope_ReturnsCorrelationState()
    {
        // Arrange
        var correlationId = "correlation-123";
        var messageId = "message-456";

        // Act
        using var scope = CorrelationContext.BeginScope(correlationId, messageId);
        var current = CorrelationContext.Current;

        // Assert
        Assert.NotNull(current);
        Assert.Equal(correlationId, current.CorrelationId);
        Assert.Equal(messageId, current.MessageId);
    }

    [Fact]
    public void Current_AfterScopeDisposed_ReturnsNull()
    {
        // Arrange
        var correlationId = "correlation-123";
        var messageId = "message-456";

        // Act
        using (var scope = CorrelationContext.BeginScope(correlationId, messageId))
        {
            // Context should be set inside scope
            Assert.NotNull(CorrelationContext.Current);
        }

        // Assert - Context should be cleared after scope disposal
        Assert.Null(CorrelationContext.Current);
    }

    #endregion

    #region CurrentCorrelationId Property Tests

    [Fact]
    public void CurrentCorrelationId_WithNoContext_ReturnsNull()
    {
        // Arrange & Act
        var correlationId = CorrelationContext.CurrentCorrelationId;

        // Assert
        Assert.Null(correlationId);
    }

    [Fact]
    public void CurrentCorrelationId_WithActiveContext_ReturnsCorrelationId()
    {
        // Arrange
        var expectedCorrelationId = "correlation-789";
        var messageId = "message-012";

        // Act
        using var scope = CorrelationContext.BeginScope(expectedCorrelationId, messageId);
        var correlationId = CorrelationContext.CurrentCorrelationId;

        // Assert
        Assert.Equal(expectedCorrelationId, correlationId);
    }

    [Fact]
    public void CurrentCorrelationId_WithNullCorrelationId_ReturnsNull()
    {
        // Arrange
        var messageId = "message-012";

        // Act
        using var scope = CorrelationContext.BeginScope(null, messageId);
        var correlationId = CorrelationContext.CurrentCorrelationId;

        // Assert
        Assert.Null(correlationId);
    }

    #endregion

    #region CurrentMessageId Property Tests

    [Fact]
    public void CurrentMessageId_WithNoContext_ReturnsNull()
    {
        // Arrange & Act
        var messageId = CorrelationContext.CurrentMessageId;

        // Assert
        Assert.Null(messageId);
    }

    [Fact]
    public void CurrentMessageId_WithActiveContext_ReturnsMessageId()
    {
        // Arrange
        var correlationId = "correlation-345";
        var expectedMessageId = "message-678";

        // Act
        using var scope = CorrelationContext.BeginScope(correlationId, expectedMessageId);
        var messageId = CorrelationContext.CurrentMessageId;

        // Assert
        Assert.Equal(expectedMessageId, messageId);
    }

    #endregion

    #region BeginScope(string, string) Tests

    [Fact]
    public void BeginScope_WithValidParameters_SetsCorrelationContext()
    {
        // Arrange
        var correlationId = "correlation-999";
        var messageId = "message-888";

        // Act
        using var scope = CorrelationContext.BeginScope(correlationId, messageId);

        // Assert
        Assert.NotNull(CorrelationContext.Current);
        Assert.Equal(correlationId, CorrelationContext.CurrentCorrelationId);
        Assert.Equal(messageId, CorrelationContext.CurrentMessageId);
    }

    [Fact]
    public void BeginScope_WithNullCorrelationId_SetsMessageIdOnly()
    {
        // Arrange
        var messageId = "message-777";

        // Act
        using var scope = CorrelationContext.BeginScope(null, messageId);

        // Assert
        Assert.NotNull(CorrelationContext.Current);
        Assert.Null(CorrelationContext.CurrentCorrelationId);
        Assert.Equal(messageId, CorrelationContext.CurrentMessageId);
    }

    [Fact]
    public void BeginScope_ReturnsDisposableScope()
    {
        // Arrange
        var correlationId = "correlation-111";
        var messageId = "message-222";

        // Act
        var scope = CorrelationContext.BeginScope(correlationId, messageId);

        // Assert
        Assert.NotNull(scope);
        Assert.IsAssignableFrom<IDisposable>(scope);

        scope.Dispose();
    }

    [Fact]
    public void BeginScope_NestedScopes_InnerScopeOverridesOuter()
    {
        // Arrange
        var outerCorrelationId = "outer-correlation";
        var outerMessageId = "outer-message";
        var innerCorrelationId = "inner-correlation";
        var innerMessageId = "inner-message";

        // Act & Assert
        using (var outerScope = CorrelationContext.BeginScope(outerCorrelationId, outerMessageId))
        {
            Assert.Equal(outerCorrelationId, CorrelationContext.CurrentCorrelationId);
            Assert.Equal(outerMessageId, CorrelationContext.CurrentMessageId);

            using (var innerScope = CorrelationContext.BeginScope(innerCorrelationId, innerMessageId))
            {
                Assert.Equal(innerCorrelationId, CorrelationContext.CurrentCorrelationId);
                Assert.Equal(innerMessageId, CorrelationContext.CurrentMessageId);
            }

            // After inner scope disposed, outer scope should be restored
            Assert.Equal(outerCorrelationId, CorrelationContext.CurrentCorrelationId);
            Assert.Equal(outerMessageId, CorrelationContext.CurrentMessageId);
        }

        // After all scopes disposed, context should be cleared
        Assert.Null(CorrelationContext.Current);
    }

    [Fact]
    public void BeginScope_DisposeTwice_DoesNotThrow()
    {
        // Arrange
        var correlationId = "correlation-333";
        var messageId = "message-444";
        var scope = CorrelationContext.BeginScope(correlationId, messageId);

        // Act & Assert
        scope.Dispose();
        scope.Dispose(); // Should not throw

        Assert.Null(CorrelationContext.Current);
    }

    #endregion

    #region BeginScope(IMessage) Tests

    [Fact]
    public void BeginScope_WithMessage_SetsCorrelationContextFromMessage()
    {
        // Arrange
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = "correlation-from-message"
        };

        // Act
        using var scope = CorrelationContext.BeginScope(message);

        // Assert
        Assert.NotNull(CorrelationContext.Current);
        Assert.Equal("correlation-from-message", CorrelationContext.CurrentCorrelationId);
        Assert.Equal(message.MessageId.ToString(), CorrelationContext.CurrentMessageId);
    }

    [Fact]
    public void BeginScope_WithMessageWithoutCorrelationId_UsesMessageIdAsCorrelationId()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message = new TestMessage
        {
            MessageId = messageId,
            CorrelationId = null
        };

        // Act
        using var scope = CorrelationContext.BeginScope(message);

        // Assert
        Assert.NotNull(CorrelationContext.Current);
        Assert.Equal(messageId.ToString(), CorrelationContext.CurrentCorrelationId);
        Assert.Equal(messageId.ToString(), CorrelationContext.CurrentMessageId);
    }

    [Fact]
    public void BeginScope_WithMessageWithEmptyCorrelationId_UsesMessageIdAsCorrelationId()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message = new TestMessage
        {
            MessageId = messageId,
            CorrelationId = string.Empty
        };

        // Act
        using var scope = CorrelationContext.BeginScope(message);

        // Assert
        Assert.NotNull(CorrelationContext.Current);
        Assert.Equal(messageId.ToString(), CorrelationContext.CurrentCorrelationId);
        Assert.Equal(messageId.ToString(), CorrelationContext.CurrentMessageId);
    }

    [Fact]
    public void BeginScope_WithMessage_AfterDispose_ClearsContext()
    {
        // Arrange
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = "correlation-555"
        };

        // Act
        using (var scope = CorrelationContext.BeginScope(message))
        {
            Assert.NotNull(CorrelationContext.Current);
        }

        // Assert
        Assert.Null(CorrelationContext.Current);
    }

    #endregion

    #region Async Flow Tests

    [Fact]
    public async Task BeginScope_FlowsAcrossAsyncBoundaries()
    {
        // Arrange
        var correlationId = "async-correlation";
        var messageId = "async-message";

        // Act & Assert
        using (var scope = CorrelationContext.BeginScope(correlationId, messageId))
        {
            Assert.Equal(correlationId, CorrelationContext.CurrentCorrelationId);

            await Task.Run(() =>
            {
                // Context should flow to async continuations
                Assert.Equal(correlationId, CorrelationContext.CurrentCorrelationId);
                Assert.Equal(messageId, CorrelationContext.CurrentMessageId);
            });

            Assert.Equal(correlationId, CorrelationContext.CurrentCorrelationId);
        }

        Assert.Null(CorrelationContext.Current);
    }

    [Fact]
    public async Task BeginScope_ParallelTasks_MaintainSeparateContexts()
    {
        // Arrange & Act
        var task1 = Task.Run(() =>
        {
            using var scope = CorrelationContext.BeginScope("correlation-1", "message-1");
            Thread.Sleep(50); // Simulate some work
            Assert.Equal("correlation-1", CorrelationContext.CurrentCorrelationId);
            Assert.Equal("message-1", CorrelationContext.CurrentMessageId);
        });

        var task2 = Task.Run(() =>
        {
            using var scope = CorrelationContext.BeginScope("correlation-2", "message-2");
            Thread.Sleep(50); // Simulate some work
            Assert.Equal("correlation-2", CorrelationContext.CurrentCorrelationId);
            Assert.Equal("message-2", CorrelationContext.CurrentMessageId);
        });

        // Assert
        await Task.WhenAll(task1, task2);
        Assert.Null(CorrelationContext.Current);
    }

    #endregion

    #region Scope Disposal Tests

    [Fact]
    public void ScopeDisposal_ClearsContext()
    {
        // Arrange
        var correlationId = "correlation-to-clear";
        var messageId = "message-to-clear";

        // Act
        using (var scope = CorrelationContext.BeginScope(correlationId, messageId))
        {
            Assert.NotNull(CorrelationContext.Current);
        }

        // Assert - Context should be cleared after scope disposal
        Assert.Null(CorrelationContext.Current);
        Assert.Null(CorrelationContext.CurrentCorrelationId);
        Assert.Null(CorrelationContext.CurrentMessageId);
    }

    [Fact]
    public void ScopeDisposal_WithNoActiveScope_DoesNotThrow()
    {
        // Arrange - No context set
        Assert.Null(CorrelationContext.Current);

        // Act & Assert - Accessing properties with no scope should not throw
        var correlationId = CorrelationContext.CurrentCorrelationId;
        var messageId = CorrelationContext.CurrentMessageId;

        Assert.Null(correlationId);
        Assert.Null(messageId);
    }

    #endregion

    #region CorrelationState Tests

    [Fact]
    public void CorrelationState_IsImmutable()
    {
        // Arrange
        var correlationId = "immutable-correlation";
        var messageId = "immutable-message";

        // Act
        var state = new CorrelationState(correlationId, messageId);

        // Assert
        Assert.Equal(correlationId, state.CorrelationId);
        Assert.Equal(messageId, state.MessageId);

        // Records are immutable by default - properties have init-only setters
        // This test verifies the constructor works correctly
    }

    [Fact]
    public void CorrelationState_SupportsRecordEquality()
    {
        // Arrange
        var correlationId = "equality-correlation";
        var messageId = "equality-message";

        // Act
        var state1 = new CorrelationState(correlationId, messageId);
        var state2 = new CorrelationState(correlationId, messageId);

        // Assert
        Assert.Equal(state1, state2);
        Assert.True(state1 == state2);
    }

    [Fact]
    public void CorrelationState_WithDifferentValues_AreNotEqual()
    {
        // Arrange
        var state1 = new CorrelationState("correlation-1", "message-1");
        var state2 = new CorrelationState("correlation-2", "message-2");

        // Assert
        Assert.NotEqual(state1, state2);
        Assert.False(state1 == state2);
    }

    [Fact]
    public void CorrelationState_WithNullCorrelationId_IsValid()
    {
        // Arrange & Act
        var state = new CorrelationState(null, "message-id");

        // Assert
        Assert.Null(state.CorrelationId);
        Assert.Equal("message-id", state.MessageId);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void BeginScope_WithEmptyStrings_SetsEmptyValues()
    {
        // Arrange & Act
        using var scope = CorrelationContext.BeginScope(string.Empty, string.Empty);

        // Assert
        Assert.NotNull(CorrelationContext.Current);
        Assert.Equal(string.Empty, CorrelationContext.CurrentCorrelationId);
        Assert.Equal(string.Empty, CorrelationContext.CurrentMessageId);
    }

    [Fact]
    public void BeginScope_WithWhitespaceStrings_PreservesWhitespace()
    {
        // Arrange
        var correlationId = "   ";
        var messageId = "\t\n";

        // Act
        using var scope = CorrelationContext.BeginScope(correlationId, messageId);

        // Assert
        Assert.Equal(correlationId, CorrelationContext.CurrentCorrelationId);
        Assert.Equal(messageId, CorrelationContext.CurrentMessageId);
    }

    [Fact]
    public void BeginScope_WithVeryLongStrings_HandlesCorrectly()
    {
        // Arrange
        var longCorrelationId = new string('a', 10000);
        var longMessageId = new string('b', 10000);

        // Act
        using var scope = CorrelationContext.BeginScope(longCorrelationId, longMessageId);

        // Assert
        Assert.Equal(longCorrelationId, CorrelationContext.CurrentCorrelationId);
        Assert.Equal(longMessageId, CorrelationContext.CurrentMessageId);
    }

    [Fact]
    public void BeginScope_MultipleSequentialScopes_MaintainIndependence()
    {
        // Arrange & Act & Assert
        using (var scope1 = CorrelationContext.BeginScope("correlation-1", "message-1"))
        {
            Assert.Equal("correlation-1", CorrelationContext.CurrentCorrelationId);
        }

        Assert.Null(CorrelationContext.Current);

        using (var scope2 = CorrelationContext.BeginScope("correlation-2", "message-2"))
        {
            Assert.Equal("correlation-2", CorrelationContext.CurrentCorrelationId);
        }

        Assert.Null(CorrelationContext.Current);
    }

    #endregion

    #region Test Helper Classes

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    #endregion
}

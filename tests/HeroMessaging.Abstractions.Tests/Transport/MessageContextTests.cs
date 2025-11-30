using System.Collections.Immutable;
using HeroMessaging.Abstractions.Transport;

namespace HeroMessaging.Abstractions.Tests.Transport;

[Trait("Category", "Unit")]
public class MessageContextTests
{
    [Fact]
    public void DefaultConstructor_InitializesWithDefaults()
    {
        // Arrange & Act
        var context = new MessageContext();

        // Assert
        Assert.Equal(string.Empty, context.TransportName);
        Assert.Equal(default, context.SourceAddress);
        Assert.NotEqual(default, context.ReceiveTimestamp);
        Assert.NotNull(context.Properties);
        Assert.Empty(context.Properties);
        Assert.Null(context.Acknowledge);
        Assert.Null(context.Reject);
        Assert.Null(context.Defer);
        Assert.Null(context.DeadLetter);
    }

    [Fact]
    public void Constructor_WithTransportAndAddress_SetsProperties()
    {
        // Arrange
        var transportName = "RabbitMQ";
        var address = new TransportAddress("test-queue");

        // Act
        var context = new MessageContext(transportName, address);

        // Assert
        Assert.Equal(transportName, context.TransportName);
        Assert.Equal(address, context.SourceAddress);
        Assert.NotEqual(default, context.ReceiveTimestamp);
        Assert.Empty(context.Properties);
    }

    [Fact]
    public void Constructor_UsesProvidedTimeProvider()
    {
        // Arrange
        var transportName = "RabbitMQ";
        var address = new TransportAddress("test-queue");
        var expectedTime = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(expectedTime);

        // Act
        var context = new MessageContext(transportName, address, timeProvider);

        // Assert
        Assert.Equal(expectedTime, context.ReceiveTimestamp);
    }

    [Fact]
    public void WithProperty_AddsProperty()
    {
        // Arrange
        var context = new MessageContext("test", new TransportAddress("queue"));
        var key = "test-key";
        var value = "test-value";

        // Act
        var updatedContext = context.WithProperty(key, value);

        // Assert
        Assert.Empty(context.Properties);
        Assert.Single(updatedContext.Properties);
        Assert.Equal(value, updatedContext.Properties[key]);
    }

    [Fact]
    public void WithProperty_MultipleProperties_AddsAll()
    {
        // Arrange
        var context = new MessageContext("test", new TransportAddress("queue"));

        // Act
        var updated = context
            .WithProperty("key1", "value1")
            .WithProperty("key2", 123)
            .WithProperty("key3", true);

        // Assert
        Assert.Equal(3, updated.Properties.Count);
        Assert.Equal("value1", updated.Properties["key1"]);
        Assert.Equal(123, updated.Properties["key2"]);
        Assert.Equal(true, updated.Properties["key3"]);
    }

    [Fact]
    public void WithProperty_ExistingKey_UpdatesValue()
    {
        // Arrange
        var context = new MessageContext("test", new TransportAddress("queue"))
            .WithProperty("key", "original");

        // Act
        var updated = context.WithProperty("key", "updated");

        // Assert
        Assert.Single(updated.Properties);
        Assert.Equal("updated", updated.Properties["key"]);
    }

    [Fact]
    public void GetProperty_WithExistingKey_ReturnsValue()
    {
        // Arrange
        var context = new MessageContext("test", new TransportAddress("queue"))
            .WithProperty("key", "value");

        // Act
        var result = context.GetProperty<string>("key");

        // Assert
        Assert.Equal("value", result);
    }

    [Fact]
    public void GetProperty_WithNonExistingKey_ReturnsDefault()
    {
        // Arrange
        var context = new MessageContext("test", new TransportAddress("queue"));

        // Act
        var result = context.GetProperty<string>("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetProperty_WithWrongType_ReturnsDefault()
    {
        // Arrange
        var context = new MessageContext("test", new TransportAddress("queue"))
            .WithProperty("key", "string-value");

        // Act
        var result = context.GetProperty<int>("key");

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task AcknowledgeAsync_WithCallback_InvokesCallback()
    {
        // Arrange
        var invoked = false;
        var context = new MessageContext("test", new TransportAddress("queue"))
        {
            Acknowledge = ct =>
            {
                invoked = true;
                return Task.CompletedTask;
            }
        };

        // Act
        await context.AcknowledgeAsync();

        // Assert
        Assert.True(invoked);
    }

    [Fact]
    public async Task AcknowledgeAsync_WithoutCallback_CompletesSuccessfully()
    {
        // Arrange
        var context = new MessageContext("test", new TransportAddress("queue"));

        // Act & Assert
        await context.AcknowledgeAsync();
        // No exception should be thrown
    }

    [Fact]
    public async Task RejectAsync_WithCallback_InvokesCallbackWithRequeue()
    {
        // Arrange
        var invokedRequeue = false;
        var context = new MessageContext("test", new TransportAddress("queue"))
        {
            Reject = (requeue, ct) =>
            {
                invokedRequeue = requeue;
                return Task.CompletedTask;
            }
        };

        // Act
        await context.RejectAsync(requeue: true);

        // Assert
        Assert.True(invokedRequeue);
    }

    [Fact]
    public async Task RejectAsync_WithoutCallback_CompletesSuccessfully()
    {
        // Arrange
        var context = new MessageContext("test", new TransportAddress("queue"));

        // Act & Assert
        await context.RejectAsync();
        // No exception should be thrown
    }

    [Fact]
    public async Task DeferAsync_WithCallback_InvokesCallbackWithDelay()
    {
        // Arrange
        TimeSpan? capturedDelay = null;
        var context = new MessageContext("test", new TransportAddress("queue"))
        {
            Defer = (delay, ct) =>
            {
                capturedDelay = delay;
                return Task.CompletedTask;
            }
        };

        // Act
        await context.DeferAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(30), capturedDelay);
    }

    [Fact]
    public async Task DeferAsync_WithoutCallback_CompletesSuccessfully()
    {
        // Arrange
        var context = new MessageContext("test", new TransportAddress("queue"));

        // Act & Assert
        await context.DeferAsync();
        // No exception should be thrown
    }

    [Fact]
    public async Task DeadLetterAsync_WithCallback_InvokesCallbackWithReason()
    {
        // Arrange
        string? capturedReason = null;
        var context = new MessageContext("test", new TransportAddress("queue"))
        {
            DeadLetter = (reason, ct) =>
            {
                capturedReason = reason;
                return Task.CompletedTask;
            }
        };

        // Act
        await context.DeadLetterAsync("Test reason");

        // Assert
        Assert.Equal("Test reason", capturedReason);
    }

    [Fact]
    public async Task DeadLetterAsync_WithoutCallback_CompletesSuccessfully()
    {
        // Arrange
        var context = new MessageContext("test", new TransportAddress("queue"));

        // Act & Assert
        await context.DeadLetterAsync();
        // No exception should be thrown
    }

    [Fact]
    public void WithExpression_CreatesNewInstance()
    {
        // Arrange
        var original = new MessageContext("original", new TransportAddress("queue1"));

        // Act
        var modified = original with { TransportName = "modified" };

        // Assert
        Assert.Equal("original", original.TransportName);
        Assert.Equal("modified", modified.TransportName);
    }

    [Fact]
    public void Equality_WithSameValues_ReturnsTrue()
    {
        // Arrange
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var address = new TransportAddress("queue");
        var context1 = new MessageContext("test", address, timeProvider);
        var context2 = new MessageContext("test", address, timeProvider);

        // Act & Assert
        Assert.Equal(context1, context2);
    }

    [Fact]
    public void Properties_IsImmutable()
    {
        // Arrange
        var context = new MessageContext("test", new TransportAddress("queue"))
            .WithProperty("key", "value");

        // Act
        var updated = context.WithProperty("key2", "value2");

        // Assert
        Assert.Single(context.Properties);
        Assert.Equal(2, updated.Properties.Count);
    }

    [Fact]
    public async Task CancellationToken_PropagatedToCallbacks()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var tokenPassed = false;
        var context = new MessageContext("test", new TransportAddress("queue"))
        {
            Acknowledge = ct =>
            {
                tokenPassed = ct == cts.Token;
                return Task.CompletedTask;
            }
        };

        // Act
        await context.AcknowledgeAsync(cts.Token);

        // Assert
        Assert.True(tokenPassed);
    }

    [Fact]
    public void GetProperty_WithComplexType_WorksCorrectly()
    {
        // Arrange
        var complexObject = new { Id = 123, Name = "Test" };
        var context = new MessageContext("test", new TransportAddress("queue"))
            .WithProperty("complex", complexObject);

        // Act
        var result = context.GetProperty<object>("complex");

        // Assert
        Assert.NotNull(result);
        Assert.Same(complexObject, result);
    }

    [Fact]
    public void ReceiveTimestamp_UsesSystemTimeByDefault()
    {
        // Arrange
        var before = TimeProvider.System.GetUtcNow();

        // Act
        var context = new MessageContext("test", new TransportAddress("queue"));

        // Assert
        var after = TimeProvider.System.GetUtcNow();
        Assert.True(context.ReceiveTimestamp >= before);
        Assert.True(context.ReceiveTimestamp <= after);
    }

    [Fact]
    public void MultipleProperties_MaintainIndependence()
    {
        // Arrange
        var context1 = new MessageContext("test", new TransportAddress("queue"))
            .WithProperty("key", "value1");
        var context2 = context1 with { };

        // Act
        var updated = context2.WithProperty("key", "value2");

        // Assert
        Assert.Equal("value1", context1.GetProperty<string>("key"));
        Assert.Equal("value2", updated.GetProperty<string>("key"));
    }
}

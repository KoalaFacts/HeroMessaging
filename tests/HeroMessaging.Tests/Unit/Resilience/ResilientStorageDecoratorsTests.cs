using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Resilience;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Resilience;

/// <summary>
/// Unit tests for resilient storage decorator classes
/// </summary>
public class ResilientStorageDecoratorsTests
{
    public class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; } = new();
    }

    #region ResilientMessageStorageDecorator Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ResilientMessageStorageDecorator_WithNullInner_ThrowsArgumentNullException()
    {
        // Arrange
        var mockPolicy = new Mock<IConnectionResiliencePolicy>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ResilientMessageStorageDecorator(null!, mockPolicy.Object));
        Assert.Equal("inner", exception.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ResilientMessageStorageDecorator_WithNullPolicy_ThrowsArgumentNullException()
    {
        // Arrange
        var mockStorage = new Mock<IMessageStorage>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ResilientMessageStorageDecorator(mockStorage.Object, null!));
        Assert.Equal("resiliencePolicy", exception.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task StoreAsync_DelegatestoInnerStorageWithResilience()
    {
        // Arrange
        var message = new TestMessage();
        var expectedId = "msg-123";
        var mockStorage = new Mock<IMessageStorage>();
        var mockPolicy = new Mock<IConnectionResiliencePolicy>();

        mockStorage.Setup(x => x.StoreAsync(message, (MessageStorageOptions?)null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedId);

        mockPolicy.Setup(x => x.ExecuteAsync(It.IsAny<Func<Task<string>>>(), "StoreMessage", It.IsAny<CancellationToken>()))
            .Returns<Func<Task<string>>, string, CancellationToken>(async (func, name, ct) => await func());

        var decorator = new ResilientMessageStorageDecorator(mockStorage.Object, mockPolicy.Object);

        // Act
        var result = await decorator.StoreAsync(message, (MessageStorageOptions?)null);

        // Assert
        Assert.Equal(expectedId, result);
        mockStorage.Verify(x => x.StoreAsync(message, (MessageStorageOptions?)null, It.IsAny<CancellationToken>()), Times.Once);
        mockPolicy.Verify(x => x.ExecuteAsync(It.IsAny<Func<Task<string>>>(), "StoreMessage", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RetrieveAsync_DelegatesToInnerStorageWithResilience()
    {
        // Arrange
        var messageId = "msg-123";
        var expectedMessage = new TestMessage();
        var mockStorage = new Mock<IMessageStorage>();
        var mockPolicy = new Mock<IConnectionResiliencePolicy>();

        mockStorage.Setup(x => x.RetrieveAsync<TestMessage>(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMessage);

        mockPolicy.Setup(x => x.ExecuteAsync(It.IsAny<Func<Task<TestMessage?>>>(), "RetrieveMessage", It.IsAny<CancellationToken>()))
            .Returns<Func<Task<TestMessage?>>, string, CancellationToken>(async (func, name, ct) => await func());

        var decorator = new ResilientMessageStorageDecorator(mockStorage.Object, mockPolicy.Object);

        // Act
        var result = await decorator.RetrieveAsync<TestMessage>(messageId);

        // Assert
        Assert.Same(expectedMessage, result);
        mockStorage.Verify(x => x.RetrieveAsync<TestMessage>(messageId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteAsync_DelegatesToInnerStorageWithResilience()
    {
        // Arrange
        var messageId = "msg-123";
        var mockStorage = new Mock<IMessageStorage>();
        var mockPolicy = new Mock<IConnectionResiliencePolicy>();

        mockStorage.Setup(x => x.DeleteAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        mockPolicy.Setup(x => x.ExecuteAsync(It.IsAny<Func<Task<bool>>>(), "DeleteMessage", It.IsAny<CancellationToken>()))
            .Returns<Func<Task<bool>>, string, CancellationToken>(async (func, name, ct) => await func());

        var decorator = new ResilientMessageStorageDecorator(mockStorage.Object, mockPolicy.Object);

        // Act
        var result = await decorator.DeleteAsync(messageId);

        // Assert
        Assert.True(result);
        mockStorage.Verify(x => x.DeleteAsync(messageId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BeginTransactionAsync_DelegatesToInnerStorageWithResilience()
    {
        // Arrange
        var mockTransaction = new Mock<IStorageTransaction>();
        var mockStorage = new Mock<IMessageStorage>();
        var mockPolicy = new Mock<IConnectionResiliencePolicy>();

        mockStorage.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockTransaction.Object);

        mockPolicy.Setup(x => x.ExecuteAsync(It.IsAny<Func<Task<IStorageTransaction>>>(), "BeginTransaction", It.IsAny<CancellationToken>()))
            .Returns<Func<Task<IStorageTransaction>>, string, CancellationToken>(async (func, name, ct) => await func());

        var decorator = new ResilientMessageStorageDecorator(mockStorage.Object, mockPolicy.Object);

        // Act
        var result = await decorator.BeginTransactionAsync();

        // Assert
        Assert.Same(mockTransaction.Object, result);
        mockStorage.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ResilientOutboxStorageDecorator Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ResilientOutboxStorageDecorator_WithNullInner_ThrowsArgumentNullException()
    {
        // Arrange
        var mockPolicy = new Mock<IConnectionResiliencePolicy>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ResilientOutboxStorageDecorator(null!, mockPolicy.Object));
        Assert.Equal("inner", exception.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ResilientOutboxStorageDecorator_WithNullPolicy_ThrowsArgumentNullException()
    {
        // Arrange
        var mockStorage = new Mock<IOutboxStorage>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ResilientOutboxStorageDecorator(mockStorage.Object, null!));
        Assert.Equal("resiliencePolicy", exception.ParamName);
    }

    #endregion

    #region ResilientInboxStorageDecorator Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ResilientInboxStorageDecorator_WithNullInner_ThrowsArgumentNullException()
    {
        // Arrange
        var mockPolicy = new Mock<IConnectionResiliencePolicy>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ResilientInboxStorageDecorator(null!, mockPolicy.Object));
        Assert.Equal("inner", exception.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ResilientInboxStorageDecorator_WithNullPolicy_ThrowsArgumentNullException()
    {
        // Arrange
        var mockStorage = new Mock<IInboxStorage>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ResilientInboxStorageDecorator(mockStorage.Object, null!));
        Assert.Equal("resiliencePolicy", exception.ParamName);
    }

    #endregion

    #region ResilientQueueStorageDecorator Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ResilientQueueStorageDecorator_WithNullInner_ThrowsArgumentNullException()
    {
        // Arrange
        var mockPolicy = new Mock<IConnectionResiliencePolicy>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ResilientQueueStorageDecorator(null!, mockPolicy.Object));
        Assert.Equal("inner", exception.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ResilientQueueStorageDecorator_WithNullPolicy_ThrowsArgumentNullException()
    {
        // Arrange
        var mockStorage = new Mock<IQueueStorage>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ResilientQueueStorageDecorator(mockStorage.Object, null!));
        Assert.Equal("resiliencePolicy", exception.ParamName);
    }

    #endregion
}

using System.Data;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Resilience;

/// <summary>
/// Unit tests for <see cref="ConnectionResilienceDecorator"/> implementation.
/// Tests cover connection resilience, retry logic, and transaction handling.
/// </summary>
#pragma warning disable CA1001 // Test class with disposable field - disposed in individual tests
public class ConnectionResilienceDecoratorTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IConnectionResiliencePolicy> _mockResiliencePolicy;
    private readonly Mock<ILogger<ConnectionResilienceDecorator>> _mockLogger;
    private readonly ConnectionResilienceDecorator _decorator;

    public ConnectionResilienceDecoratorTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockResiliencePolicy = new Mock<IConnectionResiliencePolicy>();
        _mockLogger = new Mock<ILogger<ConnectionResilienceDecorator>>();

        _decorator = new ConnectionResilienceDecorator(
            _mockUnitOfWork.Object,
            _mockResiliencePolicy.Object,
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var decorator = new ConnectionResilienceDecorator(
            _mockUnitOfWork.Object,
            _mockResiliencePolicy.Object,
            _mockLogger.Object);

        // Assert
        Assert.NotNull(decorator);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullUnitOfWork_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ConnectionResilienceDecorator(
                null!,
                _mockResiliencePolicy.Object,
                _mockLogger.Object));
        Assert.Equal("inner", exception.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullResiliencePolicy_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ConnectionResilienceDecorator(
                _mockUnitOfWork.Object,
                null!,
                _mockLogger.Object));
        Assert.Equal("resiliencePolicy", exception.ParamName);
    }

    #endregion

    #region Property Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void IsolationLevel_ReturnsInnerIsolationLevel()
    {
        // Arrange
        var expectedIsolationLevel = IsolationLevel.Serializable;
        _mockUnitOfWork.Setup(x => x.IsolationLevel).Returns(expectedIsolationLevel);

        // Act
        var actualIsolationLevel = _decorator.IsolationLevel;

        // Assert
        Assert.Equal(expectedIsolationLevel, actualIsolationLevel);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsTransactionActive_ReturnsInnerTransactionStatus()
    {
        // Arrange
        _mockUnitOfWork.Setup(x => x.IsTransactionActive).Returns(true);

        // Act
        var isActive = _decorator.IsTransactionActive;

        // Assert
        Assert.True(isActive);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OutboxStorage_ReturnsInnerOutboxStorage()
    {
        // Arrange
        var mockOutbox = new Mock<IOutboxStorage>();
        _mockUnitOfWork.Setup(x => x.OutboxStorage).Returns(mockOutbox.Object);

        // Act
        var outbox = _decorator.OutboxStorage;

        // Assert
        Assert.Same(mockOutbox.Object, outbox);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void InboxStorage_ReturnsInnerInboxStorage()
    {
        // Arrange
        var mockInbox = new Mock<IInboxStorage>();
        _mockUnitOfWork.Setup(x => x.InboxStorage).Returns(mockInbox.Object);

        // Act
        var inbox = _decorator.InboxStorage;

        // Assert
        Assert.Same(mockInbox.Object, inbox);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void QueueStorage_ReturnsInnerQueueStorage()
    {
        // Arrange
        var mockQueue = new Mock<IQueueStorage>();
        _mockUnitOfWork.Setup(x => x.QueueStorage).Returns(mockQueue.Object);

        // Act
        var queue = _decorator.QueueStorage;

        // Assert
        Assert.Same(mockQueue.Object, queue);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MessageStorage_ReturnsInnerMessageStorage()
    {
        // Arrange
        var mockMessage = new Mock<IMessageStorage>();
        _mockUnitOfWork.Setup(x => x.MessageStorage).Returns(mockMessage.Object);

        // Act
        var message = _decorator.MessageStorage;

        // Assert
        Assert.Same(mockMessage.Object, message);
    }

    #endregion

    #region BeginTransactionAsync Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BeginTransactionAsync_WithDefaultIsolationLevel_ExecutesWithResilience()
    {
        // Arrange
        _mockResiliencePolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "BeginTransaction",
                It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, string, CancellationToken>((func, _, _) => func());

        // Act
        await _decorator.BeginTransactionAsync();

        // Assert
        _mockResiliencePolicy.Verify(
            x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "BeginTransaction",
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockUnitOfWork.Verify(
            x => x.BeginTransactionAsync(IsolationLevel.ReadCommitted, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BeginTransactionAsync_WithCustomIsolationLevel_PassesCorrectLevel()
    {
        // Arrange
        var isolationLevel = IsolationLevel.Serializable;
        _mockResiliencePolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "BeginTransaction",
                It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, string, CancellationToken>((func, _, _) => func());

        // Act
        await _decorator.BeginTransactionAsync(isolationLevel);

        // Assert
        _mockUnitOfWork.Verify(
            x => x.BeginTransactionAsync(isolationLevel, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BeginTransactionAsync_WithCancellationToken_PassesToken()
    {
        // Arrange
        var cancellationToken = new CancellationTokenSource().Token;
        _mockResiliencePolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "BeginTransaction",
                cancellationToken))
            .Returns<Func<Task>, string, CancellationToken>((func, _, _) => func());

        // Act
        await _decorator.BeginTransactionAsync(cancellationToken: cancellationToken);

        // Assert
        _mockResiliencePolicy.Verify(
            x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "BeginTransaction",
                cancellationToken),
            Times.Once);
    }

    #endregion

    #region CommitAsync Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CommitAsync_ExecutesWithResilience()
    {
        // Arrange
        _mockResiliencePolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "Commit",
                It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, string, CancellationToken>((func, _, _) => func());

        // Act
        await _decorator.CommitAsync();

        // Assert
        _mockResiliencePolicy.Verify(
            x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "Commit",
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockUnitOfWork.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CommitAsync_WithCancellationToken_PassesToken()
    {
        // Arrange
        var cancellationToken = new CancellationTokenSource().Token;
        _mockResiliencePolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "Commit",
                cancellationToken))
            .Returns<Func<Task>, string, CancellationToken>((func, _, _) => func());

        // Act
        await _decorator.CommitAsync(cancellationToken);

        // Assert
        _mockResiliencePolicy.Verify(
            x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "Commit",
                cancellationToken),
            Times.Once);
    }

    #endregion

    #region RollbackAsync Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RollbackAsync_ExecutesWithResilience()
    {
        // Arrange
        _mockResiliencePolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "Rollback",
                It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, string, CancellationToken>((func, _, _) => func());

        // Act
        await _decorator.RollbackAsync();

        // Assert
        _mockResiliencePolicy.Verify(
            x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "Rollback",
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockUnitOfWork.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RollbackAsync_WithCancellationToken_PassesToken()
    {
        // Arrange
        var cancellationToken = new CancellationTokenSource().Token;
        _mockResiliencePolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "Rollback",
                cancellationToken))
            .Returns<Func<Task>, string, CancellationToken>((func, _, _) => func());

        // Act
        await _decorator.RollbackAsync(cancellationToken);

        // Assert
        _mockResiliencePolicy.Verify(
            x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "Rollback",
                cancellationToken),
            Times.Once);
    }

    #endregion

    #region SavepointAsync Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SavepointAsync_ExecutesWithResilience()
    {
        // Arrange
        var savepointName = "sp1";
        _mockResiliencePolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                $"Savepoint-{savepointName}",
                It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, string, CancellationToken>((func, _, _) => func());

        // Act
        await _decorator.SavepointAsync(savepointName);

        // Assert
        _mockResiliencePolicy.Verify(
            x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                $"Savepoint-{savepointName}",
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockUnitOfWork.Verify(
            x => x.SavepointAsync(savepointName, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SavepointAsync_WithCancellationToken_PassesToken()
    {
        // Arrange
        var savepointName = "sp1";
        var cancellationToken = new CancellationTokenSource().Token;
        _mockResiliencePolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                $"Savepoint-{savepointName}",
                cancellationToken))
            .Returns<Func<Task>, string, CancellationToken>((func, _, _) => func());

        // Act
        await _decorator.SavepointAsync(savepointName, cancellationToken);

        // Assert
        _mockResiliencePolicy.Verify(
            x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                $"Savepoint-{savepointName}",
                cancellationToken),
            Times.Once);
    }

    #endregion

    #region RollbackToSavepointAsync Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RollbackToSavepointAsync_ExecutesWithResilience()
    {
        // Arrange
        var savepointName = "sp1";
        _mockResiliencePolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                $"RollbackToSavepoint-{savepointName}",
                It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, string, CancellationToken>((func, _, _) => func());

        // Act
        await _decorator.RollbackToSavepointAsync(savepointName);

        // Assert
        _mockResiliencePolicy.Verify(
            x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                $"RollbackToSavepoint-{savepointName}",
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockUnitOfWork.Verify(
            x => x.RollbackToSavepointAsync(savepointName, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RollbackToSavepointAsync_WithCancellationToken_PassesToken()
    {
        // Arrange
        var savepointName = "sp1";
        var cancellationToken = new CancellationTokenSource().Token;
        _mockResiliencePolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                $"RollbackToSavepoint-{savepointName}",
                cancellationToken))
            .Returns<Func<Task>, string, CancellationToken>((func, _, _) => func());

        // Act
        await _decorator.RollbackToSavepointAsync(savepointName, cancellationToken);

        // Assert
        _mockResiliencePolicy.Verify(
            x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                $"RollbackToSavepoint-{savepointName}",
                cancellationToken),
            Times.Once);
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DisposeAsync_DisposesInnerUnitOfWork()
    {
        // Arrange
        _mockUnitOfWork.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);

        // Act
        await _decorator.DisposeAsync();

        // Assert
        _mockUnitOfWork.Verify(x => x.DisposeAsync(), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DisposeAsync_WhenInnerThrowsException_LogsWarningAndDoesNotThrow()
    {
        // Arrange
        var exception = new InvalidOperationException("Disposal failed");
        _mockUnitOfWork.Setup(x => x.DisposeAsync()).ThrowsAsync(exception);

        // Act & Assert - Should not throw
        await _decorator.DisposeAsync();

        // Verify logging occurred
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BeginTransactionAsync_WhenResiliencePolicyThrows_PropagatesException()
    {
        // Arrange
        var exception = new ConnectionResilienceException("Circuit breaker is open");
        _mockResiliencePolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "BeginTransaction",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act & Assert
        await Assert.ThrowsAsync<ConnectionResilienceException>(
            () => _decorator.BeginTransactionAsync());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CommitAsync_WhenResiliencePolicyThrows_PropagatesException()
    {
        // Arrange
        var exception = new ConnectionResilienceException("Max retries exceeded");
        _mockResiliencePolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "Commit",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act & Assert
        await Assert.ThrowsAsync<ConnectionResilienceException>(
            () => _decorator.CommitAsync());
    }

    #endregion

    #region Integration Scenario Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CompleteTransactionFlow_ExecutesAllOperationsWithResilience()
    {
        // Arrange
        _mockResiliencePolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, string, CancellationToken>((func, _, _) => func());

        // Act
        await _decorator.BeginTransactionAsync();
        await _decorator.SavepointAsync("sp1");
        await _decorator.CommitAsync();

        // Assert
        _mockResiliencePolicy.Verify(
            x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "BeginTransaction",
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockResiliencePolicy.Verify(
            x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "Savepoint-sp1",
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockResiliencePolicy.Verify(
            x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "Commit",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TransactionRollbackFlow_ExecutesAllOperationsWithResilience()
    {
        // Arrange
        _mockResiliencePolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, string, CancellationToken>((func, _, _) => func());

        // Act
        await _decorator.BeginTransactionAsync();
        await _decorator.SavepointAsync("sp1");
        await _decorator.RollbackToSavepointAsync("sp1");
        await _decorator.RollbackAsync();

        // Assert
        _mockResiliencePolicy.Verify(
            x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(4));
    }

    #endregion
}

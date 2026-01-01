using System.Data;
using System.Data.Common;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Resilience;

/// <summary>
/// Unit tests for <see cref="ResilientUnitOfWork"/> and <see cref="ResilientUnitOfWorkFactory"/> implementations.
/// Tests cover resilience patterns, retry logic, and factory operations.
/// </summary>
#pragma warning disable CA1001 // Test class with disposable field - disposed in individual tests
public class ResilientUnitOfWorkTests
{
    #region ResilientUnitOfWork Tests

    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IConnectionResiliencePolicy> _mockResiliencePolicy;
    private readonly Mock<ILogger> _mockLogger;
    private readonly ResilientUnitOfWork _resilientUnitOfWork;

    public ResilientUnitOfWorkTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockResiliencePolicy = new Mock<IConnectionResiliencePolicy>();
        _mockLogger = new Mock<ILogger>();

        _resilientUnitOfWork = new ResilientUnitOfWork(
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
        var unitOfWork = new ResilientUnitOfWork(
            _mockUnitOfWork.Object,
            _mockResiliencePolicy.Object,
            _mockLogger.Object);

        // Assert
        Assert.NotNull(unitOfWork);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullUnitOfWork_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ResilientUnitOfWork(
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
            new ResilientUnitOfWork(
                _mockUnitOfWork.Object,
                null!,
                _mockLogger.Object));
        Assert.Equal("resiliencePolicy", exception.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ResilientUnitOfWork(
                _mockUnitOfWork.Object,
                _mockResiliencePolicy.Object,
                null!));
        Assert.Equal("logger", exception.ParamName);
    }

    #endregion

    #region Property Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void IsolationLevel_ReturnsInnerIsolationLevel()
    {
        // Arrange
        var expectedIsolationLevel = IsolationLevel.Snapshot;
        _mockUnitOfWork.Setup(x => x.IsolationLevel).Returns(expectedIsolationLevel);

        // Act
        var actualIsolationLevel = _resilientUnitOfWork.IsolationLevel;

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
        var isActive = _resilientUnitOfWork.IsTransactionActive;

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
        var outbox = _resilientUnitOfWork.OutboxStorage;

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
        var inbox = _resilientUnitOfWork.InboxStorage;

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
        var queue = _resilientUnitOfWork.QueueStorage;

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
        var message = _resilientUnitOfWork.MessageStorage;

        // Assert
        Assert.Same(mockMessage.Object, message);
    }

    #endregion

    #region BeginTransactionAsync Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BeginTransactionAsync_ExecutesWithResilience()
    {
        // Arrange
        _mockResiliencePolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "BeginTransaction",
                It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, string, CancellationToken>((func, _, _) => func());

        // Act
        await _resilientUnitOfWork.BeginTransactionAsync(TestContext.Current.CancellationToken);

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
    public async Task BeginTransactionAsync_WithCustomIsolationLevel_LogsAndPassesCorrectLevel()
    {
        // Arrange
        var isolationLevel = IsolationLevel.RepeatableRead;
        _mockResiliencePolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "BeginTransaction",
                It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, string, CancellationToken>((func, _, _) => func());

        // Act
        await _resilientUnitOfWork.BeginTransactionAsync(isolationLevel, TestContext.Current.CancellationToken);

        // Assert
        _mockUnitOfWork.Verify(
            x => x.BeginTransactionAsync(isolationLevel, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Beginning transaction")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
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
        await _resilientUnitOfWork.CommitAsync(TestContext.Current.CancellationToken);

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
    public async Task CommitAsync_LogsDebugMessage()
    {
        // Arrange
        _mockResiliencePolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "Commit",
                It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, string, CancellationToken>((func, _, _) => func());

        // Act
        await _resilientUnitOfWork.CommitAsync(TestContext.Current.CancellationToken);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Committing transaction")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
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
        await _resilientUnitOfWork.RollbackAsync(TestContext.Current.CancellationToken);

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
    public async Task RollbackAsync_LogsDebugMessage()
    {
        // Arrange
        _mockResiliencePolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                "Rollback",
                It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, string, CancellationToken>((func, _, _) => func());

        // Act
        await _resilientUnitOfWork.RollbackAsync(TestContext.Current.CancellationToken);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Rolling back transaction")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
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
        await _resilientUnitOfWork.SavepointAsync(savepointName, TestContext.Current.CancellationToken);

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
    public async Task SavepointAsync_LogsDebugMessageWithSavepointName()
    {
        // Arrange
        var savepointName = "checkpoint1";
        _mockResiliencePolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, string, CancellationToken>((func, _, _) => func());

        // Act
        await _resilientUnitOfWork.SavepointAsync(savepointName, TestContext.Current.CancellationToken);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Creating savepoint") && v.ToString()!.Contains(savepointName)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
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
        await _resilientUnitOfWork.RollbackToSavepointAsync(savepointName, TestContext.Current.CancellationToken);

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
    public async Task RollbackToSavepointAsync_LogsDebugMessageWithSavepointName()
    {
        // Arrange
        var savepointName = "checkpoint1";
        _mockResiliencePolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, string, CancellationToken>((func, _, _) => func());

        // Act
        await _resilientUnitOfWork.RollbackToSavepointAsync(savepointName, TestContext.Current.CancellationToken);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Rolling back to savepoint") && v.ToString()!.Contains(savepointName)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
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
        await _resilientUnitOfWork.DisposeAsync(TestContext.Current.CancellationToken);

        // Assert
        _mockUnitOfWork.Verify(x => x.DisposeAsync(), Times.Once);
    }

    #endregion

    #endregion

    #region ResilientUnitOfWorkFactory Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ResilientUnitOfWorkFactory_Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange
        var mockFactory = new Mock<IUnitOfWorkFactory>();
        var mockPolicy = new Mock<IConnectionResiliencePolicy>();
        var mockLogger = new Mock<ILogger<ResilientUnitOfWorkFactory>>();

        // Act
        var factory = new ResilientUnitOfWorkFactory(
            mockFactory.Object,
            mockPolicy.Object,
            mockLogger.Object);

        // Assert
        Assert.NotNull(factory);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ResilientUnitOfWorkFactory_Constructor_WithNullInnerFactory_ThrowsArgumentNullException()
    {
        // Arrange
        var mockPolicy = new Mock<IConnectionResiliencePolicy>();
        var mockLogger = new Mock<ILogger<ResilientUnitOfWorkFactory>>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ResilientUnitOfWorkFactory(
                null!,
                mockPolicy.Object,
                mockLogger.Object));
        Assert.Equal("inner", exception.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ResilientUnitOfWorkFactory_Constructor_WithNullResiliencePolicy_ThrowsArgumentNullException()
    {
        // Arrange
        var mockFactory = new Mock<IUnitOfWorkFactory>();
        var mockLogger = new Mock<ILogger<ResilientUnitOfWorkFactory>>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ResilientUnitOfWorkFactory(
                mockFactory.Object,
                null!,
                mockLogger.Object));
        Assert.Equal("resiliencePolicy", exception.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResilientUnitOfWorkFactory_CreateAsync_WithoutIsolationLevel_CreatesResilientUnitOfWork()
    {
        // Arrange
        var mockFactory = new Mock<IUnitOfWorkFactory>();
        var mockPolicy = new Mock<IConnectionResiliencePolicy>();
        var mockLogger = new Mock<ILogger<ResilientUnitOfWorkFactory>>();
        var mockUnitOfWork = new Mock<IUnitOfWork>();

        mockFactory.Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockUnitOfWork.Object);
        mockPolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task<ResilientUnitOfWork>>>(),
                "CreateUnitOfWork",
                It.IsAny<CancellationToken>()))
            .Returns<Func<Task<ResilientUnitOfWork>>, string, CancellationToken>(async (func, _, _) => await func());

        var factory = new ResilientUnitOfWorkFactory(
            mockFactory.Object,
            mockPolicy.Object,
            mockLogger.Object);

        // Act
        var result = await factory.CreateAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<ResilientUnitOfWork>(result);
        mockPolicy.Verify(
            x => x.ExecuteAsync(
                It.IsAny<Func<Task<ResilientUnitOfWork>>>(),
                "CreateUnitOfWork",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResilientUnitOfWorkFactory_CreateAsync_WithIsolationLevel_CreatesResilientUnitOfWork()
    {
        // Arrange
        var mockFactory = new Mock<IUnitOfWorkFactory>();
        var mockPolicy = new Mock<IConnectionResiliencePolicy>();
        var mockLogger = new Mock<ILogger<ResilientUnitOfWorkFactory>>();
        var mockUnitOfWork = new Mock<IUnitOfWork>();

        mockFactory.Setup(x => x.CreateAsync(It.IsAny<IsolationLevel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockUnitOfWork.Object);
        mockPolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task<ResilientUnitOfWork>>>(),
                "CreateUnitOfWork",
                It.IsAny<CancellationToken>()))
            .Returns<Func<Task<ResilientUnitOfWork>>, string, CancellationToken>(async (func, _, _) => await func());

        var factory = new ResilientUnitOfWorkFactory(
            mockFactory.Object,
            mockPolicy.Object,
            mockLogger.Object);

        // Act
        var result = await factory.CreateAsync(IsolationLevel.Serializable, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<ResilientUnitOfWork>(result);
        mockFactory.Verify(
            x => x.CreateAsync(IsolationLevel.Serializable, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResilientUnitOfWorkFactory_CreateAsync_LogsDebugMessage()
    {
        // Arrange
        var mockFactory = new Mock<IUnitOfWorkFactory>();
        var mockPolicy = new Mock<IConnectionResiliencePolicy>();
        var mockLogger = new Mock<ILogger<ResilientUnitOfWorkFactory>>();
        var mockUnitOfWork = new Mock<IUnitOfWork>();

        mockFactory.Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockUnitOfWork.Object);
        // Setup the policy to actually execute the callback function
        mockPolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task<ResilientUnitOfWork>>>(),
                "CreateUnitOfWork",
                It.IsAny<CancellationToken>()))
            .Returns<Func<Task<ResilientUnitOfWork>>, string, CancellationToken>(async (func, _, _) => await func());

        var factory = new ResilientUnitOfWorkFactory(
            mockFactory.Object,
            mockPolicy.Object,
            mockLogger.Object);

        // Act
        await factory.CreateAsync(TestContext.Current.CancellationToken);

        // Assert
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Creating unit of work")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResilientUnitOfWorkFactory_CreateAsync_WithIsolationLevel_LogsIsolationLevel()
    {
        // Arrange
        var mockFactory = new Mock<IUnitOfWorkFactory>();
        var mockPolicy = new Mock<IConnectionResiliencePolicy>();
        var mockLogger = new Mock<ILogger<ResilientUnitOfWorkFactory>>();
        var mockUnitOfWork = new Mock<IUnitOfWork>();

        mockFactory.Setup(x => x.CreateAsync(It.IsAny<IsolationLevel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockUnitOfWork.Object);
        // Setup the policy to actually execute the callback function
        mockPolicy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Func<Task<ResilientUnitOfWork>>>(),
                "CreateUnitOfWork",
                It.IsAny<CancellationToken>()))
            .Returns<Func<Task<ResilientUnitOfWork>>, string, CancellationToken>(async (func, _, _) => await func());

        var factory = new ResilientUnitOfWorkFactory(
            mockFactory.Object,
            mockPolicy.Object,
            mockLogger.Object);

        // Act
        await factory.CreateAsync(IsolationLevel.Snapshot, TestContext.Current.CancellationToken);

        // Assert
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Creating unit of work") && v.ToString()!.Contains("Snapshot")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion
}

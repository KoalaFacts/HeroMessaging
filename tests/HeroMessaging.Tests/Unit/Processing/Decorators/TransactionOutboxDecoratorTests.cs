using System.Data;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Processing.Decorators;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing.Decorators;

[Trait("Category", "Unit")]
public class TransactionOutboxProcessorDecoratorTests
{
    private readonly Mock<IOutboxProcessor> _mockInner;
    private readonly Mock<ITransactionExecutor> _mockTransactionExecutor;
    private readonly TransactionOutboxProcessorDecorator _sut;

    public TransactionOutboxProcessorDecoratorTests()
    {
        _mockInner = new Mock<IOutboxProcessor>();
        _mockTransactionExecutor = new Mock<ITransactionExecutor>();

        _sut = new TransactionOutboxProcessorDecorator(
            _mockInner.Object,
            _mockTransactionExecutor.Object,
            IsolationLevel.ReadCommitted);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullInner_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TransactionOutboxProcessorDecorator(null!, _mockTransactionExecutor.Object));

        Assert.Equal("inner", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullTransactionExecutor_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TransactionOutboxProcessorDecorator(_mockInner.Object, null!));

        Assert.Equal("transactionExecutor", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act & Assert
        Assert.NotNull(_sut);
    }

    [Fact]
    public void Constructor_WithCustomIsolationLevel_UsesProvidedLevel()
    {
        // Act
        var decorator = new TransactionOutboxProcessorDecorator(
            _mockInner.Object,
            _mockTransactionExecutor.Object,
            IsolationLevel.Serializable);

        // Assert
        Assert.NotNull(decorator);
    }

    #endregion

    #region PublishToOutbox Tests

    [Fact]
    public async Task PublishToOutbox_WrapsInTransaction()
    {
        // Arrange
        var message = new Mock<IMessage>().Object;
        var options = new OutboxOptions();

        _mockTransactionExecutor
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<string>(),
                IsolationLevel.ReadCommitted,
                It.IsAny<CancellationToken>()))
            .Callback<Func<CancellationToken, Task>, string, IsolationLevel, CancellationToken>(
                async (func, desc, iso, ct) => await func(ct))
            .Returns(Task.CompletedTask);

        _mockInner.Setup(x => x.PublishToOutbox(message, options, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.PublishToOutbox(message, options);

        // Assert
        _mockTransactionExecutor.Verify(
            x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<string>(),
                IsolationLevel.ReadCommitted,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockInner.Verify(x => x.PublishToOutbox(message, options, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishToOutbox_WithCancellationToken_PassesTokenToTransaction()
    {
        // Arrange
        var message = new Mock<IMessage>().Object;
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _mockTransactionExecutor
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<string>(),
                IsolationLevel.ReadCommitted,
                cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.PublishToOutbox(message, null, cancellationToken);

        // Assert
        _mockTransactionExecutor.Verify(
            x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<string>(),
                IsolationLevel.ReadCommitted,
                cancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task PublishToOutbox_WhenTransactionFails_PropagatesException()
    {
        // Arrange
        var message = new Mock<IMessage>().Object;
        var expectedException = new InvalidOperationException("Transaction failed");

        _mockTransactionExecutor
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<string>(),
                It.IsAny<IsolationLevel>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _sut.PublishToOutbox(message));

        Assert.Same(expectedException, exception);
    }

    [Fact]
    public async Task PublishToOutbox_WithNullOptions_WorksCorrectly()
    {
        // Arrange
        var message = new Mock<IMessage>().Object;

        _mockTransactionExecutor
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<string>(),
                IsolationLevel.ReadCommitted,
                It.IsAny<CancellationToken>()))
            .Callback<Func<CancellationToken, Task>, string, IsolationLevel, CancellationToken>(
                async (func, desc, iso, ct) => await func(ct))
            .Returns(Task.CompletedTask);

        _mockInner.Setup(x => x.PublishToOutbox(message, null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.PublishToOutbox(message, null);

        // Assert
        _mockInner.Verify(x => x.PublishToOutbox(message, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region StartAsync Tests

    [Fact]
    public async Task StartAsync_CallsInnerStartAsync()
    {
        // Arrange
        _mockInner.Setup(x => x.StartAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.StartAsync();

        // Assert
        _mockInner.Verify(x => x.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_WithCancellationToken_PassesTokenToInner()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _mockInner.Setup(x => x.StartAsync(cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.StartAsync(cancellationToken);

        // Assert
        _mockInner.Verify(x => x.StartAsync(cancellationToken), Times.Once);
    }

    #endregion

    #region StopAsync Tests

    [Fact]
    public async Task StopAsync_CallsInnerStopAsync()
    {
        // Arrange
        _mockInner.Setup(x => x.StopAsync())
            .Returns(Task.CompletedTask);

        // Act
        await _sut.StopAsync();

        // Assert
        _mockInner.Verify(x => x.StopAsync(), Times.Once);
    }

    #endregion

    #region IsRunning Tests

    [Fact]
    public void IsRunning_ReturnsInnerIsRunning()
    {
        // Arrange
        _mockInner.Setup(x => x.IsRunning).Returns(true);

        // Act
        var result = _sut.IsRunning;

        // Assert
        Assert.True(result);
        _mockInner.Verify(x => x.IsRunning, Times.Once);
    }

    [Fact]
    public void IsRunning_WhenInnerReturnsFalse_ReturnsFalse()
    {
        // Arrange
        _mockInner.Setup(x => x.IsRunning).Returns(false);

        // Act
        var result = _sut.IsRunning;

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetMetrics Tests

    [Fact]
    public void GetMetrics_ReturnsInnerMetrics()
    {
        // Arrange
        var expectedMetrics = new Mock<IOutboxProcessorMetrics>().Object;
        _mockInner.Setup(x => x.GetMetrics()).Returns(expectedMetrics);

        // Act
        var result = _sut.GetMetrics();

        // Assert
        Assert.Same(expectedMetrics, result);
        _mockInner.Verify(x => x.GetMetrics(), Times.Once);
    }

    #endregion
}

[Trait("Category", "Unit")]
public class TransactionInboxProcessorDecoratorTests
{
    private readonly Mock<IInboxProcessor> _mockInner;
    private readonly Mock<ITransactionExecutor> _mockTransactionExecutor;
    private readonly TransactionInboxProcessorDecorator _sut;

    public TransactionInboxProcessorDecoratorTests()
    {
        _mockInner = new Mock<IInboxProcessor>();
        _mockTransactionExecutor = new Mock<ITransactionExecutor>();

        _sut = new TransactionInboxProcessorDecorator(
            _mockInner.Object,
            _mockTransactionExecutor.Object,
            IsolationLevel.ReadCommitted);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullInner_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TransactionInboxProcessorDecorator(null!, _mockTransactionExecutor.Object));

        Assert.Equal("inner", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullTransactionExecutor_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TransactionInboxProcessorDecorator(_mockInner.Object, null!));

        Assert.Equal("transactionExecutor", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act & Assert
        Assert.NotNull(_sut);
    }

    #endregion

    #region ProcessIncoming Tests

    [Fact]
    public async Task ProcessIncoming_WrapsInTransaction()
    {
        // Arrange
        var message = new Mock<IMessage>().Object;
        var options = new InboxOptions();

        _mockTransactionExecutor
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<string>(),
                IsolationLevel.ReadCommitted,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockInner.Setup(x => x.ProcessIncoming(message, options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.ProcessIncoming(message, options);

        // Assert
        Assert.True(result);
        _mockTransactionExecutor.Verify(
            x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<string>(),
                IsolationLevel.ReadCommitted,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessIncoming_WhenInnerReturnsFalse_ReturnsFalse()
    {
        // Arrange
        var message = new Mock<IMessage>().Object;

        _mockTransactionExecutor
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<string>(),
                IsolationLevel.ReadCommitted,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockInner.Setup(x => x.ProcessIncoming(message, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.ProcessIncoming(message);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ProcessIncoming_WithCancellationToken_PassesTokenToTransaction()
    {
        // Arrange
        var message = new Mock<IMessage>().Object;
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _mockTransactionExecutor
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<string>(),
                IsolationLevel.ReadCommitted,
                cancellationToken))
            .ReturnsAsync(true);

        // Act
        await _sut.ProcessIncoming(message, null, cancellationToken);

        // Assert
        _mockTransactionExecutor.Verify(
            x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<string>(),
                IsolationLevel.ReadCommitted,
                cancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task ProcessIncoming_WhenTransactionFails_PropagatesException()
    {
        // Arrange
        var message = new Mock<IMessage>().Object;
        var expectedException = new InvalidOperationException("Transaction failed");

        _mockTransactionExecutor
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<string>(),
                It.IsAny<IsolationLevel>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _sut.ProcessIncoming(message));

        Assert.Same(expectedException, exception);
    }

    #endregion

    #region StartAsync Tests

    [Fact]
    public async Task StartAsync_CallsInnerStartAsync()
    {
        // Arrange
        _mockInner.Setup(x => x.StartAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.StartAsync();

        // Assert
        _mockInner.Verify(x => x.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region StopAsync Tests

    [Fact]
    public async Task StopAsync_CallsInnerStopAsync()
    {
        // Arrange
        _mockInner.Setup(x => x.StopAsync())
            .Returns(Task.CompletedTask);

        // Act
        await _sut.StopAsync();

        // Assert
        _mockInner.Verify(x => x.StopAsync(), Times.Once);
    }

    #endregion

    #region GetUnprocessedCount Tests

    [Fact]
    public async Task GetUnprocessedCount_ReturnsInnerCount()
    {
        // Arrange
        var expectedCount = 42L;
        _mockInner.Setup(x => x.GetUnprocessedCount(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedCount);

        // Act
        var result = await _sut.GetUnprocessedCount();

        // Assert
        Assert.Equal(expectedCount, result);
        _mockInner.Verify(x => x.GetUnprocessedCount(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region IsRunning Tests

    [Fact]
    public void IsRunning_ReturnsInnerIsRunning()
    {
        // Arrange
        _mockInner.Setup(x => x.IsRunning).Returns(true);

        // Act
        var result = _sut.IsRunning;

        // Assert
        Assert.True(result);
        _mockInner.Verify(x => x.IsRunning, Times.Once);
    }

    #endregion

    #region GetMetrics Tests

    [Fact]
    public void GetMetrics_ReturnsInnerMetrics()
    {
        // Arrange
        var expectedMetrics = new Mock<IInboxProcessorMetrics>().Object;
        _mockInner.Setup(x => x.GetMetrics()).Returns(expectedMetrics);

        // Act
        var result = _sut.GetMetrics();

        // Assert
        Assert.Same(expectedMetrics, result);
        _mockInner.Verify(x => x.GetMetrics(), Times.Once);
    }

    #endregion
}

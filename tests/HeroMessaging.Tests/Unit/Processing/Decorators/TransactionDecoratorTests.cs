using System.Data;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Processing.Decorators;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing.Decorators;

[Trait("Category", "Unit")]
public sealed class TransactionCommandProcessorDecoratorTests
{
    private readonly Mock<ICommandProcessor> _innerMock;
    private readonly Mock<ITransactionExecutor> _transactionExecutorMock;

    public TransactionCommandProcessorDecoratorTests()
    {
        _innerMock = new Mock<ICommandProcessor>();
        _transactionExecutorMock = new Mock<ITransactionExecutor>();
    }

    private TransactionCommandProcessorDecorator CreateDecorator(IsolationLevel defaultIsolationLevel = IsolationLevel.ReadCommitted)
    {
        return new TransactionCommandProcessorDecorator(
            _innerMock.Object,
            _transactionExecutorMock.Object,
            defaultIsolationLevel);
    }

    #region Send (Void) - Success Cases

    [Fact]
    public async Task Send_WithVoidCommand_ExecutesInTransaction()
    {
        // Arrange
        var decorator = CreateDecorator();
        var command = new TestVoidCommand();

        _transactionExecutorMock
            .Setup(t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<string>(),
                IsolationLevel.ReadCommitted,
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, string, IsolationLevel, CancellationToken>(
                async (operation, name, level, ct) => await operation(ct));

        _innerMock
            .Setup(p => p.SendAsync(command, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await decorator.SendAsync(command, TestContext.Current.CancellationToken);

        // Assert
        _transactionExecutorMock.Verify(
            t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.Is<string>(s => s.Contains(nameof(TestVoidCommand))),
                IsolationLevel.ReadCommitted,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Send_WithVoidCommand_CallsInnerProcessor()
    {
        // Arrange
        var decorator = CreateDecorator();
        var command = new TestVoidCommand();

        _transactionExecutorMock
            .Setup(t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<string>(),
                It.IsAny<IsolationLevel>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, string, IsolationLevel, CancellationToken>(
                async (operation, name, level, ct) => await operation(ct));

        _innerMock
            .Setup(p => p.SendAsync(command, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await decorator.SendAsync(command, TestContext.Current.CancellationToken);

        // Assert
        _innerMock.Verify(p => p.SendAsync(command, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Send_WithVoidCommand_UsesDefaultIsolationLevel()
    {
        // Arrange
        var decorator = CreateDecorator(IsolationLevel.Serializable);
        var command = new TestVoidCommand();

        _transactionExecutorMock
            .Setup(t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<string>(),
                IsolationLevel.Serializable,
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, string, IsolationLevel, CancellationToken>(
                async (operation, name, level, ct) => await operation(ct));

        _innerMock
            .Setup(p => p.SendAsync(command, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await decorator.SendAsync(command, TestContext.Current.CancellationToken);

        // Assert
        _transactionExecutorMock.Verify(
            t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<string>(),
                IsolationLevel.Serializable,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Send_WithVoidCommand_PassesCancellationToken()
    {
        // Arrange
        var decorator = CreateDecorator();
        var command = new TestVoidCommand();
        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _transactionExecutorMock
            .Setup(t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<string>(),
                It.IsAny<IsolationLevel>(),
                cancellationToken))
            .Returns<Func<CancellationToken, Task>, string, IsolationLevel, CancellationToken>(
                async (operation, name, level, ct) => await operation(ct));

        _innerMock
            .Setup(p => p.SendAsync(command, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await decorator.SendAsync(command, cancellationToken);

        // Assert
        _transactionExecutorMock.Verify(
            t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<string>(),
                It.IsAny<IsolationLevel>(),
                cancellationToken),
            Times.Once);
    }

    #endregion

    #region Send (With Response) - Success Cases

    [Fact]
    public async Task Send_WithCommandResponse_ExecutesInTransaction()
    {
        // Arrange
        var decorator = CreateDecorator();
        var command = new TestCommandWithResponse();
        var expectedResponse = new TestResponse { Value = 42 };

        _transactionExecutorMock
            .Setup(t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<TestResponse>>>(),
                It.IsAny<string>(),
                IsolationLevel.ReadCommitted,
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<TestResponse>>, string, IsolationLevel, CancellationToken>(
                async (operation, name, level, ct) => await operation(ct));

        _innerMock
            .Setup(p => p.SendAsync(command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await decorator.SendAsync(command, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedResponse, result);
        _transactionExecutorMock.Verify(
            t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<TestResponse>>>(),
                It.Is<string>(s => s.Contains(nameof(TestCommandWithResponse))),
                IsolationLevel.ReadCommitted,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Send_WithCommandResponse_CallsInnerProcessor()
    {
        // Arrange
        var decorator = CreateDecorator();
        var command = new TestCommandWithResponse();
        var expectedResponse = new TestResponse { Value = 42 };

        _transactionExecutorMock
            .Setup(t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<TestResponse>>>(),
                It.IsAny<string>(),
                It.IsAny<IsolationLevel>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<TestResponse>>, string, IsolationLevel, CancellationToken>(
                async (operation, name, level, ct) => await operation(ct));

        _innerMock
            .Setup(p => p.SendAsync(command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await decorator.SendAsync(command, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedResponse, result);
        _innerMock.Verify(p => p.SendAsync(command, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Send_WithCommandResponse_ReturnsInnerResult()
    {
        // Arrange
        var decorator = CreateDecorator();
        var command = new TestCommandWithResponse();
        var expectedResponse = new TestResponse { Value = 99 };

        _transactionExecutorMock
            .Setup(t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<TestResponse>>>(),
                It.IsAny<string>(),
                It.IsAny<IsolationLevel>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<TestResponse>>, string, IsolationLevel, CancellationToken>(
                async (operation, name, level, ct) => await operation(ct));

        _innerMock
            .Setup(p => p.SendAsync(command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await decorator.SendAsync(command, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(99, result.Value);
    }

    #endregion

    #region Send - Exception Handling

    [Fact]
    public async Task Send_WithException_RethrowsException()
    {
        // Arrange
        var decorator = CreateDecorator();
        var command = new TestVoidCommand();
        var testException = new InvalidOperationException("Test error");

        _transactionExecutorMock
            .Setup(t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<string>(),
                It.IsAny<IsolationLevel>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(testException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await decorator.SendAsync(command, TestContext.Current.CancellationToken));

        Assert.Equal("Test error", exception.Message);
    }

    [Fact]
    public async Task Send_WithCommandResponse_WithException_RethrowsException()
    {
        // Arrange
        var decorator = CreateDecorator();
        var command = new TestCommandWithResponse();
        var testException = new InvalidOperationException("Test error");

        _transactionExecutorMock
            .Setup(t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<TestResponse>>>(),
                It.IsAny<string>(),
                It.IsAny<IsolationLevel>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(testException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await decorator.SendAsync(command, TestContext.Current.CancellationToken));

        Assert.Equal("Test error", exception.Message);
    }

    #endregion

    #region GetMetrics Tests

    [Fact]
    public void GetMetrics_CallsInnerProcessor()
    {
        // Arrange
        var decorator = CreateDecorator();
        var expectedMetrics = Mock.Of<IProcessorMetrics>();

        _innerMock
            .Setup(p => p.GetMetrics())
            .Returns(expectedMetrics);

        // Act
        var metrics = decorator.GetMetrics();

        // Assert
        Assert.Equal(expectedMetrics, metrics);
        _innerMock.Verify(p => p.GetMetrics(), Times.Once);
    }

    #endregion

    #region Test Helper Classes

    public class TestVoidCommand : ICommand
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    public class TestCommandWithResponse : ICommand<TestResponse>
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    public class TestResponse
    {
        public int Value { get; set; }
    }

    #endregion
}

[Trait("Category", "Unit")]
public sealed class TransactionQueryProcessorDecoratorTests
{
    private readonly Mock<IQueryProcessor> _innerMock;
    private readonly Mock<ITransactionExecutor> _transactionExecutorMock;

    public TransactionQueryProcessorDecoratorTests()
    {
        _innerMock = new Mock<IQueryProcessor>();
        _transactionExecutorMock = new Mock<ITransactionExecutor>();
    }

    private TransactionQueryProcessorDecorator CreateDecorator()
    {
        return new TransactionQueryProcessorDecorator(
            _innerMock.Object,
            _transactionExecutorMock.Object);
    }

    #region Send - Success Cases

    [Fact]
    public async Task Send_WithQuery_ExecutesInTransaction()
    {
        // Arrange
        var decorator = CreateDecorator();
        var query = new TestQuery();
        var expectedResponse = new TestQueryResponse { Data = "Test data" };

        _transactionExecutorMock
            .Setup(t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<TestQueryResponse>>>(),
                It.IsAny<string>(),
                IsolationLevel.ReadCommitted,
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<TestQueryResponse>>, string, IsolationLevel, CancellationToken>(
                async (operation, name, level, ct) => await operation(ct));

        _innerMock
            .Setup(p => p.SendAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await decorator.SendAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedResponse, result);
        _transactionExecutorMock.Verify(
            t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<TestQueryResponse>>>(),
                It.Is<string>(s => s.Contains(nameof(TestQuery))),
                IsolationLevel.ReadCommitted,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Send_WithQuery_CallsInnerProcessor()
    {
        // Arrange
        var decorator = CreateDecorator();
        var query = new TestQuery();
        var expectedResponse = new TestQueryResponse { Data = "Test data" };

        _transactionExecutorMock
            .Setup(t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<TestQueryResponse>>>(),
                It.IsAny<string>(),
                It.IsAny<IsolationLevel>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<TestQueryResponse>>, string, IsolationLevel, CancellationToken>(
                async (operation, name, level, ct) => await operation(ct));

        _innerMock
            .Setup(p => p.SendAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await decorator.SendAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedResponse, result);
        _innerMock.Verify(p => p.SendAsync(query, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Send_WithQuery_ReturnsInnerResult()
    {
        // Arrange
        var decorator = CreateDecorator();
        var query = new TestQuery();
        var expectedResponse = new TestQueryResponse { Data = "Expected data" };

        _transactionExecutorMock
            .Setup(t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<TestQueryResponse>>>(),
                It.IsAny<string>(),
                It.IsAny<IsolationLevel>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<TestQueryResponse>>, string, IsolationLevel, CancellationToken>(
                async (operation, name, level, ct) => await operation(ct));

        _innerMock
            .Setup(p => p.SendAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await decorator.SendAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Expected data", result.Data);
    }

    [Fact]
    public async Task Send_WithQuery_UsesReadCommittedIsolationLevel()
    {
        // Arrange
        var decorator = CreateDecorator();
        var query = new TestQuery();
        var expectedResponse = new TestQueryResponse { Data = "Test data" };

        _transactionExecutorMock
            .Setup(t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<TestQueryResponse>>>(),
                It.IsAny<string>(),
                IsolationLevel.ReadCommitted,
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<TestQueryResponse>>, string, IsolationLevel, CancellationToken>(
                async (operation, name, level, ct) => await operation(ct));

        _innerMock
            .Setup(p => p.SendAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        await decorator.SendAsync(query, TestContext.Current.CancellationToken);

        // Assert - Verified by mock setup
        _transactionExecutorMock.Verify(
            t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<TestQueryResponse>>>(),
                It.IsAny<string>(),
                IsolationLevel.ReadCommitted,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Send_WithQuery_PassesCancellationToken()
    {
        // Arrange
        var decorator = CreateDecorator();
        var query = new TestQuery();
        var expectedResponse = new TestQueryResponse { Data = "Test data" };
        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _transactionExecutorMock
            .Setup(t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<TestQueryResponse>>>(),
                It.IsAny<string>(),
                It.IsAny<IsolationLevel>(),
                cancellationToken))
            .Returns<Func<CancellationToken, Task<TestQueryResponse>>, string, IsolationLevel, CancellationToken>(
                async (operation, name, level, ct) => await operation(ct));

        _innerMock
            .Setup(p => p.SendAsync(query, cancellationToken))
            .ReturnsAsync(expectedResponse);

        // Act
        await decorator.SendAsync(query, cancellationToken);

        // Assert
        _transactionExecutorMock.Verify(
            t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<TestQueryResponse>>>(),
                It.IsAny<string>(),
                It.IsAny<IsolationLevel>(),
                cancellationToken),
            Times.Once);
    }

    #endregion

    #region Send - Exception Handling

    [Fact]
    public async Task Send_WithException_RethrowsException()
    {
        // Arrange
        var decorator = CreateDecorator();
        var query = new TestQuery();
        var testException = new InvalidOperationException("Query failed");

        _transactionExecutorMock
            .Setup(t => t.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<TestQueryResponse>>>(),
                It.IsAny<string>(),
                It.IsAny<IsolationLevel>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(testException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await decorator.SendAsync(query, TestContext.Current.CancellationToken));

        Assert.Equal("Query failed", exception.Message);
    }

    #endregion

    #region GetMetrics Tests

    [Fact]
    public void GetMetrics_CallsInnerProcessor()
    {
        // Arrange
        var decorator = CreateDecorator();
        var expectedMetrics = Mock.Of<IQueryProcessorMetrics>();

        _innerMock
            .Setup(p => p.GetMetrics())
            .Returns(expectedMetrics);

        // Act
        var metrics = decorator.GetMetrics();

        // Assert
        Assert.Equal(expectedMetrics, metrics);
        _innerMock.Verify(p => p.GetMetrics(), Times.Once);
    }

    #endregion

    #region Test Helper Classes

    public class TestQuery : IQuery<TestQueryResponse>
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    public class TestQueryResponse
    {
        public string? Data { get; set; }
    }

    #endregion
}

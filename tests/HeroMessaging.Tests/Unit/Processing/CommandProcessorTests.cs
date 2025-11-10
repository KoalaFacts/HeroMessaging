using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

/// <summary>
/// Unit tests for CommandProcessor
/// Tests command processing pipeline and handler invocation
/// </summary>
[Trait("Category", "Unit")]
public sealed class CommandProcessorTests
{
    private readonly Mock<ILogger<CommandProcessor>> _mockLogger;
    private readonly ServiceCollection _services;
    private readonly IServiceProvider _serviceProvider;

    public CommandProcessorTests()
    {
        _mockLogger = new Mock<ILogger<CommandProcessor>>();
        _services = new ServiceCollection();
        _serviceProvider = _services.BuildServiceProvider();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidServiceProvider_CreatesProcessor()
    {
        // Act
        var processor = new CommandProcessor(_serviceProvider, _mockLogger.Object);

        // Assert
        Assert.NotNull(processor);
        Assert.True(processor.IsRunning);
    }

    [Fact]
    public void Constructor_WithNullLogger_UsesNullLogger()
    {
        // Act
        var processor = new CommandProcessor(_serviceProvider);

        // Assert
        Assert.NotNull(processor);
    }

    #endregion

    #region Send (void) Tests

    [Fact]
    public async Task Send_WithNullCommand_ThrowsArgumentNullException()
    {
        // Arrange
        var processor = new CommandProcessor(_serviceProvider, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => processor.Send(null!));
    }

    [Fact]
    public async Task Send_WithNoRegisteredHandler_ThrowsInvalidOperationException()
    {
        // Arrange
        var processor = new CommandProcessor(_serviceProvider, _mockLogger.Object);
        var command = new TestCommand { Data = "test" };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => processor.Send(command));
        Assert.Contains("No handler found", ex.Message);
        Assert.Contains("TestCommand", ex.Message);
    }

    [Fact]
    public async Task Send_WithRegisteredHandler_InvokesHandler()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new CommandProcessor(provider, _mockLogger.Object);
        var command = new TestCommand { Data = "test data" };

        // Act
        await processor.Send(command);

        // Assert
        handlerMock.Verify(h => h.Handle(
            It.Is<TestCommand>(c => c.Data == "test data"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Send_WithCancellationToken_PropagatesToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new CommandProcessor(provider, _mockLogger.Object);
        var command = new TestCommand { Data = "test" };

        // Act
        await processor.Send(command, cts.Token);

        // Assert
        handlerMock.Verify(h => h.Handle(
            It.IsAny<TestCommand>(),
            It.Is<CancellationToken>(ct => ct == cts.Token)), Times.Once);
    }

    [Fact]
    public async Task Send_WhenHandlerThrows_PropagatesException()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        var expectedException = new InvalidOperationException("Handler error");
        handlerMock.Setup(h => h.Handle(It.IsAny<TestCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new CommandProcessor(provider, _mockLogger.Object);
        var command = new TestCommand { Data = "test" };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => processor.Send(command));
        Assert.Equal("Handler error", ex.Message);
    }

    [Fact]
    public async Task Send_WhenCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new CommandProcessor(provider, _mockLogger.Object);
        var command = new TestCommand { Data = "test" };

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.Send(command, cts.Token));
    }

    #endregion

    #region Send<TResponse> Tests

    [Fact]
    public async Task SendWithResponse_WithNullCommand_ThrowsArgumentNullException()
    {
        // Arrange
        var processor = new CommandProcessor(_serviceProvider, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            processor.Send<string>(null!));
    }

    [Fact]
    public async Task SendWithResponse_WithNoRegisteredHandler_ThrowsInvalidOperationException()
    {
        // Arrange
        var processor = new CommandProcessor(_serviceProvider, _mockLogger.Object);
        var command = new TestCommandWithResponse { Data = "test" };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.Send(command));
        Assert.Contains("No handler found", ex.Message);
    }

    [Fact]
    public async Task SendWithResponse_WithRegisteredHandler_ReturnsResponse()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommandWithResponse, string>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestCommandWithResponse>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("response value");

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new CommandProcessor(provider, _mockLogger.Object);
        var command = new TestCommandWithResponse { Data = "input" };

        // Act
        var result = await processor.Send(command);

        // Assert
        Assert.Equal("response value", result);
        handlerMock.Verify(h => h.Handle(
            It.Is<TestCommandWithResponse>(c => c.Data == "input"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendWithResponse_WhenHandlerThrows_PropagatesException()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommandWithResponse, string>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestCommandWithResponse>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Handler failed"));

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new CommandProcessor(provider, _mockLogger.Object);
        var command = new TestCommandWithResponse { Data = "test" };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => processor.Send(command));
        Assert.Equal("Handler failed", ex.Message);
    }

    #endregion

    #region GetMetrics Tests

    [Fact]
    public void GetMetrics_InitialState_ReturnsZeroMetrics()
    {
        // Arrange
        var processor = new CommandProcessor(_serviceProvider, _mockLogger.Object);

        // Act
        var metrics = processor.GetMetrics();

        // Assert
        Assert.Equal(0, metrics.ProcessedCount);
        Assert.Equal(0, metrics.FailedCount);
        Assert.Equal(TimeSpan.Zero, metrics.AverageDuration);
    }

    [Fact]
    public async Task GetMetrics_AfterSuccessfulProcessing_IncrementsProcessedCount()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new CommandProcessor(provider, _mockLogger.Object);
        var command = new TestCommand { Data = "test" };

        // Act
        await processor.Send(command);
        var metrics = processor.GetMetrics();

        // Assert
        Assert.Equal(1, metrics.ProcessedCount);
        Assert.Equal(0, metrics.FailedCount);
    }

    [Fact]
    public async Task GetMetrics_AfterFailedProcessing_IncrementsFailedCount()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new CommandProcessor(provider, _mockLogger.Object);
        var command = new TestCommand { Data = "test" };

        // Act
        try { await processor.Send(command); } catch { }
        var metrics = processor.GetMetrics();

        // Assert
        Assert.Equal(0, metrics.ProcessedCount);
        Assert.Equal(1, metrics.FailedCount);
    }

    #endregion

    #region Test Commands

    public class TestCommand : ICommand
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string Data { get; set; } = string.Empty;
    }

    public class TestCommandWithResponse : ICommand<string>
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string Data { get; set; } = string.Empty;
    }

    #endregion
}

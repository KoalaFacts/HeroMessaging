using System.Collections.Generic;
using System.Linq;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

[Trait("Category", "Unit")]
public sealed class CommandProcessorTests : IDisposable
{
    private readonly ServiceCollection _services;
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<ILogger<CommandProcessor>> _loggerMock;

    public CommandProcessorTests()
    {
        _services = new ServiceCollection();
        _loggerMock = new Mock<ILogger<CommandProcessor>>();
        _serviceProvider = _services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }

    private CommandProcessor CreateProcessor()
    {
        var services = new ServiceCollection();

        // Copy existing registrations
        foreach (var service in _services)
        {
            ((IList<ServiceDescriptor>)services).Add(service);
        }

        var provider = services.BuildServiceProvider();
        return new CommandProcessor(provider, _loggerMock.Object);
    }

    #region Send(ICommand) - Success Cases

    [Fact]
    public async Task Send_WithValidCommand_ProcessesSuccessfully()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();
        var command = new TestCommand();

        handlerMock
            .Setup(h => h.HandleAsync(command, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await processor.SendAsync(command);

        // Assert
        handlerMock.Verify(h => h.HandleAsync(command, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Send_WithValidCommand_UpdatesMetrics()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();
        var command = new TestCommand();

        handlerMock
            .Setup(h => h.HandleAsync(command, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await processor.SendAsync(command);

        // Assert
        var metrics = processor.GetMetrics();
        Assert.Equal(1, metrics.ProcessedCount);
        Assert.Equal(0, metrics.FailedCount);
    }

    [Fact]
    public async Task Send_WithMultipleCommands_ProcessesSequentially()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();
        var command1 = new TestCommand();
        var command2 = new TestCommand();

        handlerMock
            .Setup(h => h.HandleAsync(It.IsAny<TestCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await processor.SendAsync(command1);
        await processor.SendAsync(command2);

        // Assert
        var metrics = processor.GetMetrics();
        Assert.Equal(2, metrics.ProcessedCount);
        handlerMock.Verify(h => h.HandleAsync(It.IsAny<TestCommand>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    #endregion

    #region Send(ICommand<TResponse>) - Success Cases

    [Fact]
    public async Task SendWithResponse_WithValidCommand_ReturnsResponse()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommandWithResponse, string>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();
        var command = new TestCommandWithResponse();
        var expectedResponse = "Success";

        handlerMock
            .Setup(h => h.HandleAsync(command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await processor.SendAsync(command);

        // Assert
        Assert.Equal(expectedResponse, result);
    }

    [Fact]
    public async Task SendWithResponse_WithValidCommand_UpdatesMetrics()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommandWithResponse, string>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();
        var command = new TestCommandWithResponse();

        handlerMock
            .Setup(h => h.HandleAsync(command, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Success");

        // Act
        await processor.SendAsync(command);

        // Assert
        var metrics = processor.GetMetrics();
        Assert.Equal(1, metrics.ProcessedCount);
        Assert.Equal(0, metrics.FailedCount);
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task Send_WhenHandlerNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        var processor = CreateProcessor();
        var command = new TestCommand();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await processor.SendAsync(command));
        Assert.Contains("No handler found", exception.Message);
    }

    [Fact]
    public async Task Send_WhenHandlerThrowsException_PropagatesException()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();
        var command = new TestCommand();
        var expectedException = new InvalidOperationException("Handler error");

        handlerMock
            .Setup(h => h.HandleAsync(command, It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await processor.SendAsync(command));
        Assert.Equal("Handler error", exception.Message);
    }

    [Fact]
    public async Task Send_WhenHandlerThrowsException_UpdatesFailureMetrics()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();
        var command = new TestCommand();

        handlerMock
            .Setup(h => h.HandleAsync(command, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Handler error"));

        // Act
        try
        {
            await processor.SendAsync(command);
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Assert
        var metrics = processor.GetMetrics();
        Assert.Equal(0, metrics.ProcessedCount);
        Assert.Equal(1, metrics.FailedCount);
    }

    [Fact]
    public async Task Send_WhenHandlerThrowsException_LogsError()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();
        var command = new TestCommand();

        handlerMock
            .Setup(h => h.HandleAsync(command, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Handler error"));

        // Act
        try
        {
            await processor.SendAsync(command);
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Cancellation

    [Fact]
    public async Task Send_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();
        var command = new TestCommand();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await processor.SendAsync(command, cts.Token));
    }

    [Fact]
    public async Task Send_WithCancellationDuringProcessing_ThrowsOperationCanceledException()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();
        var command = new TestCommand();
        var cts = new CancellationTokenSource();

        handlerMock
            .Setup(h => h.HandleAsync(command, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                cts.Cancel();
                await Task.Delay(100, cts.Token);
            });

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await processor.SendAsync(command, cts.Token));
    }

    #endregion

    #region Null Argument Validation

    [Fact]
    public async Task Send_WithNullCommand_ThrowsArgumentNullException()
    {
        // Arrange
        var processor = CreateProcessor();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await processor.SendAsync(null!));
    }

    [Fact]
    public async Task SendWithResponse_WithNullCommand_ThrowsArgumentNullException()
    {
        // Arrange
        var processor = CreateProcessor();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await processor.SendAsync((ICommand<string>)null!));
    }

    #endregion

    #region Metrics

    [Fact]
    public void GetMetrics_WithNoCommands_ReturnsZeroMetrics()
    {
        // Arrange
        var processor = CreateProcessor();

        // Act
        var metrics = processor.GetMetrics();

        // Assert
        Assert.Equal(0, metrics.ProcessedCount);
        Assert.Equal(0, metrics.FailedCount);
        Assert.Equal(TimeSpan.Zero, metrics.AverageDuration);
    }

    [Fact]
    public async Task GetMetrics_AfterProcessing_CalculatesAverageDuration()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();
        var command = new TestCommand();

        handlerMock
            .Setup(h => h.HandleAsync(command, It.IsAny<CancellationToken>()))
            .Returns(async () => await Task.Delay(10));

        // Act
        await processor.SendAsync(command);

        // Assert
        var metrics = processor.GetMetrics();
        Assert.True(metrics.AverageDuration > TimeSpan.Zero);
    }

    [Fact]
    public async Task GetMetrics_WithMultipleCommands_TracksAllMetrics()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();

        handlerMock
            .Setup(h => h.HandleAsync(It.IsAny<TestCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await processor.SendAsync(new TestCommand());
        await processor.SendAsync(new TestCommand());

        try
        {
            handlerMock
                .Setup(h => h.HandleAsync(It.IsAny<TestCommand>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException());
            await processor.SendAsync(new TestCommand());
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Assert
        var metrics = processor.GetMetrics();
        Assert.Equal(2, metrics.ProcessedCount);
        Assert.Equal(1, metrics.FailedCount);
    }

    #endregion

    #region IsRunning

    [Fact]
    public void IsRunning_ReturnsTrue()
    {
        // Arrange
        var processor = CreateProcessor();

        // Act & Assert
        Assert.True(processor.IsRunning);
    }

    #endregion

    #region Test Helper Classes

    public class TestCommand : ICommand
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; } = [];
    }

    public class TestCommandWithResponse : ICommand<string>
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; } = [];
    }

    #endregion
}

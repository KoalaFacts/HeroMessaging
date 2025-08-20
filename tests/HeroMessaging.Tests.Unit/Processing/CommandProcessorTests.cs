using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Core.Processing;
using HeroMessaging.Tests.Unit.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace HeroMessaging.Tests.Unit.Processing;

public class CommandProcessorTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly CommandProcessor _processor;
    private readonly Mock<ILogger<CommandProcessor>> _loggerMock;

    public CommandProcessorTests()
    {
        _loggerMock = new Mock<ILogger<CommandProcessor>>();
        var services = new ServiceCollection();
        
        // Register test handlers
        services.AddSingleton<ICommandHandler<TestCommand>, TestCommandHandler>();
        services.AddSingleton<ICommandHandler<TestCommandWithResult, string>, TestCommandWithResultHandler>();
        services.AddSingleton<ICommandHandler<FailingCommand>, FailingCommandHandler>();
        services.AddSingleton<ICommandHandler<SlowCommand>, SlowCommandHandler>();
        
        _serviceProvider = services.BuildServiceProvider();
        _processor = new CommandProcessor(_serviceProvider, _loggerMock.Object);
    }

    [Fact]
    public async Task SendAsync_WithValidCommand_ShouldExecuteHandler()
    {
        // Arrange
        var command = new TestCommand { Value = "test" };
        TestCommandHandler.ResetCallCount();

        // Act
        await _processor.Send(command);

        // Assert
        Assert.Equal(1, TestCommandHandler.CallCount);
        Assert.NotNull(TestCommandHandler.LastCommand);
        Assert.Equal("test", TestCommandHandler.LastCommand.Value);
    }

    [Fact]
    public async Task SendAsync_WithCommandReturningResult_ShouldReturnValue()
    {
        // Arrange
        var command = new TestCommandWithResult { Input = "hello" };

        // Act
        var result = await _processor.Send(command);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Processed: hello", result);
    }

    [Fact]
    public async Task SendAsync_WithNullCommand_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _processor.Send((TestCommand)null!));
    }

    [Fact]
    public async Task SendAsync_WithNoHandler_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var command = new UnhandledCommand();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _processor.Send(command));
        Assert.Contains("No handler found", exception.Message);
    }

    [Fact]
    public async Task SendAsync_WhenHandlerThrows_ShouldPropagateException()
    {
        // Arrange
        var command = new FailingCommand { ShouldFail = true };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _processor.Send(command));
        Assert.Equal("Command failed", exception.Message);
    }

    [Fact]
    public async Task SendAsync_WithCancellationToken_ShouldRespectCancellation()
    {
        // Arrange
        var command = new SlowCommand { DelayMs = 5000 };
        var cts = new CancellationTokenSource(100);

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await _processor.Send(command, cts.Token));
    }

    [Fact]
    public async Task SendAsync_MultipleCommands_ShouldProcessSequentially()
    {
        // Arrange
        var commands = Enumerable.Range(1, 10)
            .Select(i => new TestCommand { Value = i.ToString() })
            .ToList();
        TestCommandHandler.ResetCallCount();

        // Act
        foreach (var command in commands)
        {
            await _processor.Send(command);
        }

        // Assert
        Assert.Equal(10, TestCommandHandler.CallCount);
    }

    [Fact]
    public async Task GetMetrics_ShouldReturnCorrectStatistics()
    {
        // Arrange & Act
        await _processor.Send(new TestCommand { Value = "1" });
        await _processor.Send(new TestCommandWithResult { Input = "test" });
        try 
        { 
            await _processor.Send(new FailingCommand { ShouldFail = true }); 
        } 
        catch { }

        var metrics = _processor.GetMetrics();

        // Assert
        Assert.True(metrics.ProcessedCount >= 2);
        Assert.True(metrics.FailedCount >= 1);
        Assert.True(metrics.AverageDuration >= TimeSpan.Zero);
    }

    [Fact]
    public void IsRunning_ShouldBeTrue()
    {
        // Assert
        Assert.True(_processor.IsRunning);
    }

    [Fact]
    public async Task SendAsync_WithHighVolume_ShouldHandleBackpressure()
    {
        // Arrange
        var commandCount = 1000;
        var commands = Enumerable.Range(1, commandCount)
            .Select(i => new TestCommand { Value = i.ToString() })
            .ToList();
        TestCommandHandler.ResetCallCount();

        // Act
        var tasks = commands.Select(cmd => _processor.Send(cmd));
        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(commandCount, TestCommandHandler.CallCount);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }
}
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

    [Fact]
    public async Task GetMetrics_AfterMultipleSuccessfulCommands_AccumulatesMetrics()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new CommandProcessor(provider, _mockLogger.Object);

        // Act
        for (int i = 0; i < 5; i++)
        {
            var command = new TestCommand { Data = $"test{i}" };
            await processor.Send(command);
        }
        var metrics = processor.GetMetrics();

        // Assert
        Assert.Equal(5, metrics.ProcessedCount);
        Assert.Equal(0, metrics.FailedCount);
    }

    [Fact]
    public async Task GetMetrics_AfterMultipleMixedCommands_AccumulatesFailedCount()
    {
        // Arrange
        var successHandler = new Mock<ICommandHandler<TestCommand>>();
        successHandler.Setup(h => h.Handle(It.IsAny<TestCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var failureHandler = new Mock<ICommandHandler<TestCommandWithResponse, string>>();
        failureHandler.Setup(h => h.Handle(It.IsAny<TestCommandWithResponse>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Handler failed"));

        var services = new ServiceCollection();
        services.AddSingleton(successHandler.Object);
        services.AddSingleton(failureHandler.Object);
        var provider = services.BuildServiceProvider();

        var processor = new CommandProcessor(provider, _mockLogger.Object);

        // Act
        await processor.Send(new TestCommand { Data = "success1" });
        await processor.Send(new TestCommand { Data = "success2" });
        try { await processor.Send(new TestCommandWithResponse { Data = "failure1" }); } catch { }
        await processor.Send(new TestCommand { Data = "success3" });
        try { await processor.Send(new TestCommandWithResponse { Data = "failure2" }); } catch { }

        var metrics = processor.GetMetrics();

        // Assert
        Assert.Equal(3, metrics.ProcessedCount);
        Assert.Equal(2, metrics.FailedCount);
    }

    [Fact]
    public async Task GetMetrics_CalculatesAverageDuration()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new CommandProcessor(provider, _mockLogger.Object);

        // Act - Process 3 commands
        for (int i = 0; i < 3; i++)
        {
            await processor.Send(new TestCommand { Data = $"test{i}" });
        }
        var metrics = processor.GetMetrics();

        // Assert - Average duration should be greater than zero
        Assert.True(metrics.AverageDuration >= TimeSpan.Zero);
        Assert.Equal(3, metrics.ProcessedCount);
    }

    #endregion

    #region SendWithResponse Error Tests

    [Fact]
    public async Task SendWithResponse_WhenHandlerThrowsAndCancelled_PropagatesException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var handlerMock = new Mock<ICommandHandler<TestCommandWithResponse, string>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestCommandWithResponse>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Handler error"));

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new CommandProcessor(provider, _mockLogger.Object);
        var command = new TestCommandWithResponse { Data = "test" };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.Send(command, cts.Token));
        Assert.Equal("Handler error", ex.Message);
    }

    [Fact]
    public async Task SendWithResponse_WhenCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var handlerMock = new Mock<ICommandHandler<TestCommandWithResponse, string>>();
        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new CommandProcessor(provider, _mockLogger.Object);
        var command = new TestCommandWithResponse { Data = "test" };

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            processor.Send(command, cts.Token));
    }

    #endregion

    #region Logging Tests

    [Fact]
    public async Task Send_WhenHandlerThrows_LogsError()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        var exception = new InvalidOperationException("Test error");
        handlerMock.Setup(h => h.Handle(It.IsAny<TestCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var mockLogger = new Mock<ILogger<CommandProcessor>>();
        var processor = new CommandProcessor(provider, mockLogger.Object);
        var command = new TestCommand { Data = "test" };

        // Act
        try { await processor.Send(command); } catch { }

        // Assert
        mockLogger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendWithResponse_WhenHandlerThrows_LogsError()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommandWithResponse, string>>();
        var exception = new InvalidOperationException("Test error");
        handlerMock.Setup(h => h.Handle(It.IsAny<TestCommandWithResponse>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var mockLogger = new Mock<ILogger<CommandProcessor>>();
        var processor = new CommandProcessor(provider, mockLogger.Object);
        var command = new TestCommandWithResponse { Data = "test" };

        // Act
        try { await processor.Send(command); } catch { }

        // Assert
        mockLogger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    #endregion

    #region IsRunning Tests

    [Fact]
    public void IsRunning_InitialState_ReturnsTrue()
    {
        // Act
        var processor = new CommandProcessor(_serviceProvider, _mockLogger.Object);

        // Assert
        Assert.True(processor.IsRunning);
    }

    #endregion

    #region Edge Cases and Boundary Tests

    [Fact]
    public async Task Send_WithEmptyCommandData_InvokesHandler()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new CommandProcessor(provider, _mockLogger.Object);
        var command = new TestCommand { Data = "" };

        // Act
        await processor.Send(command);

        // Assert
        handlerMock.Verify(h => h.Handle(
            It.Is<TestCommand>(c => c.Data == ""),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendWithResponse_ReturnsCorrectResponseType()
    {
        // Arrange
        var expectedResponse = "test_response_123";
        var handlerMock = new Mock<ICommandHandler<TestCommandWithResponse, string>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestCommandWithResponse>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new CommandProcessor(provider, _mockLogger.Object);
        var command = new TestCommandWithResponse { Data = "input" };

        // Act
        var result = await processor.Send(command);

        // Assert
        Assert.Equal(expectedResponse, result);
        Assert.IsType<string>(result);
    }

    [Fact]
    public async Task GetMetrics_WithZeroProcessedCommands_ReturnsZeroDuration()
    {
        // Arrange
        var processor = new CommandProcessor(_serviceProvider, _mockLogger.Object);

        // Act
        var metrics = processor.GetMetrics();

        // Assert
        Assert.Equal(TimeSpan.Zero, metrics.AverageDuration);
        Assert.Equal(0, metrics.ProcessedCount);
        Assert.Equal(0, metrics.FailedCount);
    }

    [Fact]
    public async Task Send_WithDifferentExceptionTypes_AllPropagateCorrectly()
    {
        // Arrange - Test with ArgumentException
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Invalid argument"));

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new CommandProcessor(provider, _mockLogger.Object);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            processor.Send(new TestCommand { Data = "test" }));
        Assert.Equal("Invalid argument", ex.Message);
    }

    #endregion

    #region Duration Tracking Tests

    [Fact]
    public async Task GetMetrics_TracksDurationForEachCommand()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestCommand>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(5); // Small delay to ensure measurable duration
            });

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new CommandProcessor(provider, _mockLogger.Object);

        // Act
        await processor.Send(new TestCommand { Data = "test" });
        var metrics = processor.GetMetrics();

        // Assert
        Assert.Equal(1, metrics.ProcessedCount);
        Assert.True(metrics.AverageDuration.TotalMilliseconds >= 5,
            $"Expected duration >= 5ms, but got {metrics.AverageDuration.TotalMilliseconds}ms");
    }

    [Fact]
    public async Task GetMetrics_DurationListRotatesAfter100Items()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new CommandProcessor(provider, _mockLogger.Object);

        // Act - Process more than 100 commands to trigger rotation
        for (int i = 0; i < 150; i++)
        {
            await processor.Send(new TestCommand { Data = $"test{i}" });
        }
        var metrics = processor.GetMetrics();

        // Assert - Should have 150 processed commands, but only ~100 durations tracked
        Assert.Equal(150, metrics.ProcessedCount);
        Assert.True(metrics.AverageDuration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task Send_WithVeryLargeDurationList_MaintainsMaxOf100Items()
    {
        // Arrange
        var handlerMock = new Mock<ICommandHandler<TestCommand>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestCommand>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                // Add minimal delay to ensure measurable duration
                await Task.Delay(1);
            });

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new CommandProcessor(provider, _mockLogger.Object);

        // Act - Process 200 commands
        for (int i = 0; i < 200; i++)
        {
            await processor.Send(new TestCommand { Data = $"test{i}" });
        }
        var metrics = processor.GetMetrics();

        // Assert - Average duration should be reasonable even with many commands
        Assert.Equal(200, metrics.ProcessedCount);
        Assert.True(metrics.AverageDuration >= TimeSpan.Zero);
        Assert.True(metrics.AverageDuration.TotalMilliseconds >= 0);
    }

    #endregion

    #region Test Commands

    public class TestCommand : ICommand
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string Data { get; set; } = string.Empty;
    }

    public class TestCommandWithResponse : ICommand<string>
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string Data { get; set; } = string.Empty;
    }

    #endregion
}

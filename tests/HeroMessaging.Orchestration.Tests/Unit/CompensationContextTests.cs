using HeroMessaging.Orchestration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Orchestration;

[Trait("Category", "Unit")]
public class CompensationContextTests
{
    [Fact]
    public void CompensationContext_InitialState_HasNoActions()
    {
        // Arrange & Act
        var context = new CompensationContext();

        // Assert
        Assert.False(context.HasActions);
        Assert.Equal(0, context.ActionCount);
    }

    [Fact]
    public void CompensationContext_WithLogger_StoresLogger()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<CompensationContext>>();

        // Act
        var context = new CompensationContext(loggerMock.Object);

        // Assert
        Assert.False(context.HasActions);
        Assert.Equal(0, context.ActionCount);
    }

    [Fact]
    public void AddCompensation_WithAction_IncreasesCount()
    {
        // Arrange
        var context = new CompensationContext();
        var action = new DelegateCompensatingAction("TestAction", () => Task.CompletedTask);

        // Act
        context.AddCompensation(action);

        // Assert
        Assert.True(context.HasActions);
        Assert.Equal(1, context.ActionCount);
    }

    [Fact]
    public void AddCompensation_WithNullAction_ThrowsArgumentNullException()
    {
        // Arrange
        var context = new CompensationContext();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => context.AddCompensation(null!));

        Assert.Equal("action", exception.ParamName);
    }

    [Fact]
    public void AddCompensation_WithLogger_LogsDebugMessage()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<CompensationContext>>();
        var context = new CompensationContext(loggerMock.Object);
        var action = new DelegateCompensatingAction("TestAction", () => Task.CompletedTask);

        // Act
        context.AddCompensation(action);

        // Assert
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Added compensation action")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void AddCompensation_WithDelegate_CreatesAction()
    {
        // Arrange
        var context = new CompensationContext();
        var compensated = false;

        // Act
        context.AddCompensation("SetFlag", () => compensated = true);

        // Assert
        Assert.True(context.HasActions);
        Assert.Equal(1, context.ActionCount);
    }

    [Fact]
    public void AddCompensation_WithAsyncDelegate_CreatesAction()
    {
        // Arrange
        var context = new CompensationContext();

        // Act
        context.AddCompensation("AsyncAction", async () => await Task.Delay(1));

        // Assert
        Assert.True(context.HasActions);
        Assert.Equal(1, context.ActionCount);
    }

    [Fact]
    public void AddCompensation_WithAsyncDelegateAndCancellation_CreatesAction()
    {
        // Arrange
        var context = new CompensationContext();

        // Act
        context.AddCompensation("AsyncActionWithCT", ct => Task.Delay(1, ct));

        // Assert
        Assert.True(context.HasActions);
        Assert.Equal(1, context.ActionCount);
    }

    [Fact]
    public async Task CompensateAsync_ExecutesActionsInReverseOrder()
    {
        // Arrange
        var context = new CompensationContext();
        var executionOrder = new List<int>();

        context.AddCompensation("First", () => executionOrder.Add(1));
        context.AddCompensation("Second", () => executionOrder.Add(2));
        context.AddCompensation("Third", () => executionOrder.Add(3));

        // Act
        await context.CompensateAsync();

        // Assert - LIFO order: 3, 2, 1
        Assert.Equal(new[] { 3, 2, 1 }, executionOrder);
        Assert.False(context.HasActions); // All actions consumed
    }

    [Fact]
    public async Task CompensateAsync_WithAsyncActions_ExecutesSuccessfully()
    {
        // Arrange
        var context = new CompensationContext();
        var executed = false;

        context.AddCompensation("AsyncAction", async () =>
        {
            await Task.Delay(10);
            executed = true;
        });

        // Act
        await context.CompensateAsync();

        // Assert
        Assert.True(executed);
    }

    [Fact]
    public async Task CompensateAsync_WithNoActions_CompletesSuccessfully()
    {
        // Arrange
        var context = new CompensationContext();

        // Act & Assert - Should not throw
        await context.CompensateAsync();
        Assert.False(context.HasActions);
    }

    [Fact]
    public async Task CompensateAsync_WithSingleAction_ExecutesSuccessfully()
    {
        // Arrange
        var context = new CompensationContext();
        var executed = false;

        context.AddCompensation("SingleAction", () => executed = true);

        // Act
        await context.CompensateAsync();

        // Assert
        Assert.True(executed);
        Assert.False(context.HasActions);
    }

    [Fact]
    public async Task CompensateAsync_WithLogger_LogsInformationMessages()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<CompensationContext>>();
        var context = new CompensationContext(loggerMock.Object);
        context.AddCompensation("TestAction", () => Task.CompletedTask);

        // Act
        await context.CompensateAsync();

        // Assert
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Starting compensation")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Successfully compensated")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CompensateAsync_WithFailingAction_StopsOnFirstError()
    {
        // Arrange
        var context = new CompensationContext();
        var firstExecuted = false;
        var secondExecuted = false;

        context.AddCompensation("First", () => firstExecuted = true);
        context.AddCompensation("Failing", () => throw new InvalidOperationException("Test failure"));
        context.AddCompensation("Second", () => secondExecuted = true);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AggregateException>(
            async () => await context.CompensateAsync(stopOnFirstError: true));

        Assert.Contains("Compensation failed for action 'Failing'", exception.Message);
        Assert.IsType<CompensationException>(exception.InnerExceptions[0]);
        Assert.False(firstExecuted); // Never reached
        Assert.True(secondExecuted); // Executed before failure
    }

    [Fact]
    public async Task CompensateAsync_WithFailingAction_ContinuesOnError()
    {
        // Arrange
        var context = new CompensationContext();
        var firstExecuted = false;
        var thirdExecuted = false;

        context.AddCompensation("First", () => firstExecuted = true);
        context.AddCompensation("Failing", () => throw new InvalidOperationException("Test failure"));
        context.AddCompensation("Third", () => thirdExecuted = true);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AggregateException>(
            async () => await context.CompensateAsync(stopOnFirstError: false));

        Assert.Contains("Compensation completed with 1 errors", exception.Message);
        Assert.True(firstExecuted); // All actions attempted
        Assert.True(thirdExecuted);
    }

    [Fact]
    public async Task CompensateAsync_WithMultipleFailures_CollectsAllExceptions()
    {
        // Arrange
        var context = new CompensationContext();
        context.AddCompensation("First", () => throw new InvalidOperationException("Error 1"));
        context.AddCompensation("Second", () => throw new ArgumentException("Error 2"));
        context.AddCompensation("Third", () => Task.CompletedTask);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AggregateException>(
            async () => await context.CompensateAsync(stopOnFirstError: false));

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.All(exception.InnerExceptions, e => Assert.IsType<CompensationException>(e));
    }

    [Fact]
    public async Task CompensateAsync_WithFailingAsyncAction_HandlesException()
    {
        // Arrange
        var context = new CompensationContext();
        var executed = false;

        context.AddCompensation("FailingAsync", async () =>
        {
            executed = true;
            await Task.Delay(10);
            throw new TimeoutException("Async failure");
        });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AggregateException>(
            async () => await context.CompensateAsync());

        Assert.True(executed);
        Assert.NotEmpty(exception.InnerExceptions);
    }

    [Fact]
    public async Task CompensateAsync_WithLogger_LogsErrorForFailure()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<CompensationContext>>();
        var context = new CompensationContext(loggerMock.Object);
        context.AddCompensation("FailingAction", () => throw new InvalidOperationException("Test error"));

        // Act & Assert
        _ = await Assert.ThrowsAsync<AggregateException>(
            async () => await context.CompensateAsync(stopOnFirstError: false));

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to compensate")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Clear_RemovesAllActions()
    {
        // Arrange
        var context = new CompensationContext();
        context.AddCompensation("Action1", () => Task.CompletedTask);
        context.AddCompensation("Action2", () => Task.CompletedTask);

        // Act
        context.Clear();

        // Assert
        Assert.False(context.HasActions);
        Assert.Equal(0, context.ActionCount);
    }

    [Fact]
    public void Clear_WithLogger_LogsDebugMessage()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<CompensationContext>>();
        var context = new CompensationContext(loggerMock.Object);
        context.AddCompensation("Action", () => Task.CompletedTask);

        // Act
        context.Clear();

        // Assert
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Cleared all compensation")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CompensateAsync_AfterClear_DoesNothing()
    {
        // Arrange
        var context = new CompensationContext();
        var executed = false;
        context.AddCompensation("Action", () => executed = true);
        context.Clear();

        // Act
        await context.CompensateAsync();

        // Assert
        Assert.False(executed);
    }

    [Fact]
    public void DelegateCompensatingAction_WithAction_ExecutesSynchronously()
    {
        // Arrange
        var executed = false;
        var action = new DelegateCompensatingAction("Test", () => executed = true);

        // Act
        action.CompensateAsync().GetAwaiter().GetResult();

        // Assert
        Assert.True(executed);
    }

    [Fact]
    public void DelegateCompensatingAction_WithNullActionName_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new DelegateCompensatingAction(null!, () => Task.CompletedTask));

        Assert.Equal("actionName", exception.ParamName);
    }

    [Fact]
    public void DelegateCompensatingAction_WithNullFunc_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new DelegateCompensatingAction("Test", (Func<CancellationToken, Task>)null!));

        Assert.Equal("compensateFunc", exception.ParamName);
    }

    [Fact]
    public void DelegateCompensatingAction_ActionName_IsPreserved()
    {
        // Arrange
        var action = new DelegateCompensatingAction("MyCompensation", () => Task.CompletedTask);

        // Act & Assert
        Assert.Equal("MyCompensation", action.ActionName);
    }

    [Fact]
    public async Task DelegateCompensatingAction_WithAsyncFunc_ExecutesAsynchronously()
    {
        // Arrange
        var executed = false;
        var action = new DelegateCompensatingAction("Test", async () =>
        {
            await Task.Delay(10);
            executed = true;
        });

        // Act
        await action.CompensateAsync();

        // Assert
        Assert.True(executed);
    }

    [Fact]
    public async Task DelegateCompensatingAction_WithCancellationToken_PassesToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        CancellationToken? receivedToken = null;
        var action = new DelegateCompensatingAction("Test", ct =>
        {
            receivedToken = ct;
            return Task.CompletedTask;
        });

        // Act
        await action.CompensateAsync(cts.Token);

        // Assert
        Assert.NotNull(receivedToken);
        Assert.Equal(cts.Token, receivedToken.Value);
    }

    [Fact]
    public async Task DelegateCompensatingAction_WithCancelledToken_PropagatesCancellation()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var action = new DelegateCompensatingAction("Test", ct =>
        {
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException();
            return Task.CompletedTask;
        });

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await action.CompensateAsync(cts.Token));
    }

    [Fact]
    public async Task DelegateCompensatingAction_WithSyncAction_ConvertsToAsync()
    {
        // Arrange
        var executed = false;
        var action = new DelegateCompensatingAction("SyncTest", () => executed = true);

        // Act
        await action.CompensateAsync();

        // Assert
        Assert.True(executed);
    }

    [Fact]
    public void CompensationException_ContainsActionName()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new CompensationException("TestAction", innerException);

        // Assert
        Assert.Equal("TestAction", exception.ActionName);
        Assert.Contains("TestAction", exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void CompensationException_WithDifferentInnerExceptionTypes_PreservesType()
    {
        // Arrange
        var innerException = new TimeoutException("Timeout occurred");

        // Act
        var exception = new CompensationException("TimeoutAction", innerException);

        // Assert
        Assert.Equal("TimeoutAction", exception.ActionName);
        Assert.IsType<TimeoutException>(exception.InnerException);
        Assert.Contains("Timeout occurred", exception.Message);
    }

    [Fact]
    public async Task CompensateAsync_WithMixedSyncAndAsyncActions_ExecutesAllInOrder()
    {
        // Arrange
        var context = new CompensationContext();
        var executionOrder = new List<string>();

        context.AddCompensation("Sync1", () =>
        {
            executionOrder.Add("Sync1");
        });

        context.AddCompensation("Async1", async () =>
        {
            await Task.Delay(5);
            executionOrder.Add("Async1");
        });

        context.AddCompensation("Sync2", () =>
        {
            executionOrder.Add("Sync2");
        });

        // Act
        await context.CompensateAsync();

        // Assert
        Assert.Equal(new[] { "Sync2", "Async1", "Sync1" }, executionOrder);
    }

    [Fact]
    public async Task CompensateAsync_StopOnFirstError_WithMultipleActionsBeforeFailure_StopsImmediately()
    {
        // Arrange
        var context = new CompensationContext();
        var executionLog = new List<int>();

        context.AddCompensation("A1", () => executionLog.Add(1));
        context.AddCompensation("A2", () => executionLog.Add(2));
        context.AddCompensation("A3", () => throw new InvalidOperationException("Fail"));
        context.AddCompensation("A4", () => executionLog.Add(4)); // Should not execute

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AggregateException>(
            async () => await context.CompensateAsync(stopOnFirstError: true));

        // Compensation runs in reverse: A4 executes first (adds 4), A3 fails and stops
        // A2 and A1 never execute because stopOnFirstError is true
        Assert.Equal(new[] { 4 }, executionLog);
        Assert.Single(exception.InnerExceptions);
    }

    [Fact]
    public async Task CompensateAsync_ContinueOnError_WithMultipleFailures_ExecutesAllActions()
    {
        // Arrange
        var context = new CompensationContext();
        var executionLog = new List<int>();

        context.AddCompensation("A1", () =>
        {
            executionLog.Add(1);
            throw new Exception("E1");
        });

        context.AddCompensation("A2", () =>
        {
            executionLog.Add(2);
        });

        context.AddCompensation("A3", () =>
        {
            executionLog.Add(3);
            throw new Exception("E3");
        });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AggregateException>(
            async () => await context.CompensateAsync(stopOnFirstError: false));

        // All actions should execute
        Assert.Equal(new[] { 3, 2, 1 }, executionLog);
        Assert.Equal(2, exception.InnerExceptions.Count);
    }

    [Fact]
    public void ExtensionMethod_AddCompensation_WithAsyncDelegate_WorksCorrectly()
    {
        // Arrange
        var context = new CompensationContext();
        var executed = false;

        // Act
        context.AddCompensation("ExtensionAsync", async () =>
        {
            executed = true;
            await Task.Delay(1);
        });

        // Assert
        Assert.True(context.HasActions);
        Assert.Equal(1, context.ActionCount);
    }

    [Fact]
    public async Task ExtensionMethod_AddCompensation_WithAsyncDelegate_ExecutesCorrectly()
    {
        // Arrange
        var context = new CompensationContext();
        var executed = false;

        context.AddCompensation("ExtensionAsync", async () =>
        {
            executed = true;
            await Task.Delay(1);
        });

        // Act
        await context.CompensateAsync();

        // Assert
        Assert.True(executed);
    }

    [Fact]
    public void ExtensionMethod_AddCompensation_WithActionDelegate_WorksCorrectly()
    {
        // Arrange
        var context = new CompensationContext();
        var executed = false;

        // Act
        context.AddCompensation("ExtensionAction", () => executed = true);

        // Assert
        Assert.True(context.HasActions);
        Assert.Equal(1, context.ActionCount);
    }

    [Fact]
    public async Task ExtensionMethod_AddCompensation_WithActionDelegate_ExecutesCorrectly()
    {
        // Arrange
        var context = new CompensationContext();
        var executed = false;

        context.AddCompensation("ExtensionAction", () => executed = true);

        // Act
        await context.CompensateAsync();

        // Assert
        Assert.True(executed);
    }

    [Fact]
    public void ExtensionMethod_AddCompensation_WithCancellationTokenDelegate_WorksCorrectly()
    {
        // Arrange
        var context = new CompensationContext();
        CancellationToken? receivedToken = null;

        // Act
        context.AddCompensation("ExtensionWithCT", ct =>
        {
            receivedToken = ct;
            return Task.CompletedTask;
        });

        // Assert
        Assert.True(context.HasActions);
        Assert.Equal(1, context.ActionCount);
    }

    [Fact]
    public async Task ExtensionMethod_AddCompensation_WithCancellationTokenDelegate_ExecutesCorrectly()
    {
        // Arrange
        var context = new CompensationContext();
        var cts = new CancellationTokenSource();
        CancellationToken? receivedToken = null;

        context.AddCompensation("ExtensionWithCT", ct =>
        {
            receivedToken = ct;
            return Task.CompletedTask;
        });

        // Act
        await context.CompensateAsync(cancellationToken: cts.Token);

        // Assert
        Assert.NotNull(receivedToken);
        Assert.Equal(cts.Token, receivedToken.Value);
    }

    [Fact]
    public void ActionCount_Property_ReturnsCorrectValue()
    {
        // Arrange
        var context = new CompensationContext();
        Assert.Equal(0, context.ActionCount);

        // Act
        context.AddCompensation("A1", () => Task.CompletedTask);
        context.AddCompensation("A2", () => Task.CompletedTask);

        // Assert
        Assert.Equal(2, context.ActionCount);
    }

    [Fact]
    public void HasActions_Property_ReturnsCorrectValue()
    {
        // Arrange
        var context = new CompensationContext();
        Assert.False(context.HasActions);

        // Act
        context.AddCompensation("A1", () => Task.CompletedTask);

        // Assert
        Assert.True(context.HasActions);
    }
}

using HeroMessaging.Orchestration;
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
}

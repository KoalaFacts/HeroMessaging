using HeroMessaging.Orchestration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Orchestration
{
    [Trait("Category", "Unit")]
    public sealed class CompensationFrameworkTests
    {
        #region CompensationContext Tests

        [Fact]
        public void CompensationContext_Constructor_WithNullLogger_CreatesInstance()
        {
            // Act
            var context = new CompensationContext(null);

            // Assert
            Assert.NotNull(context);
            Assert.False(context.HasActions);
            Assert.Equal(0, context.ActionCount);
        }

        [Fact]
        public void CompensationContext_Constructor_WithLogger_CreatesInstance()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<CompensationContext>>();

            // Act
            var context = new CompensationContext(loggerMock.Object);

            // Assert
            Assert.NotNull(context);
            Assert.False(context.HasActions);
            Assert.Equal(0, context.ActionCount);
        }

        [Fact]
        public void AddCompensation_WithNullAction_ThrowsArgumentNullException()
        {
            // Arrange
            var context = new CompensationContext();

            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() => context.AddCompensation(null!));
            Assert.Equal("action", ex.ParamName);
        }

        [Fact]
        public void AddCompensation_WithValidAction_AddsToStack()
        {
            // Arrange
            var context = new CompensationContext();
            var action = new Mock<ICompensatingAction>();
            action.Setup(a => a.ActionName).Returns("TestAction");

            // Act
            context.AddCompensation(action.Object);

            // Assert
            Assert.True(context.HasActions);
            Assert.Equal(1, context.ActionCount);
        }

        [Fact]
        public void AddCompensation_MultipleActions_MaintainsCount()
        {
            // Arrange
            var context = new CompensationContext();
            var action1 = CreateMockAction("Action1");
            var action2 = CreateMockAction("Action2");
            var action3 = CreateMockAction("Action3");

            // Act
            context.AddCompensation(action1.Object);
            context.AddCompensation(action2.Object);
            context.AddCompensation(action3.Object);

            // Assert
            Assert.Equal(3, context.ActionCount);
            Assert.True(context.HasActions);
        }

        [Fact]
        public async Task CompensateAsync_WithNoActions_CompletesSuccessfully()
        {
            // Arrange
            var context = new CompensationContext();

            // Act & Assert - should not throw
            await context.CompensateAsync();
        }

        [Fact]
        public async Task CompensateAsync_WithSingleAction_ExecutesAction()
        {
            // Arrange
            var context = new CompensationContext();
            var action = CreateMockAction("TestAction");
            context.AddCompensation(action.Object);

            // Act
            await context.CompensateAsync();

            // Assert
            action.Verify(a => a.CompensateAsync(It.IsAny<CancellationToken>()), Times.Once);
            Assert.False(context.HasActions);
            Assert.Equal(0, context.ActionCount);
        }

        [Fact]
        public async Task CompensateAsync_WithMultipleActions_ExecutesInReverseOrder()
        {
            // Arrange
            var executionOrder = new List<string>();
            var context = new CompensationContext();

            var action1 = new DelegateCompensatingAction("Action1", () =>
            {
                executionOrder.Add("Action1");
                return Task.CompletedTask;
            });
            var action2 = new DelegateCompensatingAction("Action2", () =>
            {
                executionOrder.Add("Action2");
                return Task.CompletedTask;
            });
            var action3 = new DelegateCompensatingAction("Action3", () =>
            {
                executionOrder.Add("Action3");
                return Task.CompletedTask;
            });

            context.AddCompensation(action1);
            context.AddCompensation(action2);
            context.AddCompensation(action3);

            // Act
            await context.CompensateAsync();

            // Assert - LIFO order
            Assert.Equal(3, executionOrder.Count);
            Assert.Equal("Action3", executionOrder[0]);
            Assert.Equal("Action2", executionOrder[1]);
            Assert.Equal("Action1", executionOrder[2]);
        }

        [Fact]
        public async Task CompensateAsync_WithFailingAction_ThrowsAggregateException()
        {
            // Arrange
            var context = new CompensationContext();
            var failingAction = new DelegateCompensatingAction("FailingAction", () =>
                throw new InvalidOperationException("Compensation failed"));

            context.AddCompensation(failingAction);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<AggregateException>(
                async () => await context.CompensateAsync());

            Assert.Single(ex.InnerExceptions);
            Assert.IsType<CompensationException>(ex.InnerExceptions[0]);
            Assert.Contains("FailingAction", ex.Message);
        }

        [Fact]
        public async Task CompensateAsync_WithStopOnFirstErrorTrue_StopsOnFirstFailure()
        {
            // Arrange
            var executionOrder = new List<string>();
            var context = new CompensationContext();

            var action1 = new DelegateCompensatingAction("Action1", () =>
            {
                executionOrder.Add("Action1");
                return Task.CompletedTask;
            });
            var failingAction = new DelegateCompensatingAction("FailingAction", () =>
            {
                executionOrder.Add("FailingAction");
                throw new InvalidOperationException("Failed");
            });
            var action3 = new DelegateCompensatingAction("Action3", () =>
            {
                executionOrder.Add("Action3");
                return Task.CompletedTask;
            });

            context.AddCompensation(action1);
            context.AddCompensation(failingAction);
            context.AddCompensation(action3);

            // Act & Assert
            await Assert.ThrowsAsync<AggregateException>(
                async () => await context.CompensateAsync(stopOnFirstError: true));

            // Only Action3 and FailingAction should have executed (LIFO)
            Assert.Equal(2, executionOrder.Count);
            Assert.Equal("Action3", executionOrder[0]);
            Assert.Equal("FailingAction", executionOrder[1]);
        }

        [Fact]
        public async Task CompensateAsync_WithStopOnFirstErrorFalse_ContinuesOnFailure()
        {
            // Arrange
            var executionOrder = new List<string>();
            var context = new CompensationContext();

            var action1 = new DelegateCompensatingAction("Action1", () =>
            {
                executionOrder.Add("Action1");
                return Task.CompletedTask;
            });
            var failingAction = new DelegateCompensatingAction("FailingAction", () =>
            {
                executionOrder.Add("FailingAction");
                throw new InvalidOperationException("Failed");
            });
            var action3 = new DelegateCompensatingAction("Action3", () =>
            {
                executionOrder.Add("Action3");
                return Task.CompletedTask;
            });

            context.AddCompensation(action1);
            context.AddCompensation(failingAction);
            context.AddCompensation(action3);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<AggregateException>(
                async () => await context.CompensateAsync(stopOnFirstError: false));

            // All actions should have executed despite the failure
            Assert.Equal(3, executionOrder.Count);
            Assert.Equal("Action3", executionOrder[0]);
            Assert.Equal("FailingAction", executionOrder[1]);
            Assert.Equal("Action1", executionOrder[2]);
            Assert.Single(ex.InnerExceptions);
        }

        [Fact]
        public async Task CompensateAsync_WithMultipleFailures_CollectsAllExceptions()
        {
            // Arrange
            var context = new CompensationContext();

            var failing1 = new DelegateCompensatingAction("Failing1", () =>
                throw new InvalidOperationException("Error 1"));
            var failing2 = new DelegateCompensatingAction("Failing2", () =>
                throw new InvalidOperationException("Error 2"));

            context.AddCompensation(failing1);
            context.AddCompensation(failing2);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<AggregateException>(
                async () => await context.CompensateAsync(stopOnFirstError: false));

            Assert.Equal(2, ex.InnerExceptions.Count);
            Assert.All(ex.InnerExceptions, e => Assert.IsType<CompensationException>(e));
        }

        [Fact]
        public async Task CompensateAsync_WithCancellation_PropagatesCancellation()
        {
            // Arrange
            var context = new CompensationContext();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            var action = new DelegateCompensatingAction("Action", async (ct) =>
            {
                await Task.Delay(1000, ct); // Will throw if cancelled
            });

            context.AddCompensation(action);

            // Act & Assert
            await Assert.ThrowsAsync<AggregateException>(
                async () => await context.CompensateAsync(cancellationToken: cts.Token));
        }

        [Fact]
        public void Clear_WithActions_RemovesAllActions()
        {
            // Arrange
            var context = new CompensationContext();
            context.AddCompensation(CreateMockAction("Action1").Object);
            context.AddCompensation(CreateMockAction("Action2").Object);

            // Act
            context.Clear();

            // Assert
            Assert.False(context.HasActions);
            Assert.Equal(0, context.ActionCount);
        }

        [Fact]
        public async Task Clear_AfterClear_DoesNotExecuteActions()
        {
            // Arrange
            var context = new CompensationContext();
            var action = CreateMockAction("Action");
            context.AddCompensation(action.Object);
            context.Clear();

            // Act
            await context.CompensateAsync();

            // Assert
            action.Verify(a => a.CompensateAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        #endregion

        #region DelegateCompensatingAction Tests

        [Fact]
        public void DelegateCompensatingAction_WithNullActionName_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new DelegateCompensatingAction(null!, () => Task.CompletedTask));
            Assert.Equal("actionName", ex.ParamName);
        }

        [Fact]
        public void DelegateCompensatingAction_WithNullFuncTask_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new DelegateCompensatingAction("Action", (Func<Task>)null!));
            Assert.Equal("compensateFunc", ex.ParamName);
        }

        [Fact]
        public void DelegateCompensatingAction_WithNullFuncCancellationToken_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new DelegateCompensatingAction("Action", (Func<CancellationToken, Task>)null!));
            Assert.Equal("compensateFunc", ex.ParamName);
        }

        [Fact]
        public void DelegateCompensatingAction_WithNullAction_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new DelegateCompensatingAction("Action", (Action)null!));
            Assert.Equal("compensateAction", ex.ParamName);
        }

        [Fact]
        public async Task DelegateCompensatingAction_WithAction_ExecutesSuccessfully()
        {
            // Arrange
            var executed = false;
            var action = new DelegateCompensatingAction("Test", () => executed = true);

            // Act
            await action.CompensateAsync();

            // Assert
            Assert.True(executed);
        }

        [Fact]
        public async Task DelegateCompensatingAction_WithFuncTask_ExecutesSuccessfully()
        {
            // Arrange
            var executed = false;
            var action = new DelegateCompensatingAction("Test", () =>
            {
                executed = true;
                return Task.CompletedTask;
            });

            // Act
            await action.CompensateAsync();

            // Assert
            Assert.True(executed);
        }

        [Fact]
        public async Task DelegateCompensatingAction_WithFuncCancellationToken_PassesCancellationToken()
        {
            // Arrange
            CancellationToken? receivedToken = null;
            var cts = new CancellationTokenSource();
            var action = new DelegateCompensatingAction("Test", (ct) =>
            {
                receivedToken = ct;
                return Task.CompletedTask;
            });

            // Act
            await action.CompensateAsync(cts.Token);

            // Assert
            Assert.NotNull(receivedToken);
            Assert.Equal(cts.Token, receivedToken);
        }

        [Fact]
        public void DelegateCompensatingAction_ActionName_ReturnsCorrectName()
        {
            // Arrange
            var action = new DelegateCompensatingAction("MyAction", () => Task.CompletedTask);

            // Act & Assert
            Assert.Equal("MyAction", action.ActionName);
        }

        #endregion

        #region CompensationException Tests

        [Fact]
        public void CompensationException_Constructor_SetsPropertiesCorrectly()
        {
            // Arrange
            var innerException = new InvalidOperationException("Inner error");

            // Act
            var exception = new CompensationException("TestAction", innerException);

            // Assert
            Assert.Equal("TestAction", exception.ActionName);
            Assert.Same(innerException, exception.InnerException);
            Assert.Contains("TestAction", exception.Message);
            Assert.Contains("Inner error", exception.Message);
        }

        #endregion

        #region CompensationExtensions Tests

        [Fact]
        public void AddCompensation_WithFuncCancellationToken_CreatesAndAddsAction()
        {
            // Arrange
            var context = new CompensationContext();
            var executed = false;

            // Act
            context.AddCompensation("TestAction", (ct) =>
            {
                executed = true;
                return Task.CompletedTask;
            });

            // Assert
            Assert.True(context.HasActions);
            Assert.Equal(1, context.ActionCount);
        }

        [Fact]
        public async Task AddCompensation_WithFuncCancellationToken_ExecutesWhenCompensated()
        {
            // Arrange
            var context = new CompensationContext();
            var executed = false;

            context.AddCompensation("TestAction", (ct) =>
            {
                executed = true;
                return Task.CompletedTask;
            });

            // Act
            await context.CompensateAsync();

            // Assert
            Assert.True(executed);
        }

        [Fact]
        public void AddCompensation_WithAction_CreatesAndAddsAction()
        {
            // Arrange
            var context = new CompensationContext();
            var executed = false;

            // Act
            context.AddCompensation("TestAction", () => executed = true);

            // Assert
            Assert.True(context.HasActions);
            Assert.Equal(1, context.ActionCount);
        }

        [Fact]
        public async Task AddCompensation_WithAction_ExecutesWhenCompensated()
        {
            // Arrange
            var context = new CompensationContext();
            var executed = false;

            context.AddCompensation("TestAction", () => executed = true);

            // Act
            await context.CompensateAsync();

            // Assert
            Assert.True(executed);
        }

        [Fact]
        public void AddCompensation_WithFuncTask_CreatesAndAddsAction()
        {
            // Arrange
            var context = new CompensationContext();
            var executed = false;

            // Act
            context.AddCompensation("TestAction", () =>
            {
                executed = true;
                return Task.CompletedTask;
            });

            // Assert
            Assert.True(context.HasActions);
            Assert.Equal(1, context.ActionCount);
        }

        [Fact]
        public async Task AddCompensation_WithFuncTask_ExecutesWhenCompensated()
        {
            // Arrange
            var context = new CompensationContext();
            var executed = false;

            context.AddCompensation("TestAction", () =>
            {
                executed = true;
                return Task.CompletedTask;
            });

            // Act
            await context.CompensateAsync();

            // Assert
            Assert.True(executed);
        }

        #endregion

        #region Helper Methods

        private Mock<ICompensatingAction> CreateMockAction(string actionName)
        {
            var mock = new Mock<ICompensatingAction>();
            mock.Setup(a => a.ActionName).Returns(actionName);
            mock.Setup(a => a.CompensateAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            return mock;
        }

        #endregion
    }
}

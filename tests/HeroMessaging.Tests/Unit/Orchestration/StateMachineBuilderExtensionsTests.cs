using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Tests.Unit.Orchestration;

/// <summary>
/// Unit tests for StateMachineBuilderExtensions.
/// </summary>
[Trait("Category", "Unit")]
public class StateMachineBuilderExtensionsTests
{
    #region Test Helpers

    private class TestSaga : ISaga
    {
        public Guid CorrelationId { get; set; } = Guid.NewGuid();
        public string CurrentState { get; set; } = "Initial";
        public bool IsCompleted { get; set; }
        public int Version { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string? Data { get; set; }
        public int Counter { get; set; }
    }

    private record TestEvent(string Data) : IEvent
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }

        // For saga correlation operations (Guid)
        public Guid SagaCorrelationId { get; init; } = Guid.NewGuid();
    }

    private record AnotherEvent() : IEvent
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }

        // For saga correlation operations (Guid)
        public Guid SagaCorrelationId { get; init; } = Guid.NewGuid();
    }

    #endregion

    #region InState Tests

    [Fact]
    public void InState_CreatesStateWithCorrectName()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var testEvent = new Event<TestEvent>("TestEvent");

        // Set initial state first
        builder.Initially().When(testEvent).TransitionTo(new State("Pending"));

        // Act
        var configurator = builder.InState("Processing").When(testEvent).TransitionTo(new State("Completed"));

        // Assert
        var definition = builder.Build();
        Assert.True(definition.Transitions.ContainsKey("Processing"));
    }

    [Fact]
    public void InState_AllowsChainingWhenConfigurator()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var testEvent = new Event<TestEvent>("TestEvent");

        // Set initial state first
        builder.Initially().When(testEvent).TransitionTo(new State("Pending"));

        // Act - Chain methods
        builder.InState("Pending")
               .When(testEvent)
               .Then(ctx => { ctx.Instance.Data = ctx.Data.Data; })
               .TransitionTo(new State("Completed"));

        // Assert
        var definition = builder.Build();
        Assert.True(definition.Transitions.ContainsKey("Pending"));
    }

    #endregion

    #region MarkAsCompleted Tests

    [Fact]
    public async Task MarkAsCompleted_SetsIsCompletedToTrue()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var testEvent = new Event<TestEvent>("TestEvent");

        builder.Initially()
               .When(testEvent)
               .TransitionTo(new State("Completed"))
               .MarkAsCompleted();

        var definition = builder.Build();
        var saga = new TestSaga();
        var eventData = new TestEvent("test");

        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        var serviceProvider = services.BuildServiceProvider();

        var context = new StateContext<TestSaga, TestEvent>(
            saga,
            eventData,
            serviceProvider,
            new CompensationContext());

        // Act - Execute the transition action
        var transitions = definition.Transitions["Initial"];
        var transition = transitions[0] as StateTransition<TestSaga, TestEvent>;
        if (transition?.Action != null)
        {
            await transition.Action(context);
        }

        // Assert
        Assert.True(saga.IsCompleted);
    }

    #endregion

    #region ThenAll Tests

    [Fact]
    public async Task ThenAll_ExecutesAllActionsInSequence()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var testEvent = new Event<TestEvent>("TestEvent");
        var executionOrder = new List<int>();

        builder.Initially()
               .When(testEvent)
               .ThenAll(
                   async ctx => { executionOrder.Add(1); await Task.CompletedTask; },
                   async ctx => { executionOrder.Add(2); await Task.CompletedTask; },
                   async ctx => { executionOrder.Add(3); await Task.CompletedTask; }
               )
               .TransitionTo(new State("Processing"));

        var definition = builder.Build();
        var saga = new TestSaga();
        var eventData = new TestEvent("test");

        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var context = new StateContext<TestSaga, TestEvent>(
            saga,
            eventData,
            serviceProvider,
            new CompensationContext());

        // Act
        var transitions = definition.Transitions["Initial"];
        var transition = transitions[0] as StateTransition<TestSaga, TestEvent>;
        if (transition?.Action != null)
        {
            await transition.Action(context);
        }

        // Assert
        Assert.Equal(new[] { 1, 2, 3 }, executionOrder);
    }

    #endregion

    #region CompensateWith Tests

    [Fact]
    public async Task CompensateWith_AddsCompensationAction()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var testEvent = new Event<TestEvent>("TestEvent");
        var compensationExecuted = false;

        builder.Initially()
               .When(testEvent)
               .CompensateWith("TestCompensation", async ct =>
               {
                   compensationExecuted = true;
                   await Task.CompletedTask;
               })
               .TransitionTo(new State("Processing"));

        var definition = builder.Build();
        var saga = new TestSaga();
        var eventData = new TestEvent("test");

        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var compensationContext = new CompensationContext();
        var context = new StateContext<TestSaga, TestEvent>(
            saga,
            eventData,
            serviceProvider,
            compensationContext);

        // Act
        var transitions = definition.Transitions["Initial"];
        var transition = transitions[0] as StateTransition<TestSaga, TestEvent>;
        if (transition?.Action != null)
        {
            await transition.Action(context);
        }

        // Execute compensation
        await compensationContext.CompensateAsync(cancellationToken: CancellationToken.None);

        // Assert
        Assert.True(compensationExecuted);
    }

    #endregion

    #region SetProperty Tests

    [Fact]
    public async Task SetProperty_SetsSagaProperty()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var testEvent = new Event<TestEvent>("TestEvent");

        builder.Initially()
               .When(testEvent)
               .SetProperty(
                   (saga, value) => saga.Data = value,
                   ctx => ctx.Data.Data
               )
               .TransitionTo(new State("Processing"));

        var definition = builder.Build();
        var saga = new TestSaga();
        var eventData = new TestEvent("TestValue");

        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var context = new StateContext<TestSaga, TestEvent>(
            saga,
            eventData,
            serviceProvider,
            new CompensationContext());

        // Act
        var transitions = definition.Transitions["Initial"];
        var transition = transitions[0] as StateTransition<TestSaga, TestEvent>;
        if (transition?.Action != null)
        {
            await transition.Action(context);
        }

        // Assert
        Assert.Equal("TestValue", saga.Data);
    }

    #endregion

    #region CopyFrom Tests

    [Fact]
    public async Task CopyFrom_CopiesDataFromEventToSaga()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var testEvent = new Event<TestEvent>("TestEvent");

        builder.Initially()
               .When(testEvent)
               .CopyFrom((saga, evt) =>
               {
                   saga.Data = evt.Data;
                   saga.CorrelationId = evt.SagaCorrelationId;
               })
               .TransitionTo(new State("Processing"));

        var definition = builder.Build();
        var saga = new TestSaga();
        var correlationId = Guid.NewGuid();
        var eventData = new TestEvent("CopiedData") { SagaCorrelationId = correlationId };

        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var context = new StateContext<TestSaga, TestEvent>(
            saga,
            eventData,
            serviceProvider,
            new CompensationContext());

        // Act
        var transitions = definition.Transitions["Initial"];
        var transition = transitions[0] as StateTransition<TestSaga, TestEvent>;
        if (transition?.Action != null)
        {
            await transition.Action(context);
        }

        // Assert
        Assert.Equal("CopiedData", saga.Data);
        Assert.Equal(correlationId, saga.CorrelationId);
    }

    #endregion

    #region ConditionalWhenConfigurator If Tests

    [Fact]
    public async Task If_WhenConditionTrue_ExecutesThenAction()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var testEvent = new Event<TestEvent>("TestEvent");
        var thenExecuted = false;
        var elseExecuted = false;

        builder.Initially()
               .When(testEvent)
               .If(ctx => ctx.Data.Data == "execute")
               .Then(ctx => { thenExecuted = true; })
               .Else()
               .Then(ctx => { elseExecuted = true; })
               .EndIf()
               .TransitionTo(new State("Processing"));

        var definition = builder.Build();
        var saga = new TestSaga();
        var eventData = new TestEvent("execute");

        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var context = new StateContext<TestSaga, TestEvent>(
            saga,
            eventData,
            serviceProvider,
            new CompensationContext());

        // Act
        var transitions = definition.Transitions["Initial"];
        var transition = transitions[0] as StateTransition<TestSaga, TestEvent>;
        if (transition?.Action != null)
        {
            await transition.Action(context);
        }

        // Assert
        Assert.True(thenExecuted);
        Assert.False(elseExecuted);
    }

    [Fact]
    public async Task If_WhenConditionFalse_ExecutesElseAction()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var testEvent = new Event<TestEvent>("TestEvent");
        var thenExecuted = false;
        var elseExecuted = false;

        builder.Initially()
               .When(testEvent)
               .If(ctx => ctx.Data.Data == "execute")
               .Then(ctx => { thenExecuted = true; })
               .Else()
               .Then(ctx => { elseExecuted = true; })
               .EndIf()
               .TransitionTo(new State("Processing"));

        var definition = builder.Build();
        var saga = new TestSaga();
        var eventData = new TestEvent("other");

        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var context = new StateContext<TestSaga, TestEvent>(
            saga,
            eventData,
            serviceProvider,
            new CompensationContext());

        // Act
        var transitions = definition.Transitions["Initial"];
        var transition = transitions[0] as StateTransition<TestSaga, TestEvent>;
        if (transition?.Action != null)
        {
            await transition.Action(context);
        }

        // Assert
        Assert.False(thenExecuted);
        Assert.True(elseExecuted);
    }

    [Fact]
    public async Task If_EndIfWithoutElse_WorksCorrectly()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var testEvent = new Event<TestEvent>("TestEvent");
        var actionExecuted = false;

        builder.Initially()
               .When(testEvent)
               .If(ctx => ctx.Data.Data == "execute")
               .Then(ctx => { actionExecuted = true; })
               .EndIf()
               .TransitionTo(new State("Processing"));

        var definition = builder.Build();
        var saga = new TestSaga();
        var eventData = new TestEvent("execute");

        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var context = new StateContext<TestSaga, TestEvent>(
            saga,
            eventData,
            serviceProvider,
            new CompensationContext());

        // Act
        var transitions = definition.Transitions["Initial"];
        var transition = transitions[0] as StateTransition<TestSaga, TestEvent>;
        if (transition?.Action != null)
        {
            await transition.Action(context);
        }

        // Assert
        Assert.True(actionExecuted);
    }

    #endregion

    #region ConditionalWhenConfigurator TransitionTo Tests

    [Fact]
    public async Task If_TransitionTo_WithConditionTrue_SetsState()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var testEvent = new Event<TestEvent>("TestEvent");

        builder.Initially()
               .When(testEvent)
               .If(ctx => ctx.Data.Data == "approve")
               .TransitionTo("Approved")
               .Else()
               .TransitionTo("Rejected")
               .EndIf();

        var definition = builder.Build();
        var saga = new TestSaga();
        var eventData = new TestEvent("approve");

        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var context = new StateContext<TestSaga, TestEvent>(
            saga,
            eventData,
            serviceProvider,
            new CompensationContext());

        // Act
        var transitions = definition.Transitions["Initial"];
        var transition = transitions[0] as StateTransition<TestSaga, TestEvent>;
        if (transition?.Action != null)
        {
            await transition.Action(context);
        }

        // Assert
        Assert.Equal("Approved", saga.CurrentState);
    }

    [Fact]
    public async Task If_TransitionTo_WithConditionFalse_SetsElseState()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var testEvent = new Event<TestEvent>("TestEvent");

        builder.Initially()
               .When(testEvent)
               .If(ctx => ctx.Data.Data == "approve")
               .TransitionTo("Approved")
               .Else()
               .TransitionTo("Rejected")
               .EndIf();

        var definition = builder.Build();
        var saga = new TestSaga();
        var eventData = new TestEvent("deny");

        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var context = new StateContext<TestSaga, TestEvent>(
            saga,
            eventData,
            serviceProvider,
            new CompensationContext());

        // Act
        var transitions = definition.Transitions["Initial"];
        var transition = transitions[0] as StateTransition<TestSaga, TestEvent>;
        if (transition?.Action != null)
        {
            await transition.Action(context);
        }

        // Assert
        Assert.Equal("Rejected", saga.CurrentState);
    }

    #endregion

    #region ElseConfigurator Tests

    [Fact]
    public async Task Else_TransitionTo_WithState_SetsState()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var testEvent = new Event<TestEvent>("TestEvent");
        var elseState = new State("ElseState");

        builder.Initially()
               .When(testEvent)
               .If(ctx => false)  // Always false
               .Then(ctx => { })
               .Else()
               .TransitionTo(elseState)
               .EndIf();

        var definition = builder.Build();
        var saga = new TestSaga();
        var eventData = new TestEvent("test");

        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var context = new StateContext<TestSaga, TestEvent>(
            saga,
            eventData,
            serviceProvider,
            new CompensationContext());

        // Act
        var transitions = definition.Transitions["Initial"];
        var transition = transitions[0] as StateTransition<TestSaga, TestEvent>;
        if (transition?.Action != null)
        {
            await transition.Action(context);
        }

        // Assert
        Assert.Equal("ElseState", saga.CurrentState);
    }

    [Fact]
    public async Task Else_Then_WithAsyncAction_ExecutesAction()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var testEvent = new Event<TestEvent>("TestEvent");
        var executed = false;

        builder.Initially()
               .When(testEvent)
               .If(ctx => false)  // Always false
               .Then(ctx => { })
               .Else()
               .Then(async ctx => { executed = true; await Task.CompletedTask; })
               .EndIf()
               .TransitionTo(new State("Processing"));

        var definition = builder.Build();
        var saga = new TestSaga();
        var eventData = new TestEvent("test");

        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var context = new StateContext<TestSaga, TestEvent>(
            saga,
            eventData,
            serviceProvider,
            new CompensationContext());

        // Act
        var transitions = definition.Transitions["Initial"];
        var transition = transitions[0] as StateTransition<TestSaga, TestEvent>;
        if (transition?.Action != null)
        {
            await transition.Action(context);
        }

        // Assert
        Assert.True(executed);
    }

    #endregion

    #region Method Chaining Tests

    [Fact]
    public void AllExtensions_SupportFluentChaining()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var testEvent = new Event<TestEvent>("TestEvent");

        // Act & Assert - Should not throw
        builder.Initially()
               .When(testEvent)
               .SetProperty((s, v) => s.Data = v, ctx => ctx.Data.Data)
               .CopyFrom((s, e) => s.CorrelationId = e.SagaCorrelationId)
               .ThenAll(
                   async ctx => await Task.CompletedTask,
                   async ctx => await Task.CompletedTask
               )
               .CompensateWith("Comp1", async ct => await Task.CompletedTask)
               .TransitionTo(new State("Processing"));

        var definition = builder.Build();
        Assert.NotNull(definition);
        Assert.Contains("Initial", definition.Transitions.Keys);
    }

    [Fact]
    public void ConditionalExtensions_SupportFluentChaining()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var testEvent = new Event<TestEvent>("TestEvent");

        // Act & Assert - Should not throw
        builder.Initially()
               .When(testEvent)
               .If(ctx => ctx.Data.Data.Length > 5)
               .Then(ctx => ctx.Instance.Counter++)
               .TransitionTo("LongData")
               .Else()
               .Then(ctx => ctx.Instance.Counter--)
               .TransitionTo("ShortData")
               .EndIf();

        var definition = builder.Build();
        Assert.NotNull(definition);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task ComplexStateMachine_WithMultipleExtensions_WorksCorrectly()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var orderCreated = new Event<TestEvent>("TestEvent");
        var orderApproved = new Event<AnotherEvent>("AnotherEvent");
        var executionLog = new List<string>();

        // Build complex state machine
        builder.Initially()
               .When(orderCreated)
               .CopyFrom((saga, evt) => saga.Data = evt.Data)
               .SetProperty((saga, val) => saga.Counter = val, ctx => 1)
               .CompensateWith("UndoOrder", async ct =>
               {
                   executionLog.Add("Compensation");
                   await Task.CompletedTask;
               })
               .TransitionTo(new State("Pending"));

        builder.InState("Pending")
               .When(orderApproved)
               .If(ctx => ctx.Instance.Data?.Length > 3)
               .Then(ctx =>
               {
                   executionLog.Add("Approved");
                   ctx.Instance.Counter++;
               })
               .TransitionTo("Approved")
               .Else()
               .Then(ctx =>
               {
                   executionLog.Add("Rejected");
               })
               .TransitionTo("Rejected")
               .EndIf();

        var definition = builder.Build();

        // Process first event
        var saga = new TestSaga();
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var context1 = new StateContext<TestSaga, TestEvent>(
            saga,
            new TestEvent("TestData"),
            serviceProvider,
            new CompensationContext());

        var initialTransitions = definition.Transitions["Initial"];
        var initialTransition = initialTransitions[0] as StateTransition<TestSaga, TestEvent>;
        if (initialTransition?.Action != null)
        {
            await initialTransition.Action(context1);
        }

        // Assert first event processed
        Assert.Equal("TestData", saga.Data);
        Assert.Equal(1, saga.Counter);

        // Process second event
        saga.CurrentState = "Pending";  // Simulate state transition

        var context2 = new StateContext<TestSaga, AnotherEvent>(
            saga,
            new AnotherEvent(),
            serviceProvider,
            new CompensationContext());

        var pendingTransitions = definition.Transitions["Pending"];
        var pendingTransition = pendingTransitions[0] as StateTransition<TestSaga, AnotherEvent>;
        if (pendingTransition?.Action != null)
        {
            await pendingTransition.Action(context2);
        }

        // Assert second event processed with condition true (data length > 3)
        Assert.Contains("Approved", executionLog);
        Assert.Equal("Approved", saga.CurrentState);
        Assert.Equal(2, saga.Counter);
    }

    #endregion
}

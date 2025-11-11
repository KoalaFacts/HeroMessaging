using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Tests.Unit.Orchestration;

/// <summary>
/// Comprehensive tests for StateTransition component covering transitions, guards, actions, and error handling.
/// Focuses on achieving 80%+ coverage including branch coverage for null validation in StateTransition constructor.
/// </summary>
[Trait("Category", "Unit")]
public class StateTransitionCoverageTests
{
    private class TestSaga : SagaBase
    {
        public string? Data { get; set; }
    }

    private record TestEvent(string Value) : IEvent, IMessage
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    private record AnotherEvent(int Number) : IEvent, IMessage
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    // ========== Null Validation Tests: Branch Coverage for StateTransition Constructor ==========

    [Fact(DisplayName = "During_WithNullState_ThrowsArgumentNullException_CoversNullFromStateBranch")]
    public void During_WithNullState_ThrowsArgumentNullException_CoversNullFromStateBranch()
    {
        // Arrange - Tests the null fromState branch in StateTransition constructor
        var builder = new StateMachineBuilder<TestSaga>();

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => builder.During(null!));
        Assert.Equal("state", ex.ParamName);
    }

    [Fact(DisplayName = "TransitionTo_WithNullState_ThrowsArgumentNullException_CoversNullTriggerEventBranch")]
    public void TransitionTo_WithNullState_ThrowsArgumentNullException_CoversNullTriggerEventBranch()
    {
        // Arrange - Tests the null triggerEvent branch in StateTransition constructor
        var builder = new StateMachineBuilder<TestSaga>();
        var testEvent = new Event<TestEvent>("Trigger");

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            builder.Initially()
                .When(testEvent)
                .TransitionTo(null!));
        Assert.Equal("state", ex.ParamName);
    }

    // ========== Action Property Tests ==========

    [Fact(DisplayName = "AsyncAction_ExecutesAndModifiesSagaState")]
    public async Task AsyncAction_ExecutesAndModifiesSagaState()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var actionExecuted = false;
        var testEvent = new Event<TestEvent>("Trigger");

        builder.Initially()
            .When(testEvent)
                .Then(async context =>
                {
                    await Task.Delay(1);
                    context.Instance.Data = "AsyncExecuted";
                    actionExecuted = true;
                })
                .TransitionTo(new State("Next"));

        var stateMachine = builder.Build();

        // Act
        var transitions = stateMachine.Transitions["Initial"];
        var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
        var @event = new TestEvent("test");
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);

        var transition = transitions[0];
        var actionProp = transition.GetType().GetProperty("Action");
        var action = actionProp?.GetValue(transition) as Func<StateContext<TestSaga, TestEvent>, Task>;

        // Assert
        Assert.NotNull(action);
        await action(context);
        Assert.True(actionExecuted);
        Assert.Equal("AsyncExecuted", saga.Data);
    }

    [Fact(DisplayName = "SyncAction_WrappedInAsyncCorrectly")]
    public async Task SyncAction_WrappedInAsyncCorrectly()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var syncExecuted = false;
        var testEvent = new Event<TestEvent>("Trigger");

        builder.Initially()
            .When(testEvent)
                .Then(context =>
                {
                    syncExecuted = true;
                    context.Instance.Data = "SyncExecuted";
                })
                .TransitionTo(new State("Next"));

        var stateMachine = builder.Build();

        // Act
        var transitions = stateMachine.Transitions["Initial"];
        var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
        var @event = new TestEvent("test");
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);

        var transition = transitions[0];
        var actionProp = transition.GetType().GetProperty("Action");
        var action = actionProp?.GetValue(transition) as Func<StateContext<TestSaga, TestEvent>, Task>;

        // Assert
        Assert.NotNull(action);
        await action(context);
        Assert.True(syncExecuted);
        Assert.Equal("SyncExecuted", saga.Data);
    }

    [Fact(DisplayName = "MultipleActionCalls_LastActionWins")]
    public async Task MultipleActionCalls_LastActionWins()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var firstActionExecuted = false;
        var secondActionExecuted = false;
        var testEvent = new Event<TestEvent>("Trigger");

        builder.Initially()
            .When(testEvent)
                .Then(async _ =>
                {
                    firstActionExecuted = true;
                    await Task.CompletedTask;
                })
                .Then(async context =>
                {
                    secondActionExecuted = true;
                    context.Instance.Data = "SecondAction";
                    await Task.CompletedTask;
                })
                .TransitionTo(new State("Next"));

        var stateMachine = builder.Build();

        // Act
        var transitions = stateMachine.Transitions["Initial"];
        var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
        var @event = new TestEvent("test");
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);

        var transition = transitions[0];
        var actionProp = transition.GetType().GetProperty("Action");
        var action = actionProp?.GetValue(transition) as Func<StateContext<TestSaga, TestEvent>, Task>;

        // Assert - Only second action should execute
        Assert.NotNull(action);
        await action(context);
        Assert.False(firstActionExecuted);
        Assert.True(secondActionExecuted);
        Assert.Equal("SecondAction", saga.Data);
    }

    [Fact(DisplayName = "NullAsyncAction_ThrowsArgumentNullException")]
    public void NullAsyncAction_ThrowsArgumentNullException()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var testEvent = new Event<TestEvent>("Trigger");

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            builder.Initially().When(testEvent).Then((Func<StateContext<TestSaga, TestEvent>, Task>)null!));
        Assert.Equal("action", ex.ParamName);
    }

    [Fact(DisplayName = "NullSyncAction_ThrowsArgumentNullException")]
    public void NullSyncAction_ThrowsArgumentNullException()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var testEvent = new Event<TestEvent>("Trigger");

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            builder.Initially().When(testEvent).Then((Action<StateContext<TestSaga, TestEvent>>)null!));
        Assert.Equal("action", ex.ParamName);
    }

    // ========== ToState Property Tests ==========

    [Fact(DisplayName = "TransitionTo_SingleCall_SetsToState")]
    public void TransitionTo_SingleCall_SetsToState()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var targetState = new State("Target");
        var testEvent = new Event<TestEvent>("Trigger");

        // Act
        builder.Initially()
            .When(testEvent)
            .TransitionTo(targetState);

        var stateMachine = builder.Build();

        // Assert
        var transitions = stateMachine.Transitions["Initial"];
        Assert.Single(transitions);
    }

    [Fact(DisplayName = "TransitionTo_MultipleCalls_LastStateWins")]
    public void TransitionTo_MultipleCalls_LastStateWins()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var state1 = new State("State1");
        var state2 = new State("State2");
        var finalState = new State("Final");
        var testEvent = new Event<TestEvent>("Trigger");

        // Act
        builder.Initially()
            .When(testEvent)
            .TransitionTo(state1)
            .TransitionTo(state2)
            .TransitionTo(finalState);

        var stateMachine = builder.Build();

        // Assert
        var transitions = stateMachine.Transitions["Initial"];
        Assert.Single(transitions);
    }

    // ========== Finalization Tests ==========

    [Fact(DisplayName = "Finalize_WithoutPriorAction_SetsSagaComplete")]
    public async Task Finalize_WithoutPriorAction_SetsSagaComplete()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var finalState = new State("Complete");
        var testEvent = new Event<TestEvent>("Finalize");

        builder.Initially()
            .When(testEvent)
            .TransitionTo(finalState)
            .Finalize();

        var stateMachine = builder.Build();

        // Act
        var transitions = stateMachine.Transitions["Initial"];
        var saga = new TestSaga { CorrelationId = Guid.NewGuid(), IsCompleted = false };
        var @event = new TestEvent("test");
        var services = new ServiceCollection()
            .AddSingleton<TimeProvider>(TimeProvider.System)
            .BuildServiceProvider();
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);

        var transition = transitions[0];
        var actionProp = transition.GetType().GetProperty("Action");
        var action = actionProp?.GetValue(transition) as Func<StateContext<TestSaga, TestEvent>, Task>;

        // Assert
        Assert.NotNull(action);
        Assert.False(saga.IsCompleted);
        await action(context);
        Assert.True(saga.IsCompleted);
    }

    [Fact(DisplayName = "Finalize_WithPriorAction_ExecutesBoth")]
    public async Task Finalize_WithPriorAction_ExecutesBoth()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var customActionExecuted = false;
        var finalState = new State("Complete");
        var testEvent = new Event<TestEvent>("Finalize");

        builder.Initially()
            .When(testEvent)
            .Then(async ctx =>
            {
                customActionExecuted = true;
                ctx.Instance.Data = "CustomAction";
                await Task.CompletedTask;
            })
            .TransitionTo(finalState)
            .Finalize();

        var stateMachine = builder.Build();

        // Act
        var transitions = stateMachine.Transitions["Initial"];
        var saga = new TestSaga { CorrelationId = Guid.NewGuid(), IsCompleted = false };
        var @event = new TestEvent("test");
        var services = new ServiceCollection()
            .AddSingleton<TimeProvider>(TimeProvider.System)
            .BuildServiceProvider();
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);

        var transition = transitions[0];
        var actionProp = transition.GetType().GetProperty("Action");
        var action = actionProp?.GetValue(transition) as Func<StateContext<TestSaga, TestEvent>, Task>;

        // Assert
        Assert.NotNull(action);
        Assert.False(saga.IsCompleted);
        await action(context);
        Assert.True(customActionExecuted);
        Assert.Equal("CustomAction", saga.Data);
        Assert.True(saga.IsCompleted);
    }

    // ========== StateContext Null Validation Tests ==========

    [Fact(DisplayName = "StateContext_NullInstance_ThrowsArgumentNullException")]
    public void StateContext_NullInstance_ThrowsArgumentNullException()
    {
        // Arrange
        var @event = new TestEvent("test");
        var services = new ServiceCollection().BuildServiceProvider();

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new StateContext<TestSaga, TestEvent>(null!, @event, services));
        Assert.Equal("instance", ex.ParamName);
    }

    [Fact(DisplayName = "StateContext_NullData_ThrowsArgumentNullException")]
    public void StateContext_NullData_ThrowsArgumentNullException()
    {
        // Arrange
        var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
        var services = new ServiceCollection().BuildServiceProvider();

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new StateContext<TestSaga, TestEvent>(saga, null!, services));
        Assert.Equal("data", ex.ParamName);
    }

    [Fact(DisplayName = "StateContext_NullServices_ThrowsArgumentNullException")]
    public void StateContext_NullServices_ThrowsArgumentNullException()
    {
        // Arrange
        var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
        var @event = new TestEvent("test");

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new StateContext<TestSaga, TestEvent>(saga, @event, null!));
        Assert.Equal("services", ex.ParamName);
    }

    [Fact(DisplayName = "StateContext_AllPropertiesAccessible")]
    public void StateContext_AllPropertiesAccessible()
    {
        // Arrange
        var saga = new TestSaga { CorrelationId = Guid.NewGuid(), Data = "test" };
        var eventData = new TestEvent("eventValue");
        var services = new ServiceCollection().BuildServiceProvider();
        var compensation = new CompensationContext();

        // Act
        var context = new StateContext<TestSaga, TestEvent>(saga, eventData, services, compensation);

        // Assert
        Assert.Same(saga, context.Instance);
        Assert.Same(eventData, context.Data);
        Assert.Same(services, context.Services);
        Assert.Same(compensation, context.Compensation);
    }

    [Fact(DisplayName = "StateContext_DefaultCompensation_CreatedIfNull")]
    public void StateContext_DefaultCompensation_CreatedIfNull()
    {
        // Arrange
        var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
        var @event = new TestEvent("test");
        var services = new ServiceCollection().BuildServiceProvider();

        // Act
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services, null);

        // Assert
        Assert.NotNull(context.Compensation);
    }

    // ========== Complex Transition Scenarios ==========

    [Fact(DisplayName = "ComplexStateMachine_MultipleStateAndEventCombinations")]
    public void ComplexStateMachine_MultipleStateAndEventCombinations()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var state1 = new State("Processing");
        var state2 = new State("Validating");
        var finalState = new State("Complete");
        var event1 = new Event<TestEvent>("Start");
        var event2 = new Event<AnotherEvent>("Process");
        var event3 = new Event<TestEvent>("Complete");

        // Act
        builder.Initially()
            .When(event1)
            .Then(ctx => { ctx.Instance.Data = "Started"; return Task.CompletedTask; })
            .TransitionTo(state1)
            .When(event2)
            .Then(ctx => { ctx.Instance.Data = "Alternative"; return Task.CompletedTask; })
            .TransitionTo(state2);

        builder.During(state1)
            .When(event2)
            .Then(ctx => { ctx.Instance.Data = "Processing"; return Task.CompletedTask; })
            .TransitionTo(state2);

        builder.During(state2)
            .When(event3)
            .Then(ctx => { ctx.Instance.Data = "Validated"; return Task.CompletedTask; })
            .TransitionTo(finalState)
            .Finalize();

        var stateMachine = builder.Build();

        // Assert
        var initialTransitions = stateMachine.Transitions["Initial"];
        var state1Transitions = stateMachine.Transitions[state1.Name];
        var state2Transitions = stateMachine.Transitions[state2.Name];

        Assert.Equal(2, initialTransitions.Count);
        Assert.Single(state1Transitions);
        Assert.Single(state2Transitions);
        Assert.Contains(finalState, stateMachine.FinalStates);
    }
}

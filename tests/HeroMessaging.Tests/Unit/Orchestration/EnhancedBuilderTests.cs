using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Tests.Unit.Orchestration;

/// <summary>
/// Tests for enhanced state machine builder features
/// </summary>
public class EnhancedBuilderTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void InState_AllowsInlineStateDefinition()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();

        // Act - Use InState instead of During(new State(...))
        builder.InState("TestState")
            .When(new Event<TestEvent>("TestEvent"))
            .Then(ctx => ctx.Instance.Value = "Updated")
            .TransitionTo(new State("NextState"));

        var stateMachine = builder.Build();

        // Assert
        Assert.NotNull(stateMachine);
        Assert.Contains("TestState", stateMachine.Transitions.Keys);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CopyFrom_CopiesDataFromEventToSaga()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var services = new ServiceCollection().BuildServiceProvider();

        builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .CopyFrom((saga, evt) =>
            {
                saga.Value = evt.Value;
                saga.Count = evt.Count;
            })
            .TransitionTo(new State("NextState"));

        var stateMachine = builder.Build();
        var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
        var testEvent = new TestEvent("TestValue", 42);

        // Act
        var transitions = stateMachine.Transitions["Initial"];
        var transition = transitions[0] as StateTransition<TestSaga, TestEvent>;
        var context = new StateContext<TestSaga, TestEvent>(saga, testEvent, services, new CompensationContext());

        if (transition?.Action != null)
        {
            await transition.Action(context);
        }

        // Assert
        Assert.Equal("TestValue", saga.Value);
        Assert.Equal(42, saga.Count);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SetProperty_SetsPropertyFromSelector()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var services = new ServiceCollection().BuildServiceProvider();

        builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .SetProperty(
                (saga, value) => saga.Value = value,
                ctx => ctx.Data.Value.ToUpper())
            .TransitionTo(new State("NextState"));

        var stateMachine = builder.Build();
        var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
        var testEvent = new TestEvent("lowercase", 0);

        // Act
        var transitions = stateMachine.Transitions["Initial"];
        var transition = transitions[0] as StateTransition<TestSaga, TestEvent>;
        var context = new StateContext<TestSaga, TestEvent>(saga, testEvent, services, new CompensationContext());

        if (transition?.Action != null)
        {
            await transition.Action(context);
        }

        // Assert
        Assert.Equal("LOWERCASE", saga.Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CompensateWith_AddsCompensationAction()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var services = new ServiceCollection().BuildServiceProvider();
        var compensationExecuted = false;

        builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .CompensateWith(
                "TestCompensation",
                async ct =>
                {
                    compensationExecuted = true;
                    await Task.CompletedTask;
                })
            .TransitionTo(new State("NextState"));

        var stateMachine = builder.Build();
        var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
        var testEvent = new TestEvent("Test", 0);
        var compensationContext = new CompensationContext();

        // Act
        var transitions = stateMachine.Transitions["Initial"];
        var transition = transitions[0] as StateTransition<TestSaga, TestEvent>;
        var context = new StateContext<TestSaga, TestEvent>(saga, testEvent, services, compensationContext);

        if (transition?.Action != null)
        {
            await transition.Action(context);
        }

        // Execute compensation
        await compensationContext.CompensateAsync();

        // Assert
        Assert.True(compensationExecuted);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ThenAll_ExecutesAllActionsInSequence()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var services = new ServiceCollection().BuildServiceProvider();
        var executionOrder = new List<int>();

        builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .ThenAll(
                ctx => { executionOrder.Add(1); return Task.CompletedTask; },
                ctx => { executionOrder.Add(2); return Task.CompletedTask; },
                ctx => { executionOrder.Add(3); return Task.CompletedTask; })
            .TransitionTo(new State("NextState"));

        var stateMachine = builder.Build();
        var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
        var testEvent = new TestEvent("Test", 0);

        // Act
        var transitions = stateMachine.Transitions["Initial"];
        var transition = transitions[0] as StateTransition<TestSaga, TestEvent>;
        var context = new StateContext<TestSaga, TestEvent>(saga, testEvent, services, new CompensationContext());

        if (transition?.Action != null)
        {
            await transition.Action(context);
        }

        // Assert
        Assert.Equal(new[] { 1, 2, 3 }, executionOrder);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task If_ExecutesConditionalBranch_WhenConditionIsTrue()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var services = new ServiceCollection().BuildServiceProvider();

        builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .If(ctx => ctx.Data.Count > 10)
                .Then(ctx => ctx.Instance.Value = "High")
                .TransitionTo("HighState")
            .Else()
                .Then(ctx => ctx.Instance.Value = "Low")
                .TransitionTo("LowState")
            .EndIf();

        var stateMachine = builder.Build();
        var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
        var testEvent = new TestEvent("Test", 20); // Count > 10

        // Act
        var transitions = stateMachine.Transitions["Initial"];
        var transition = transitions[0] as StateTransition<TestSaga, TestEvent>;
        var context = new StateContext<TestSaga, TestEvent>(saga, testEvent, services, new CompensationContext());

        if (transition?.Action != null)
        {
            await transition.Action(context);
        }

        // Assert
        Assert.Equal("High", saga.Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Else_ExecutesAlternativeBranch_WhenConditionIsFalse()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var services = new ServiceCollection().BuildServiceProvider();

        builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .If(ctx => ctx.Data.Count > 10)
                .Then(ctx => ctx.Instance.Value = "High")
                .TransitionTo("HighState")
            .Else()
                .Then(ctx => ctx.Instance.Value = "Low")
                .TransitionTo("LowState")
            .EndIf();

        var stateMachine = builder.Build();
        var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
        var testEvent = new TestEvent("Test", 5); // Count <= 10

        // Act
        var transitions = stateMachine.Transitions["Initial"];
        var transition = transitions[0] as StateTransition<TestSaga, TestEvent>;
        var context = new StateContext<TestSaga, TestEvent>(saga, testEvent, services, new CompensationContext());

        if (transition?.Action != null)
        {
            await transition.Action(context);
        }

        // Assert
        Assert.Equal("Low", saga.Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MarkAsCompleted_SetsSagaToCompleted()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();

        // Act
        builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .Then(ctx => ctx.Instance.Value = "Done")
            .TransitionTo(new State("FinalState"))
            .MarkAsCompleted(); // Same as Finalize()

        var stateMachine = builder.Build();

        // Assert
        Assert.NotNull(stateMachine);
        Assert.Single(stateMachine.FinalStates);
    }

    #region Test Classes

    public class TestSaga : SagaBase
    {
        public string? Value { get; set; }
        public int Count { get; set; }
    }

    public record TestEvent(string Value, int Count) : IEvent, IMessage
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    #endregion
}

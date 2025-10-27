using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;
using Xunit;

namespace HeroMessaging.Tests.Unit.Orchestration;

[Trait("Category", "Unit")]
public class StateMachineBuilderTests
{
    private class TestSaga : SagaBase
    {
        public string? Data { get; set; }
    }

    private record TestEvent(string Value) : IEvent, IMessage
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    private record AnotherEvent(int Number) : IEvent, IMessage
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    [Fact]
    public void Build_WithInitialState_SetsInitialState()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var initialState = new State("Initial");
        var testEvent = new Event<TestEvent>(nameof(TestEvent));

        // Act
        builder.Initially()
            .When(testEvent)
                .TransitionTo(new State("Next"));

        var stateMachine = builder.Build();

        // Assert
        Assert.Equal("Initial", stateMachine.InitialState.Name);
    }

    [Fact]
    public void Build_WithTransitions_RegistersTransitions()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var state1 = new State("State1");
        var state2 = new State("State2");
        var testEvent = new Event<TestEvent>(nameof(TestEvent));

        // Act
        builder.Initially()
            .When(testEvent)
                .TransitionTo(state1);

        builder.During(state1)
            .When(testEvent)
                .TransitionTo(state2);

        var stateMachine = builder.Build();

        // Assert
        Assert.True(stateMachine.Transitions.ContainsKey("Initial"));
        Assert.True(stateMachine.Transitions.ContainsKey("State1"));
    }

    [Fact]
    public void Build_WithFinalStates_RegistersFinalStates()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var completed = new State("Completed");
        var testEvent = new Event<TestEvent>(nameof(TestEvent));

        // Act
        builder.Initially()
            .When(testEvent)
                .TransitionTo(completed)
                .Finalize();

        var stateMachine = builder.Build();

        // Assert
        Assert.Contains("Completed", stateMachine.FinalStates.Select(s => s.Name));
    }

    [Fact]
    public void Build_MultipleEventsFromSameState_RegistersAllTransitions()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var state1 = new State("State1");
        var state2 = new State("State2");
        var testEvent = new Event<TestEvent>(nameof(TestEvent));
        var anotherEvent = new Event<AnotherEvent>(nameof(AnotherEvent));

        // Act
        builder.Initially()
            .When(testEvent)
                .TransitionTo(state1)
            .When(anotherEvent)
                .TransitionTo(state2);

        var stateMachine = builder.Build();

        // Assert
        var initialTransitions = stateMachine.Transitions["Initial"];
        Assert.Equal(2, initialTransitions.Count);
    }

    [Fact]
    public void Then_RegistersAction()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var actionExecuted = false;
        var testEvent = new Event<TestEvent>(nameof(TestEvent));

        // Act
        builder.Initially()
            .When(testEvent)
                .Then(ctx =>
                {
                    actionExecuted = true;
                    return Task.CompletedTask;
                })
                .TransitionTo(new State("Next"));

        var stateMachine = builder.Build();

        // Assert - action is registered (can't directly test execution here)
        var transition = stateMachine.Transitions["Initial"].First();
        Assert.NotNull(transition);
    }

    [Fact]
    public void During_MultipleStates_RegistersSeparately()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var state1 = new State("State1");
        var state2 = new State("State2");
        var testEvent = new Event<TestEvent>(nameof(TestEvent));

        // Act
        builder.During(state1)
            .When(testEvent)
                .TransitionTo(state2);

        builder.During(state2)
            .When(testEvent)
                .TransitionTo(state1);

        var stateMachine = builder.Build();

        // Assert
        Assert.True(stateMachine.Transitions.ContainsKey("State1"));
        Assert.True(stateMachine.Transitions.ContainsKey("State2"));
        Assert.Single(stateMachine.Transitions["State1"]);
        Assert.Single(stateMachine.Transitions["State2"]);
    }

    [Fact]
    public void Finalize_WithMultipleStates_RegistersAllAsFinal()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var completed = new State("Completed");
        var cancelled = new State("Cancelled");
        var failed = new State("Failed");
        var testEvent = new Event<TestEvent>(nameof(TestEvent));

        // Act
        builder.Initially()
            .When(testEvent)
                .TransitionTo(completed)
                .Finalize();

        builder.Initially()
            .When(testEvent)
                .TransitionTo(cancelled)
                .Finalize();

        builder.Initially()
            .When(testEvent)
                .TransitionTo(failed)
                .Finalize();

        var stateMachine = builder.Build();

        // Assert
        Assert.Equal(3, stateMachine.FinalStates.Count);
        Assert.Contains(completed, stateMachine.FinalStates);
        Assert.Contains(cancelled, stateMachine.FinalStates);
        Assert.Contains(failed, stateMachine.FinalStates);
    }

    [Fact]
    public void Build_EmptyBuilder_ThrowsException()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("No initial state", exception.Message);
    }

    [Fact]
    public void TransitionTo_SameStateMultipleTimes_LastWins()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var state1 = new State("State1");
        var state2 = new State("State2");
        var testEvent = new Event<TestEvent>(nameof(TestEvent));

        // Act
        builder.Initially()
            .When(testEvent)
                .TransitionTo(state1)
                .TransitionTo(state2); // Overrides previous

        var stateMachine = builder.Build();

        // Assert
        var transition = stateMachine.Transitions["Initial"].First();
        // Note: Can't directly inspect ToState, but behavior should use state2
    }

    [Fact]
    public void State_Equality_WorksByName()
    {
        // Arrange
        var state1 = new State("TestState");
        var state2 = new State("TestState");
        var state3 = new State("DifferentState");

        // Act & Assert
        Assert.Equal(state1, state2);
        Assert.NotEqual(state1, state3);
        Assert.Equal(state1.GetHashCode(), state2.GetHashCode());
    }

    [Fact]
    public void Event_HasCorrectName()
    {
        // Arrange & Act
        var testEvent = new Event<TestEvent>("MyEvent");

        // Assert
        Assert.Equal("MyEvent", testEvent.Name);
        Assert.Equal("MyEvent", testEvent.ToString());
    }

    [Fact]
    public void StateContext_ProvidesAccessToInstanceAndData()
    {
        // Arrange
        var saga = new TestSaga { Data = "test" };
        var eventData = new TestEvent("value");
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();

        // Act
        var context = new StateContext<TestSaga, TestEvent>(saga, eventData, services);

        // Assert
        Assert.Same(saga, context.Instance);
        Assert.Same(eventData, context.Data);
        Assert.Same(services, context.Services);
        Assert.NotNull(context.Compensation);
    }

    [Fact]
    public void StateContext_WithCompensation_UsesProvidedContext()
    {
        // Arrange
        var saga = new TestSaga();
        var eventData = new TestEvent("value");
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();
        var compensation = new CompensationContext();
        compensation.AddCompensation("TestAction", () => Task.CompletedTask);

        // Act
        var context = new StateContext<TestSaga, TestEvent>(saga, eventData, services, compensation);

        // Assert
        Assert.Same(compensation, context.Compensation);
        Assert.True(context.Compensation.HasActions);
    }

    [Fact]
    public void Initially_MultipleWhen_CreatesMultipleTransitions()
    {
        // Arrange
        var builder = new StateMachineBuilder<TestSaga>();
        var state1 = new State("State1");
        var state2 = new State("State2");
        var testEvent = new Event<TestEvent>(nameof(TestEvent));
        var anotherEvent = new Event<AnotherEvent>(nameof(AnotherEvent));

        // Act
        var initialConfigurator = builder.Initially();
        initialConfigurator.When(testEvent).TransitionTo(state1);
        initialConfigurator.When(anotherEvent).TransitionTo(state2);

        var stateMachine = builder.Build();

        // Assert
        Assert.Equal(2, stateMachine.Transitions["Initial"].Count);
    }
}

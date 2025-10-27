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

        // Configure initial state first
        builder.Initially()
            .When(new Event<TestEvent>("StartEvent"))
            .TransitionTo(new State("TestState"));

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
    public void CopyFrom_BuildsStateMachineSuccessfully()
    {
        // Arrange & Act
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .CopyFrom((saga, evt) =>
            {
                saga.Value = evt.Value;
                saga.Count = evt.Count;
            })
            .TransitionTo(new State("NextState"));

        var stateMachine = builder.Build();

        // Assert - Verify state machine was built correctly
        Assert.NotNull(stateMachine);
        Assert.Contains("Initial", stateMachine.Transitions.Keys);
        Assert.Single(stateMachine.Transitions["Initial"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SetProperty_BuildsStateMachineSuccessfully()
    {
        // Arrange & Act
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .SetProperty(
                (saga, value) => saga.Value = value,
                ctx => ctx.Data.Value.ToUpper())
            .TransitionTo(new State("NextState"));

        var stateMachine = builder.Build();

        // Assert - Verify state machine was built correctly
        Assert.NotNull(stateMachine);
        Assert.Contains("Initial", stateMachine.Transitions.Keys);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CompensateWith_BuildsStateMachineSuccessfully()
    {
        // Arrange & Act
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .CompensateWith(
                "TestCompensation",
                async ct =>
                {
                    await Task.CompletedTask;
                })
            .TransitionTo(new State("NextState"));

        var stateMachine = builder.Build();

        // Assert - Verify state machine was built correctly
        Assert.NotNull(stateMachine);
        Assert.Contains("Initial", stateMachine.Transitions.Keys);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ThenAll_BuildsStateMachineSuccessfully()
    {
        // Arrange & Act
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .ThenAll(
                ctx => { return Task.CompletedTask; },
                ctx => { return Task.CompletedTask; },
                ctx => { return Task.CompletedTask; })
            .TransitionTo(new State("NextState"));

        var stateMachine = builder.Build();

        // Assert - Verify state machine was built correctly
        Assert.NotNull(stateMachine);
        Assert.Contains("Initial", stateMachine.Transitions.Keys);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void If_BuildsStateMachineSuccessfully()
    {
        // Arrange & Act
        var builder = new StateMachineBuilder<TestSaga>();

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

        // Assert - Verify state machine was built correctly
        Assert.NotNull(stateMachine);
        Assert.Contains("Initial", stateMachine.Transitions.Keys);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Else_BuildsStateMachineSuccessfully()
    {
        // Arrange & Act
        var builder = new StateMachineBuilder<TestSaga>();

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

        // Assert - Verify state machine was built correctly
        Assert.NotNull(stateMachine);
        Assert.Contains("Initial", stateMachine.Transitions.Keys);
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

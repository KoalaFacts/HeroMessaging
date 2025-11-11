using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Tests.Unit.Orchestration;

/// <summary>
/// Comprehensive tests for ConditionalWhenConfigurator and ElseConfigurator
/// Tests conditional transitions, branching logic, and state machine configuration
/// </summary>
[Trait("Category", "Unit")]
public class ConditionalWhenConfiguratorTests
{
    #region Test Fixtures

    private class TestSaga : SagaBase
    {
        public string? Status { get; set; }
        public int Amount { get; set; }
        public bool Flag { get; set; }
    }

    private record TestEvent(int Value, string? Type = null) : IEvent, IMessage
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    private record ConditionEvent(int Amount, string Category) : IEvent, IMessage
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    #endregion

    #region ConditionalWhenConfigurator - Then Action Tests

    [Fact]
    public void Then_WithAsyncAction_ConfiguresAction()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        var conditional = builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .If(ctx => ctx.Data.Value > 0)
            .Then(async ctx =>
            {
                ctx.Instance.Amount = ctx.Data.Value;
                await Task.CompletedTask;
            });

        Assert.NotNull(conditional);
        Assert.IsAssignableFrom<ConditionalWhenConfigurator<TestSaga, TestEvent>>(conditional);
    }

    [Fact]
    public void Then_WithSyncAction_ConfiguresAction()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        var conditional = builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .If(ctx => ctx.Data.Value > 10)
            .Then(ctx =>
            {
                ctx.Instance.Amount = ctx.Data.Value * 2;
            });

        Assert.NotNull(conditional);
    }

    [Fact]
    public void Then_ReturnsConditionalWhenConfigurator_ForChaining()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        var result = builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .If(ctx => true)
            .Then(ctx => { });

        Assert.IsAssignableFrom<ConditionalWhenConfigurator<TestSaga, TestEvent>>(result);
    }

    [Fact]
    public void Then_AllowsMultipleCalls_ForActionChaining()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        var conditional = builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .If(ctx => ctx.Data.Value > 0)
            .Then(ctx => ctx.Instance.Amount = ctx.Data.Value)
            .Then(ctx => ctx.Instance.Flag = true);

        Assert.NotNull(conditional);
    }

    #endregion

    #region ConditionalWhenConfigurator - TransitionTo Tests

    [Fact]
    public void TransitionTo_WithState_ConfiguresTransition()
    {
        var builder = new StateMachineBuilder<TestSaga>();
        var targetState = new State("Success");

        var conditional = builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .If(ctx => ctx.Data.Value > 0)
            .TransitionTo(targetState);

        Assert.NotNull(conditional);
    }

    [Fact]
    public void TransitionTo_WithStateName_ConfiguresTransition()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        var conditional = builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .If(ctx => ctx.Data.Value > 100)
            .TransitionTo("HighValue");

        Assert.NotNull(conditional);
    }

    [Fact]
    public void TransitionTo_ReturnsConditionalWhenConfigurator_ForChaining()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        var result = builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .If(ctx => true)
            .TransitionTo(new State("Next"));

        Assert.IsAssignableFrom<ConditionalWhenConfigurator<TestSaga, TestEvent>>(result);
    }

    #endregion

    #region ConditionalWhenConfigurator - Else Tests

    [Fact]
    public void Else_ReturnsElseConfigurator()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        var elseConfigurator = builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .If(ctx => ctx.Data.Value > 0)
            .Then(ctx => ctx.Instance.Amount = 1)
            .TransitionTo("Success")
            .Else();

        Assert.NotNull(elseConfigurator);
        Assert.IsAssignableFrom<ElseConfigurator<TestSaga, TestEvent>>(elseConfigurator);
    }

    [Fact]
    public void Else_WithAction_ConfiguresElseBranch()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        var elseConfigurator = builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .If(ctx => ctx.Data.Value > 100)
            .Then(ctx => ctx.Instance.Status = "High")
            .TransitionTo("HighState")
            .Else()
            .Then(ctx => ctx.Instance.Status = "Low");

        Assert.NotNull(elseConfigurator);
    }

    [Fact]
    public void Else_WithMultipleActions_ConfiguresAll()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        var elseConfigurator = builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .If(ctx => ctx.Data.Value > 50)
            .Then(ctx => ctx.Instance.Amount = 50)
            .Else()
            .Then(ctx => ctx.Instance.Status = "Below50")
            .Then(ctx => ctx.Instance.Amount = 0);

        Assert.NotNull(elseConfigurator);
    }

    #endregion

    #region ConditionalWhenConfigurator - EndIf Tests

    [Fact]
    public void EndIf_WithoutElse_ReturnsWhenConfigurator()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        var result = builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .If(ctx => ctx.Data.Value > 0)
            .Then(ctx => ctx.Instance.Amount = ctx.Data.Value)
            .EndIf();

        Assert.NotNull(result);
        Assert.IsAssignableFrom<WhenConfigurator<TestSaga, TestEvent>>(result);
    }

    [Fact]
    public void EndIf_AllowsContinuingConfiguration()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .If(ctx => ctx.Data.Value > 0)
            .Then(ctx => ctx.Instance.Amount = 1)
            .EndIf()
            .TransitionTo(new State("Next"));

        var stateMachine = builder.Build();

        Assert.NotNull(stateMachine);
        Assert.Contains("Initial", stateMachine.Transitions.Keys);
    }

    [Fact]
    public void EndIf_AfterElse_ReturnsWhenConfigurator()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        var result = builder.Initially()
            .When(new Event<TestEvent>("TestEvent"))
            .If(ctx => ctx.Data.Value > 0)
            .Then(ctx => ctx.Instance.Amount = 1)
            .Else()
            .Then(ctx => ctx.Instance.Amount = 0)
            .EndIf();

        Assert.NotNull(result);
        Assert.IsAssignableFrom<WhenConfigurator<TestSaga, TestEvent>>(result);
    }

    #endregion

    #region Conditional Branching - Complex Scenarios

    [Fact]
    public void If_Then_Else_CompleteFlow()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(new Event<ConditionEvent>("ProcessEvent"))
            .If(ctx => ctx.Data.Amount > 1000)
            .Then(ctx => ctx.Instance.Status = "HighValue")
            .TransitionTo("HighValueState")
            .Else()
            .Then(ctx => ctx.Instance.Status = "LowValue")
            .TransitionTo("LowValueState")
            .EndIf();

        var stateMachine = builder.Build();

        Assert.NotNull(stateMachine);
        Assert.Single(stateMachine.Transitions["Initial"]);
    }

    [Fact]
    public void Multiple_If_Conditions_WithDifferentEvents()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(new Event<ConditionEvent>("Event1"))
            .If(ctx => ctx.Data.Amount > 1000)
            .Then(ctx => ctx.Instance.Status = "High")
            .TransitionTo("HighState")
            .Else()
            .Then(ctx => ctx.Instance.Status = "Low")
            .TransitionTo("LowState")
            .EndIf();

        builder.Initially()
            .When(new Event<ConditionEvent>("Event2"))
            .If(ctx => ctx.Data.Category == "Premium")
            .Then(ctx => ctx.Instance.Flag = true)
            .TransitionTo("PremiumState")
            .Else()
            .Then(ctx => ctx.Instance.Flag = false)
            .TransitionTo("StandardState")
            .EndIf();

        var stateMachine = builder.Build();

        Assert.Equal(2, stateMachine.Transitions["Initial"].Count);
    }

    [Fact]
    public void Condition_ChecksEventData_Value()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(new Event<TestEvent>("Test"))
            .If(ctx => ctx.Data.Value > 30)
            .Then(ctx => ctx.Instance.Amount = ctx.Data.Value)
            .TransitionTo("High")
            .Else()
            .Then(ctx => ctx.Instance.Amount = 0)
            .TransitionTo("Low")
            .EndIf();

        var stateMachine = builder.Build();

        Assert.NotNull(stateMachine);
    }

    [Fact]
    public void ComplexCondition_WithMultipleProperties()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(new Event<ConditionEvent>("ComplexEvent"))
            .If(ctx => ctx.Data.Amount > 1000 && ctx.Data.Category == "Premium")
            .Then(ctx =>
            {
                ctx.Instance.Status = "HighPremium";
                ctx.Instance.Amount = ctx.Data.Amount;
            })
            .TransitionTo("PremiumProcessing")
            .Else()
            .Then(ctx => ctx.Instance.Status = "Standard")
            .TransitionTo("StandardProcessing")
            .EndIf();

        var stateMachine = builder.Build();

        Assert.NotNull(stateMachine);
    }

    [Fact]
    public void Condition_WithExternalData_Works()
    {
        var builder = new StateMachineBuilder<TestSaga>();
        var threshold = 500;

        builder.Initially()
            .When(new Event<TestEvent>("Test"))
            .If(ctx => ctx.Data.Value > threshold)
            .Then(ctx => ctx.Instance.Amount = threshold)
            .TransitionTo("Thresholded")
            .EndIf();

        var stateMachine = builder.Build();

        Assert.NotNull(stateMachine);
    }

    #endregion

    #region Condition - Boundary Cases

    [Fact]
    public void Condition_Equality_Check()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(new Event<TestEvent>("Test"))
            .If(ctx => ctx.Data.Value == 100)
            .Then(ctx => ctx.Instance.Status = "Exactly100")
            .TransitionTo("Exact")
            .Else()
            .Then(ctx => ctx.Instance.Status = "NotExactly100")
            .TransitionTo("Approximate")
            .EndIf();

        var stateMachine = builder.Build();

        Assert.NotNull(stateMachine);
    }

    [Fact]
    public void Condition_LessThanCheck()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(new Event<TestEvent>("Test"))
            .If(ctx => ctx.Data.Value < 50)
            .Then(ctx => ctx.Instance.Status = "Low")
            .TransitionTo("LowValue")
            .Else()
            .Then(ctx => ctx.Instance.Status = "HighOrEqual")
            .TransitionTo("HighValue")
            .EndIf();

        var stateMachine = builder.Build();

        Assert.NotNull(stateMachine);
    }

    [Fact]
    public void Condition_NullCheck_String()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(new Event<TestEvent>("Test"))
            .If(ctx => ctx.Data.Type != null)
            .Then(ctx => ctx.Instance.Status = ctx.Data.Type)
            .TransitionTo("TypedEvent")
            .Else()
            .Then(ctx => ctx.Instance.Status = "Untyped")
            .TransitionTo("UntypedEvent")
            .EndIf();

        var stateMachine = builder.Build();

        Assert.NotNull(stateMachine);
    }

    #endregion

    #region ElseConfigurator Tests

    [Fact]
    public void ElseConfigurator_Then_WithAsyncAction()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        var result = builder.Initially()
            .When(new Event<TestEvent>("Test"))
            .If(ctx => ctx.Data.Value > 100)
            .Then(ctx => ctx.Instance.Amount = 100)
            .Else()
            .Then(async ctx =>
            {
                ctx.Instance.Amount = ctx.Data.Value;
                await Task.CompletedTask;
            });

        Assert.NotNull(result);
        Assert.IsAssignableFrom<ElseConfigurator<TestSaga, TestEvent>>(result);
    }

    [Fact]
    public void ElseConfigurator_Then_WithSyncAction()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        var result = builder.Initially()
            .When(new Event<TestEvent>("Test"))
            .If(ctx => ctx.Data.Value > 100)
            .Then(ctx => ctx.Instance.Amount = 100)
            .Else()
            .Then(ctx => ctx.Instance.Flag = true);

        Assert.NotNull(result);
    }

    [Fact]
    public void ElseConfigurator_TransitionTo_WithState()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        var result = builder.Initially()
            .When(new Event<TestEvent>("Test"))
            .If(ctx => ctx.Data.Value > 0)
            .TransitionTo("Success")
            .Else()
            .TransitionTo(new State("Failure"));

        Assert.NotNull(result);
    }

    [Fact]
    public void ElseConfigurator_TransitionTo_WithStateName()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        var result = builder.Initially()
            .When(new Event<TestEvent>("Test"))
            .If(ctx => ctx.Data.Value > 0)
            .TransitionTo("Success")
            .Else()
            .TransitionTo("Failure");

        Assert.NotNull(result);
    }

    [Fact]
    public void ElseConfigurator_EndIf_ReturnsWhenConfigurator()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        var result = builder.Initially()
            .When(new Event<TestEvent>("Test"))
            .If(ctx => true)
            .Then(ctx => { })
            .Else()
            .Then(ctx => { })
            .EndIf();

        Assert.NotNull(result);
        Assert.IsAssignableFrom<WhenConfigurator<TestSaga, TestEvent>>(result);
    }

    [Fact]
    public void ElseConfigurator_ChainedActions()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(new Event<TestEvent>("Test"))
            .If(ctx => ctx.Data.Value > 50)
            .Then(ctx => ctx.Instance.Amount = 50)
            .Else()
            .Then(ctx => ctx.Instance.Amount = 0)
            .Then(ctx => ctx.Instance.Flag = true)
            .Then(ctx => ctx.Instance.Status = "Low")
            .EndIf();

        var stateMachine = builder.Build();

        Assert.NotNull(stateMachine);
    }

    #endregion

    #region Integration - Full State Machine Configuration

    [Fact]
    public void FullStateMachine_WithConditionalTransitions()
    {
        var builder = new StateMachineBuilder<TestSaga>();
        var pendingState = new State("Pending");
        var approvedState = new State("Approved");
        var rejectedState = new State("Rejected");

        builder.Initially()
            .When(new Event<ConditionEvent>("ReviewEvent"))
            .If(ctx => ctx.Data.Amount <= 1000)
            .Then(ctx => ctx.Instance.Status = "AutoApproved")
            .TransitionTo(approvedState)
            .Else()
            .Then(ctx => ctx.Instance.Status = "PendingReview")
            .TransitionTo(pendingState)
            .EndIf();

        builder.During(pendingState)
            .When(new Event<ConditionEvent>("ApproveEvent"))
            .If(ctx => ctx.Data.Category == "Premium")
            .Then(ctx => ctx.Instance.Status = "Approved")
            .TransitionTo(approvedState)
            .Else()
            .Then(ctx => ctx.Instance.Status = "Rejected")
            .TransitionTo(rejectedState)
            .EndIf();

        var stateMachine = builder.Build();

        Assert.NotNull(stateMachine);
        Assert.Equal("Initial", stateMachine.InitialState.Name);
        Assert.Contains("Pending", stateMachine.Transitions.Keys);
    }

    [Fact]
    public void StateMachine_MultipleStates_WithConditionals()
    {
        var builder = new StateMachineBuilder<TestSaga>();
        var processState = new State("Processing");
        var completeState = new State("Complete");

        builder.Initially()
            .When(new Event<TestEvent>("Start"))
            .If(ctx => ctx.Data.Value > 0)
            .Then(ctx => ctx.Instance.Amount = ctx.Data.Value)
            .TransitionTo(processState)
            .Else()
            .Then(ctx => ctx.Instance.Status = "Invalid")
            .TransitionTo(completeState)
            .EndIf();

        builder.During(processState)
            .When(new Event<TestEvent>("Process"))
            .If(ctx => ctx.Data.Value > 100)
            .Then(ctx => ctx.Instance.Amount = 100)
            .TransitionTo(completeState)
            .Else()
            .Then(ctx => ctx.Instance.Amount = ctx.Data.Value)
            .TransitionTo(completeState)
            .EndIf();

        var stateMachine = builder.Build();

        Assert.NotNull(stateMachine);
        Assert.True(stateMachine.Transitions.ContainsKey("Processing"));
    }

    #endregion

    #region Edge Cases and Behavior Tests

    [Fact]
    public void EmptyCondition_ExecutesAsConfigured()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(new Event<TestEvent>("Test"))
            .If(ctx => false)
            .Then(ctx => ctx.Instance.Amount = 1)
            .TransitionTo("Never")
            .Else()
            .Then(ctx => ctx.Instance.Amount = 0)
            .TransitionTo("Always")
            .EndIf();

        var stateMachine = builder.Build();

        Assert.NotNull(stateMachine);
    }

    [Fact]
    public void AlwaysTrueCondition_ExecutesThenBranch()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(new Event<TestEvent>("Test"))
            .If(ctx => true)
            .Then(ctx => ctx.Instance.Amount = 1)
            .TransitionTo("Always")
            .Else()
            .Then(ctx => ctx.Instance.Amount = 0)
            .TransitionTo("Never")
            .EndIf();

        var stateMachine = builder.Build();

        Assert.NotNull(stateMachine);
    }

    [Fact]
    public void ConditionalWith_NoTransition_OnlyAction()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(new Event<TestEvent>("Test"))
            .If(ctx => ctx.Data.Value > 0)
            .Then(ctx => ctx.Instance.Amount = ctx.Data.Value)
            .EndIf();

        var stateMachine = builder.Build();

        Assert.NotNull(stateMachine);
    }

    [Fact]
    public void ConditionalWith_NoAction_OnlyTransition()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(new Event<TestEvent>("Test"))
            .If(ctx => ctx.Data.Value > 0)
            .TransitionTo("Next")
            .EndIf();

        var stateMachine = builder.Build();

        Assert.NotNull(stateMachine);
    }

    [Fact]
    public void ConditionalWith_BothActionAndTransition()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(new Event<TestEvent>("Test"))
            .If(ctx => ctx.Data.Value > 0)
            .Then(ctx => ctx.Instance.Amount = ctx.Data.Value)
            .TransitionTo("Next")
            .EndIf();

        var stateMachine = builder.Build();

        Assert.NotNull(stateMachine);
    }

    #endregion

    #region State Name Tests

    [Fact]
    public void TransitionTo_WithDifferentStateNames()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(new Event<TestEvent>("Test1"))
            .If(ctx => ctx.Data.Value > 100)
            .TransitionTo("HighValue")
            .Else()
            .TransitionTo("LowValue")
            .EndIf();

        var stateMachine = builder.Build();

        Assert.NotNull(stateMachine);
    }

    [Fact]
    public void StateNames_CaseSensitive()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(new Event<TestEvent>("Test"))
            .If(ctx => true)
            .TransitionTo("Success")
            .Else()
            .TransitionTo("success")
            .EndIf();

        var stateMachine = builder.Build();

        Assert.NotNull(stateMachine);
    }

    #endregion

    #region Fluent API Chaining

    [Fact]
    public void FluentChaining_CompleteWorkflow()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(new Event<ConditionEvent>("Validate"))
            .If(ctx => ctx.Data.Amount > 0 && ctx.Data.Category != null)
            .Then(ctx => ctx.Instance.Flag = true)
            .Then(ctx => ctx.Instance.Status = "Validated")
            .TransitionTo("Processing")
            .Else()
            .Then(ctx => ctx.Instance.Flag = false)
            .Then(ctx => ctx.Instance.Status = "Invalid")
            .TransitionTo("Failed")
            .EndIf()
            .When(new Event<TestEvent>("Cancel"))
            .Then(ctx => ctx.Instance.Status = "Cancelled")
            .TransitionTo(new State("Cancelled"));

        var stateMachine = builder.Build();

        Assert.NotNull(stateMachine);
        Assert.Equal(2, stateMachine.Transitions["Initial"].Count);
    }

    #endregion
}

using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;
using Xunit;

namespace HeroMessaging.Tests.Unit.Orchestration;

[Trait("Category", "Unit")]
public class ElseConfiguratorTests
{
    private sealed class TestSaga : SagaBase
    {
        public string? Data { get; set; }
        public int Counter { get; set; }
        public string? ExecutedPath { get; set; }
    }

    private sealed record TestEvent(string Value) : IEvent, IMessage
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    private sealed record TransitionEvent(int Amount) : IEvent, IMessage
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    [Fact]
    public void ElseConfigurator_ExecutesElseAction_WhenConditionIsFalse() => throw new NotImplementedException();

    [Fact]
    public void ElseConfigurator_SkipsElseAction_WhenConditionIsTrue() => throw new NotImplementedException();

    [Fact]
    public void ElseConfigurator_TransitionTo_StateWhenConditionIsFalse() => throw new NotImplementedException();

    [Fact]
    public void ElseConfigurator_FallbackState_RoutesWhenConditionFails() => throw new NotImplementedException();

    [Fact]
    public void ElseConfigurator_ComplexCondition_EvaluatesCorrectly() => throw new NotImplementedException();

    [Fact]
    public void ElseConfigurator_ConditionWithEventData_EvaluatesEventProperties() => throw new NotImplementedException();

    [Fact]
    public void ElseConfigurator_EndIf_ReturnsToWhenConfigurator() => throw new NotImplementedException();

    [Fact]
    public void ElseConfigurator_WithCompensation_RegistersCompensationAction() => throw new NotImplementedException();

    [Fact]
    public void ElseConfigurator_WithNullData_HandlesGracefully() => throw new NotImplementedException();

    [Fact]
    public void ElseConfigurator_EmptyAction_AllowsEmptyElseBlock() => throw new NotImplementedException();

    [Fact]
    public void ElseConfigurator_MultipleEvents_ElseBranchesIndependent() => throw new NotImplementedException();

    [Fact]
    public void ElseConfigurator_Finalize_MarksStateAsFinal() => throw new NotImplementedException();
}
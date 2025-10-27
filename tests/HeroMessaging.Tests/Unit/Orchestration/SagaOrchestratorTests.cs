using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroMessaging.Tests.Unit.Orchestration;

[Trait("Category", "Unit")]
public class SagaOrchestratorTests
{
    private class TestSaga : SagaBase
    {
        public string? ProcessedData { get; set; }
        public int EventCount { get; set; }
    }

    private record TestEvent(string Data) : IEvent, IMessage
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    private record AnotherTestEvent(string Value) : IEvent, IMessage
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    private static readonly State Initial = new("Initial");
    private static readonly State Processing = new("Processing");
    private static readonly State Completed = new("Completed");

    private static readonly Event<TestEvent> TestEventTrigger = new(nameof(TestEvent));
    private static readonly Event<AnotherTestEvent> AnotherEventTrigger = new(nameof(AnotherTestEvent));

    private static StateMachineDefinition<TestSaga> CreateTestStateMachine()
    {
        var builder = new StateMachineBuilder<TestSaga>();

        builder.Initially()
            .When(TestEventTrigger)
                .Then(ctx =>
                {
                    ctx.Instance.ProcessedData = ctx.Data.Data;
                    ctx.Instance.EventCount++;
                    return Task.CompletedTask;
                })
                .TransitionTo(Processing);

        builder.During(Processing)
            .When(AnotherEventTrigger)
                .Then(ctx =>
                {
                    ctx.Instance.ProcessedData += " " + ctx.Data.Value;
                    ctx.Instance.EventCount++;
                    return Task.CompletedTask;
                })
                .TransitionTo(Completed)
                .Finalize();

        return builder.Build();
    }

    [Fact]
    public async Task ProcessAsync_WithNoCorrelationId_LogsWarningAndReturns()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var stateMachine = CreateTestStateMachine();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<TestSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<TestSaga>(repository, stateMachine, services, logger);

        var eventWithoutCorrelation = new TestEvent("test");

        // Act
        await orchestrator.ProcessAsync(eventWithoutCorrelation);

        // Assert
        Assert.Equal(0, repository.Count);
    }

    [Fact]
    public async Task ProcessAsync_NewSaga_CreatesAndSavesSaga()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var stateMachine = CreateTestStateMachine();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<TestSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<TestSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();
        var testEvent = new TestEvent("test data") { CorrelationId = correlationId.ToString() };

        // Act
        await orchestrator.ProcessAsync(testEvent);

        // Assert
        var saga = await repository.FindAsync(correlationId);
        Assert.NotNull(saga);
        Assert.Equal(correlationId, saga!.CorrelationId);
        Assert.Equal("Processing", saga.CurrentState);
        Assert.Equal("test data", saga.ProcessedData);
        Assert.Equal(1, saga.EventCount);
    }

    [Fact]
    public async Task ProcessAsync_ExistingSaga_UpdatesSaga()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var stateMachine = CreateTestStateMachine();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<TestSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<TestSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();

        // Create initial saga
        var firstEvent = new TestEvent("first") { CorrelationId = correlationId.ToString() };
        await orchestrator.ProcessAsync(firstEvent);

        // Act - Process second event
        var secondEvent = new AnotherTestEvent("second") { CorrelationId = correlationId.ToString() };
        await orchestrator.ProcessAsync(secondEvent);

        // Assert
        var saga = await repository.FindAsync(correlationId);
        Assert.NotNull(saga);
        Assert.Equal("Completed", saga!.CurrentState);
        Assert.Equal("first second", saga.ProcessedData);
        Assert.Equal(2, saga.EventCount);
        Assert.True(saga.IsCompleted);
    }

    [Fact]
    public async Task ProcessAsync_NoTransitionForEvent_DoesNothing()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var stateMachine = CreateTestStateMachine();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<TestSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<TestSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();

        // Create saga in Processing state
        var firstEvent = new TestEvent("first") { CorrelationId = correlationId.ToString() };
        await orchestrator.ProcessAsync(firstEvent);

        var sagaAfterFirst = await repository.FindAsync(correlationId);
        var versionAfterFirst = sagaAfterFirst!.Version;

        // Act - Send TestEvent again (no transition defined from Processing state)
        var secondEvent = new TestEvent("second") { CorrelationId = correlationId.ToString() };
        await orchestrator.ProcessAsync(secondEvent);

        // Assert - Saga unchanged
        var saga = await repository.FindAsync(correlationId);
        Assert.Equal("Processing", saga!.CurrentState);
        Assert.Equal("first", saga.ProcessedData); // Not updated
        Assert.Equal(versionAfterFirst, saga.Version); // Version unchanged
    }

    [Fact]
    public async Task ProcessAsync_ExecutesTransitionAction()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var actionExecuted = false;

        var builder = new StateMachineBuilder<TestSaga>();
        builder.Initially()
            .When(TestEventTrigger)
                .Then(ctx =>
                {
                    actionExecuted = true;
                    return Task.CompletedTask;
                })
                .TransitionTo(Processing);

        var stateMachine = builder.Build();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<TestSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<TestSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();
        var testEvent = new TestEvent("data") { CorrelationId = correlationId.ToString() };

        // Act
        await orchestrator.ProcessAsync(testEvent);

        // Assert
        Assert.True(actionExecuted);
    }

    [Fact]
    public async Task ProcessAsync_IncrementsVersion()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var stateMachine = CreateTestStateMachine();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<TestSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<TestSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();
        var event1 = new TestEvent("first") { CorrelationId = correlationId.ToString() };
        var event2 = new AnotherTestEvent("second") { CorrelationId = correlationId.ToString() };

        // Act
        await orchestrator.ProcessAsync(event1);
        var sagaAfterFirst = await repository.FindAsync(correlationId);
        var versionAfterFirst = sagaAfterFirst!.Version; // Capture value (saga is a reference)

        await orchestrator.ProcessAsync(event2);
        var sagaAfterSecond = await repository.FindAsync(correlationId);

        // Assert
        // NOTE: Can't check sagaAfterFirst.Version here because it's a reference that gets
        // modified by event2. Captured value before event2 to test properly.
        Assert.Equal(0, versionAfterFirst); // New saga starts at 0
        Assert.Equal(1, sagaAfterSecond!.Version); // Incremented after update
    }

    [Fact]
    public async Task ProcessAsync_WithIMessageEvent_ExtractsCorrelationId()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var stateMachine = CreateTestStateMachine();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<TestSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<TestSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();
        var testEvent = new TestEvent("data") { CorrelationId = correlationId.ToString() };

        // Act
        await orchestrator.ProcessAsync(testEvent);

        // Assert
        var saga = await repository.FindAsync(correlationId);
        Assert.NotNull(saga);
    }

    [Fact]
    public async Task ProcessAsync_FinalizeState_MarksAsCompleted()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var stateMachine = CreateTestStateMachine();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<TestSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<TestSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();
        var event1 = new TestEvent("first") { CorrelationId = correlationId.ToString() };
        var event2 = new AnotherTestEvent("second") { CorrelationId = correlationId.ToString() };

        // Act
        await orchestrator.ProcessAsync(event1);
        await orchestrator.ProcessAsync(event2);

        // Assert
        var saga = await repository.FindAsync(correlationId);
        Assert.True(saga!.IsCompleted);
        Assert.Equal("Completed", saga.CurrentState);
    }

    [Fact]
    public async Task ProcessAsync_SetsTimestamps()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var stateMachine = CreateTestStateMachine();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = NullLogger<SagaOrchestrator<TestSaga>>.Instance;
        var orchestrator = new SagaOrchestrator<TestSaga>(repository, stateMachine, services, logger);

        var correlationId = Guid.NewGuid();
        var testEvent = new TestEvent("data") { CorrelationId = correlationId.ToString() };
        var beforeTime = DateTime.UtcNow;

        // Act
        await orchestrator.ProcessAsync(testEvent);

        // Assert
        var saga = await repository.FindAsync(correlationId);
        Assert.NotNull(saga);
        Assert.True(saga!.CreatedAt >= beforeTime);
        Assert.True(saga.UpdatedAt >= saga.CreatedAt);
    }
}

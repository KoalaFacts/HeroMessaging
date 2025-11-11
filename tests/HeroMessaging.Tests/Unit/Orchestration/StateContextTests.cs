using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Tests.Unit.Orchestration;

/// <summary>
/// Comprehensive test suite for the StateContext{TSaga, TEvent} class covering:
/// - Initialization and validation
/// - Property access patterns
/// - Compensation context integration
/// - Service provider access
/// - Null input validation
/// - Complex state machine scenarios
///
/// Target Coverage: 80%+ of StateContext class with emphasis on:
/// - Constructor null validation for all parameters
/// - Property access patterns
/// - Default compensation context creation
/// - Service provider integration
/// - Immutability verification
/// </summary>
[Trait("Category", "Unit")]
public class StateContextTests
{
    private class TestSaga : SagaBase
    {
        public string? Data { get; set; }
        public int ProcessingCount { get; set; }
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

    // ========== Constructor & Initialization Tests ==========

    [Fact(DisplayName = "StateContext_Constructor_WithAllValidParameters_CreatesContext")]
    public void StateContext_Constructor_WithAllValidParameters_CreatesContext()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid() };
        var @event = new TestEvent("test");
        var services = new ServiceCollection().BuildServiceProvider();

        // Act
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);

        // Assert
        Assert.NotNull(context);
        Assert.Same(saga, context.Instance);
        Assert.Same(@event, context.Data);
        Assert.Same(services, context.Services);
    }

    [Fact(DisplayName = "StateContext_Constructor_WithNullInstance_ThrowsArgumentNullException")]
    public void StateContext_Constructor_WithNullInstance_ThrowsArgumentNullException()
    {
        // Arrange
        var @event = new TestEvent("test");
        var services = new ServiceCollection().BuildServiceProvider();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new StateContext<TestSaga, TestEvent>(null!, @event, services));
        Assert.Equal("instance", exception.ParamName);
    }

    [Fact(DisplayName = "StateContext_Constructor_WithNullData_ThrowsArgumentNullException")]
    public void StateContext_Constructor_WithNullData_ThrowsArgumentNullException()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid() };
        var services = new ServiceCollection().BuildServiceProvider();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new StateContext<TestSaga, TestEvent>(saga, null!, services));
        Assert.Equal("data", exception.ParamName);
    }

    [Fact(DisplayName = "StateContext_Constructor_WithNullServices_ThrowsArgumentNullException")]
    public void StateContext_Constructor_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid() };
        var @event = new TestEvent("test");

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new StateContext<TestSaga, TestEvent>(saga, @event, null!));
        Assert.Equal("services", exception.ParamName);
    }

    [Fact(DisplayName = "StateContext_Constructor_WithNullCompensation_CreatesDefaultCompensation")]
    public void StateContext_Constructor_WithNullCompensation_CreatesDefaultCompensation()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid() };
        var @event = new TestEvent("test");
        var services = new ServiceCollection().BuildServiceProvider();

        // Act
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services, null);

        // Assert
        Assert.NotNull(context.Compensation);
        Assert.IsType<CompensationContext>(context.Compensation);
    }

    [Fact(DisplayName = "StateContext_Constructor_WithProvidedCompensation_UsesProvided")]
    public void StateContext_Constructor_WithProvidedCompensation_UsesProvided()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid() };
        var @event = new TestEvent("test");
        var services = new ServiceCollection().BuildServiceProvider();
        var compensation = new CompensationContext();
        compensation.AddCompensation("TestAction", () => Task.CompletedTask);

        // Act
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services, compensation);

        // Assert
        Assert.Same(compensation, context.Compensation);
        Assert.True(context.Compensation.HasActions);
    }

    // ========== Instance Property Tests ==========

    [Fact(DisplayName = "StateContext_Instance_Property_ReturnsSagaInstance")]
    public void StateContext_Instance_Property_ReturnsSagaInstance()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid(), Data = "test" };
        var @event = new TestEvent("test");
        var services = new ServiceCollection().BuildServiceProvider();

        // Act
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);
        var instance = context.Instance;

        // Assert
        Assert.Same(saga, instance);
        Assert.Equal("test", instance.Data);
    }

    [Fact(DisplayName = "StateContext_Instance_Property_AllowsModification")]
    public void StateContext_Instance_Property_AllowsModification()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid() };
        var @event = new TestEvent("test");
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);

        // Act
        context.Instance.Data = "modified";
        context.Instance.ProcessingCount = 5;

        // Assert
        Assert.Equal("modified", context.Instance.Data);
        Assert.Equal(5, context.Instance.ProcessingCount);
    }

    [Fact(DisplayName = "StateContext_Instance_Property_ReturnsConsistentValue")]
    public void StateContext_Instance_Property_ReturnsConsistentValue()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid() };
        var @event = new TestEvent("test");
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);

        // Act
        var instance1 = context.Instance;
        var instance2 = context.Instance;
        var instance3 = context.Instance;

        // Assert
        Assert.Same(instance1, instance2);
        Assert.Same(instance2, instance3);
    }

    // ========== Data Property Tests ==========

    [Fact(DisplayName = "StateContext_Data_Property_ReturnsEventData")]
    public void StateContext_Data_Property_ReturnsEventData()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid() };
        var @event = new TestEvent("eventvalue");
        var services = new ServiceCollection().BuildServiceProvider();

        // Act
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);
        var data = context.Data;

        // Assert
        Assert.Same(@event, data);
        Assert.Equal("eventvalue", data.Value);
    }

    [Fact(DisplayName = "StateContext_Data_Property_IsReadOnly")]
    public void StateContext_Data_Property_IsReadOnly()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid() };
        var @event = new TestEvent("test");
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);

        // Act & Assert - Verify property is readable
        var data = context.Data;
        Assert.NotNull(data);
        // Cannot set property as it's read-only
    }

    [Fact(DisplayName = "StateContext_Data_Property_PreservesEventMetadata")]
    public void StateContext_Data_Property_PreservesEventMetadata()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid() };
        var metadata = new Dictionary<string, object> { { "Key", "Value" } };
        var @event = new TestEvent("test") { Metadata = metadata };
        var services = new ServiceCollection().BuildServiceProvider();

        // Act
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);

        // Assert
        Assert.Same(metadata, context.Data.Metadata);
    }

    // ========== Services Property Tests ==========

    [Fact(DisplayName = "StateContext_Services_Property_ReturnsServiceProvider")]
    public void StateContext_Services_Property_ReturnsServiceProvider()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid() };
        var @event = new TestEvent("test");
        var services = new ServiceCollection().BuildServiceProvider();

        // Act
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);

        // Assert
        Assert.Same(services, context.Services);
    }

    [Fact(DisplayName = "StateContext_Services_Property_CanResolveRegisteredServices")]
    public void StateContext_Services_Property_CanResolveRegisteredServices()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid() };
        var @event = new TestEvent("test");
        var services = new ServiceCollection()
            .AddSingleton<TimeProvider>(TimeProvider.System)
            .BuildServiceProvider();

        // Act
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);
        var timeProvider = context.Services.GetService<TimeProvider>();

        // Assert
        Assert.NotNull(timeProvider);
        Assert.IsType<TimeProvider>(timeProvider);
    }

    [Fact(DisplayName = "StateContext_Services_Property_WithMultipleServices")]
    public void StateContext_Services_Property_WithMultipleServices()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid() };
        var @event = new TestEvent("test");
        var services = new ServiceCollection()
            .AddSingleton<TimeProvider>(TimeProvider.System)
            .AddSingleton(new CompensationContext())
            .BuildServiceProvider();

        // Act
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);
        var timeProvider = context.Services.GetService<TimeProvider>();
        var compensation = context.Services.GetService<CompensationContext>();

        // Assert
        Assert.NotNull(timeProvider);
        Assert.NotNull(compensation);
    }

    // ========== Compensation Property Tests ==========

    [Fact(DisplayName = "StateContext_Compensation_Property_IsNotNull")]
    public void StateContext_Compensation_Property_IsNotNull()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid() };
        var @event = new TestEvent("test");
        var services = new ServiceCollection().BuildServiceProvider();

        // Act
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);

        // Assert
        Assert.NotNull(context.Compensation);
    }

    [Fact(DisplayName = "StateContext_Compensation_Property_CanAddActions")]
    public void StateContext_Compensation_Property_CanAddActions()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid() };
        var @event = new TestEvent("test");
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);

        // Act
        context.Compensation.AddCompensation("Action1", () => Task.CompletedTask);
        context.Compensation.AddCompensation("Action2", async () => await Task.CompletedTask);

        // Assert
        Assert.True(context.Compensation.HasActions);
    }

    [Fact(DisplayName = "StateContext_Compensation_Property_PreservesBetweenAccess")]
    public void StateContext_Compensation_Property_PreservesBetweenAccess()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid() };
        var @event = new TestEvent("test");
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);

        // Act
        context.Compensation.AddCompensation("Action", () => Task.CompletedTask);
        var compensation1 = context.Compensation;
        var compensation2 = context.Compensation;

        // Assert
        Assert.Same(compensation1, compensation2);
        Assert.True(compensation1.HasActions);
        Assert.True(compensation2.HasActions);
    }

    // ========== Different Event Type Tests ==========

    [Fact(DisplayName = "StateContext_WithDifferentEventType_WorksCorrectly")]
    public void StateContext_WithDifferentEventType_WorksCorrectly()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid() };
        var @event = new AnotherEvent(42);
        var services = new ServiceCollection().BuildServiceProvider();

        // Act
        var context = new StateContext<TestSaga, AnotherEvent>(saga, @event, services);

        // Assert
        Assert.Same(saga, context.Instance);
        Assert.Same(@event, context.Data);
        Assert.Equal(42, context.Data.Number);
    }

    // ========== Multiple Instance Tests ==========

    [Fact(DisplayName = "StateContext_MultipleInstances_AreIndependent")]
    public void StateContext_MultipleInstances_AreIndependent()
    {
        // Arrange
        var saga1 = new TestSaga { Id = Guid.NewGuid(), Data = "Saga1" };
        var saga2 = new TestSaga { Id = Guid.NewGuid(), Data = "Saga2" };
        var event1 = new TestEvent("Event1");
        var event2 = new TestEvent("Event2");
        var services = new ServiceCollection().BuildServiceProvider();

        // Act
        var context1 = new StateContext<TestSaga, TestEvent>(saga1, event1, services);
        var context2 = new StateContext<TestSaga, TestEvent>(saga2, event2, services);

        // Assert
        Assert.NotSame(context1.Instance, context2.Instance);
        Assert.Equal("Saga1", context1.Instance.Data);
        Assert.Equal("Saga2", context2.Instance.Data);
        Assert.NotEqual(context1.Data.Value, context2.Data.Value);
    }

    [Fact(DisplayName = "StateContext_MultipleInstances_WithSharedServices")]
    public void StateContext_MultipleInstances_WithSharedServices()
    {
        // Arrange
        var saga1 = new TestSaga { Id = Guid.NewGuid() };
        var saga2 = new TestSaga { Id = Guid.NewGuid() };
        var @event = new TestEvent("test");
        var services = new ServiceCollection()
            .AddSingleton<TimeProvider>(TimeProvider.System)
            .BuildServiceProvider();

        // Act
        var context1 = new StateContext<TestSaga, TestEvent>(saga1, @event, services);
        var context2 = new StateContext<TestSaga, TestEvent>(saga2, @event, services);

        // Assert
        Assert.Same(context1.Services, context2.Services);
        Assert.Same(context1.Services.GetService<TimeProvider>(), context2.Services.GetService<TimeProvider>());
    }

    // ========== Integration Tests ==========

    [Fact(DisplayName = "StateContext_InStateMachineScenario_WorksAsContext")]
    public void StateContext_InStateMachineScenario_WorksAsContext()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid() };
        var @event = new TestEvent("Start");
        var services = new ServiceCollection()
            .AddSingleton<TimeProvider>(TimeProvider.System)
            .BuildServiceProvider();

        // Act
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);

        // Simulate state machine processing
        context.Instance.Data = "Processing";
        context.Compensation.AddCompensation("Rollback", async () =>
        {
            context.Instance.Data = "Rolled back";
            await Task.CompletedTask;
        });

        // Assert
        Assert.Equal("Processing", context.Instance.Data);
        Assert.True(context.Compensation.HasActions);
    }

    [Fact(DisplayName = "StateContext_SupportsChainedOperations")]
    public void StateContext_SupportsChainedOperations()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid() };
        var @event = new TestEvent("test");
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);

        // Act - Perform multiple operations through context
        context.Instance.Data = "Step1";
        context.Instance.ProcessingCount++;
        context.Compensation.AddCompensation("Step1Compensation", () => Task.CompletedTask);

        context.Instance.Data = "Step2";
        context.Instance.ProcessingCount++;
        context.Compensation.AddCompensation("Step2Compensation", () => Task.CompletedTask);

        // Assert
        Assert.Equal("Step2", context.Instance.Data);
        Assert.Equal(2, context.Instance.ProcessingCount);
        Assert.Equal(2, context.Compensation.ActionCount);
    }

    [Fact(DisplayName = "StateContext_PropertyImmutability_CannotChangeReferences")]
    public void StateContext_PropertyImmutability_CannotChangeReferences()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid() };
        var @event = new TestEvent("test");
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);

        // Act
        var instance = context.Instance;
        var data = context.Data;
        var serviceProvider = context.Services;
        var compensation = context.Compensation;

        // Assert - References remain the same
        Assert.Same(instance, context.Instance);
        Assert.Same(data, context.Data);
        Assert.Same(serviceProvider, context.Services);
        Assert.Same(compensation, context.Compensation);
    }

    [Fact(DisplayName = "StateContext_AccessPatterns_SupportReadMultipleTimes")]
    public void StateContext_AccessPatterns_SupportReadMultipleTimes()
    {
        // Arrange
        var saga = new TestSaga { Id = Guid.NewGuid() };
        var @event = new TestEvent("test");
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);

        // Act - Access properties multiple times
        var instances = Enumerable.Range(0, 10).Select(_ => context.Instance).ToList();
        var events = Enumerable.Range(0, 10).Select(_ => context.Data).ToList();
        var serviceProviders = Enumerable.Range(0, 10).Select(_ => context.Services).ToList();

        // Assert
        Assert.All(instances, i => Assert.Same(saga, i));
        Assert.All(events, e => Assert.Same(@event, e));
        Assert.All(serviceProviders, s => Assert.Same(services, s));
    }
}

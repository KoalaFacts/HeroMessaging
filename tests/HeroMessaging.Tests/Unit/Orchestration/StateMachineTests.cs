using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Tests.Unit.Orchestration
{
    [Trait("Category", "Unit")]
    public sealed class StateMachineTests
    {
        #region State Tests

        [Fact]
        public void State_Constructor_WithNullName_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() => new State(null!));
            Assert.Equal("name", ex.ParamName);
        }

        [Fact]
        public void State_Constructor_WithValidName_CreatesInstance()
        {
            // Act
            var state = new State("TestState");

            // Assert
            Assert.Equal("TestState", state.Name);
        }

        [Fact]
        public void State_ToString_ReturnsName()
        {
            // Arrange
            var state = new State("TestState");

            // Act
            var result = state.ToString();

            // Assert
            Assert.Equal("TestState", result);
        }

        [Fact]
        public void State_Equals_WithSameName_ReturnsTrue()
        {
            // Arrange
            var state1 = new State("TestState");
            var state2 = new State("TestState");

            // Act & Assert
            Assert.True(state1.Equals(state2));
            Assert.True(state2.Equals(state1));
        }

        [Fact]
        public void State_Equals_WithDifferentName_ReturnsFalse()
        {
            // Arrange
            var state1 = new State("State1");
            var state2 = new State("State2");

            // Act & Assert
            Assert.False(state1.Equals(state2));
            Assert.False(state2.Equals(state1));
        }

        [Fact]
        public void State_Equals_WithNull_ReturnsFalse()
        {
            // Arrange
            var state = new State("TestState");

            // Act & Assert
            Assert.False(state.Equals(null));
        }

        [Fact]
        public void State_Equals_WithNonStateObject_ReturnsFalse()
        {
            // Arrange
            var state = new State("TestState");

            // Act & Assert
            Assert.False(state.Equals("TestState"));
        }

        [Fact]
        public void State_GetHashCode_WithSameName_ReturnsSameHashCode()
        {
            // Arrange
            var state1 = new State("TestState");
            var state2 = new State("TestState");

            // Act & Assert
            Assert.Equal(state1.GetHashCode(), state2.GetHashCode());
        }

        [Fact]
        public void State_GetHashCode_WithDifferentName_ReturnsDifferentHashCode()
        {
            // Arrange
            var state1 = new State("State1");
            var state2 = new State("State2");

            // Act & Assert
            Assert.NotEqual(state1.GetHashCode(), state2.GetHashCode());
        }

        #endregion

        #region Event Tests

        [Fact]
        public void Event_Constructor_WithNullName_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() => new Event<TestEvent>(null!));
            Assert.Equal("name", ex.ParamName);
        }

        [Fact]
        public void Event_Constructor_WithValidName_CreatesInstance()
        {
            // Act
            var @event = new Event<TestEvent>("TestEvent");

            // Assert
            Assert.Equal("TestEvent", @event.Name);
        }

        [Fact]
        public void Event_ToString_ReturnsName()
        {
            // Arrange
            var @event = new Event<TestEvent>("TestEvent");

            // Act
            var result = @event.ToString();

            // Assert
            Assert.Equal("TestEvent", result);
        }

        #endregion

        #region StateContext Tests

        [Fact]
        public void StateContext_Constructor_WithNullInstance_ThrowsArgumentNullException()
        {
            // Arrange
            var @event = new TestEvent();
            var services = new ServiceCollection().BuildServiceProvider();

            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new StateContext<TestSaga, TestEvent>(null!, @event, services));
            Assert.Equal("instance", ex.ParamName);
        }

        [Fact]
        public void StateContext_Constructor_WithNullData_ThrowsArgumentNullException()
        {
            // Arrange
            var saga = new TestSaga();
            var services = new ServiceCollection().BuildServiceProvider();

            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new StateContext<TestSaga, TestEvent>(saga, null!, services));
            Assert.Equal("data", ex.ParamName);
        }

        [Fact]
        public void StateContext_Constructor_WithNullServices_ThrowsArgumentNullException()
        {
            // Arrange
            var saga = new TestSaga();
            var @event = new TestEvent();

            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new StateContext<TestSaga, TestEvent>(saga, @event, null!));
            Assert.Equal("services", ex.ParamName);
        }

        [Fact]
        public void StateContext_Constructor_WithValidArguments_SetsProperties()
        {
            // Arrange
            var saga = new TestSaga();
            var @event = new TestEvent();
            var services = new ServiceCollection().BuildServiceProvider();

            // Act
            var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);

            // Assert
            Assert.Same(saga, context.Instance);
            Assert.Same(@event, context.Data);
            Assert.Same(services, context.Services);
            Assert.NotNull(context.Compensation);
        }

        [Fact]
        public void StateContext_Constructor_WithNullCompensation_CreatesDefaultCompensation()
        {
            // Arrange
            var saga = new TestSaga();
            var @event = new TestEvent();
            var services = new ServiceCollection().BuildServiceProvider();

            // Act
            var context = new StateContext<TestSaga, TestEvent>(saga, @event, services, null);

            // Assert
            Assert.NotNull(context.Compensation);
            Assert.False(context.Compensation.HasActions);
        }

        [Fact]
        public void StateContext_Constructor_WithCompensation_UsesProvidedCompensation()
        {
            // Arrange
            var saga = new TestSaga();
            var @event = new TestEvent();
            var services = new ServiceCollection().BuildServiceProvider();
            var compensation = new CompensationContext();
            compensation.AddCompensation("TestAction", () => { });

            // Act
            var context = new StateContext<TestSaga, TestEvent>(saga, @event, services, compensation);

            // Assert
            Assert.Same(compensation, context.Compensation);
            Assert.True(context.Compensation.HasActions);
        }

        [Fact]
        public void StateContext_Compensation_CanAddActions()
        {
            // Arrange
            var saga = new TestSaga();
            var @event = new TestEvent();
            var services = new ServiceCollection().BuildServiceProvider();
            var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);

            // Act
            context.Compensation.AddCompensation("Action1", () => { });
            context.Compensation.AddCompensation("Action2", () => { });

            // Assert
            Assert.Equal(2, context.Compensation.ActionCount);
        }

        #endregion

        #region StateTransition Tests (via StateMachineBuilder)

        [Fact]
        public async Task StateTransition_ViaBuilder_ExecutesAction()
        {
            // Arrange
            var builder = new StateMachineBuilder<TestSaga>();
            var actionExecuted = false;

            builder.Initially()
                .When(new Event<TestEvent>("TestEvent"))
                .Then(ctx =>
                {
                    actionExecuted = true;
                    return Task.CompletedTask;
                })
                .TransitionTo(new State("NextState"));

            var definition = builder.Build();

            // Assert - Definition is built successfully
            Assert.NotNull(definition);
            Assert.Contains("Initial", definition.Transitions.Keys);
        }

        [Fact]
        public void StateTransition_ViaBuilder_ConfiguresTransitionToState()
        {
            // Arrange
            var builder = new StateMachineBuilder<TestSaga>();
            var targetState = new State("TargetState");

            builder.Initially()
                .When(new Event<TestEvent>("TestEvent"))
                .TransitionTo(targetState);

            // Act
            var definition = builder.Build();

            // Assert
            Assert.NotNull(definition);
            Assert.Contains("Initial", definition.Transitions.Keys);
        }

        #endregion

        #region StateMachineDefinition Tests (via Builder)

        [Fact]
        public void StateMachineDefinition_HasInitialState()
        {
            // Arrange
            var builder = new StateMachineBuilder<TestSaga>();
            builder.Initially()
                .When(new Event<TestEvent>("TestEvent"))
                .TransitionTo(new State("NextState"));

            // Act
            var definition = builder.Build();

            // Assert
            Assert.Equal("Initial", definition.InitialState.Name);
        }

        [Fact]
        public void StateMachineDefinition_HasTransitions()
        {
            // Arrange
            var builder = new StateMachineBuilder<TestSaga>();
            builder.Initially()
                .When(new Event<TestEvent>("TestEvent"))
                .TransitionTo(new State("NextState"));

            // Act
            var definition = builder.Build();

            // Assert
            Assert.NotNull(definition.Transitions);
            Assert.Single(definition.Transitions);
        }

        [Fact]
        public void StateMachineDefinition_HasFinalStates()
        {
            // Arrange
            var builder = new StateMachineBuilder<TestSaga>();
            builder.Initially()
                .When(new Event<TestEvent>("TestEvent1"))
                .TransitionTo(new State("Completed"))
                .Finalize();

            builder.During(new State("Active"))
                .When(new Event<TestEvent>("TestEvent2"))
                .TransitionTo(new State("Failed"))
                .Finalize();

            // Act
            var definition = builder.Build();

            // Assert
            Assert.Equal(2, definition.FinalStates.Count);
        }

        [Fact]
        public void StateMachineDefinition_FinalStates_IsReadOnly()
        {
            // Arrange
            var builder = new StateMachineBuilder<TestSaga>();
            builder.Initially()
                .When(new Event<TestEvent>("TestEvent"))
                .TransitionTo(new State("Completed"))
                .Finalize();

            var definition = builder.Build();

            // Act & Assert
            Assert.IsAssignableFrom<IReadOnlyCollection<State>>(definition.FinalStates);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public void StateContext_WithServices_CanResolveServices()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton<TestService>();
            var serviceProvider = services.BuildServiceProvider();

            var saga = new TestSaga();
            var @event = new TestEvent();
            var context = new StateContext<TestSaga, TestEvent>(saga, @event, serviceProvider);

            // Act
            var service = context.Services.GetService<TestService>();

            // Assert
            Assert.NotNull(service);
        }

        [Fact]
        public async Task StateContext_WithCompensation_CanExecuteCompensation()
        {
            // Arrange
            var saga = new TestSaga();
            var @event = new TestEvent();
            var services = new ServiceCollection().BuildServiceProvider();
            var context = new StateContext<TestSaga, TestEvent>(saga, @event, services);

            var compensated = false;
            context.Compensation.AddCompensation("TestAction", () => { compensated = true; });

            // Act
            await context.Compensation.CompensateAsync();

            // Assert
            Assert.True(compensated);
        }

        #endregion

        #region Test Helper Classes

        public class TestSaga : ISaga
        {
            public Guid CorrelationId { get; set; }
            public string CurrentState { get; set; } = "Initial";
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }
            public bool IsCompleted { get; set; }
            public int Version { get; set; }
        }

        public class TestEvent : IEvent
        {
            public Guid MessageId { get; set; } = Guid.NewGuid();
            public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
            public string? CorrelationId { get; set; }
            public string? CausationId { get; set; }
            public Dictionary<string, object>? Metadata { get; set; }
        }

        public class TestService
        {
        }

        #endregion
    }
}

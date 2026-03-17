using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Orchestration
{
    [Trait("Category", "Unit")]
    public sealed class SagaOrchestratorTests
    {
        private readonly Mock<ISagaRepository<TestSaga>> _repositoryMock;
        private readonly Mock<ILogger<SagaOrchestrator<TestSaga>>> _loggerMock;
        private readonly FakeTimeProvider _timeProvider;
        private readonly IServiceProvider _serviceProvider;
        private readonly StateMachineDefinition<TestSaga> _stateMachine;

        public SagaOrchestratorTests()
        {
            _repositoryMock = new Mock<ISagaRepository<TestSaga>>();
            _loggerMock = new Mock<ILogger<SagaOrchestrator<TestSaga>>>();
            _timeProvider = new FakeTimeProvider();

            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(_timeProvider);
            _serviceProvider = services.BuildServiceProvider();

            // Build a simple state machine for testing
            var builder = new StateMachineBuilder<TestSaga>();
            builder.Initially()
                .When(new Event<OrderStartedEvent>("OrderStartedEvent"))
                .TransitionTo(new State("ProcessingPayment"));

            builder.During(new State("ProcessingPayment"))
                .When(new Event<PaymentCompletedEvent>("PaymentCompletedEvent"))
                .TransitionTo(new State("Completed"))
                .Finalize();

            _stateMachine = builder.Build();
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullRepository_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new SagaOrchestrator<TestSaga>(null!, _stateMachine, _serviceProvider, _loggerMock.Object, _timeProvider));
            Assert.Equal("repository", ex.ParamName);
        }

        [Fact]
        public void Constructor_WithNullStateMachine_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new SagaOrchestrator<TestSaga>(_repositoryMock.Object, null!, _serviceProvider, _loggerMock.Object, _timeProvider));
            Assert.Equal("stateMachine", ex.ParamName);
        }

        [Fact]
        public void Constructor_WithNullServices_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new SagaOrchestrator<TestSaga>(_repositoryMock.Object, _stateMachine, null!, _loggerMock.Object, _timeProvider));
            Assert.Equal("services", ex.ParamName);
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new SagaOrchestrator<TestSaga>(_repositoryMock.Object, _stateMachine, _serviceProvider, null!, _timeProvider));
            Assert.Equal("logger", ex.ParamName);
        }

        [Fact]
        public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new SagaOrchestrator<TestSaga>(_repositoryMock.Object, _stateMachine, _serviceProvider, _loggerMock.Object, null!));
            Assert.Equal("timeProvider", ex.ParamName);
        }

        [Fact]
        public void Constructor_WithValidArguments_CreatesInstance()
        {
            // Act
            var orchestrator = new SagaOrchestrator<TestSaga>(
                _repositoryMock.Object, _stateMachine, _serviceProvider, _loggerMock.Object, _timeProvider);

            // Assert
            Assert.NotNull(orchestrator);
        }

        #endregion

        #region ProcessAsync - Event Correlation Tests

        [Fact]
        public async Task ProcessAsync_WithNoCorrelationId_DoesNotProcessEvent()
        {
            // Arrange
            var orchestrator = CreateOrchestrator();
            var @event = new EventWithoutCorrelation();

            // Act
            await orchestrator.ProcessAsync(@event);

            // Assert
            _repositoryMock.Verify(r => r.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ProcessAsync_WithCorrelationIdInMessage_ProcessesEvent()
        {
            // Arrange
            var orchestrator = CreateOrchestrator();
            var correlationId = Guid.NewGuid();
            var @event = new OrderStartedEvent { CorrelationId = correlationId.ToString() };

            _repositoryMock.Setup(r => r.FindAsync(correlationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((TestSaga?)null);

            // Act
            await orchestrator.ProcessAsync(@event);

            // Assert
            _repositoryMock.Verify(r => r.FindAsync(correlationId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_WithCorrelationIdProperty_ProcessesEvent()
        {
            // Arrange
            var orchestrator = CreateOrchestrator();
            var correlationId = Guid.NewGuid();
            var @event = new EventWithCorrelationProperty { CorrelationId = correlationId };

            _repositoryMock.Setup(r => r.FindAsync(correlationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((TestSaga?)null);

            // Act
            await orchestrator.ProcessAsync(@event);

            // Assert
            _repositoryMock.Verify(r => r.FindAsync(correlationId, It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region ProcessAsync - New Saga Tests

        [Fact]
        public async Task ProcessAsync_WithNewSaga_CreatesSagaInstance()
        {
            // Arrange
            var orchestrator = CreateOrchestrator();
            var correlationId = Guid.NewGuid();
            var @event = new OrderStartedEvent { CorrelationId = correlationId.ToString() };

            _repositoryMock.Setup(r => r.FindAsync(correlationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((TestSaga?)null);

            // Act
            await orchestrator.ProcessAsync(@event);

            // Assert
            _repositoryMock.Verify(r => r.SaveAsync(
                It.Is<TestSaga>(s => s.CorrelationId == correlationId),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_WithNewSaga_SetsInitialState()
        {
            // Arrange
            var orchestrator = CreateOrchestrator();
            var correlationId = Guid.NewGuid();
            var @event = new OrderStartedEvent { CorrelationId = correlationId.ToString() };

            string? savedState = null;
            _repositoryMock.Setup(r => r.FindAsync(correlationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((TestSaga?)null);
            _repositoryMock.Setup(r => r.SaveAsync(It.IsAny<TestSaga>(), It.IsAny<CancellationToken>()))
                .Callback<TestSaga, CancellationToken>((s, ct) => savedState = s.CurrentState)
                .Returns(Task.CompletedTask);

            // Act
            await orchestrator.ProcessAsync(@event);

            // Assert
            Assert.NotNull(savedState);
            Assert.Equal("Initial", savedState);
        }

        [Fact]
        public async Task ProcessAsync_WithNewSagaAndTransition_TransitionsToNewState()
        {
            // Arrange
            var orchestrator = CreateOrchestrator();
            var correlationId = Guid.NewGuid();
            var @event = new OrderStartedEvent { CorrelationId = correlationId.ToString() };

            TestSaga? savedSaga = null;
            _repositoryMock.Setup(r => r.FindAsync(correlationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((TestSaga?)null);
            _repositoryMock.Setup(r => r.SaveAsync(It.IsAny<TestSaga>(), It.IsAny<CancellationToken>()))
                .Callback<TestSaga, CancellationToken>((s, ct) => savedSaga = s)
                .Returns(Task.CompletedTask);

            // Act
            await orchestrator.ProcessAsync(@event);

            // Assert
            Assert.NotNull(savedSaga);
            Assert.Equal("ProcessingPayment", savedSaga.CurrentState);
        }

        #endregion

        #region ProcessAsync - Existing Saga Tests

        [Fact]
        public async Task ProcessAsync_WithExistingSaga_UpdatesSaga()
        {
            // Arrange
            var orchestrator = CreateOrchestrator();
            var correlationId = Guid.NewGuid();
            var existingSaga = new TestSaga
            {
                CorrelationId = correlationId,
                CurrentState = "ProcessingPayment"
            };
            var @event = new PaymentCompletedEvent { CorrelationId = correlationId.ToString() };

            _repositoryMock.Setup(r => r.FindAsync(correlationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingSaga);

            // Act
            await orchestrator.ProcessAsync(@event);

            // Assert
            _repositoryMock.Verify(r => r.UpdateAsync(existingSaga, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_WithExistingSagaAndTransition_TransitionsState()
        {
            // Arrange
            var orchestrator = CreateOrchestrator();
            var correlationId = Guid.NewGuid();
            var existingSaga = new TestSaga
            {
                CorrelationId = correlationId,
                CurrentState = "ProcessingPayment"
            };
            var @event = new PaymentCompletedEvent { CorrelationId = correlationId.ToString() };

            _repositoryMock.Setup(r => r.FindAsync(correlationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingSaga);

            // Act
            await orchestrator.ProcessAsync(@event);

            // Assert
            Assert.Equal("Completed", existingSaga.CurrentState);
        }

        [Fact]
        public async Task ProcessAsync_WithFinalizeTransition_MarksAsCompleted()
        {
            // Arrange
            var orchestrator = CreateOrchestrator();
            var correlationId = Guid.NewGuid();
            var existingSaga = new TestSaga
            {
                CorrelationId = correlationId,
                CurrentState = "ProcessingPayment"
            };
            var @event = new PaymentCompletedEvent { CorrelationId = correlationId.ToString() };

            _repositoryMock.Setup(r => r.FindAsync(correlationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingSaga);

            // Act
            await orchestrator.ProcessAsync(@event);

            // Assert
            Assert.True(existingSaga.IsCompleted);
        }

        #endregion

        #region ProcessAsync - State Transition Action Tests

        [Fact]
        public async Task ProcessAsync_WithTransitionAction_ExecutesAction()
        {
            // Arrange
            var actionExecuted = false;
            var builder = new StateMachineBuilder<TestSaga>();
            builder.Initially()
                .When(new Event<OrderStartedEvent>("OrderStartedEvent"))
                .Then(ctx => { actionExecuted = true; })
                .TransitionTo(new State("ProcessingPayment"));

            var stateMachine = builder.Build();
            var orchestrator = new SagaOrchestrator<TestSaga>(
                _repositoryMock.Object, stateMachine, _serviceProvider, _loggerMock.Object, _timeProvider);

            var correlationId = Guid.NewGuid();
            var @event = new OrderStartedEvent { CorrelationId = correlationId.ToString() };

            _repositoryMock.Setup(r => r.FindAsync(correlationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((TestSaga?)null);

            // Act
            await orchestrator.ProcessAsync(@event);

            // Assert
            Assert.True(actionExecuted);
        }

        [Fact]
        public async Task ProcessAsync_WithAsyncTransitionAction_ExecutesAction()
        {
            // Arrange
            var actionExecuted = false;
            var builder = new StateMachineBuilder<TestSaga>();
            builder.Initially()
                .When(new Event<OrderStartedEvent>("OrderStartedEvent"))
                .Then(async ctx =>
                {
                    await Task.Delay(1);
                    actionExecuted = true;
                })
                .TransitionTo(new State("ProcessingPayment"));

            var stateMachine = builder.Build();
            var orchestrator = new SagaOrchestrator<TestSaga>(
                _repositoryMock.Object, stateMachine, _serviceProvider, _loggerMock.Object, _timeProvider);

            var correlationId = Guid.NewGuid();
            var @event = new OrderStartedEvent { CorrelationId = correlationId.ToString() };

            _repositoryMock.Setup(r => r.FindAsync(correlationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((TestSaga?)null);

            // Act
            await orchestrator.ProcessAsync(@event);

            // Assert
            Assert.True(actionExecuted);
        }

        [Fact]
        public async Task ProcessAsync_WithTransitionAction_ReceivesCorrectContext()
        {
            // Arrange
            StateContext<TestSaga, OrderStartedEvent>? receivedContext = null;
            var builder = new StateMachineBuilder<TestSaga>();
            builder.Initially()
                .When(new Event<OrderStartedEvent>("OrderStartedEvent"))
                .Then(ctx => { receivedContext = ctx; })
                .TransitionTo(new State("ProcessingPayment"));

            var stateMachine = builder.Build();
            var orchestrator = new SagaOrchestrator<TestSaga>(
                _repositoryMock.Object, stateMachine, _serviceProvider, _loggerMock.Object, _timeProvider);

            var correlationId = Guid.NewGuid();
            var @event = new OrderStartedEvent { CorrelationId = correlationId.ToString() };

            _repositoryMock.Setup(r => r.FindAsync(correlationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((TestSaga?)null);

            // Act
            await orchestrator.ProcessAsync(@event);

            // Assert
            Assert.NotNull(receivedContext);
            Assert.Same(@event, receivedContext.Data);
            Assert.NotNull(receivedContext.Instance);
            Assert.Same(_serviceProvider, receivedContext.Services);
        }

        #endregion

        #region ProcessAsync - Edge Cases

        [Fact]
        public async Task ProcessAsync_WithNoMatchingTransition_DoesNotUpdate()
        {
            // Arrange
            var orchestrator = CreateOrchestrator();
            var correlationId = Guid.NewGuid();
            var existingSaga = new TestSaga
            {
                CorrelationId = correlationId,
                CurrentState = "Completed" // No transitions from this state
            };
            var @event = new OrderStartedEvent { CorrelationId = correlationId.ToString() };

            _repositoryMock.Setup(r => r.FindAsync(correlationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingSaga);

            // Act
            await orchestrator.ProcessAsync(@event);

            // Assert
            _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<TestSaga>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ProcessAsync_WithWrongEventTypeForState_DoesNotUpdate()
        {
            // Arrange
            var orchestrator = CreateOrchestrator();
            var correlationId = Guid.NewGuid();
            var existingSaga = new TestSaga
            {
                CorrelationId = correlationId,
                CurrentState = "ProcessingPayment"
            };
            // Send OrderStartedEvent instead of PaymentCompletedEvent
            var @event = new OrderStartedEvent { CorrelationId = correlationId.ToString() };

            _repositoryMock.Setup(r => r.FindAsync(correlationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingSaga);

            // Act
            await orchestrator.ProcessAsync(@event);

            // Assert
            _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<TestSaga>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ProcessAsync_WithTransitionActionException_PropagatesException()
        {
            // Arrange
            var builder = new StateMachineBuilder<TestSaga>();
            builder.Initially()
                .When(new Event<OrderStartedEvent>("OrderStartedEvent"))
                .Then(ctx => throw new InvalidOperationException("Action failed"))
                .TransitionTo(new State("ProcessingPayment"));

            var stateMachine = builder.Build();
            var orchestrator = new SagaOrchestrator<TestSaga>(
                _repositoryMock.Object, stateMachine, _serviceProvider, _loggerMock.Object, _timeProvider);

            var correlationId = Guid.NewGuid();
            var @event = new OrderStartedEvent { CorrelationId = correlationId.ToString() };

            _repositoryMock.Setup(r => r.FindAsync(correlationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((TestSaga?)null);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await orchestrator.ProcessAsync(@event));
        }

        #endregion

        #region Helper Methods

        private SagaOrchestrator<TestSaga> CreateOrchestrator()
        {
            return new SagaOrchestrator<TestSaga>(
                _repositoryMock.Object,
                _stateMachine,
                _serviceProvider,
                _loggerMock.Object,
                _timeProvider);
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

        public class OrderStartedEvent : IEvent, IMessage
        {
            public Guid MessageId { get; set; } = Guid.NewGuid();
            public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
            public string? CorrelationId { get; set; }
            public string? CausationId { get; set; }
            public Dictionary<string, object>? Metadata { get; set; }
        }

        public class PaymentCompletedEvent : IEvent, IMessage
        {
            public Guid MessageId { get; set; } = Guid.NewGuid();
            public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
            public string? CorrelationId { get; set; }
            public string? CausationId { get; set; }
            public Dictionary<string, object>? Metadata { get; set; }
        }

        public class EventWithoutCorrelation : IEvent, IMessage
        {
            public Guid MessageId { get; set; } = Guid.NewGuid();
            public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
            public string? CorrelationId { get; set; } = null;
            public string? CausationId { get; set; }
            public Dictionary<string, object>? Metadata { get; set; }
        }

        public class EventWithCorrelationProperty : IEvent
        {
            public Guid MessageId { get; set; } = Guid.NewGuid();
            public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
            string? IMessage.CorrelationId => CorrelationId.ToString();
            public string? CausationId { get; set; }
            public Dictionary<string, object>? Metadata { get; set; }

            // For reflection-based extraction test
            public Guid CorrelationId { get; set; }
        }

        #endregion
    }
}

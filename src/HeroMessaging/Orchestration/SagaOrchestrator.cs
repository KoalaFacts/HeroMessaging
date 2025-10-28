using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Sagas;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Orchestration;

/// <summary>
/// Orchestrator for coordinating saga execution
/// Receives events, routes them to appropriate saga instances, and manages state transitions
/// </summary>
/// <typeparam name="TSaga">Type of saga being orchestrated</typeparam>
public class SagaOrchestrator<TSaga> where TSaga : class, ISaga, new()
{
    private readonly ISagaRepository<TSaga> _repository;
    private readonly StateMachineDefinition<TSaga> _stateMachine;
    private readonly IServiceProvider _services;
    private readonly ILogger<SagaOrchestrator<TSaga>> _logger;
    private readonly TimeProvider _timeProvider;

    public SagaOrchestrator(
        ISagaRepository<TSaga> repository,
        StateMachineDefinition<TSaga> stateMachine,
        IServiceProvider services,
        ILogger<SagaOrchestrator<TSaga>> logger,
        TimeProvider timeProvider)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Process an event for a saga instance
    /// </summary>
    public async Task ProcessAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        var correlationId = ExtractCorrelationId(@event);
        if (correlationId == Guid.Empty)
        {
            _logger.LogWarning("Event {EventType} has no correlation ID, cannot route to saga", typeof(TEvent).Name);
            return;
        }

        // Find or create saga instance
        var saga = await _repository.FindAsync(correlationId, cancellationToken);
        var isNew = saga == null;

        if (isNew)
        {
            saga = new TSaga
            {
                CorrelationId = correlationId,
                CurrentState = _stateMachine.InitialState.Name
            };
            // Note: CreatedAt and UpdatedAt will be set by repository.SaveAsync()

            _logger.LogInformation("Creating new saga {SagaType} with correlation {CorrelationId}",
                typeof(TSaga).Name, correlationId);
        }

        // Find matching transition
        var currentState = saga.CurrentState;
        if (!_stateMachine.Transitions.TryGetValue(currentState, out var transitions))
        {
            _logger.LogWarning("No transitions defined for state {State} in saga {SagaType}",
                currentState, typeof(TSaga).Name);
            return;
        }

        // Find transition for this event type
        var transition = transitions
            .OfType<StateTransition<TSaga, TEvent>>()
            .FirstOrDefault(t => t.TriggerEvent.Name == typeof(TEvent).Name);

        if (transition == null)
        {
            _logger.LogDebug("No transition found for event {EventType} in state {State}",
                typeof(TEvent).Name, currentState);
            return;
        }

        _logger.LogInformation("Processing event {EventType} for saga {CorrelationId} in state {State}",
            typeof(TEvent).Name, correlationId, currentState);

        // Execute transition action
        if (transition.Action != null)
        {
            var context = new StateContext<TSaga, TEvent>(saga, @event, _services);
            await transition.Action(context);
        }

        // Transition to new state if specified
        if (transition.ToState != null)
        {
            var oldState = saga.CurrentState;
            saga.CurrentState = transition.ToState.Name;
            // Note: UpdatedAt will be set by repository.UpdateAsync()

            _logger.LogInformation("Saga {CorrelationId} transitioned from {OldState} to {NewState}",
                correlationId, oldState, saga.CurrentState);
        }

        // Persist saga (UpdateAsync increments version internally)
        if (isNew)
        {
            await _repository.SaveAsync(saga, cancellationToken);
        }
        else
        {
            await _repository.UpdateAsync(saga, cancellationToken);
        }

        _logger.LogDebug("Saga {CorrelationId} persisted with version {Version}",
            correlationId, saga.Version);
    }

    private Guid ExtractCorrelationId(IEvent @event)
    {
        if (@event is IMessage message && !string.IsNullOrEmpty(message.CorrelationId))
        {
            if (Guid.TryParse(message.CorrelationId, out var correlationId))
            {
                return correlationId;
            }
        }

        // Try to extract from event properties using reflection
        var correlationIdProperty = @event.GetType().GetProperty("CorrelationId");
        if (correlationIdProperty != null)
        {
            var value = correlationIdProperty.GetValue(@event);
            if (value is Guid guidValue)
            {
                return guidValue;
            }
            if (value is string stringValue && Guid.TryParse(stringValue, out var parsedGuid))
            {
                return parsedGuid;
            }
        }

        return Guid.Empty;
    }
}

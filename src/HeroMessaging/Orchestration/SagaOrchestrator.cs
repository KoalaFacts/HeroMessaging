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
    /// Processes an event for a saga instance by finding or creating the saga, locating the appropriate state transition, executing the transition action, and persisting the updated saga state.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to process. Must implement <see cref="IEvent"/>.</typeparam>
    /// <param name="event">The event to process. Must contain a correlation ID to identify or create the target saga instance.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when the event has been processed and the saga state has been persisted.</returns>
    /// <exception cref="ArgumentException">Thrown when the event has no correlation ID (Guid.Empty).</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <remarks>
    /// This method orchestrates the complete saga event processing workflow:
    /// 1. Extracts correlation ID from the event (from IMessage.CorrelationId or event properties)
    /// 2. Finds existing saga instance or creates a new one with the initial state
    /// 3. Looks up state transitions defined for the current state
    /// 4. Finds transition matching the event type
    /// 5. Executes the transition action if defined
    /// 6. Transitions the saga to the new state if specified
    /// 7. Persists the updated saga state (new sagas use SaveAsync, existing use UpdateAsync)
    ///
    /// Correlation ID Extraction:
    /// - First checks if event implements IMessage and has a CorrelationId property
    /// - Falls back to reflection to find CorrelationId property on the event itself
    /// - Supports both Guid and string (parsed to Guid) property values
    /// - Returns Guid.Empty if no correlation ID found (will log warning and return early)
    ///
    /// State Machine Processing:
    /// - Current state determines which transitions are available
    /// - Events not matching any transition are logged but don't cause errors
    /// - Transition actions receive StateContext with saga, event, and service provider
    /// - State changes are optional (transition can execute logic without changing state)
    ///
    /// Persistence:
    /// - New sagas: SaveAsync() sets CreatedAt, UpdatedAt, Version = 1
    /// - Existing sagas: UpdateAsync() updates UpdatedAt and increments Version
    /// - Version is used for optimistic concurrency control
    ///
    /// Example usage:
    /// <code>
    /// // Event triggers saga start
    /// var orderPlacedEvent = new OrderPlacedEvent
    /// {
    ///     CorrelationId = Guid.NewGuid(),
    ///     OrderId = "ORD-123",
    ///     CustomerId = customerId
    /// };
    ///
    /// await sagaOrchestrator.ProcessAsync(orderPlacedEvent, cancellationToken);
    /// // Creates new saga in "OrderPlaced" state
    ///
    /// // Event triggers saga transition
    /// var paymentReceivedEvent = new PaymentReceivedEvent
    /// {
    ///     CorrelationId = orderPlacedEvent.CorrelationId,
    ///     OrderId = "ORD-123",
    ///     Amount = 99.99m
    /// };
    ///
    /// await sagaOrchestrator.ProcessAsync(paymentReceivedEvent, cancellationToken);
    /// // Transitions saga from "OrderPlaced" to "PaymentReceived"
    ///
    /// // Event with no matching transition
    /// var unknownEvent = new SomeOtherEvent
    /// {
    ///     CorrelationId = orderPlacedEvent.CorrelationId
    /// };
    ///
    /// await sagaOrchestrator.ProcessAsync(unknownEvent, cancellationToken);
    /// // Logs debug message, no state change, no error
    /// </code>
    /// </remarks>
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

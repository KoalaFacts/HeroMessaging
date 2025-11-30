using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Sagas;

namespace HeroMessaging.Orchestration;

/// <summary>
/// Fluent builder for defining state machines for sagas
/// Inspired by MassTransit's Automatonymous
/// </summary>
/// <typeparam name="TSaga">Type of saga this state machine coordinates</typeparam>
public class StateMachineBuilder<TSaga> where TSaga : class, ISaga
{
    private readonly Dictionary<string, List<object>> _transitions = [];
    private readonly List<Func<TSaga, IServiceProvider, Task>> _initialActions = [];
    private readonly HashSet<State> _finalStates = [];
    private State? _initialState;

    /// <summary>
    /// Define the initial state and what happens when saga is created
    /// </summary>
    public InitialStateConfigurator<TSaga> Initially()
    {
        return new InitialStateConfigurator<TSaga>(this);
    }

    /// <summary>
    /// Define behavior during a specific state
    /// </summary>
    public DuringStateConfigurator<TSaga> During(State state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        return new DuringStateConfigurator<TSaga>(this, state);
    }

    internal void SetInitialState(State state)
    {
        _initialState = state ?? throw new ArgumentNullException(nameof(state));
    }

    internal void AddInitialAction(Func<TSaga, IServiceProvider, Task> action)
    {
        _initialActions.Add(action ?? throw new ArgumentNullException(nameof(action)));
    }

    internal void AddTransition<TEvent>(State fromState, StateTransition<TSaga, TEvent> transition)
        where TEvent : IEvent
    {
        var key = fromState.Name;
        if (!_transitions.ContainsKey(key))
        {
            _transitions[key] = [];
        }
        _transitions[key].Add(transition);
    }

    internal void AddFinalState(State state)
    {
        _finalStates.Add(state ?? throw new ArgumentNullException(nameof(state)));
    }

    /// <summary>
    /// Build the configured state machine
    /// </summary>
    public StateMachineDefinition<TSaga> Build()
    {
        if (_initialState == null)
        {
            throw new InvalidOperationException("Initial state must be configured using Initially()");
        }

        return new StateMachineDefinition<TSaga>(_initialState, _transitions, _initialActions, _finalStates);
    }
}

/// <summary>
/// Configurator for initial state
/// </summary>
public class InitialStateConfigurator<TSaga> where TSaga : class, ISaga
{
    private readonly StateMachineBuilder<TSaga> _builder;

    internal InitialStateConfigurator(StateMachineBuilder<TSaga> builder)
    {
        _builder = builder;
    }

    /// <summary>
    /// Define what happens when a specific event occurs in the initial state
    /// </summary>
    public WhenConfigurator<TSaga, TEvent> When<TEvent>(Event<TEvent> @event)
        where TEvent : IEvent
    {
        var initialState = new State("Initial");
        _builder.SetInitialState(initialState);
        return new WhenConfigurator<TSaga, TEvent>(_builder, initialState, @event);
    }
}

/// <summary>
/// Configurator for behavior during a specific state
/// </summary>
public class DuringStateConfigurator<TSaga> where TSaga : class, ISaga
{
    private readonly StateMachineBuilder<TSaga> _builder;
    private readonly State _state;

    internal DuringStateConfigurator(StateMachineBuilder<TSaga> builder, State state)
    {
        _builder = builder;
        _state = state;
    }

    /// <summary>
    /// Define what happens when a specific event occurs during this state
    /// </summary>
    public WhenConfigurator<TSaga, TEvent> When<TEvent>(Event<TEvent> @event)
        where TEvent : IEvent
    {
        return new WhenConfigurator<TSaga, TEvent>(_builder, _state, @event);
    }
}

/// <summary>
/// Configurator for event handling
/// </summary>
public class WhenConfigurator<TSaga, TEvent>
    where TSaga : class, ISaga
    where TEvent : IEvent
{
    private readonly StateMachineBuilder<TSaga> _builder;
    private readonly State _fromState;
    private readonly StateTransition<TSaga, TEvent> _transition;

    internal WhenConfigurator(StateMachineBuilder<TSaga> builder, State fromState, Event<TEvent> @event)
    {
        _builder = builder;
        _fromState = fromState;
        _transition = new StateTransition<TSaga, TEvent>(fromState, @event);
        _builder.AddTransition(fromState, _transition);
    }

    /// <summary>
    /// Execute an action when this event occurs
    /// </summary>
    public WhenConfigurator<TSaga, TEvent> Then(Func<StateContext<TSaga, TEvent>, Task> action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));

        // Compose with existing action instead of replacing
        var existingAction = _transition.Action;
        if (existingAction == null)
        {
            _transition.Action = action;
        }
        else
        {
            _transition.Action = async context =>
            {
                await existingAction(context);
                await action(context);
            };
        }
        return this;
    }

    /// <summary>
    /// Execute a synchronous action when this event occurs
    /// </summary>
    public WhenConfigurator<TSaga, TEvent> Then(Action<StateContext<TSaga, TEvent>> action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        // Delegate to async version which handles action composition
        return Then(context =>
        {
            action(context);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Transition to a new state
    /// </summary>
    public WhenConfigurator<TSaga, TEvent> TransitionTo(State state)
    {
        _transition.ToState = state ?? throw new ArgumentNullException(nameof(state));
        return this;
    }

    /// <summary>
    /// Mark the target state as a final state and mark saga as complete
    /// </summary>
    public WhenConfigurator<TSaga, TEvent> Finalize()
    {
        // Mark target state as final
        if (_transition.ToState != null)
        {
            _builder.AddFinalState(_transition.ToState);
        }

        // Wrap the existing action to also mark saga as completed
        var existingAction = _transition.Action;
        _transition.Action = async context =>
        {
            // Execute existing action first
            if (existingAction != null)
            {
                await existingAction(context);
            }

            // Mark saga as completed
            context.Instance.IsCompleted = true;
            var timeProvider = (TimeProvider)context.Services.GetService(typeof(TimeProvider))
                ?? throw new InvalidOperationException("TimeProvider is not registered in the service collection. Ensure TimeProvider is registered before using saga orchestration.");
            context.Instance.UpdatedAt = timeProvider.GetUtcNow();
        };

        return this;
    }

    /// <summary>
    /// Define what happens when another event occurs in the same state
    /// Allows chaining multiple event handlers for the same state
    /// </summary>
    public WhenConfigurator<TSaga, TNewEvent> When<TNewEvent>(Event<TNewEvent> @event)
        where TNewEvent : IEvent
    {
        return new WhenConfigurator<TSaga, TNewEvent>(_builder, _fromState, @event);
    }
}

/// <summary>
/// Compiled state machine definition
/// </summary>
public class StateMachineDefinition<TSaga> where TSaga : class, ISaga
{
    public State InitialState { get; }
    public IReadOnlyCollection<State> FinalStates { get; }

    /// <summary>
    /// Dictionary of state transitions keyed by state name
    /// Internal for orchestrator use, exposed for testing
    /// </summary>
    public Dictionary<string, List<object>> Transitions { get; }

    internal List<Func<TSaga, IServiceProvider, Task>> InitialActions { get; }

    internal StateMachineDefinition(
        State initialState,
        Dictionary<string, List<object>> transitions,
        List<Func<TSaga, IServiceProvider, Task>> initialActions,
        HashSet<State> finalStates)
    {
        InitialState = initialState;
        Transitions = transitions;
        InitialActions = initialActions;
        FinalStates = finalStates.ToList().AsReadOnly();
    }
}

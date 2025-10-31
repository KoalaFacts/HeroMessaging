using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Sagas;

namespace HeroMessaging.Orchestration;

/// <summary>
/// Represents a state in a state machine
/// </summary>
public class State
{
    public string Name { get; }

    public State(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public override string ToString() => Name;
    public override bool Equals(object? obj) => obj is State state && state.Name == Name;
    public override int GetHashCode() => Name.GetHashCode();
}

/// <summary>
/// Represents an event that can trigger state transitions
/// </summary>
/// <typeparam name="TData">Type of event data</typeparam>
public class Event<TData> where TData : IEvent
{
    public string Name { get; }

    public Event(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public override string ToString() => Name;
}

/// <summary>
/// Context for state machine execution providing access to saga data and current event
/// </summary>
/// <typeparam name="TSaga">Type of saga</typeparam>
/// <typeparam name="TEvent">Type of triggering event</typeparam>
public class StateContext<TSaga, TEvent>
    where TSaga : ISaga
    where TEvent : IEvent
{
    /// <summary>
    /// The saga instance being processed
    /// </summary>
    public TSaga Instance { get; }

    /// <summary>
    /// The event that triggered this transition
    /// </summary>
    public TEvent Data { get; }

    /// <summary>
    /// Services available for sending commands/publishing events
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Compensation context for registering compensating actions
    /// Use this to register actions that will be executed if the saga fails
    /// </summary>
    public CompensationContext Compensation { get; }

    public StateContext(TSaga instance, TEvent data, IServiceProvider services, CompensationContext? compensation = null)
    {
        Instance = instance ?? throw new ArgumentNullException(nameof(instance));
        Data = data ?? throw new ArgumentNullException(nameof(data));
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Compensation = compensation ?? new CompensationContext();
    }
}

/// <summary>
/// Defines a state transition triggered by an event
/// </summary>
internal class StateTransition<TSaga, TEvent>
    where TSaga : ISaga
    where TEvent : IEvent
{
    public State FromState { get; }

    /// <summary>
    /// Gets the event that triggers this state transition
    /// </summary>
    public Event<TEvent> TriggerEvent { get; }

    /// <summary>
    /// Gets or sets the target state to transition to when this event is triggered
    /// </summary>
    public State? ToState { get; set; }

    /// <summary>
    /// Gets or sets the action to execute during the state transition
    /// </summary>
    public Func<StateContext<TSaga, TEvent>, Task>? Action { get; set; }

    public StateTransition(State fromState, Event<TEvent> triggerEvent)
    {
        FromState = fromState ?? throw new ArgumentNullException(nameof(fromState));
        TriggerEvent = triggerEvent ?? throw new ArgumentNullException(nameof(triggerEvent));
    }
}

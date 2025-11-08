using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Sagas;

namespace HeroMessaging.Orchestration;

/// <summary>
/// Enhanced builder extensions for more intuitive and powerful state machine definition
/// </summary>
public static class StateMachineBuilderExtensions
{
    /// <summary>
    /// Define a state inline without pre-declaring a State instance
    /// </summary>
    public static DuringStateConfigurator<TSaga> InState<TSaga>(
        this StateMachineBuilder<TSaga> builder,
        string stateName)
        where TSaga : class, ISaga
    {
        return builder.During(new State(stateName));
    }

    /// <summary>
    /// Mark a state as final - saga will be completed when entering this state
    /// </summary>
    public static WhenConfigurator<TSaga, TEvent> MarkAsCompleted<TSaga, TEvent>(
        this WhenConfigurator<TSaga, TEvent> configurator)
        where TSaga : class, ISaga
        where TEvent : IEvent
    {
        return configurator.Finalize();
    }

    /// <summary>
    /// Execute multiple actions in sequence
    /// </summary>
    public static WhenConfigurator<TSaga, TEvent> ThenAll<TSaga, TEvent>(
        this WhenConfigurator<TSaga, TEvent> configurator,
        params Func<StateContext<TSaga, TEvent>, Task>[] actions)
        where TSaga : class, ISaga
        where TEvent : IEvent
    {
        return configurator.Then(async ctx =>
        {
            foreach (var action in actions)
            {
                await action(ctx);
            }
        });
    }

    /// <summary>
    /// Add compensation action with a fluent API
    /// </summary>
    public static WhenConfigurator<TSaga, TEvent> CompensateWith<TSaga, TEvent>(
        this WhenConfigurator<TSaga, TEvent> configurator,
        string actionName,
        Func<CancellationToken, Task> compensationAction)
        where TSaga : class, ISaga
        where TEvent : IEvent
    {
        return configurator.Then(ctx =>
        {
            ctx.Compensation.AddCompensation(actionName, compensationAction);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Publish an event during the transition
    /// </summary>
    public static WhenConfigurator<TSaga, TEvent> Publish<TSaga, TEvent, TPublishEvent>(
        this WhenConfigurator<TSaga, TEvent> configurator,
        Func<StateContext<TSaga, TEvent>, TPublishEvent> eventFactory)
        where TSaga : class, ISaga
        where TEvent : IEvent
        where TPublishEvent : IEvent
    {
        return configurator.Then(async ctx =>
        {
            var messagingService = ctx.Services.GetService(typeof(IHeroMessaging)) as IHeroMessaging;
            if (messagingService != null)
            {
                var eventToPublish = eventFactory(ctx);
                await messagingService.Publish(eventToPublish);
            }
        });
    }

    /// <summary>
    /// Set a saga property during transition
    /// </summary>
    public static WhenConfigurator<TSaga, TEvent> SetProperty<TSaga, TEvent, TValue>(
        this WhenConfigurator<TSaga, TEvent> configurator,
        Action<TSaga, TValue> propertySetter,
        Func<StateContext<TSaga, TEvent>, TValue> valueSelector)
        where TSaga : class, ISaga
        where TEvent : IEvent
    {
        return configurator.Then(ctx =>
        {
            var value = valueSelector(ctx);
            propertySetter(ctx.Instance, value);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Copy data from event to saga using a fluent API
    /// </summary>
    public static WhenConfigurator<TSaga, TEvent> CopyFrom<TSaga, TEvent>(
        this WhenConfigurator<TSaga, TEvent> configurator,
        Action<TSaga, TEvent> copyAction)
        where TSaga : class, ISaga
        where TEvent : IEvent
    {
        return configurator.Then(ctx =>
        {
            copyAction(ctx.Instance, ctx.Data);
            return Task.CompletedTask;
        });
    }
}

/// <summary>
/// Conditional transition configurator
/// </summary>
public class ConditionalWhenConfigurator<TSaga, TEvent>
    where TSaga : class, ISaga
    where TEvent : IEvent
{
    private readonly WhenConfigurator<TSaga, TEvent> _configurator;
    private readonly Func<StateContext<TSaga, TEvent>, bool> _condition;
    private Func<StateContext<TSaga, TEvent>, Task>? _thenAction;
    private State? _thenState;

    internal ConditionalWhenConfigurator(
        WhenConfigurator<TSaga, TEvent> configurator,
        Func<StateContext<TSaga, TEvent>, bool> condition)
    {
        _configurator = configurator;
        _condition = condition;
    }

    /// <summary>
    /// Action to execute if condition is true
    /// </summary>
    public ConditionalWhenConfigurator<TSaga, TEvent> Then(Func<StateContext<TSaga, TEvent>, Task> action)
    {
        _thenAction = action;
        return this;
    }

    /// <summary>
    /// Action to execute if condition is true (synchronous)
    /// </summary>
    public ConditionalWhenConfigurator<TSaga, TEvent> Then(Action<StateContext<TSaga, TEvent>> action)
    {
        _thenAction = ctx =>
        {
            action(ctx);
            return Task.CompletedTask;
        };
        return this;
    }

    /// <summary>
    /// State to transition to if condition is true
    /// </summary>
    public ConditionalWhenConfigurator<TSaga, TEvent> TransitionTo(State state)
    {
        _thenState = state;
        return this;
    }

    /// <summary>
    /// State to transition to if condition is true (inline)
    /// </summary>
    public ConditionalWhenConfigurator<TSaga, TEvent> TransitionTo(string stateName)
    {
        _thenState = new State(stateName);
        return this;
    }

    /// <summary>
    /// Define behavior when condition is false
    /// </summary>
    public ElseConfigurator<TSaga, TEvent> Else()
    {
        // Wrap the condition into the main configurator
        _configurator.Then(async ctx =>
        {
            if (_condition(ctx))
            {
                if (_thenAction != null)
                {
                    await _thenAction(ctx);
                }
            }
        });

        if (_thenState != null)
        {
            _configurator.Then(ctx =>
            {
                if (_condition(ctx))
                {
                    ctx.Instance.CurrentState = _thenState.Name;
                    // Note: UpdatedAt will be set by repository.UpdateAsync()
                }
                return Task.CompletedTask;
            });
        }

        return new ElseConfigurator<TSaga, TEvent>(_configurator, _condition);
    }

    /// <summary>
    /// Complete the conditional without an else clause
    /// </summary>
    public WhenConfigurator<TSaga, TEvent> EndIf()
    {
        return Else().EndIf();
    }
}

/// <summary>
/// Else clause for conditional transitions
/// </summary>
public class ElseConfigurator<TSaga, TEvent>
    where TSaga : class, ISaga
    where TEvent : IEvent
{
    private readonly WhenConfigurator<TSaga, TEvent> _configurator;
    private readonly Func<StateContext<TSaga, TEvent>, bool> _condition;

    internal ElseConfigurator(
        WhenConfigurator<TSaga, TEvent> configurator,
        Func<StateContext<TSaga, TEvent>, bool> condition)
    {
        _configurator = configurator;
        _condition = condition;
    }

    /// <summary>
    /// Action to execute if condition is false
    /// </summary>
    public ElseConfigurator<TSaga, TEvent> Then(Func<StateContext<TSaga, TEvent>, Task> action)
    {
        _configurator.Then(async ctx =>
        {
            if (!_condition(ctx))
            {
                await action(ctx);
            }
        });
        return this;
    }

    /// <summary>
    /// Action to execute if condition is false (synchronous)
    /// </summary>
    public ElseConfigurator<TSaga, TEvent> Then(Action<StateContext<TSaga, TEvent>> action)
    {
        return Then(ctx =>
        {
            action(ctx);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// State to transition to if condition is false
    /// </summary>
    public ElseConfigurator<TSaga, TEvent> TransitionTo(State state)
    {
        _configurator.Then(ctx =>
        {
            if (!_condition(ctx))
            {
                ctx.Instance.CurrentState = state.Name;
                // Note: UpdatedAt will be set by repository.UpdateAsync()
            }
            return Task.CompletedTask;
        });
        return this;
    }

    /// <summary>
    /// State to transition to if condition is false (inline)
    /// </summary>
    public ElseConfigurator<TSaga, TEvent> TransitionTo(string stateName)
    {
        return TransitionTo(new State(stateName));
    }

    /// <summary>
    /// Complete the conditional block
    /// </summary>
    public WhenConfigurator<TSaga, TEvent> EndIf()
    {
        return _configurator;
    }
}

/// <summary>
/// Extension methods for adding conditional logic to state transitions
/// </summary>
public static class ConditionalTransitionExtensions
{
    /// <summary>
    /// Add conditional logic to a transition
    /// </summary>
    public static ConditionalWhenConfigurator<TSaga, TEvent> If<TSaga, TEvent>(
        this WhenConfigurator<TSaga, TEvent> configurator,
        Func<StateContext<TSaga, TEvent>, bool> condition)
        where TSaga : class, ISaga
        where TEvent : IEvent
    {
        return new ConditionalWhenConfigurator<TSaga, TEvent>(configurator, condition);
    }
}

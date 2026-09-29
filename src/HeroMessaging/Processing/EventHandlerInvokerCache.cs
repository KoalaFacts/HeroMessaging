using System.Collections.Concurrent;
using System.Reflection;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;

namespace HeroMessaging.Processing;

internal static class EventHandlerInvokerCache
{
    private static readonly ConcurrentDictionary<Type, Func<object, IEvent, CancellationToken, Task>> Invokers = new();
    private static readonly MethodInfo CreateMethod = typeof(EventHandlerInvokerCache)
        .GetMethod(nameof(CreateInvoker), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static Func<object, IEvent, CancellationToken, Task> Get(Type eventType) =>
        Invokers.GetOrAdd(eventType, static type =>
            (Func<object, IEvent, CancellationToken, Task>)CreateMethod.MakeGenericMethod(type).Invoke(null, null)!);

    private static Func<object, IEvent, CancellationToken, Task> CreateInvoker<TEvent>()
        where TEvent : IEvent =>
        static (handler, message, cancellationToken) =>
            ((IEventHandler<TEvent>)handler).HandleAsync((TEvent)message, cancellationToken);
}

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;

namespace HeroMessaging.Processing;

/// <summary>
/// High-performance cache for handler type information to avoid reflection overhead.
/// All methods are thread-safe and lock-free using ConcurrentDictionary.
/// </summary>
internal static class HandlerTypeCache
{
    // Dummy type to enable nameof() usage for compile-time safety
    private sealed class DummyEvent : IEvent
    {
        public Guid MessageId { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    // Method name validated at compile-time via nameof
    private static readonly string HandleAsyncMethodName = nameof(IEventHandler<>.HandleAsync);

    // Cache for MakeGenericType results
    private static readonly ConcurrentDictionary<Type, Type> _eventHandlerTypes = new();
    private static readonly ConcurrentDictionary<Type, Type> _commandHandlerTypes = new();
    private static readonly ConcurrentDictionary<(Type, Type), Type> _commandWithResponseHandlerTypes = new();
    private static readonly ConcurrentDictionary<(Type, Type), Type> _queryHandlerTypes = new();

    // Cache for GetMethod results
    private static readonly ConcurrentDictionary<Type, MethodInfo> _handleMethods = new();

    /// <summary>
    /// Gets the IEventHandler&lt;T&gt; type for the given event type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Type GetEventHandlerType(Type eventType)
    {
        return _eventHandlerTypes.GetOrAdd(eventType, static t =>
            typeof(IEventHandler<>).MakeGenericType(t));
    }

    /// <summary>
    /// Gets the ICommandHandler&lt;T&gt; type for the given command type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Type GetCommandHandlerType(Type commandType)
    {
        return _commandHandlerTypes.GetOrAdd(commandType, static t =>
            typeof(ICommandHandler<>).MakeGenericType(t));
    }

    /// <summary>
    /// Gets the ICommandHandler&lt;TCommand, TResponse&gt; type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Type GetCommandWithResponseHandlerType(Type commandType, Type responseType)
    {
        return _commandWithResponseHandlerTypes.GetOrAdd((commandType, responseType), static key =>
            typeof(ICommandHandler<,>).MakeGenericType(key.Item1, key.Item2));
    }

    /// <summary>
    /// Gets the IQueryHandler&lt;TQuery, TResponse&gt; type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Type GetQueryHandlerType(Type queryType, Type responseType)
    {
        return _queryHandlerTypes.GetOrAdd((queryType, responseType), static key =>
            typeof(IQueryHandler<,>).MakeGenericType(key.Item1, key.Item2));
    }

    /// <summary>
    /// Gets the HandleAsync method for a handler type. Cached after first lookup.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MethodInfo GetHandleMethod(Type handlerType)
    {
        return _handleMethods.GetOrAdd(handlerType, t =>
            t.GetMethod(HandleAsyncMethodName) ?? throw new InvalidOperationException($"{HandleAsyncMethodName} method not found on {t.Name}"));
    }
}

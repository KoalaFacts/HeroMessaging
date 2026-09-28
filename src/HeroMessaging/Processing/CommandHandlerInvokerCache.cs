using System.Collections.Concurrent;
using System.Reflection;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Handlers;

namespace HeroMessaging.Processing;

internal static class CommandHandlerInvokerCache
{
    private static readonly ConcurrentDictionary<Type, CommandInvoker> Commands = new();
    private static readonly MethodInfo CreateCommandMethod = typeof(CommandHandlerInvokerCache)
        .GetMethod(nameof(CreateCommandInvoker), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo CreateResponseMethod = typeof(CommandHandlerInvokerCache)
        .GetMethod(nameof(CreateResponseInvoker), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static CommandInvoker Get(Type commandType) => Commands.GetOrAdd(commandType, static type =>
    {
        var handlerType = typeof(ICommandHandler<>).MakeGenericType(type);
        var invoke = (Func<object, ICommand, CancellationToken, Task>)CreateCommandMethod
            .MakeGenericMethod(type).Invoke(null, null)!;
        return new CommandInvoker(handlerType, invoke);
    });

    public static ResponseInvoker<TResponse> GetResponse<TResponse>(Type commandType) =>
        ResponseCache<TResponse>.Invokers.GetOrAdd(commandType, static type =>
        {
            var handlerType = typeof(ICommandHandler<,>).MakeGenericType(type, typeof(TResponse));
            var invoke = (Func<object, ICommand<TResponse>, CancellationToken, Task<TResponse>>)CreateResponseMethod
                .MakeGenericMethod(type, typeof(TResponse)).Invoke(null, null)!;
            return new ResponseInvoker<TResponse>(handlerType, invoke);
        });

    private static Func<object, ICommand, CancellationToken, Task> CreateCommandInvoker<TCommand>()
        where TCommand : ICommand =>
        static (handler, command, cancellationToken) =>
            ((ICommandHandler<TCommand>)handler).HandleAsync((TCommand)command, cancellationToken);

    private static Func<object, ICommand<TResponse>, CancellationToken, Task<TResponse>> CreateResponseInvoker<TCommand, TResponse>()
        where TCommand : ICommand<TResponse> =>
        static (handler, command, cancellationToken) =>
            ((ICommandHandler<TCommand, TResponse>)handler).HandleAsync((TCommand)command, cancellationToken);

    private static class ResponseCache<TResponse>
    {
        public static readonly ConcurrentDictionary<Type, ResponseInvoker<TResponse>> Invokers = new();
    }

    internal sealed record CommandInvoker(Type HandlerType, Func<object, ICommand, CancellationToken, Task> Invoke);

    internal sealed record ResponseInvoker<TResponse>(
        Type HandlerType,
        Func<object, ICommand<TResponse>, CancellationToken, Task<TResponse>> Invoke);
}

using System.Threading.Tasks.Dataflow;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Core.Processing;

public class CommandProcessor : ICommandProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CommandProcessor> _logger;
    private readonly ActionBlock<Func<Task>> _processingBlock;

    public CommandProcessor(IServiceProvider serviceProvider, ILogger<CommandProcessor> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        
        _processingBlock = new ActionBlock<Func<Task>>(
            async action => await action(),
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 1,
                BoundedCapacity = 100
            });
    }

    public async Task Send(ICommand command, CancellationToken cancellationToken = default)
    {
        var handlerType = typeof(ICommandHandler<>).MakeGenericType(command.GetType());
        var handler = _serviceProvider.GetService(handlerType);
        
        if (handler == null)
        {
            throw new InvalidOperationException($"No handler found for command type {command.GetType().Name}");
        }

        var tcs = new TaskCompletionSource<bool>();
        
        await _processingBlock.SendAsync(async () =>
        {
            try
            {
                var handleMethod = handlerType.GetMethod("Handle");
                await (Task)handleMethod!.Invoke(handler, new object[] { command, cancellationToken })!;
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing command {CommandType}", command.GetType().Name);
                tcs.SetException(ex);
            }
        }, cancellationToken);

        await tcs.Task;
    }

    public async Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResponse));
        var handler = _serviceProvider.GetService(handlerType);
        
        if (handler == null)
        {
            throw new InvalidOperationException($"No handler found for command type {command.GetType().Name}");
        }

        var tcs = new TaskCompletionSource<TResponse>();
        
        await _processingBlock.SendAsync(async () =>
        {
            try
            {
                var handleMethod = handlerType.GetMethod("Handle");
                var result = await (Task<TResponse>)handleMethod!.Invoke(handler, new object[] { command, cancellationToken })!;
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing command {CommandType}", command.GetType().Name);
                tcs.SetException(ex);
            }
        }, cancellationToken);

        return await tcs.Task;
    }
}

public interface ICommandProcessor
{
    Task Send(ICommand command, CancellationToken cancellationToken = default);
    Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);
}
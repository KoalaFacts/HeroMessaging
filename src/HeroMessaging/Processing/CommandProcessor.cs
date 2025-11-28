using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;

public class CommandProcessor : ICommandProcessor, IProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CommandProcessor> _logger;
    private readonly ActionBlock<Func<Task>> _processingBlock;
    private readonly ProcessorMetricsCollector _metrics = new();

    public bool IsRunning { get; private set; } = true;

    public CommandProcessor(IServiceProvider serviceProvider, ILogger<CommandProcessor>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CommandProcessor>.Instance;

        _processingBlock = new ActionBlock<Func<Task>>(
            async action => await action().ConfigureAwait(false),
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 1,
                BoundedCapacity = ProcessingConstants.DefaultBoundedCapacity
            });
    }

    public async Task Send(ICommand command, CancellationToken cancellationToken = default)
    {
        CompatibilityHelpers.ThrowIfNull(command, nameof(command));

        var handlerType = typeof(ICommandHandler<>).MakeGenericType(command.GetType());
        var handler = _serviceProvider.GetService(handlerType);

        if (handler == null)
        {
            throw new InvalidOperationException($"No handler found for command type {command.GetType().Name}");
        }

        var tcs = new TaskCompletionSource<bool>();

        var posted = await _processingBlock.SendAsync(async () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var handleMethod = handlerType.GetMethod("Handle");
                var sw = Stopwatch.StartNew();
                await ((Task)handleMethod!.Invoke(handler, [command, cancellationToken])!).ConfigureAwait(false);
                sw.Stop();

                _metrics.RecordSuccess(sw.ElapsedMilliseconds);
                tcs.SetResult(true);
            }
            catch (OperationCanceledException)
            {
                tcs.SetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                _metrics.RecordFailure();
                _logger.LogError(ex, "Error processing command {CommandType}", command.GetType().Name);
                tcs.SetException(ex);
            }
        }, cancellationToken).ConfigureAwait(false);

        if (!posted)
        {
            tcs.SetCanceled(cancellationToken);
        }

        await tcs.Task.ConfigureAwait(false);
    }

    public async Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        CompatibilityHelpers.ThrowIfNull(command, nameof(command));

        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResponse));
        var handler = _serviceProvider.GetService(handlerType);

        if (handler == null)
        {
            throw new InvalidOperationException($"No handler found for command type {command.GetType().Name}");
        }

        var tcs = new TaskCompletionSource<TResponse>();

        var posted = await _processingBlock.SendAsync(async () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var handleMethod = handlerType.GetMethod("Handle");
                var sw = Stopwatch.StartNew();
                var result = await ((Task<TResponse>)handleMethod!.Invoke(handler, [command, cancellationToken])!).ConfigureAwait(false);
                sw.Stop();

                _metrics.RecordSuccess(sw.ElapsedMilliseconds);
                tcs.SetResult(result);
            }
            catch (OperationCanceledException)
            {
                tcs.SetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                _metrics.RecordFailure();
                _logger.LogError(ex, "Error processing command {CommandType}", command.GetType().Name);
                tcs.SetException(ex);
            }
        }, cancellationToken).ConfigureAwait(false);

        if (!posted)
        {
            tcs.SetCanceled(cancellationToken);
        }

        return await tcs.Task.ConfigureAwait(false);
    }

    public IProcessorMetrics GetMetrics() => _metrics.GetMetrics();
}

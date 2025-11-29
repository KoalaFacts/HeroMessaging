using System.Diagnostics;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;

/// <summary>
/// Zero-allocation command processor using SemaphoreSlim for serialization.
/// Eliminates TaskCompletionSource and async lambda allocations.
/// </summary>
public class CommandProcessor : ICommandProcessor, IProcessor, IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CommandProcessor> _logger;
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private readonly ProcessorMetricsCollector _metrics = new();
    private volatile bool _isRunning = true;

    public bool IsRunning => _isRunning;

    public CommandProcessor(IServiceProvider serviceProvider, ILogger<CommandProcessor>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CommandProcessor>.Instance;
    }

    public async Task SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        CompatibilityHelpers.ThrowIfNull(command, nameof(command));

        if (!_isRunning)
        {
            throw new ObjectDisposedException(nameof(CommandProcessor));
        }

        // Use cached handler type - avoids MakeGenericType allocation after first call
        var handlerType = HandlerTypeCache.GetCommandHandlerType(command.GetType());
        var handler = _serviceProvider.GetService(handlerType);

        if (handler == null)
        {
            throw new InvalidOperationException($"No handler found for command type {command.GetType().Name}");
        }

        // Cache the handle method to avoid reflection on each call
        var handleMethod = HandlerTypeCache.GetHandleMethod(handlerType);

        await _processingLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            await ((Task)handleMethod.Invoke(handler, [command, cancellationToken])!).ConfigureAwait(false);
            sw.Stop();

            _metrics.RecordSuccess(sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _metrics.RecordFailure();
            _logger.LogError(ex, "Error processing command {CommandType}", command.GetType().Name);
            throw;
        }
        finally
        {
            _processingLock.Release();
        }
    }

    public async Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        CompatibilityHelpers.ThrowIfNull(command, nameof(command));

        if (!_isRunning)
        {
            throw new ObjectDisposedException(nameof(CommandProcessor));
        }

        // Use cached handler type - avoids MakeGenericType allocation after first call
        var handlerType = HandlerTypeCache.GetCommandWithResponseHandlerType(command.GetType(), typeof(TResponse));
        var handler = _serviceProvider.GetService(handlerType);

        if (handler == null)
        {
            throw new InvalidOperationException($"No handler found for command type {command.GetType().Name}");
        }

        // Cache the handle method to avoid reflection on each call
        var handleMethod = HandlerTypeCache.GetHandleMethod(handlerType);

        await _processingLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            var result = await ((Task<TResponse>)handleMethod.Invoke(handler, [command, cancellationToken])!).ConfigureAwait(false);
            sw.Stop();

            _metrics.RecordSuccess(sw.ElapsedMilliseconds);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _metrics.RecordFailure();
            _logger.LogError(ex, "Error processing command {CommandType}", command.GetType().Name);
            throw;
        }
        finally
        {
            _processingLock.Release();
        }
    }

    public IProcessorMetrics GetMetrics() => _metrics.GetMetrics();

    /// <summary>
    /// Disposes the command processor.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        _isRunning = false;
        _processingLock.Dispose();
        return ValueTask.CompletedTask;
    }
}

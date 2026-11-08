using System.Runtime.ExceptionServices;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Idempotency;
using HeroMessaging.Idempotency.Decorators;
using HeroMessaging.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;

/// <summary>
/// Zero-allocation command processor using SemaphoreSlim for serialization.
/// Eliminates TaskCompletionSource and async lambda allocations.
/// </summary>
/// <remarks>
/// When an <see cref="IIdempotencyStore"/> and an <see cref="IIdempotencyPolicy"/> are registered (for example by
/// <c>WithIdempotency()</c>), commands without a response are deduplicated per handler with keys
/// <c>{configured key}:handler:{handler type}</c>: a redelivered command is not handled again, a cached idempotent
/// failure is rethrown, and transient failures and cancellations are not cached. Commands with a response are not
/// deduplicated yet.
/// </remarks>
public class CommandProcessor : ICommandProcessor, IProcessor, IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CommandProcessor> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private readonly ProcessorMetricsCollector _metrics;
    private readonly IdempotencyDecorator? _idempotency;
    private volatile bool _isRunning = true;
    /// <summary>
    /// Gets is running.
    /// </summary>

    public bool IsRunning => _isRunning;
    /// <summary>
    /// Initializes a new instance of the <see cref="CommandProcessor"/> class.
    /// </summary>

    public CommandProcessor(IServiceProvider serviceProvider, ILogger<CommandProcessor>? logger = null, TimeProvider? timeProvider = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CommandProcessor>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _metrics = new ProcessorMetricsCollector(_timeProvider);
        _idempotency = CreateIdempotency(serviceProvider, _timeProvider);
    }
    /// <summary>
    /// Executes send async.
    /// </summary>

    public async Task SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        CompatibilityHelpers.ThrowIfNull(command, nameof(command));

        if (!_isRunning)
        {
            throw new ObjectDisposedException(nameof(CommandProcessor));
        }

        var invoker = CommandHandlerInvokerCache.Get(command.GetType());
        var handler = _serviceProvider.GetService(invoker.HandlerType) ?? throw new InvalidOperationException($"No handler found for command type {command.GetType().Name}");

        await _processingLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startTime = _timeProvider.GetTimestamp();
            if (_idempotency is null)
            {
                await invoker.Invoke(handler, command, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await InvokeIdempotentAsync(_idempotency, handler, invoker.HandlerType, command, cancellationToken)
                    .ConfigureAwait(false);
            }
            var elapsedMs = _timeProvider.GetElapsedTime(startTime).TotalMilliseconds;

            _metrics.RecordSuccess((long)elapsedMs);
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
    /// <summary>
    /// Executes send async.
    /// </summary>

    public async Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        CompatibilityHelpers.ThrowIfNull(command, nameof(command));

        if (!_isRunning)
        {
            throw new ObjectDisposedException(nameof(CommandProcessor));
        }

        var invoker = CommandHandlerInvokerCache.GetResponse<TResponse>(command.GetType());
        var handler = _serviceProvider.GetService(invoker.HandlerType) ?? throw new InvalidOperationException($"No handler found for command type {command.GetType().Name}");

        await _processingLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startTime = _timeProvider.GetTimestamp();
            var result = await invoker.Invoke(handler, command, cancellationToken).ConfigureAwait(false);
            var elapsedMs = _timeProvider.GetElapsedTime(startTime).TotalMilliseconds;

            _metrics.RecordSuccess((long)elapsedMs);
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
    private static IdempotencyDecorator? CreateIdempotency(IServiceProvider serviceProvider, TimeProvider timeProvider)
    {
        var store = serviceProvider.GetService<IIdempotencyStore>();
        var policy = serviceProvider.GetService<IIdempotencyPolicy>();
        if (store is null || policy is null)
        {
            return null;
        }

        var logger = serviceProvider.GetService<ILogger<IdempotencyDecorator>>()
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<IdempotencyDecorator>.Instance;
        return new IdempotencyDecorator(CommandHandlerStep.Instance, store, new HandlerScopedIdempotencyPolicy(policy),
            logger, timeProvider);
    }

    private static async Task InvokeIdempotentAsync(IdempotencyDecorator idempotency, object handler, Type handlerType,
        ICommand command, CancellationToken cancellationToken)
    {
        var context = new ProcessingContext(nameof(CommandProcessor)) { Handler = handler, HandlerType = handlerType };
        var result = await idempotency.ProcessAsync(command, context, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            ExceptionDispatchInfo.Capture(result.Exception
                ?? new InvalidOperationException(result.Message ?? $"Command {command.GetType().Name} failed")).Throw();
        }
    }

    /// <summary>
    /// Runs the command handler for the idempotency decorator. Handler failures become a failed result so the
    /// decorator can classify and cache them; cancellation propagates so it is never cached.
    /// </summary>
    private sealed class CommandHandlerStep : IMessageProcessor
    {
        public static readonly CommandHandlerStep Instance = new();

        public async ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context,
            CancellationToken cancellationToken = default)
        {
            var invoker = CommandHandlerInvokerCache.Get(message.GetType());
            try
            {
                await invoker.Invoke(context.Handler!, (ICommand)message, cancellationToken).ConfigureAwait(false);
                return ProcessingResult.Successful();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (!ErrorClassifier.IsCritical(ex))
            {
                return ProcessingResult.Failed(ex);
            }
        }
    }

    /// <summary>
    /// Executes get metrics.
    /// </summary>

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

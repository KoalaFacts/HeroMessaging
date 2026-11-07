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
                await invoker.Invoke(handler, command, cancellationToken).ConfigureAwait(false);
            else
                await InvokeIdempotentAsync(_idempotency, command, handler, invoker.HandlerType, cancellationToken).ConfigureAwait(false);
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
    // Commands without a response are deduplicated by message ID when WithIdempotency() registered a store
    // and a policy. Keys are handler-scoped, like the event bus, so a command never shares a key with an event.
    private static IdempotencyDecorator? CreateIdempotency(IServiceProvider serviceProvider, TimeProvider timeProvider)
    {
        var store = serviceProvider.GetService<IIdempotencyStore>();
        var policy = serviceProvider.GetService<IIdempotencyPolicy>();
        if (store is null || policy is null)
            return null;

        var logger = serviceProvider.GetService<ILogger<IdempotencyDecorator>>()
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<IdempotencyDecorator>.Instance;
        return new IdempotencyDecorator(CommandHandlerProcessor.Instance, store, new HandlerScopedIdempotencyPolicy(policy), logger, timeProvider);
    }

    private static async Task InvokeIdempotentAsync(IdempotencyDecorator idempotency, ICommand command, object handler, Type handlerType,
        CancellationToken cancellationToken)
    {
        var context = new ProcessingContext(nameof(CommandProcessor)) { Handler = handler, HandlerType = handlerType };
        var result = await idempotency.ProcessAsync(command, context, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            ExceptionDispatchInfo.Capture(result.Exception
                ?? new InvalidOperationException($"Command {command.GetType().Name} failed: {result.Message}")).Throw();
        }
    }

    // Runs the command's handler and turns its exception into a failed result, so the idempotency
    // decorator can classify and cache it; the processor rethrows the original exception.
    private sealed class CommandHandlerProcessor : IMessageProcessor
    {
        public static readonly CommandHandlerProcessor Instance = new();

        public async ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var invoker = CommandHandlerInvokerCache.Get(message.GetType());
                await invoker.Invoke(context.Handler!, (ICommand)message, cancellationToken).ConfigureAwait(false);
                return ProcessingResult.Successful();
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

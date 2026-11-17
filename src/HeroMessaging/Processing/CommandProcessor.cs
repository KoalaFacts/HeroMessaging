using System.Runtime.ExceptionServices;
using System.Text.Json;
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
/// failure is rethrown, and transient failures and cancellations are not cached. Commands with a response are
/// deduplicated the same way and a redelivery returns the cached response; a response read back from a durable store
/// as JSON is converted to the response type with the registered <see cref="IJsonSerializer"/>.
/// </remarks>
public class CommandProcessor : ICommandProcessor, IProcessor, IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CommandProcessor> _logger;
    private readonly TimeProvider _timeProvider;
    private const string ResponseInvocationKey = "HeroMessaging.CommandProcessor.ResponseInvocation";
    private static readonly JsonSerializerOptions CachedResponseJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private readonly ProcessorMetricsCollector _metrics;
    private readonly IdempotencyDecorator? _idempotency;
    private readonly IdempotencyDecorator? _responseIdempotency;
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
        _idempotency = CreateIdempotency(serviceProvider, _timeProvider, CommandHandlerStep.Instance);
        _responseIdempotency = _idempotency is null
            ? null
            : CreateIdempotency(serviceProvider, _timeProvider, ResponseCommandHandlerStep.Instance);
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
            var result = _responseIdempotency is null
                ? await invoker.Invoke(handler, command, cancellationToken).ConfigureAwait(false)
                : await InvokeIdempotentAsync(_responseIdempotency, handler, invoker, command, cancellationToken)
                    .ConfigureAwait(false);
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
    private static IdempotencyDecorator? CreateIdempotency(IServiceProvider serviceProvider, TimeProvider timeProvider,
        IMessageProcessor handlerStep)
    {
        var store = serviceProvider.GetService<IIdempotencyStore>();
        var policy = serviceProvider.GetService<IIdempotencyPolicy>();
        if (store is null || policy is null)
        {
            return null;
        }

        var logger = serviceProvider.GetService<ILogger<IdempotencyDecorator>>()
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<IdempotencyDecorator>.Instance;
        return new IdempotencyDecorator(handlerStep, new CommitDeferredIdempotencyStore(store),
            new HandlerScopedIdempotencyPolicy(policy),
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

    private async Task<TResponse> InvokeIdempotentAsync<TResponse>(IdempotencyDecorator idempotency, object handler,
        CommandHandlerInvokerCache.ResponseInvoker<TResponse> invoker, ICommand<TResponse> command, CancellationToken cancellationToken)
    {
        // The handler itself stays in the context so the idempotency key names the handler type, as for commands
        // without a response; the typed invoker travels in the metadata.
        var context = new ProcessingContext(nameof(CommandProcessor)) { Handler = handler, HandlerType = invoker.HandlerType }
            .WithMetadata(ResponseInvocationKey, new ResponseInvocation<TResponse>(handler, invoker));
        var result = await idempotency.ProcessAsync(command, context, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            ExceptionDispatchInfo.Capture(result.Exception
                ?? new InvalidOperationException(result.Message ?? $"Command {command.GetType().Name} failed")).Throw();
        }

        return ConvertResponse<TResponse>(result.Data, command);
    }

    /// <summary>
    /// Returns a response produced in this process as is, and converts a cached response that a durable store read
    /// back as JSON to <typeparamref name="TResponse"/>.
    /// </summary>
    private TResponse ConvertResponse<TResponse>(object? data, ICommand<TResponse> command)
    {
        switch (data)
        {
            case TResponse response:
                return response;
            case null:
                return default!;
            case JsonElement json:
                var serializer = _serviceProvider.GetService<IJsonSerializer>()
                    ?? throw new InvalidOperationException(
                        $"[HM-IDEMPOTENCY-RESPONSE] The cached response for command {command.GetType().Name} " +
                        $"(message {command.MessageId}) is JSON, but no {nameof(IJsonSerializer)} is registered to convert it " +
                        $"to {typeof(TResponse).Name}. Register HeroMessaging with AddHeroMessaging(), which adds the default serializer.");
                try
                {
                    return serializer.DeserializeFromString<TResponse>(json.GetRawText(), CachedResponseJsonOptions)!;
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException)
                {
                    throw new InvalidOperationException(
                        $"[HM-IDEMPOTENCY-RESPONSE] The cached response for command {command.GetType().Name} " +
                        $"(message {command.MessageId}) could not be converted from JSON to {typeof(TResponse).Name}: {ex.Message} " +
                        "Use a response type that round-trips through System.Text.Json (public constructor or settable " +
                        "properties, no interface or abstract type), or clear the idempotency record for this message.", ex);
                }
            default:
                throw new InvalidOperationException(
                    $"[HM-IDEMPOTENCY-RESPONSE] The cached response for command {command.GetType().Name} " +
                    $"(message {command.MessageId}) is a {data.GetType().Name}, not a {typeof(TResponse).Name}. " +
                    "Clear the idempotency record for this message, or make the idempotency store return the stored " +
                    "response type or its JSON.");
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

    /// <summary>The handler and its typed invoker for one command with a response.</summary>
    private interface IResponseInvocation
    {
        Task<object?> InvokeAsync(ICommand command, CancellationToken cancellationToken);
    }

    private sealed class ResponseInvocation<TResponse>(object handler, CommandHandlerInvokerCache.ResponseInvoker<TResponse> invoker)
        : IResponseInvocation
    {
        public async Task<object?> InvokeAsync(ICommand command, CancellationToken cancellationToken) =>
            await invoker.Invoke(handler, (ICommand<TResponse>)command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a command handler that returns a response for the idempotency decorator; the response becomes the
    /// result's data so the decorator caches it. Failures and cancellation are handled as for
    /// <see cref="CommandHandlerStep"/>.
    /// </summary>
    private sealed class ResponseCommandHandlerStep : IMessageProcessor
    {
        public static readonly ResponseCommandHandlerStep Instance = new();

        public async ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await context.GetMetadataReference<IResponseInvocation>(ResponseInvocationKey)!
                    .InvokeAsync((ICommand)message, cancellationToken).ConfigureAwait(false);
                return ProcessingResult.Successful(data: response);
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

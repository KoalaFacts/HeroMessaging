using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Idempotency;
using HeroMessaging.Idempotency.Decorators;
using HeroMessaging.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;

/// <summary>
/// Zero-allocation query processor using SemaphoreSlim for serialization.
/// Eliminates TaskCompletionSource and async lambda allocations.
/// </summary>
/// <remarks>
/// When an <see cref="IIdempotencyStore"/> and an <see cref="IIdempotencyPolicy"/> are registered (for example by
/// <c>WithIdempotency()</c>), queries are deduplicated by message with keys <c>{configured key}:handler:{query type}</c>
/// (one handler serves a query type, and the query type is known on JIT and NativeAOT alike): a redelivered query
/// returns the cached response without running the handler, a cached idempotent failure is rethrown, and transient
/// failures and cancellations are not cached. A response read back from a durable store as JSON is converted to the
/// response type with the registered <see cref="IJsonSerializer"/>.
/// </remarks>
public class QueryProcessor : IQueryProcessor, IProcessor, IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QueryProcessor> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private readonly ProcessorMetricsCollector _metrics;
    private readonly IdempotencyDecorator? _idempotency;
    private const string QueryInvocationKey = "HeroMessaging.QueryProcessor.Invocation";
    private volatile bool _isRunning = true;
    /// <summary>
    /// Gets is running.
    /// </summary>

    public bool IsRunning => _isRunning;
    /// <summary>
    /// Initializes a new instance of the <see cref="QueryProcessor"/> class.
    /// </summary>

    public QueryProcessor(IServiceProvider serviceProvider, ILogger<QueryProcessor>? logger = null, TimeProvider? timeProvider = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryProcessor>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _metrics = new ProcessorMetricsCollector(_timeProvider);
        _idempotency = CreateIdempotency(serviceProvider, _timeProvider);
    }
    /// <summary>
    /// Executes send async.
    /// </summary>

    public async Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        CompatibilityHelpers.ThrowIfNull(query, nameof(query));

        if (!_isRunning)
        {
            throw new ObjectDisposedException(nameof(QueryProcessor));
        }

        object? handler = null;
        MethodInfo? handleMethod = null;
        Func<object, CancellationToken, Task<TResponse>>? generated = null;
        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            // Preserve the existing manual-registration path on JIT runtimes.
            (handler, handleMethod) = ResolveDynamicHandler(query);
        }
        else
        {
            // Resolve before waiting to preserve handler lifetime and failure ordering.
            generated = ResolveGeneratedHandler(query);
        }

        await _processingLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startTime = _timeProvider.GetTimestamp();
            var result = _idempotency is null
                ? await Invoke(query, handler, handleMethod, generated, cancellationToken).ConfigureAwait(false)
                : await InvokeIdempotentAsync(query, handler, handleMethod, generated, cancellationToken).ConfigureAwait(false);
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
            _logger.LogError(ex, "Error processing query {QueryType}", query.GetType().Name);
            throw;
        }
        finally
        {
            _processingLock.Release();
        }
    }
    private static Task<TResponse> Invoke<TResponse>(IQuery<TResponse> query, object? handler, MethodInfo? handleMethod,
        Func<object, CancellationToken, Task<TResponse>>? generated, CancellationToken cancellationToken) =>
        generated is not null
            ? generated(query, cancellationToken)
            : (Task<TResponse>)handleMethod!.Invoke(handler, BindingFlags.DoNotWrapExceptions,
                binder: null, parameters: [query, cancellationToken], culture: null)!;

    private async Task<TResponse> InvokeIdempotentAsync<TResponse>(IQuery<TResponse> query, object? handler,
        MethodInfo? handleMethod, Func<object, CancellationToken, Task<TResponse>>? generated, CancellationToken cancellationToken)
    {
        // The query itself is the context's handler so the key names the query type on both dispatch paths.
        var context = new ProcessingContext(nameof(QueryProcessor)) { Handler = query }
            .WithMetadata(QueryInvocationKey, new QueryInvocation<TResponse>(handler, handleMethod, generated));
        var result = await _idempotency!.ProcessAsync(query, context, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            ExceptionDispatchInfo.Capture(result.Exception
                ?? new InvalidOperationException(result.Message ?? $"Query {query.GetType().Name} failed")).Throw();
        }

        return CachedResponseConverter.Convert<TResponse>(result.Data, _serviceProvider, "query", query);
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
        return new IdempotencyDecorator(QueryHandlerStep.Instance, new CommitDeferredIdempotencyStore(store),
            new HandlerScopedIdempotencyPolicy(policy), logger, timeProvider);
    }

    /// <summary>The resolved handler of one query, invoked by <see cref="QueryHandlerStep"/>.</summary>
    private interface IQueryInvocation
    {
        Task<object?> InvokeAsync(IMessage query, CancellationToken cancellationToken);
    }

    private sealed class QueryInvocation<TResponse>(object? handler, MethodInfo? handleMethod,
        Func<object, CancellationToken, Task<TResponse>>? generated) : IQueryInvocation
    {
        public async Task<object?> InvokeAsync(IMessage query, CancellationToken cancellationToken) =>
            await Invoke((IQuery<TResponse>)query, handler, handleMethod, generated, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the query handler for the idempotency decorator; the response becomes the result's data so the decorator
    /// caches it. Handler failures become a failed result so the decorator can classify them; cancellation propagates
    /// so it is never cached.
    /// </summary>
    private sealed class QueryHandlerStep : IMessageProcessor
    {
        public static readonly QueryHandlerStep Instance = new();

        public async ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await context.GetMetadataReference<IQueryInvocation>(QueryInvocationKey)!
                    .InvokeAsync(message, cancellationToken).ConfigureAwait(false);
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

    private Func<object, CancellationToken, Task<TResponse>> ResolveGeneratedHandler<TResponse>(IQuery<TResponse> query)
    {
        if (_serviceProvider is IKeyedServiceProvider keyedProvider &&
            keyedProvider.GetKeyedService(typeof(Func<object, CancellationToken, Task<TResponse>>),
                (typeof(IQueryHandler<,>), query.GetType())) is Func<object, CancellationToken, Task<TResponse>> generated)
        {
            return generated;
        }

        throw new InvalidOperationException($"No generated handler found for query type {query.GetType().Name}. Use AddGeneratedHandlers for NativeAOT query dispatch.");
    }

    private (object Handler, MethodInfo Method) ResolveDynamicHandler<TResponse>(IQuery<TResponse> query)
    {
        var handlerType = HandlerTypeCache.GetQueryHandlerType(query.GetType(), typeof(TResponse));
        var handler = _serviceProvider.GetService(handlerType) ?? throw new InvalidOperationException($"No handler found for query type {query.GetType().Name}");
        var handleMethod = HandlerTypeCache.GetHandleMethod(handlerType);
        return (handler, handleMethod);
    }

    /// <summary>
    /// Executes get metrics.
    /// </summary>

    public IQueryProcessorMetrics GetMetrics() => _metrics.GetQueryMetrics();

    /// <summary>
    /// Disposes the query processor.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        _isRunning = false;
        _processingLock.Dispose();
        return ValueTask.CompletedTask;
    }
}

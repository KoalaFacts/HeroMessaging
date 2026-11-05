using System.Reflection;
using System.Runtime.CompilerServices;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;

/// <summary>
/// Zero-allocation query processor using SemaphoreSlim for serialization.
/// Eliminates TaskCompletionSource and async lambda allocations.
/// </summary>
public class QueryProcessor : IQueryProcessor, IProcessor, IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QueryProcessor> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private readonly ProcessorMetricsCollector _metrics;
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
            var execution = generated is not null
                ? generated(query, cancellationToken)
                : (Task<TResponse>)handleMethod!.Invoke(handler, BindingFlags.DoNotWrapExceptions,
                    binder: null, parameters: [query, cancellationToken], culture: null)!;
            var result = await execution.ConfigureAwait(false);
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

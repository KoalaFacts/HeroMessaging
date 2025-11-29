using System.Diagnostics;
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
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private readonly ProcessorMetricsCollector _metrics = new();
    private volatile bool _isRunning = true;

    public bool IsRunning => _isRunning;

    public QueryProcessor(IServiceProvider serviceProvider, ILogger<QueryProcessor>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryProcessor>.Instance;
    }

    public async Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        CompatibilityHelpers.ThrowIfNull(query, nameof(query));

        if (!_isRunning)
        {
            throw new ObjectDisposedException(nameof(QueryProcessor));
        }

        // Use cached handler type - avoids MakeGenericType allocation after first call
        var handlerType = HandlerTypeCache.GetQueryHandlerType(query.GetType(), typeof(TResponse));
        var handler = _serviceProvider.GetService(handlerType);

        if (handler == null)
        {
            throw new InvalidOperationException($"No handler found for query type {query.GetType().Name}");
        }

        // Cache the handle method to avoid reflection on each call
        var handleMethod = HandlerTypeCache.GetHandleMethod(handlerType);

        await _processingLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            var result = await ((Task<TResponse>)handleMethod.Invoke(handler, [query, cancellationToken])!).ConfigureAwait(false);
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
            _logger.LogError(ex, "Error processing query {QueryType}", query.GetType().Name);
            throw;
        }
        finally
        {
            _processingLock.Release();
        }
    }

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

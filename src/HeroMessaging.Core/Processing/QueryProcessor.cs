using System.Threading.Tasks.Dataflow;
using System.Diagnostics;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Abstractions.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Core.Processing;

public class QueryProcessor : IQueryProcessor, IProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QueryProcessor> _logger;
    private readonly ActionBlock<Func<Task>> _processingBlock;
    private long _processedCount;
    private long _failedCount;
    private long _cacheHits;
    private readonly List<long> _durations = new();
    private readonly object _metricsLock = new();
    
    public bool IsRunning { get; private set; } = true;

    public QueryProcessor(IServiceProvider serviceProvider, ILogger<QueryProcessor> logger)
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

    public async Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        var handlerType = typeof(IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(TResponse));
        var handler = _serviceProvider.GetService(handlerType);
        
        if (handler == null)
        {
            throw new InvalidOperationException($"No handler found for query type {query.GetType().Name}");
        }

        var tcs = new TaskCompletionSource<TResponse>();
        
        await _processingBlock.SendAsync(async () =>
        {
            try
            {
                var handleMethod = handlerType.GetMethod("Handle");
                var sw = Stopwatch.StartNew();
                var result = await (Task<TResponse>)handleMethod!.Invoke(handler, [query, cancellationToken])!;
                sw.Stop();
                
                lock (_metricsLock)
                {
                    _processedCount++;
                    _durations.Add(sw.ElapsedMilliseconds);
                    if (_durations.Count > 100) _durations.RemoveAt(0);
                }
                
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                lock (_metricsLock)
                {
                    _failedCount++;
                }
                
                _logger.LogError(ex, "Error processing query {QueryType}", query.GetType().Name);
                tcs.SetException(ex);
            }
        }, cancellationToken);

        return await tcs.Task;
    }
    
    public IQueryProcessorMetrics GetMetrics()
    {
        lock (_metricsLock)
        {
            return new QueryProcessorMetrics
            {
                ProcessedCount = _processedCount,
                FailedCount = _failedCount,
                AverageDuration = _durations.Count > 0 
                    ? TimeSpan.FromMilliseconds(_durations.Average())
                    : TimeSpan.Zero,
                CacheHitRate = _processedCount > 0 
                    ? (double)_cacheHits / _processedCount 
                    : 0
            };
        }
    }
}

public class QueryProcessorMetrics : IQueryProcessorMetrics
{
    public long ProcessedCount { get; init; }
    public long FailedCount { get; init; }
    public TimeSpan AverageDuration { get; init; }
    public double CacheHitRate { get; init; }
}

public interface IQueryProcessor
{
    Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);
}
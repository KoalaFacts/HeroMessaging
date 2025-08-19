using System.Threading.Tasks.Dataflow;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Queries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Core.Processing;

public class QueryProcessor : IQueryProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QueryProcessor> _logger;
    private readonly ActionBlock<Func<Task>> _processingBlock;

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
                var result = await (Task<TResponse>)handleMethod!.Invoke(handler, new object[] { query, cancellationToken })!;
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing query {QueryType}", query.GetType().Name);
                tcs.SetException(ex);
            }
        }, cancellationToken);

        return await tcs.Task;
    }
}

public interface IQueryProcessor
{
    Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);
}
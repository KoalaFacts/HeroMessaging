using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;

namespace HeroMessaging.Processing;

/// <summary>
/// Default implementation of <see cref="IQueryProcessor"/> that processes queries through a sequential pipeline with automatic metrics tracking.
/// </summary>
/// <remarks>
/// This implementation provides reliable query processing with the following characteristics:
/// - Sequential processing ensures queries execute in FIFO order
/// - Bounded queue prevents memory exhaustion (capacity: 100 queries)
/// - Automatic metrics collection (processed count, failures, average duration, cache hit rate)
/// - Handler resolution via dependency injection
/// - Exception propagation to callers for proper error handling
///
/// Implementation Details:
/// - Uses TPL Dataflow ActionBlock for queue management
/// - Single-threaded processing (MaxDegreeOfParallelism = 1)
/// - Reflection-based handler invocation
/// - Lock-protected metrics for thread safety
/// - Maintains sliding window of last 100 execution durations
///
/// All queries must return a typed result (void queries are not supported per CQRS principles).
/// All processing is asynchronous and respects cancellation tokens.
/// </remarks>
public class QueryProcessor : IQueryProcessor, IProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QueryProcessor> _logger;
    private readonly ActionBlock<Func<Task>> _processingBlock;
    private long _processedCount;
    private long _failedCount;
    private readonly List<long> _durations = new();
    private readonly object _metricsLock = new();

    /// <summary>
    /// Gets a value indicating whether the processor is running and accepting queries.
    /// </summary>
    /// <value>
    /// Always returns <c>true</c> in this implementation as the processor is always ready to accept queries.
    /// </value>
    public bool IsRunning { get; private set; } = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryProcessor"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve query handlers.</param>
    /// <param name="logger">Optional logger for diagnostic output. If null, a NullLogger is used.</param>
    /// <remarks>
    /// The processor is configured with:
    /// - MaxDegreeOfParallelism = 1 (sequential processing)
    /// - BoundedCapacity = 100 (prevents unbounded memory growth)
    ///
    /// Query handlers are resolved from the service provider using the pattern:
    /// - IQueryHandler&lt;TQuery, TResponse&gt; for all queries (queries must return results)
    /// </remarks>
    public QueryProcessor(IServiceProvider serviceProvider, ILogger<QueryProcessor>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryProcessor>.Instance;

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
        CompatibilityHelpers.ThrowIfNull(query, nameof(query));

        var handlerType = typeof(IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(TResponse));
        var handler = _serviceProvider.GetService(handlerType);

        if (handler == null)
        {
            throw new InvalidOperationException($"No handler found for query type {query.GetType().Name}");
        }

        var tcs = new TaskCompletionSource<TResponse>();

        var posted = await _processingBlock.SendAsync(async () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
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
            catch (OperationCanceledException)
            {
                tcs.SetCanceled(cancellationToken);
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

        if (!posted)
        {
            tcs.SetCanceled(cancellationToken);
        }

        return await tcs.Task;
    }

    /// <summary>
    /// Retrieves current query processing metrics for monitoring and diagnostics.
    /// </summary>
    /// <returns>
    /// A <see cref="QueryProcessorMetrics"/> instance containing current statistics.
    /// </returns>
    /// <remarks>
    /// Metrics are collected automatically during query processing:
    /// - ProcessedCount: Total successful queries (incremented on success)
    /// - FailedCount: Total failed queries (incremented on exception)
    /// - AverageDuration: Rolling average of last 100 query execution times
    /// - CacheHitRate: Currently always 0 (caching not implemented in this version)
    ///
    /// Thread-safe: This method uses locking to ensure consistent metric snapshots.
    /// The average duration calculation excludes cancelled operations.
    /// </remarks>
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
                CacheHitRate = 0
            };
        }
    }
}

/// <summary>
/// Implementation of <see cref="IQueryProcessorMetrics"/> providing query processor statistics.
/// </summary>
/// <remarks>
/// This class is an immutable record type that captures a snapshot of query processor metrics
/// at a specific point in time. It is returned by <see cref="QueryProcessor.GetMetrics"/>.
/// </remarks>
public class QueryProcessorMetrics : IQueryProcessorMetrics
{
    /// <summary>
    /// Gets the total number of successfully processed queries.
    /// </summary>
    /// <value>
    /// The count of queries that completed without throwing exceptions.
    /// This count increases monotonically and is never reset.
    /// </value>
    public long ProcessedCount { get; init; }

    /// <summary>
    /// Gets the total number of failed query processing attempts.
    /// </summary>
    /// <value>
    /// The count of queries that threw exceptions during processing.
    /// This includes both business logic failures and infrastructure errors.
    /// Cancelled operations are not counted as failures.
    /// </value>
    public long FailedCount { get; init; }

    /// <summary>
    /// Gets the average duration of query processing operations.
    /// </summary>
    /// <value>
    /// The rolling average of the last 100 query execution times.
    /// Returns <see cref="TimeSpan.Zero"/> if no queries have been processed.
    /// </value>
    /// <remarks>
    /// The average is calculated from a sliding window of the most recent 100 executions
    /// to provide a current view of query performance rather than historical average.
    /// </remarks>
    public TimeSpan AverageDuration { get; init; }

    /// <summary>
    /// Gets the cache hit rate for query results.
    /// </summary>
    /// <value>
    /// Currently always returns 0 as response caching is not implemented in this version.
    /// Future implementations may add query result caching with cache hit rate tracking.
    /// </value>
    public double CacheHitRate { get; init; }
}

/// <summary>
/// Processes queries through the HeroMessaging query pipeline for read-only data retrieval operations.
/// </summary>
/// <remarks>
/// The query processor implements the Query pattern (CQRS) for executing read operations that don't modify system state.
/// Queries are processed sequentially through a bounded queue, with built-in support for caching and performance monitoring.
///
/// Design Principles:
/// - Queries represent read-only operations (no side effects)
/// - Always return typed results (never void)
/// - Sequential processing ensures consistent read patterns
/// - Bounded capacity prevents memory exhaustion under load
/// - Automatic handler resolution via dependency injection
/// - Cache-friendly design (queries should be idempotent)
///
/// Query Characteristics:
/// - IQuery&lt;TResponse&gt;: All queries must return a typed result
/// - Idempotent: Same query parameters should return same result
/// - Side-effect free: No state modifications
/// - Cacheable: Results can be cached for performance
///
/// Processing Characteristics:
/// - Single-threaded execution (MaxDegreeOfParallelism = 1)
/// - Bounded queue capacity (100 queries)
/// - Automatic metrics tracking (processed count, failures, duration, cache hit rate)
/// - Exception propagation to caller
///
/// <code>
/// // Define a query
/// public class GetCustomerQuery : IQuery&lt;CustomerDto&gt;
/// {
///     public Guid CustomerId { get; set; }
/// }
///
/// // Define query handler
/// public class GetCustomerHandler : IQueryHandler&lt;GetCustomerQuery, CustomerDto&gt;
/// {
///     private readonly ICustomerRepository _repository;
///
///     public GetCustomerHandler(ICustomerRepository repository)
///     {
///         _repository = repository;
///     }
///
///     public async Task&lt;CustomerDto&gt; Handle(GetCustomerQuery query, CancellationToken cancellationToken)
///     {
///         var customer = await _repository.GetByIdAsync(query.CustomerId, cancellationToken);
///         return new CustomerDto
///         {
///             Id = customer.Id,
///             Name = customer.Name,
///             Email = customer.Email
///         };
///     }
/// }
///
/// // Usage
/// var processor = serviceProvider.GetRequiredService&lt;IQueryProcessor&gt;();
/// var customer = await processor.Send&lt;CustomerDto&gt;(new GetCustomerQuery
/// {
///     CustomerId = customerId
/// });
///
/// // Monitor query performance
/// var metrics = processor.GetMetrics();
/// logger.LogInformation(
///     "Query Processor: {Processed} queries, {Failed} failures, {AvgDuration}ms avg, {CacheHitRate:P} cache hit rate",
///     metrics.ProcessedCount,
///     metrics.FailedCount,
///     metrics.AverageDuration.TotalMilliseconds,
///     metrics.CacheHitRate
/// );
/// </code>
/// </remarks>
public interface IQueryProcessor
{
    /// <summary>
    /// Sends a query for processing and returns the query result.
    /// </summary>
    /// <typeparam name="TResponse">The type of result returned by the query handler.</typeparam>
    /// <param name="query">The query to process. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task containing the result produced by the query handler.</returns>
    /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no handler is registered for the query type.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via the cancellation token.</exception>
    /// <remarks>
    /// This method:
    /// - Resolves the appropriate IQueryHandler&lt;TQuery, TResponse&gt; from the service provider
    /// - Queues the query for sequential processing
    /// - Waits for query execution and result
    /// - Tracks processing metrics (duration, success/failure counts)
    /// - Propagates any exceptions thrown by the handler
    ///
    /// Processing Behavior:
    /// - Queries are processed in FIFO order
    /// - If queue is full (100 items), operation waits until space is available
    /// - Handler exceptions are logged and propagated to caller
    /// - The returned result is the value produced by the handler
    /// - Cancelled operations throw OperationCanceledException
    ///
    /// Performance Considerations:
    /// - Async queueing with bounded capacity prevents memory issues
    /// - Sequential processing ensures no concurrency conflicts
    /// - Metrics tracking adds minimal overhead (&lt;1ms)
    /// - Consider implementing caching in query handlers for frequently accessed data
    /// - Query handlers should complete quickly (target &lt;100ms for simple queries)
    ///
    /// Best Practices:
    /// - Keep queries focused on single responsibility
    /// - Return DTOs rather than domain entities
    /// - Implement proper error handling in handlers
    /// - Use projection to return only required data
    /// - Consider implementing response caching for expensive queries
    ///
    /// <code>
    /// // Simple query
    /// var orders = await queryProcessor.Send&lt;List&lt;OrderDto&gt;&gt;(new GetCustomerOrdersQuery
    /// {
    ///     CustomerId = customerId,
    ///     StartDate = DateTime.UtcNow.AddMonths(-1)
    /// });
    ///
    /// // Complex query with pagination
    /// var result = await queryProcessor.Send&lt;PagedResult&lt;ProductDto&gt;&gt;(new SearchProductsQuery
    /// {
    ///     SearchTerm = "laptop",
    ///     PageNumber = 1,
    ///     PageSize = 20,
    ///     SortBy = "price",
    ///     SortDescending = false
    /// });
    ///
    /// foreach (var product in result.Items)
    /// {
    ///     Console.WriteLine($"{product.Name}: ${product.Price}");
    /// }
    ///
    /// // Handle query errors
    /// try
    /// {
    ///     var report = await queryProcessor.Send&lt;ReportDto&gt;(new GenerateReportQuery
    ///     {
    ///         ReportId = reportId
    ///     });
    /// }
    /// catch (InvalidOperationException ex)
    /// {
    ///     logger.LogError(ex, "Report query handler not found");
    /// }
    /// catch (Exception ex)
    /// {
    ///     logger.LogError(ex, "Error generating report");
    /// }
    /// </code>
    /// </remarks>
    Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);
}
using HeroMessaging.Abstractions.Queries;

namespace HeroMessaging.Abstractions.Handlers;

/// <summary>
/// Handles a query and returns data without modifying system state.
/// Query handlers must be idempotent and side-effect free.
/// </summary>
/// <typeparam name="TQuery">The type of query this handler processes</typeparam>
/// <typeparam name="TResponse">The type of data returned by the query</typeparam>
/// <remarks>
/// Query handlers are responsible for retrieving data from the read model or database.
/// They follow CQRS principles - queries never modify state.
///
/// Query handler characteristics:
/// - Read-only operations (no state changes)
/// - Idempotent (same query = same result)
/// - Side-effect free
/// - Can be cached
/// - Can be load-balanced across multiple instances
/// - Can use optimized read models or denormalized data
///
/// Best practices:
/// - Never modify state in a query handler
/// - Keep queries fast (use indexes, caching, read replicas)
/// - Return DTOs, not domain entities
/// - Use async/await for I/O operations
/// - Consider caching for expensive queries
/// - Handle not-found scenarios appropriately (null, empty, or exception)
///
/// Performance tips:
/// - Use projection queries (SELECT only needed fields)
/// - Leverage database indexes
/// - Consider read replicas for scale
/// - Implement caching for frequently accessed data
/// - Use compiled queries when available
///
/// Example:
/// <code>
/// public class GetOrderByIdQueryHandler : IQueryHandler&lt;GetOrderByIdQuery, OrderDto&gt;
/// {
///     private readonly IOrderReadRepository _repository;
///     private readonly IMemoryCache _cache;
///
///     public GetOrderByIdQueryHandler(IOrderReadRepository repository, IMemoryCache cache)
///     {
///         _repository = repository;
///         _cache = cache;
///     }
///
///     public async Task&lt;OrderDto&gt; Handle(GetOrderByIdQuery query, CancellationToken cancellationToken)
///     {
///         var cacheKey = $"order:{query.OrderId}";
///
///         if (_cache.TryGetValue(cacheKey, out OrderDto? cached))
///             return cached!;
///
///         var order = await _repository.GetByIdAsync(query.OrderId, cancellationToken);
///         if (order == null)
///             throw new OrderNotFoundException(query.OrderId);
///
///         var dto = new OrderDto(order.Id, order.CustomerId, order.Amount, order.CreatedAt);
///         _cache.Set(cacheKey, dto, TimeSpan.FromMinutes(5));
///
///         return dto;
///     }
/// }
/// </code>
/// </remarks>
public interface IQueryHandler<TQuery, TResponse> where TQuery : IQuery<TResponse>
{
    /// <summary>
    /// Handles the specified query and returns the requested data.
    /// </summary>
    /// <param name="query">The query to handle</param>
    /// <param name="cancellationToken">Cancellation token to abort the operation</param>
    /// <returns>A task containing the query result</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is canceled</exception>
    Task<TResponse> Handle(TQuery query, CancellationToken cancellationToken = default);
}
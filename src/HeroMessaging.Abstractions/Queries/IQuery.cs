using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Queries;

/// <summary>
/// Represents a query in the CQRS pattern - a request for data that does not modify system state.
/// Queries always return a response and must be side-effect free (idempotent and safe).
/// </summary>
/// <typeparam name="TResponse">The type of data returned by the query</typeparam>
/// <remarks>
/// Queries represent requests to read data without changing system state.
/// They follow the Query pattern and are processed by <see cref="Handlers.IQueryHandler{TQuery,TResponse}"/>.
///
/// Queries should:
/// - Use question-like naming (GetOrderById, FindCustomersByName, SearchProducts)
/// - Be immutable (use records or readonly properties)
/// - Never modify system state (read-only operations)
/// - Always return a TResponse (queries must provide data)
/// - Be idempotent (same query = same result)
/// - Be cacheable when appropriate
///
/// Queries differ from Commands:
/// - Commands: Write operations (Create/Update/Delete)
/// - Queries: Read operations (Get/Find/Search)
///
/// Example:
/// <code>
/// public record GetOrderByIdQuery(string OrderId) : IQuery&lt;OrderDto&gt;;
/// public record OrderDto(string OrderId, string CustomerId, decimal Amount, DateTime CreatedAt);
/// </code>
/// </remarks>
public interface IQuery<TResponse> : IMessage<TResponse>
{
}
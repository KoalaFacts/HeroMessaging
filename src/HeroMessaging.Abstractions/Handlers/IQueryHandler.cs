using HeroMessaging.Abstractions.Queries;

namespace HeroMessaging.Abstractions.Handlers;

/// <summary>
/// Handles a query and returns a response. Queries should be side-effect free.
/// </summary>
/// <typeparam name="TQuery">The type of query to handle</typeparam>
/// <typeparam name="TResponse">The type of response returned</typeparam>
public interface IQueryHandler<TQuery, TResponse> where TQuery : IQuery<TResponse>
{
    /// <summary>
    /// Handles the query and returns a response asynchronously.
    /// </summary>
    /// <param name="query">The query to handle</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The query response</returns>
    Task<TResponse> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}

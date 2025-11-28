using HeroMessaging.Abstractions.Queries;

namespace HeroMessaging.Abstractions;

/// <summary>
/// Provides query sending capabilities.
/// </summary>
/// <remarks>
/// This interface is a subset of <see cref="IHeroMessaging"/> for consumers
/// that only need to send queries. Use this for dependency injection when
/// your component only needs query capabilities.
/// </remarks>
public interface IQuerySender
{
    /// <summary>
    /// Sends a query and awaits its response from the registered handler.
    /// </summary>
    /// <typeparam name="TResponse">The type of response expected from the query handler.</typeparam>
    /// <param name="query">The query to execute.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The response from the query handler.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no handler is registered for the query type.</exception>
    Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);
}

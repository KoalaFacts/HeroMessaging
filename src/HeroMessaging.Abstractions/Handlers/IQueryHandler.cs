using HeroMessaging.Abstractions.Queries;

namespace HeroMessaging.Abstractions.Handlers;

public interface IQueryHandler<TQuery, TResponse> where TQuery : IQuery<TResponse>
{
    Task<TResponse> Handle(TQuery query, CancellationToken cancellationToken = default);
}
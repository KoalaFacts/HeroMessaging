using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Queries;

/// <summary>
/// Marker interface for query messages that request data without side effects.
/// Queries are handled by exactly one handler and always return a response.
/// </summary>
/// <typeparam name="TResponse">The type of response returned by the query handler</typeparam>
public interface IQuery<TResponse> : IMessage<TResponse>
{
}

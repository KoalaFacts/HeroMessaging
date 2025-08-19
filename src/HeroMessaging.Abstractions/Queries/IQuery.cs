using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Queries;

public interface IQuery<TResponse> : IMessage<TResponse>
{
}
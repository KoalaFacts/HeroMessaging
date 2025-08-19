using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Commands;

public interface ICommand : IMessage
{
}

public interface ICommand<TResponse> : ICommand, IMessage<TResponse>
{
}
using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Commands;

/// <summary>
/// Marker interface for command messages that represent intent to change state.
/// Commands are handled by exactly one handler and may return a response.
/// </summary>
public interface ICommand : IMessage
{
}

/// <summary>
/// Command message that returns a response of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TResponse">The type of response returned by the command handler</typeparam>
public interface ICommand<TResponse> : ICommand, IMessage<TResponse>
{
}

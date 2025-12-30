using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Events;

/// <summary>
/// Marker interface for event messages that represent something that has happened.
/// Events can be handled by multiple handlers (publish/subscribe pattern).
/// </summary>
public interface IEvent : IMessage
{
}

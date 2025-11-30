using HeroMessaging.Abstractions.Events;

namespace HeroMessaging.Abstractions.Handlers;

public interface IEventHandler<TEvent> where TEvent : IEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}

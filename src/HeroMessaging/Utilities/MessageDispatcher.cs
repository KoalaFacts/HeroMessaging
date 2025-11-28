using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Utilities;

/// <summary>
/// Utility for dispatching messages to the appropriate processor based on message type.
/// Centralizes the command/event routing logic used by inbox, outbox, and queue processors.
/// </summary>
internal static class MessageDispatcher
{
    /// <summary>
    /// Dispatches a message to the appropriate handler via IHeroMessaging.
    /// </summary>
    /// <param name="messaging">The messaging service to dispatch through.</param>
    /// <param name="message">The message to dispatch.</param>
    /// <param name="logger">Optional logger for unknown message types.</param>
    /// <param name="source">Source identifier for logging (e.g., "inbox", "outbox", "queue").</param>
    /// <returns>True if the message was dispatched, false if type was unknown.</returns>
    public static async Task<bool> DispatchAsync(
        IHeroMessaging messaging,
        IMessage message,
        ILogger? logger = null,
        string? source = null)
    {
        switch (message)
        {
            case ICommand command:
                await messaging.SendAsync(command).ConfigureAwait(false);
                return true;

            case IEvent @event:
                await messaging.PublishAsync(@event).ConfigureAwait(false);
                return true;

            default:
                logger?.LogWarning(
                    "Unknown message type in {Source}: {MessageType}",
                    source ?? "processor",
                    message.GetType().Name);
                return false;
        }
    }
}

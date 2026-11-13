using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Processing;
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
    /// <param name="cancellationToken">Cancellation token for message handlers.</param>
    /// <returns>True if the message was dispatched, false if type was unknown.</returns>
    public static async Task<bool> DispatchAsync(
        IHeroMessaging messaging,
        IMessage message,
        ILogger? logger = null,
        string? source = null,
        CancellationToken cancellationToken = default)
    {
        switch (message)
        {
            case ICommand command:
                await messaging.SendAsync(command, cancellationToken).ConfigureAwait(false);
                return true;

            case IEvent @event:
                // Wait for every handler: a persisted entry must not be marked processed while a
                // handler is still queued, running or has failed.
                // The processor's token must reach the handlers so a handler still running at stop is cancelled.
                var receipt = (messaging is IHandlerCancellableEventPublisher publisher
                    ? await publisher.PublishAndWaitWithHandlerCancellationAsync(@event, cancellationToken).ConfigureAwait(false)
                    : await messaging.PublishAndWaitAsync(@event, cancellationToken).ConfigureAwait(false))
                    ?? throw new InvalidOperationException(
                        $"[HM-DISPATCH-EVENT-HANDLER] Event {@event.GetType().Name} ({@event.MessageId}) from {source ?? "processor"} " +
                        "returned no publish receipt. Use an IHeroMessaging implementation whose PublishAndWaitAsync returns a receipt.");
                if (!receipt.Success)
                    throw CreateHandlerFailure(@event, receipt, source);
                return true;

            default:
                logger?.LogWarning(
                    "Unknown message type in {Source}: {MessageType}",
                    source ?? "processor",
                    message.GetType().Name);
                return false;
        }
    }

    private static InvalidOperationException CreateHandlerFailure(IEvent @event, EventPublishReceipt receipt, string? source)
    {
        var failed = receipt.Handlers.Where(handler => handler.Status != EventHandlerStatus.Succeeded).ToArray();
        var details = string.Join("; ", failed.Select(Describe));
        var inner = failed.Select(handler => handler.Result?.Exception).FirstOrDefault(exception => exception is not null);

        return new InvalidOperationException(
            $"[HM-DISPATCH-EVENT-HANDLER] Event {@event.GetType().Name} ({@event.MessageId}) from {source ?? "processor"} " +
            $"was not handled successfully: {details}. Fix the failing handler; the entry is retried or marked failed by the {source ?? "processor"} policy.",
            inner);
    }

    private static string Describe(EventHandlerReceipt handler)
    {
        var reason = handler.Result?.Exception?.Message ?? handler.Result?.Message;
        return reason is null
            ? $"{handler.HandlerType.Name}: {handler.Status}"
            : $"{handler.HandlerType.Name}: {handler.Status} ({reason})";
    }
}

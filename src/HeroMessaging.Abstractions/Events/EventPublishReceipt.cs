namespace HeroMessaging.Abstractions.Events;

/// <summary>
/// Immutable results of in-process delivery, in handler registration order.
/// This is not a durable or broker acknowledgement.
/// </summary>
public sealed class EventPublishReceipt
{
    /// <summary>Creates a snapshot of all handler delivery outcomes.</summary>
    /// <param name="messageId">The event identifier captured before publication.</param>
    /// <param name="handlers">Outcomes in handler registration order.</param>
    public EventPublishReceipt(Guid messageId, IEnumerable<EventHandlerReceipt> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        MessageId = messageId;
        Handlers = Array.AsReadOnly(handlers.ToArray());
        Success = Handlers.All(handler => handler.Status == EventHandlerStatus.Succeeded);
    }

    /// <summary>Gets the event identifier captured before publication.</summary>
    public Guid MessageId { get; }

    /// <summary>Gets the immutable handler outcomes in registration order.</summary>
    public IReadOnlyList<EventHandlerReceipt> Handlers { get; }

    /// <summary>
    /// Gets whether all handler deliveries succeeded. An empty handler list is a successful no-op,
    /// not evidence that any handler executed.
    /// </summary>
    public bool Success { get; }
}

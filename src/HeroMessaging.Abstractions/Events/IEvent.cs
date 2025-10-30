using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Events;

/// <summary>
/// Represents a domain event - something that has already happened in the system.
/// Events are published to multiple subscribers and represent facts about state changes.
/// </summary>
/// <remarks>
/// Events represent something that has occurred in the past (past tense naming).
/// They follow the Event-Driven Architecture pattern and can be handled by zero or more
/// <see cref="Handlers.IEventHandler{TEvent}"/> implementations.
///
/// Events should:
/// - Use past-tense naming (OrderCreated, CustomerUpdated, PaymentProcessed)
/// - Be immutable (use records or readonly properties)
/// - Contain all data needed to understand what happened
/// - Represent facts (cannot be rejected or validated - they already happened)
/// - Be published after the state change is committed
///
/// Event characteristics:
/// - Fire-and-forget: Publisher doesn't wait for handlers
/// - Multiple handlers: 0 to N handlers can process the same event
/// - No response: Events don't return values
/// - Loose coupling: Publisher doesn't know about subscribers
///
/// Use cases:
/// - Notify other bounded contexts of changes
/// - Trigger side effects (send email, update cache, etc.)
/// - Event sourcing and audit trails
/// - Saga choreography
///
/// Example:
/// <code>
/// public record OrderCreatedEvent(
///     string OrderId,
///     string CustomerId,
///     decimal Amount,
///     DateTime CreatedAt
/// ) : IEvent
/// {
///     public Guid MessageId { get; init; } = Guid.NewGuid();
///     public DateTime Timestamp { get; init; } = DateTime.UtcNow;
///     public string? CorrelationId { get; init; }
///     public string? CausationId { get; init; }
///     public Dictionary&lt;string, object&gt;? Metadata { get; init; }
/// }
/// </code>
/// </remarks>
public interface IEvent : IMessage
{
}
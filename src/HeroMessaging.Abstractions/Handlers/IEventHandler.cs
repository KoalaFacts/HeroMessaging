using HeroMessaging.Abstractions.Events;

namespace HeroMessaging.Abstractions.Handlers;

/// <summary>
/// Handles a domain event. Multiple handlers can process the same event type.
/// Event handlers represent side effects and reactions to state changes that have already occurred.
/// </summary>
/// <typeparam name="TEvent">The type of event this handler processes</typeparam>
/// <remarks>
/// Event handlers process events asynchronously and independently.
/// Unlike commands (1 handler) and queries (1 handler), events can have 0 to N handlers.
///
/// Event handler characteristics:
/// - Multiple handlers can subscribe to the same event
/// - Handlers execute independently (failure in one doesn't affect others)
/// - Fire-and-forget (no response expected)
/// - Eventually consistent
/// - Should be idempotent (safe to process same event multiple times)
///
/// Use cases:
/// - Send notifications (email, SMS, push)
/// - Update read models / projections
/// - Update caches
/// - Trigger workflows or sagas
/// - Log audit trails
/// - Integrate with external systems
/// - Publish to message brokers
///
/// Best practices:
/// - Make handlers idempotent (use event IDs to detect duplicates)
/// - Keep handlers fast and focused
/// - Avoid long-running operations (or use background jobs)
/// - Handle failures gracefully (events may be replayed)
/// - Use correlation IDs for tracing
/// - Don't throw exceptions for business logic failures
/// - Consider using inbox pattern for exactly-once processing
///
/// Error handling:
/// - Transient errors: Retry automatically
/// - Permanent errors: Move to dead-letter queue
/// - Don't let one handler failure block others
///
/// Example:
/// <code>
/// public class OrderCreatedEventHandler : IEventHandler&lt;OrderCreatedEvent&gt;
/// {
///     private readonly IEmailService _emailService;
///     private readonly ILogger&lt;OrderCreatedEventHandler&gt; _logger;
///
///     public OrderCreatedEventHandler(IEmailService emailService, ILogger&lt;OrderCreatedEventHandler&gt; logger)
///     {
///         _emailService = emailService;
///         _logger = logger;
///     }
///
///     public async Task Handle(OrderCreatedEvent @event, CancellationToken cancellationToken)
///     {
///         try
///         {
///             _logger.LogInformation("Sending order confirmation email for order {OrderId}", @event.OrderId);
///
///             await _emailService.SendOrderConfirmationAsync(
///                 @event.CustomerId,
///                 @event.OrderId,
///                 @event.Amount,
///                 cancellationToken);
///
///             _logger.LogInformation("Order confirmation email sent successfully for order {OrderId}", @event.OrderId);
///         }
///         catch (Exception ex)
///         {
///             _logger.LogError(ex, "Failed to send order confirmation email for order {OrderId}", @event.OrderId);
///             // Don't throw - let other handlers continue processing
///             // Event will be retried based on retry policy
///         }
///     }
/// }
/// </code>
/// </remarks>
public interface IEventHandler<TEvent> where TEvent : IEvent
{
    /// <summary>
    /// Handles the specified event.
    /// </summary>
    /// <param name="event">The event to handle</param>
    /// <param name="cancellationToken">Cancellation token to abort the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// Event handlers should be idempotent and handle failures gracefully.
    /// Exceptions will cause the event to be retried based on the configured retry policy.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown when the operation is canceled</exception>
    Task Handle(TEvent @event, CancellationToken cancellationToken = default);
}
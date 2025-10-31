using HeroMessaging.Abstractions.Scheduling;

namespace HeroMessaging.Scheduling;

/// <summary>
/// Defines the contract for handling scheduled message delivery and delivery failures.
/// Responsible for delivering messages when they become due and managing delivery failure scenarios
/// including retry logic, dead-lettering, and error escalation.
/// </summary>
/// <remarks>
/// The message delivery handler is the bridge between the message scheduler and the message transport.
/// When a scheduled message's delivery time arrives, the scheduler invokes this handler to perform
/// the actual delivery operation. If delivery fails, the handler manages retry policies and failure handling.
///
/// Core responsibilities:
/// - Deliver scheduled messages to their destination queue/topic at the scheduled time
/// - Handle delivery failures with appropriate retry strategies
/// - Integrate with error handling and dead-letter queue systems
/// - Track delivery attempts and update scheduling state
/// - Provide observability for scheduled message delivery
///
/// Typical workflow:
/// 1. Scheduler polls for due messages and finds one ready for delivery
/// 2. Scheduler calls <see cref="DeliverAsync"/> with the scheduled message
/// 3. Handler retrieves the original message from storage
/// 4. Handler publishes the message to the transport (queue/topic)
/// 5. On success: Message is marked as delivered and removed from scheduling storage
/// 6. On failure: <see cref="HandleDeliveryFailureAsync"/> is called to determine retry/failure action
///
/// Integration patterns:
///
/// **Basic Implementation with Retry**
/// <code>
/// public class MessageDeliveryHandler : IMessageDeliveryHandler
/// {
///     private readonly IMessageBus _messageBus;
///     private readonly IScheduleStorage _storage;
///     private readonly ILogger _logger;
///
///     public MessageDeliveryHandler(
///         IMessageBus messageBus,
///         IScheduleStorage storage,
///         ILogger&lt;MessageDeliveryHandler&gt; logger)
///     {
///         _messageBus = messageBus;
///         _storage = storage;
///         _logger = logger;
///     }
///
///     public async Task DeliverAsync(
///         ScheduledMessage scheduledMessage,
///         CancellationToken cancellationToken)
///     {
///         _logger.LogInformation("Delivering scheduled message {ScheduleId}", scheduledMessage.ScheduleId);
///
///         // Retrieve original message from storage
///         var message = await _storage.GetMessageAsync(scheduledMessage.ScheduleId, cancellationToken);
///
///         // Publish to destination
///         await _messageBus.PublishAsync(message, scheduledMessage.Destination, cancellationToken);
///
///         // Mark as delivered
///         await _storage.MarkDeliveredAsync(scheduledMessage.ScheduleId, cancellationToken);
///
///         _logger.LogInformation("Successfully delivered scheduled message {ScheduleId}", scheduledMessage.ScheduleId);
///     }
///
///     public async Task HandleDeliveryFailureAsync(
///         Guid scheduleId,
///         Exception exception,
///         CancellationToken cancellationToken)
///     {
///         _logger.LogError(exception, "Failed to deliver scheduled message {ScheduleId}", scheduleId);
///
///         var scheduledMessage = await _storage.GetScheduledMessageAsync(scheduleId, cancellationToken);
///
///         // Retry transient failures with exponential backoff
///         if (exception is TimeoutException || exception is HttpRequestException)
///         {
///             if (scheduledMessage.RetryCount &lt; 3)
///             {
///                 var delay = TimeSpan.FromSeconds(Math.Pow(2, scheduledMessage.RetryCount));
///                 var nextAttempt = DateTime.UtcNow.Add(delay);
///
///                 await _storage.RescheduleAsync(scheduleId, nextAttempt, cancellationToken);
///                 _logger.LogWarning("Rescheduled message {ScheduleId} for retry at {NextAttempt}", scheduleId, nextAttempt);
///                 return;
///             }
///         }
///
///         // Max retries exceeded - send to dead-letter queue
///         _logger.LogError("Max retries exceeded for {ScheduleId}, sending to DLQ", scheduleId);
///         await _storage.MoveToDeadLetterAsync(scheduleId, exception.Message, cancellationToken);
///     }
/// }
/// </code>
///
/// **Advanced Implementation with Circuit Breaker**
/// <code>
/// public class ResilientMessageDeliveryHandler : IMessageDeliveryHandler
/// {
///     private readonly IMessageBus _messageBus;
///     private readonly IScheduleStorage _storage;
///     private readonly ICircuitBreaker _circuitBreaker;
///     private readonly IMetrics _metrics;
///
///     public async Task DeliverAsync(
///         ScheduledMessage scheduledMessage,
///         CancellationToken cancellationToken)
///     {
///         using var activity = _metrics.StartActivity("ScheduledMessage.Deliver");
///         activity.AddTag("scheduleId", scheduledMessage.ScheduleId);
///
///         try
///         {
///             // Execute with circuit breaker protection
///             await _circuitBreaker.ExecuteAsync(async () =>
///             {
///                 var message = await _storage.GetMessageAsync(scheduledMessage.ScheduleId, cancellationToken);
///                 await _messageBus.PublishAsync(message, scheduledMessage.Destination, cancellationToken);
///             }, cancellationToken);
///
///             await _storage.MarkDeliveredAsync(scheduledMessage.ScheduleId, cancellationToken);
///             _metrics.IncrementCounter("scheduled_messages_delivered");
///         }
///         catch (Exception ex)
///         {
///             _metrics.IncrementCounter("scheduled_messages_delivery_failed");
///             throw; // Let scheduler invoke HandleDeliveryFailureAsync
///         }
///     }
///
///     public async Task HandleDeliveryFailureAsync(
///         Guid scheduleId,
///         Exception exception,
///         CancellationToken cancellationToken)
///     {
///         var scheduledMessage = await _storage.GetScheduledMessageAsync(scheduleId, cancellationToken);
///
///         // Circuit is open - reschedule for later
///         if (exception is CircuitBreakerOpenException)
///         {
///             var nextAttempt = DateTime.UtcNow.AddMinutes(5);
///             await _storage.RescheduleAsync(scheduleId, nextAttempt, cancellationToken);
///             _metrics.IncrementCounter("scheduled_messages_circuit_breaker_delay");
///             return;
///         }
///
///         // Apply retry policy based on exception type
///         var retryStrategy = DetermineRetryStrategy(exception, scheduledMessage.RetryCount);
///
///         if (retryStrategy.ShouldRetry)
///         {
///             var nextAttempt = DateTime.UtcNow.Add(retryStrategy.Delay);
///             await _storage.IncrementRetryAsync(scheduleId, nextAttempt, cancellationToken);
///             _metrics.IncrementCounter("scheduled_messages_retried");
///         }
///         else
///         {
///             await _storage.MoveToDeadLetterAsync(scheduleId, exception.Message, cancellationToken);
///             _metrics.IncrementCounter("scheduled_messages_dead_lettered");
///         }
///     }
/// }
/// </code>
///
/// **Registration in DI Container**
/// <code>
/// services.AddScoped&lt;IMessageDeliveryHandler, MessageDeliveryHandler&gt;();
///
/// // Or with decorator pattern for added functionality
/// services.AddScoped&lt;IMessageDeliveryHandler, MessageDeliveryHandler&gt;();
/// services.Decorate&lt;IMessageDeliveryHandler, MetricsDecorator&gt;();
/// services.Decorate&lt;IMessageDeliveryHandler, RetryDecorator&gt;();
/// </code>
///
/// Error handling patterns:
/// - Transient errors (network, timeout): Retry with exponential backoff
/// - Permanent errors (validation, not found): Send to dead-letter queue immediately
/// - Circuit breaker open: Delay retry to allow recovery
/// - Max retries exceeded: Dead-letter with detailed failure reason
///
/// Performance considerations:
/// - Keep delivery operations fast (&lt;100ms target)
/// - Use async I/O for all network/storage operations
/// - Implement timeouts to prevent hanging deliveries
/// - Batch storage operations when possible
/// - Monitor delivery latency and failure rates
///
/// Best practices:
/// - Always use structured logging with correlation IDs
/// - Emit metrics for delivery success/failure rates
/// - Implement health checks for delivery handler availability
/// - Use circuit breakers to prevent cascading failures
/// - Document retry policies and failure scenarios
/// - Test failure scenarios in integration tests
/// </remarks>
public interface IMessageDeliveryHandler
{
    /// <summary>
    /// Delivers a scheduled message that has become due for delivery.
    /// </summary>
    /// <param name="scheduledMessage">
    /// The scheduled message to deliver. Contains the schedule ID, destination, delivery time,
    /// and metadata needed for delivery tracking. Must not be null.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token to cancel the delivery operation. Useful for graceful shutdown
    /// and timeout enforcement.
    /// </param>
    /// <returns>
    /// A task that completes when the message has been successfully delivered and marked as delivered
    /// in the schedule storage. The task completes synchronously if the delivery succeeds.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scheduledMessage"/> is null.</exception>
    /// <exception cref="MessageNotFoundException">
    /// Thrown when the scheduled message cannot be found in storage.
    /// This typically indicates the message was deleted or the schedule ID is invalid.
    /// </exception>
    /// <exception cref="DeliveryException">
    /// Thrown when message delivery fails due to transport errors, destination unavailability,
    /// or other delivery-related failures. The scheduler will call <see cref="HandleDeliveryFailureAsync"/>
    /// to handle the failure.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when the operation is cancelled via the <paramref name="cancellationToken"/>.
    /// </exception>
    /// <remarks>
    /// This method is called by the message scheduler when a scheduled message's delivery time
    /// has arrived. The handler should:
    /// 1. Retrieve the original message payload from schedule storage
    /// 2. Publish the message to the destination queue/topic via the message transport
    /// 3. Mark the scheduled message as delivered in storage
    /// 4. Handle any errors by throwing appropriate exceptions
    ///
    /// The scheduler will catch exceptions and invoke <see cref="HandleDeliveryFailureAsync"/>
    /// to determine the appropriate failure handling action (retry, dead-letter, escalate).
    ///
    /// Delivery process:
    /// <code>
    /// public async Task DeliverAsync(
    ///     ScheduledMessage scheduledMessage,
    ///     CancellationToken cancellationToken)
    /// {
    ///     // 1. Retrieve message payload from storage
    ///     var message = await _storage.GetMessageAsync(
    ///         scheduledMessage.ScheduleId,
    ///         cancellationToken);
    ///
    ///     // 2. Publish to destination
    ///     await _transport.PublishAsync(
    ///         message,
    ///         scheduledMessage.Destination,
    ///         cancellationToken);
    ///
    ///     // 3. Mark as delivered
    ///     await _storage.MarkDeliveredAsync(
    ///         scheduledMessage.ScheduleId,
    ///         cancellationToken);
    /// }
    /// </code>
    ///
    /// Error handling:
    /// <code>
    /// try
    /// {
    ///     await deliveryHandler.DeliverAsync(scheduledMessage, cancellationToken);
    /// }
    /// catch (TimeoutException ex)
    /// {
    ///     // Scheduler will call HandleDeliveryFailureAsync
    ///     // which may reschedule for retry
    ///     await deliveryHandler.HandleDeliveryFailureAsync(
    ///         scheduledMessage.ScheduleId,
    ///         ex,
    ///         cancellationToken);
    /// }
    /// </code>
    ///
    /// Performance considerations:
    /// - Target &lt;100ms for typical message delivery
    /// - Use async I/O for all storage and transport operations
    /// - Implement timeouts to prevent hanging deliveries (5-30 seconds typical)
    /// - Batch storage operations if delivering multiple messages
    /// - Monitor delivery latency with metrics/tracing
    ///
    /// Concurrency:
    /// - This method may be called concurrently for different scheduled messages
    /// - Ensure thread-safe access to shared resources
    /// - Each scheduled message is delivered independently
    /// - Use proper locking if modifying shared state
    ///
    /// Idempotency:
    /// - Consider implementing idempotent delivery to handle retries safely
    /// - Use unique message IDs to detect duplicate deliveries
    /// - Store delivery receipts to prevent duplicate processing
    /// - Handle "already delivered" scenarios gracefully
    /// </remarks>
    Task DeliverAsync(ScheduledMessage scheduledMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles a failure that occurred during scheduled message delivery.
    /// Determines the appropriate action such as retrying, dead-lettering, or escalating the failure.
    /// </summary>
    /// <param name="scheduleId">
    /// The unique identifier of the scheduled message that failed delivery.
    /// Used to retrieve the scheduled message details and update its state in storage.
    /// </param>
    /// <param name="exception">
    /// The exception that occurred during delivery. Contains diagnostic information about
    /// the failure cause, used to determine the appropriate failure handling strategy.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token to cancel the failure handling operation. Useful for graceful shutdown.
    /// </param>
    /// <returns>
    /// A task that completes when the failure has been handled. The specific action taken
    /// (retry, dead-letter, escalate) depends on the exception type, retry count, and failure policy.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="scheduleId"/> is empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="exception"/> is null.</exception>
    /// <exception cref="MessageNotFoundException">
    /// Thrown when the scheduled message cannot be found in storage using the provided schedule ID.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when the operation is cancelled via the <paramref name="cancellationToken"/>.
    /// </exception>
    /// <remarks>
    /// This method is called by the scheduler when <see cref="DeliverAsync"/> throws an exception.
    /// The handler analyzes the failure and determines the appropriate action:
    /// - Retry with delay for transient errors (network, timeout)
    /// - Dead-letter for permanent errors (validation, not found) or max retries exceeded
    /// - Escalate for critical errors requiring immediate attention
    ///
    /// Common failure handling patterns:
    ///
    /// **Transient Errors with Exponential Backoff**
    /// <code>
    /// public async Task HandleDeliveryFailureAsync(
    ///     Guid scheduleId,
    ///     Exception exception,
    ///     CancellationToken cancellationToken)
    /// {
    ///     var scheduledMessage = await _storage.GetScheduledMessageAsync(scheduleId, cancellationToken);
    ///
    ///     // Retry transient errors with exponential backoff
    ///     if (exception is TimeoutException or HttpRequestException)
    ///     {
    ///         if (scheduledMessage.RetryCount &lt; 3)
    ///         {
    ///             var delay = TimeSpan.FromSeconds(Math.Pow(2, scheduledMessage.RetryCount));
    ///             var nextAttempt = DateTime.UtcNow.Add(delay);
    ///
    ///             await _storage.IncrementRetryAsync(scheduleId, nextAttempt, cancellationToken);
    ///             _logger.LogWarning("Retrying delivery of {ScheduleId} at {NextAttempt}", scheduleId, nextAttempt);
    ///             return;
    ///         }
    ///     }
    ///
    ///     // Max retries or permanent error - dead-letter
    ///     await _storage.MoveToDeadLetterAsync(scheduleId, exception.Message, cancellationToken);
    ///     _logger.LogError(exception, "Dead-lettered {ScheduleId} after delivery failure", scheduleId);
    /// }
    /// </code>
    ///
    /// **Exception-Based Failure Strategies**
    /// <code>
    /// public async Task HandleDeliveryFailureAsync(
    ///     Guid scheduleId,
    ///     Exception exception,
    ///     CancellationToken cancellationToken)
    /// {
    ///     var scheduledMessage = await _storage.GetScheduledMessageAsync(scheduleId, cancellationToken);
    ///
    ///     var strategy = exception switch
    ///     {
    ///         TimeoutException => RetryStrategy.ExponentialBackoff(maxRetries: 3),
    ///         HttpRequestException => RetryStrategy.ExponentialBackoff(maxRetries: 5),
    ///         ValidationException => RetryStrategy.NoRetry(), // Dead-letter immediately
    ///         CircuitBreakerOpenException => RetryStrategy.FixedDelay(TimeSpan.FromMinutes(5)),
    ///         _ => RetryStrategy.LinearBackoff(maxRetries: 3)
    ///     };
    ///
    ///     if (strategy.ShouldRetry(scheduledMessage.RetryCount))
    ///     {
    ///         var nextAttempt = DateTime.UtcNow.Add(strategy.GetDelay(scheduledMessage.RetryCount));
    ///         await _storage.IncrementRetryAsync(scheduleId, nextAttempt, cancellationToken);
    ///     }
    ///     else
    ///     {
    ///         await _storage.MoveToDeadLetterAsync(scheduleId, exception.Message, cancellationToken);
    ///     }
    /// }
    /// </code>
    ///
    /// **Integration with Error Handler**
    /// <code>
    /// public async Task HandleDeliveryFailureAsync(
    ///     Guid scheduleId,
    ///     Exception exception,
    ///     CancellationToken cancellationToken)
    /// {
    ///     var scheduledMessage = await _storage.GetScheduledMessageAsync(scheduleId, cancellationToken);
    ///     var message = await _storage.GetMessageAsync(scheduleId, cancellationToken);
    ///
    ///     // Delegate to error handler for consistent failure handling
    ///     var errorContext = new ErrorContext
    ///     {
    ///         RetryCount = scheduledMessage.RetryCount,
    ///         MaxRetries = 3,
    ///         Component = "ScheduledMessageDelivery",
    ///         QueueName = scheduledMessage.Destination,
    ///         FirstFailureTime = scheduledMessage.FirstFailureTime,
    ///         LastFailureTime = DateTime.UtcNow
    ///     };
    ///
    ///     var result = await _errorHandler.HandleError(message, exception, errorContext, cancellationToken);
    ///
    ///     switch (result.Action)
    ///     {
    ///         case ErrorAction.Retry:
    ///             var nextAttempt = DateTime.UtcNow.Add(result.RetryDelay ?? TimeSpan.FromSeconds(5));
    ///             await _storage.IncrementRetryAsync(scheduleId, nextAttempt, cancellationToken);
    ///             break;
    ///
    ///         case ErrorAction.SendToDeadLetter:
    ///             await _storage.MoveToDeadLetterAsync(scheduleId, result.Reason, cancellationToken);
    ///             break;
    ///
    ///         case ErrorAction.Discard:
    ///             await _storage.DeleteAsync(scheduleId, cancellationToken);
    ///             break;
    ///
    ///         case ErrorAction.Escalate:
    ///             _logger.LogCritical(exception, "Critical failure for {ScheduleId}", scheduleId);
    ///             throw; // Re-throw for higher-level handling
    ///     }
    /// }
    /// </code>
    ///
    /// Retry strategies:
    /// - Exponential backoff: 1s, 2s, 4s, 8s, 16s... (for transient errors)
    /// - Linear backoff: 5s, 10s, 15s, 20s... (for rate limiting)
    /// - Fixed delay: 1 minute (for circuit breaker open)
    /// - No retry: Immediate dead-letter (for validation errors)
    ///
    /// Performance considerations:
    /// - Complete failure handling in &lt;50ms when possible
    /// - Avoid expensive operations in failure path
    /// - Use async I/O for storage updates
    /// - Monitor retry and dead-letter rates
    /// - Alert on high failure rates
    ///
    /// Best practices:
    /// - Log all failures with full exception details
    /// - Include correlation IDs for distributed tracing
    /// - Emit metrics for different failure types
    /// - Configure appropriate max retries (3-5 typical)
    /// - Use exponential backoff for transient errors
    /// - Dead-letter after max retries to prevent infinite loops
    /// - Store failure reason with dead-lettered messages
    /// - Monitor dead-letter queue for manual intervention
    /// </remarks>
    Task HandleDeliveryFailureAsync(Guid scheduleId, Exception exception, CancellationToken cancellationToken = default);
}

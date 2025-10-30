using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.ErrorHandling;

/// <summary>
/// Defines the contract for handling message processing errors in the HeroMessaging system.
/// Implements custom error handling strategies including retry logic, dead-letter routing, and error escalation.
/// </summary>
/// <remarks>
/// The error handler is invoked when a message handler throws an exception during processing.
/// It determines the appropriate action to take based on the error type, retry count, and context.
///
/// Common error handling patterns:
/// - Transient errors (network, timeout): Retry with exponential backoff
/// - Validation errors: Send to dead-letter queue or discard
/// - Critical errors: Escalate to monitoring/alerting systems
/// - Max retries exceeded: Send to dead-letter queue for manual intervention
///
/// Example implementation:
/// <code>
/// public class DefaultErrorHandler : IErrorHandler
/// {
///     public async Task&lt;ErrorHandlingResult&gt; HandleError&lt;T&gt;(
///         T message, Exception error, ErrorContext context, CancellationToken cancellationToken)
///     {
///         // Retry transient errors with exponential backoff
///         if (error is TimeoutException || error is HttpRequestException)
///         {
///             if (context.RetryCount &lt; context.MaxRetries)
///             {
///                 var delay = TimeSpan.FromSeconds(Math.Pow(2, context.RetryCount));
///                 return ErrorHandlingResult.Retry(delay);
///             }
///         }
///
///         // Send to dead-letter queue after max retries
///         if (context.RetryCount >= context.MaxRetries)
///         {
///             return ErrorHandlingResult.SendToDeadLetter($"Max retries exceeded: {error.Message}");
///         }
///
///         // Discard invalid messages
///         if (error is ValidationException)
///         {
///             return ErrorHandlingResult.Discard($"Invalid message: {error.Message}");
///         }
///
///         // Escalate critical errors
///         return ErrorHandlingResult.Escalate();
///     }
/// }
/// </code>
///
/// Register in DI container:
/// <code>
/// services.AddSingleton&lt;IErrorHandler, DefaultErrorHandler&gt;();
/// </code>
/// </remarks>
public interface IErrorHandler
{
    /// <summary>
    /// Handles an error that occurred during message processing and determines the appropriate action.
    /// </summary>
    /// <typeparam name="T">The type of message that failed processing. Must implement <see cref="IMessage"/>.</typeparam>
    /// <param name="message">The message that failed to process</param>
    /// <param name="error">The exception that occurred during processing</param>
    /// <param name="context">Context information about the error including retry count, component, and metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// A task containing the <see cref="ErrorHandlingResult"/> that specifies the action to take
    /// (Retry, SendToDeadLetter, Discard, or Escalate)
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when message, error, or context is null</exception>
    /// <remarks>
    /// This method is called by the messaging infrastructure when a handler throws an exception.
    /// The returned action determines how the system handles the failure:
    /// - <see cref="ErrorAction.Retry"/>: Attempt to process the message again after the specified delay
    /// - <see cref="ErrorAction.SendToDeadLetter"/>: Move to dead-letter queue for later inspection/retry
    /// - <see cref="ErrorAction.Discard"/>: Permanently discard the message
    /// - <see cref="ErrorAction.Escalate"/>: Re-throw exception to caller for higher-level handling
    /// </remarks>
    Task<ErrorHandlingResult> HandleError<T>(T message, Exception error, ErrorContext context, CancellationToken cancellationToken = default) where T : IMessage;
}

/// <summary>
/// Provides contextual information about an error that occurred during message processing.
/// Contains retry state, component information, timing data, and custom metadata.
/// </summary>
/// <remarks>
/// This class is passed to <see cref="IErrorHandler.HandleError{T}"/> to provide full context
/// about the failure, enabling intelligent error handling decisions.
///
/// Example usage:
/// <code>
/// var context = new ErrorContext
/// {
///     RetryCount = 2,
///     MaxRetries = 3,
///     Component = "OrderProcessingHandler",
///     QueueName = "orders-queue",
///     FirstFailureTime = DateTime.UtcNow.AddMinutes(-5),
///     LastFailureTime = DateTime.UtcNow,
///     Metadata = new Dictionary&lt;string, object&gt;
///     {
///         ["ErrorCode"] = "TIMEOUT",
///         ["Endpoint"] = "https://api.example.com/orders"
///     }
/// };
/// </code>
/// </remarks>
public class ErrorContext
{
    /// <summary>
    /// Gets or sets the number of times processing has been retried for this message.
    /// Starts at 0 for the first failure, increments with each retry attempt.
    /// </summary>
    /// <remarks>
    /// Use this to implement exponential backoff or determine when to give up retrying.
    /// Compare against <see cref="MaxRetries"/> to enforce retry limits.
    /// </remarks>
    public int RetryCount { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of retry attempts allowed before giving up.
    /// Default policies typically set this to 3-5 retries.
    /// </summary>
    /// <remarks>
    /// When <see cref="RetryCount"/> reaches this value, messages should typically be
    /// sent to the dead-letter queue instead of retrying again.
    /// </remarks>
    public int MaxRetries { get; set; }

    /// <summary>
    /// Gets or sets the name of the component or handler where the error occurred.
    /// Used for diagnostics, logging, and routing errors to specialized handlers.
    /// </summary>
    /// <remarks>
    /// Examples: "OrderProcessingHandler", "PaymentService", "InventoryQueue"
    /// </remarks>
    public string Component { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the queue being processed, if applicable.
    /// Null for non-queued operations (commands, queries, direct event publishing).
    /// </summary>
    /// <remarks>
    /// Used to apply queue-specific error handling policies and track error rates per queue.
    /// </remarks>
    public string? QueueName { get; set; }

    /// <summary>
    /// Gets or sets custom metadata associated with the error.
    /// Can contain error codes, endpoint URLs, correlation IDs, or any diagnostic information.
    /// </summary>
    /// <remarks>
    /// Common metadata keys:
    /// - "ErrorCode": Application-specific error codes
    /// - "CorrelationId": Distributed tracing identifiers
    /// - "Endpoint": API endpoints or service URLs involved
    /// - "UserId": User context when error occurred
    /// - "TenantId": Multi-tenant context
    /// </remarks>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Gets or sets when the message first failed processing.
    /// Used to track how long a message has been failing and enforce time-based policies.
    /// </summary>
    /// <remarks>
    /// Set to the first failure time and should not change across retries.
    /// Compare with current time to implement time-based retry windows
    /// (e.g., give up after 1 hour of retrying).
    /// </remarks>
    public DateTime FirstFailureTime { get; set; }

    /// <summary>
    /// Gets or sets when the message most recently failed processing.
    /// Updated with each retry attempt to track failure frequency.
    /// </summary>
    /// <remarks>
    /// Set to the current time on each failure.
    /// Use to calculate time between retries and implement rate limiting.
    /// </remarks>
    public DateTime LastFailureTime { get; set; }
}

/// <summary>
/// Represents the result of error handling, specifying what action should be taken for a failed message.
/// Provides factory methods for common error handling patterns.
/// </summary>
/// <remarks>
/// Returned by <see cref="IErrorHandler.HandleError{T}"/> to instruct the messaging system
/// how to proceed after a message processing failure.
///
/// Use the static factory methods for creating results:
/// <code>
/// // Retry after 5 seconds
/// return ErrorHandlingResult.Retry(TimeSpan.FromSeconds(5));
///
/// // Send to dead-letter queue with reason
/// return ErrorHandlingResult.SendToDeadLetter("Invalid order format");
///
/// // Discard the message permanently
/// return ErrorHandlingResult.Discard("Duplicate message");
///
/// // Escalate to caller (re-throw exception)
/// return ErrorHandlingResult.Escalate();
/// </code>
/// </remarks>
public class ErrorHandlingResult
{
    /// <summary>
    /// Gets or sets the action to take for the failed message.
    /// See <see cref="ErrorAction"/> for available actions.
    /// </summary>
    public ErrorAction Action { get; set; }

    /// <summary>
    /// Gets or sets the delay before retrying message processing.
    /// Only applicable when <see cref="Action"/> is <see cref="ErrorAction.Retry"/>.
    /// </summary>
    /// <remarks>
    /// Use exponential backoff for transient errors:
    /// - First retry: 1-2 seconds
    /// - Second retry: 4-8 seconds
    /// - Third retry: 16-32 seconds
    /// - Etc.
    /// </remarks>
    public TimeSpan? RetryDelay { get; set; }

    /// <summary>
    /// Gets or sets the human-readable reason for the error handling action.
    /// Stored with dead-letter messages or discard logs for diagnostics.
    /// </summary>
    /// <remarks>
    /// Should be descriptive enough to understand why the action was taken:
    /// - "Max retries exceeded after 3 attempts"
    /// - "Invalid message format: missing required field 'CustomerId'"
    /// - "Timeout connecting to payment service"
    /// </remarks>
    public string? Reason { get; set; }

    /// <summary>
    /// Creates a result indicating the message should be retried after the specified delay.
    /// </summary>
    /// <param name="delay">How long to wait before retrying. Use exponential backoff for transient errors.</param>
    /// <returns>An <see cref="ErrorHandlingResult"/> with <see cref="ErrorAction.Retry"/> action</returns>
    /// <remarks>
    /// Use for transient errors that are likely to succeed on retry:
    /// - Network timeouts
    /// - Temporary service unavailability
    /// - Database deadlocks
    /// - Rate limiting (429 responses)
    ///
    /// Example with exponential backoff:
    /// <code>
    /// var delay = TimeSpan.FromSeconds(Math.Pow(2, context.RetryCount));
    /// return ErrorHandlingResult.Retry(delay);
    /// </code>
    /// </remarks>
    public static ErrorHandlingResult Retry(TimeSpan delay) => new() { Action = ErrorAction.Retry, RetryDelay = delay };

    /// <summary>
    /// Creates a result indicating the message should be moved to the dead-letter queue.
    /// </summary>
    /// <param name="reason">Human-readable explanation of why the message was dead-lettered</param>
    /// <returns>An <see cref="ErrorHandlingResult"/> with <see cref="ErrorAction.SendToDeadLetter"/> action</returns>
    /// <remarks>
    /// Use for messages that cannot be processed successfully but should be retained for investigation:
    /// - Max retries exceeded
    /// - Unrecoverable errors (missing dependencies, broken data)
    /// - Errors requiring manual intervention
    ///
    /// Dead-lettered messages can be:
    /// - Inspected for debugging
    /// - Retried after fixing root cause
    /// - Permanently discarded if invalid
    ///
    /// Example:
    /// <code>
    /// if (context.RetryCount >= context.MaxRetries)
    /// {
    ///     return ErrorHandlingResult.SendToDeadLetter(
    ///         $"Failed after {context.RetryCount} retries: {error.Message}");
    /// }
    /// </code>
    /// </remarks>
    public static ErrorHandlingResult SendToDeadLetter(string reason) => new() { Action = ErrorAction.SendToDeadLetter, Reason = reason };

    /// <summary>
    /// Creates a result indicating the message should be permanently discarded without further processing.
    /// </summary>
    /// <param name="reason">Human-readable explanation of why the message was discarded</param>
    /// <returns>An <see cref="ErrorHandlingResult"/> with <see cref="ErrorAction.Discard"/> action</returns>
    /// <remarks>
    /// Use for messages that are invalid and should not be retained:
    /// - Duplicate messages (already processed)
    /// - Malformed/corrupt messages that cannot be deserialized
    /// - Messages for obsolete/removed features
    /// - Spam or invalid requests
    ///
    /// WARNING: Discarded messages are lost permanently. Use <see cref="SendToDeadLetter"/>
    /// if there's any possibility the message might be needed later.
    ///
    /// Example:
    /// <code>
    /// if (error is MessageCorruptedException)
    /// {
    ///     return ErrorHandlingResult.Discard("Message payload is corrupted and cannot be recovered");
    /// }
    /// </code>
    /// </remarks>
    public static ErrorHandlingResult Discard(string reason) => new() { Action = ErrorAction.Discard, Reason = reason };

    /// <summary>
    /// Creates a result indicating the error should be escalated to the caller (re-throws the exception).
    /// </summary>
    /// <returns>An <see cref="ErrorHandlingResult"/> with <see cref="ErrorAction.Escalate"/> action</returns>
    /// <remarks>
    /// Use when the error handler cannot make a decision and wants to delegate to higher-level error handling:
    /// - Critical system errors
    /// - Security violations
    /// - Unknown error types
    /// - Errors requiring immediate operator attention
    ///
    /// The original exception will be re-thrown, allowing it to bubble up through:
    /// - Application error handlers
    /// - Logging middleware
    /// - Global exception handlers
    /// - Circuit breakers
    ///
    /// Example:
    /// <code>
    /// if (error is SecurityException || error is OutOfMemoryException)
    /// {
    ///     // Critical errors should be escalated immediately
    ///     return ErrorHandlingResult.Escalate();
    /// }
    /// </code>
    /// </remarks>
    public static ErrorHandlingResult Escalate() => new() { Action = ErrorAction.Escalate };
}

/// <summary>
/// Defines the action to take when handling a failed message.
/// </summary>
/// <remarks>
/// These actions are returned by <see cref="IErrorHandler.HandleError{T}"/> to control
/// error handling behavior in the messaging system.
/// </remarks>
public enum ErrorAction
{
    /// <summary>
    /// Retry processing the message after a delay.
    /// Use for transient errors (network issues, timeouts, temporary service unavailability).
    /// Specify retry delay in <see cref="ErrorHandlingResult.RetryDelay"/>.
    /// </summary>
    Retry,

    /// <summary>
    /// Move the message to the dead-letter queue for manual inspection and possible retry.
    /// Use when max retries are exceeded or for errors requiring investigation.
    /// Specify reason in <see cref="ErrorHandlingResult.Reason"/>.
    /// </summary>
    SendToDeadLetter,

    /// <summary>
    /// Permanently discard the message without further processing.
    /// Use for invalid, duplicate, or corrupt messages that should not be retained.
    /// Specify reason in <see cref="ErrorHandlingResult.Reason"/> for audit logs.
    /// </summary>
    Discard,

    /// <summary>
    /// Escalate the error to the caller by re-throwing the exception.
    /// Use for critical errors, security violations, or when unable to make a handling decision.
    /// The original exception will propagate to higher-level error handlers.
    /// </summary>
    Escalate
}
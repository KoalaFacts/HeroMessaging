using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.ErrorHandling;

/// <summary>
/// Default implementation of <see cref="IErrorHandler"/> that provides intelligent error handling with transient error detection, retry logic, and dead-letter queue integration.
/// </summary>
/// <remarks>
/// This error handler implements a comprehensive error handling strategy:
/// - Detects transient errors (timeouts, cancellations) and recommends retry
/// - Identifies critical errors (OOM, stack overflow) and escalates them
/// - Sends messages to dead-letter queue after max retries exceeded
/// - Uses exponential backoff with jitter for retry delays
/// - Tracks retry counts and failure times in error context
///
/// Error Classification:
/// - Transient: TimeoutException, TaskCanceledException, or messages containing "timeout"/"transient"
/// - Critical: OutOfMemoryException, StackOverflowException, AccessViolationException
/// - Retryable: Transient errors within retry limit (default: 3 attempts)
/// - Dead-letter: Non-transient errors or max retries exceeded
///
/// Retry Strategy:
/// - Exponential backoff: delay = 2^retryCount seconds
/// - Jitter: ±30% random variation to prevent thundering herd
/// - Maximum delay: Capped at 30 seconds
/// - Retry count tracking: Maintains attempt count across retries
///
/// This handler is suitable for most messaging scenarios and provides a good
/// balance between resilience and performance.
/// </remarks>
public class DefaultErrorHandler(ILogger<DefaultErrorHandler> logger, IDeadLetterQueue deadLetterQueue, TimeProvider timeProvider) : IErrorHandler
{
    private readonly ILogger<DefaultErrorHandler> _logger = logger;
    private readonly IDeadLetterQueue _deadLetterQueue = deadLetterQueue;
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>
    /// Handles an error that occurred during message processing and determines the appropriate recovery action.
    /// </summary>
    /// <typeparam name="T">The message type that implements <see cref="IMessage"/>.</typeparam>
    /// <param name="message">The message that failed processing. Must not be null.</param>
    /// <param name="error">The exception that occurred during processing. Must not be null.</param>
    /// <param name="context">Context information about the error including component, retry count, and metadata.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>
    /// An <see cref="ErrorHandlingResult"/> indicating the recommended action:
    /// - Retry: For transient errors within retry limit (includes calculated delay)
    /// - Escalate: For critical errors that should not be retried
    /// - SendToDeadLetter: For non-retryable errors or max retries exceeded (includes reason)
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <remarks>
    /// Decision Logic:
    /// 1. If error is transient AND retryCount &lt; maxRetries: Return Retry with exponential backoff delay
    /// 2. If error is critical (OOM, etc.): Return Escalate
    /// 3. If retryCount >= maxRetries: Send to dead-letter queue and return SendToDeadLetter
    /// 4. Default: Send to dead-letter queue and return SendToDeadLetter
    ///
    /// Transient Error Detection:
    /// - Exception type is TimeoutException or TaskCanceledException
    /// - Exception message contains "timeout" or "transient" (case-insensitive)
    /// - Inner exception is transient (recursive check)
    ///
    /// Critical Error Detection:
    /// - OutOfMemoryException: System resource exhaustion
    /// - StackOverflowException: Infinite recursion or excessive call depth
    /// - AccessViolationException: Memory access violation
    ///
    /// Dead-Letter Queue Context:
    /// Messages sent to the dead-letter queue include:
    /// - Failure reason (e.g., "Max retries (3) exceeded")
    /// - Original exception details
    /// - Component that encountered the error
    /// - Retry count at time of failure
    /// - Failure timestamp
    /// - Any custom metadata from error context
    ///
    /// Retry Delay Calculation:
    /// - Base delay: 2^retryCount seconds
    /// - Jitter: ±30% random variation
    /// - Maximum: Capped at 30 seconds
    /// - Example delays: 1s (try 0), 2s (try 1), 4s (try 2), 8s (try 3), etc.
    ///
    /// <code>
    /// // Example: Handler determines retry for timeout
    /// var handler = new DefaultErrorHandler(logger, deadLetterQueue, timeProvider);
    /// var error = new TimeoutException("Database query timed out");
    /// var context = new ErrorContext
    /// {
    ///     Component = "CommandHandler",
    ///     RetryCount = 1,
    ///     MaxRetries = 3
    /// };
    ///
    /// var result = await handler.HandleError(message, error, context);
    /// // result.Action == ErrorAction.Retry
    /// // result.RetryDelay.HasValue == true (approximately 2-3 seconds with jitter)
    ///
    /// // Example: Handler escalates critical error
    /// var oomError = new OutOfMemoryException();
    /// var result2 = await handler.HandleError(message, oomError, context);
    /// // result2.Action == ErrorAction.Escalate
    ///
    /// // Example: Handler sends to dead-letter after max retries
    /// context.RetryCount = 3;
    /// var result3 = await handler.HandleError(message, error, context);
    /// // result3.Action == ErrorAction.SendToDeadLetter
    /// // result3.Reason == "Max retries (3) exceeded. Last error: Database query timed out"
    /// // Message is now in dead-letter queue
    /// </code>
    /// </remarks>
    public async Task<ErrorHandlingResult> HandleError<T>(T message, Exception error, ErrorContext context, CancellationToken cancellationToken = default) where T : IMessage
    {
        _logger.LogError(error,
            "Error processing message {MessageId} of type {MessageType} in component {Component}. Retry {RetryCount}/{MaxRetries}",
            message.MessageId, message.GetType().Name, context.Component, context.RetryCount, context.MaxRetries);

        // Determine action based on error type and retry count
        if (IsTransientError(error) && context.RetryCount < context.MaxRetries)
        {
            var delay = CalculateRetryDelay(context.RetryCount);
            _logger.LogWarning("Transient error detected. Will retry message {MessageId} after {Delay}ms",
                message.MessageId, delay.TotalMilliseconds);
            return ErrorHandlingResult.Retry(delay);
        }

        if (IsCriticalError(error))
        {
            _logger.LogCritical("Critical error detected for message {MessageId}. Escalating.", message.MessageId);
            return ErrorHandlingResult.Escalate();
        }

        if (context.RetryCount >= context.MaxRetries)
        {
            var reason = $"Max retries ({context.MaxRetries}) exceeded. Last error: {error.Message}";
            _logger.LogError("Message {MessageId} exceeded max retries. Sending to dead letter queue.", message.MessageId);

            await _deadLetterQueue.SendToDeadLetter(message, new DeadLetterContext
            {
                Reason = reason,
                Exception = error,
                Component = context.Component,
                RetryCount = context.RetryCount,
                FailureTime = _timeProvider.GetUtcNow().DateTime,
                Metadata = context.Metadata
            }, cancellationToken);

            return ErrorHandlingResult.SendToDeadLetter(reason);
        }

        // Default: send to dead letter
        var defaultReason = $"Unhandled error: {error.Message}";
        await _deadLetterQueue.SendToDeadLetter(message, new DeadLetterContext
        {
            Reason = defaultReason,
            Exception = error,
            Component = context.Component,
            RetryCount = context.RetryCount,
            FailureTime = _timeProvider.GetUtcNow().DateTime,
            Metadata = context.Metadata
        }, cancellationToken);

        return ErrorHandlingResult.SendToDeadLetter(defaultReason);
    }

    private bool IsTransientError(Exception error)
    {
        return error is TimeoutException ||
               error is TaskCanceledException ||
               (error.InnerException != null && IsTransientError(error.InnerException)) ||
               CompatibilityHelpers.Contains(error.Message, "timeout", StringComparison.OrdinalIgnoreCase) ||
               CompatibilityHelpers.Contains(error.Message, "transient", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsCriticalError(Exception error)
    {
        return error is OutOfMemoryException ||
               error is StackOverflowException ||
               error is AccessViolationException;
    }

    private TimeSpan CalculateRetryDelay(int retryCount)
    {
        // Exponential backoff with jitter
        var baseDelay = Math.Pow(2, retryCount);
        var jitter = RandomHelper.Instance.NextDouble() * 0.3; // 30% jitter
        var delaySeconds = baseDelay * (1 + jitter);

        // Cap at 30 seconds
        return TimeSpan.FromSeconds(Math.Min(delaySeconds, 30));
    }
}
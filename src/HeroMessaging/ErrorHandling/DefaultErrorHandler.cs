using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.ErrorHandling;

public class DefaultErrorHandler : IErrorHandler
{
    private readonly ILogger<DefaultErrorHandler> _logger;
    private readonly IDeadLetterQueue _deadLetterQueue;

    public DefaultErrorHandler(ILogger<DefaultErrorHandler> logger, IDeadLetterQueue deadLetterQueue)
    {
        _logger = logger;
        _deadLetterQueue = deadLetterQueue;
    }

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
                FailureTime = DateTime.UtcNow,
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
            FailureTime = DateTime.UtcNow,
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
using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.ErrorHandling;

public class DefaultErrorHandler(ILogger<DefaultErrorHandler> logger, IDeadLetterQueue deadLetterQueue, TimeProvider timeProvider) : IErrorHandler
{
    private readonly ILogger<DefaultErrorHandler> _logger = logger;
    private readonly IDeadLetterQueue _deadLetterQueue = deadLetterQueue;
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<ErrorHandlingResult> HandleErrorAsync<T>(T message, Exception error, ErrorContext context, CancellationToken cancellationToken = default) where T : IMessage
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

            await _deadLetterQueue.SendToDeadLetterAsync(message, new DeadLetterContext
            {
                Reason = reason,
                Exception = error,
                Component = context.Component,
                RetryCount = context.RetryCount,
                FailureTime = _timeProvider.GetUtcNow(),
                Metadata = context.Metadata
            }, cancellationToken);

            return ErrorHandlingResult.SendToDeadLetter(reason);
        }

        // Default: send to dead letter
        var defaultReason = $"Unhandled error: {error.Message}";
        await _deadLetterQueue.SendToDeadLetterAsync(message, new DeadLetterContext
        {
            Reason = defaultReason,
            Exception = error,
            Component = context.Component,
            RetryCount = context.RetryCount,
            FailureTime = _timeProvider.GetUtcNow(),
            Metadata = context.Metadata
        }, cancellationToken);

        return ErrorHandlingResult.SendToDeadLetter(defaultReason);
    }

    private static bool IsTransientError(Exception error) => ErrorClassifier.IsTransient(error);

    private static bool IsCriticalError(Exception error) => ErrorClassifier.IsCritical(error);

    private static TimeSpan CalculateRetryDelay(int retryCount) =>
        RetryDelayCalculator.Calculate(retryCount, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
}

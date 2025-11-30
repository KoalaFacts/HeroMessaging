using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing.Decorators;

/// <summary>
/// Decorator that adds error handling and retry logic to message processing
/// </summary>
public class ErrorHandlingDecorator(
    IMessageProcessor inner,
    IErrorHandler errorHandler,
    ILogger<ErrorHandlingDecorator> logger,
    TimeProvider timeProvider,
    int maxRetries = 3) : MessageProcessorDecorator(inner)
{
    private readonly IErrorHandler _errorHandler = errorHandler;
    private readonly ILogger<ErrorHandlingDecorator> _logger = logger;
    private readonly int _maxRetries = maxRetries;
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public override async ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context, CancellationToken cancellationToken = default)
    {
        var retryCount = 0;
        Exception? lastException = null;

        while (retryCount <= _maxRetries)
        {
            try
            {
                var result = await _inner.ProcessAsync(message, context, cancellationToken).ConfigureAwait(false);

                if (result.Success)
                {
                    if (retryCount > 0)
                    {
                        _logger.LogInformation("Message {MessageId} succeeded after {RetryCount} retries",
                            message.MessageId, retryCount);
                    }
                    return result;
                }

                // If processing failed but no exception, treat as permanent failure
                if (result.Exception == null)
                {
                    return result;
                }

                lastException = result.Exception;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            if (lastException != null)
            {
                _logger.LogError(lastException,
                    "Error processing message {MessageId} of type {MessageType}. Attempt {RetryCount}/{MaxRetries}",
                    message.MessageId, message.GetType().Name, retryCount + 1, _maxRetries + 1);

                var errorContext = new ErrorContext
                {
                    RetryCount = retryCount,
                    MaxRetries = _maxRetries,
                    Component = context.Component,
                    FirstFailureTime = context.FirstFailureTime ?? _timeProvider.GetUtcNow(),
                    LastFailureTime = _timeProvider.GetUtcNow(),
                    Metadata = context.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                };

                var errorResult = await _errorHandler.HandleErrorAsync(message, lastException, errorContext, cancellationToken).ConfigureAwait(false);

                switch (errorResult.Action)
                {
                    case ErrorAction.Retry:
                        retryCount++;
                        context = context.WithRetry(retryCount, context.FirstFailureTime);

                        if (errorResult.RetryDelay.HasValue)
                        {
                            _logger.LogDebug("Waiting {Delay}ms before retry", errorResult.RetryDelay.Value.TotalMilliseconds);
                            await Task.Delay(errorResult.RetryDelay.Value, _timeProvider, cancellationToken).ConfigureAwait(false);
                        }
                        continue;

                    case ErrorAction.SendToDeadLetter:
                        _logger.LogWarning("Message {MessageId} sent to dead letter queue: {Reason}",
                            message.MessageId, errorResult.Reason);
                        return ProcessingResult.Failed(lastException, $"Sent to DLQ: {errorResult.Reason}");

                    case ErrorAction.Discard:
                        _logger.LogWarning("Message {MessageId} discarded: {Reason}",
                            message.MessageId, errorResult.Reason);
                        return ProcessingResult.Failed(lastException, $"Discarded: {errorResult.Reason}");

                    case ErrorAction.Escalate:
                        _logger.LogCritical(lastException, "Critical error processing message {MessageId}. Escalating.",
                            message.MessageId);
                        throw lastException;
                    default:
                        break;
                }
            }

            retryCount++;
        }

        // Max retries exceeded
        _logger.LogError("Message {MessageId} failed after {MaxRetries} retries", message.MessageId, _maxRetries);
        return ProcessingResult.Failed(lastException ?? new Exception("Processing failed"),
            $"Failed after {_maxRetries} retries");
    }
}

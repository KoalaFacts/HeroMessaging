using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Policies;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Logging;

// Uses centralized RetryDelayCalculator and ErrorClassifier utilities

namespace HeroMessaging.Processing.Decorators;

/// <summary>
/// Decorator that adds retry logic to message processing
/// </summary>
public class RetryDecorator(
    IMessageProcessor inner,
    ILogger<RetryDecorator> logger,
    IRetryPolicy? retryPolicy = null) : MessageProcessorDecorator(inner)
{
    private readonly ILogger<RetryDecorator> _logger = logger;
    private readonly IRetryPolicy _retryPolicy = retryPolicy ?? new ExponentialBackoffRetryPolicy();

    public override async ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context, CancellationToken cancellationToken = default)
    {
        var retryCount = 0;
        var maxRetries = _retryPolicy.MaxRetries;
        Exception? lastException = null;

        while (retryCount <= maxRetries)
        {
            try
            {
                var result = await _inner.ProcessAsync(message, context, cancellationToken).ConfigureAwait(false);

                if (result.Success || !_retryPolicy.ShouldRetry(result.Exception, retryCount))
                {
                    if (retryCount > 0 && result.Success)
                    {
                        _logger.LogInformation("Message {MessageId} succeeded after {RetryCount} retries",
                            message.MessageId, retryCount);
                    }
                    return result;
                }

                lastException = result.Exception;
            }
            catch (Exception ex) when (_retryPolicy.ShouldRetry(ex, retryCount))
            {
                lastException = ex;
            }

            if (retryCount < maxRetries)
            {
                var delay = _retryPolicy.GetRetryDelay(retryCount);
                _logger.LogWarning("Retry {RetryCount}/{MaxRetries} for message {MessageId} after {DelayMs}ms",
                    retryCount + 1, maxRetries, message.MessageId, delay.TotalMilliseconds);

                context = context.WithRetry(retryCount + 1, context.FirstFailureTime);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            retryCount++;
        }

        _logger.LogError("Message {MessageId} failed after {MaxRetries} retries", message.MessageId, maxRetries);
        return ProcessingResult.Failed(
            lastException ?? new Exception("Processing failed after retries"),
            $"Failed after {maxRetries} retries");
    }
}

/// <summary>
/// Exponential backoff retry policy with jitter.
/// Uses centralized RetryDelayCalculator and ErrorClassifier utilities.
/// </summary>
public class ExponentialBackoffRetryPolicy(
    int maxRetries = 3,
    TimeSpan? baseDelay = null,
    TimeSpan? maxDelay = null,
    double jitterFactor = RetryDelayCalculator.DefaultJitterFactor) : IRetryPolicy
{
    public int MaxRetries { get; } = maxRetries;
    private readonly TimeSpan _baseDelay = baseDelay ?? RetryDelayCalculator.DefaultBaseDelay;
    private readonly TimeSpan _maxDelay = maxDelay ?? RetryDelayCalculator.DefaultMaxDelay;
    private readonly double _jitterFactor = jitterFactor;

    public bool ShouldRetry(Exception? exception, int attemptNumber)
    {
        return ErrorClassifier.ShouldRetry(exception, attemptNumber, MaxRetries);
    }

    public TimeSpan GetRetryDelay(int attemptNumber)
    {
        return RetryDelayCalculator.Calculate(attemptNumber, _baseDelay, _maxDelay, _jitterFactor);
    }
}
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Policies;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing.Decorators;

/// <summary>
/// Decorator that adds retry logic to message processing
/// </summary>
public class RetryDecorator : MessageProcessorDecorator
{
    private readonly ILogger<RetryDecorator> _logger;
    private readonly IRetryPolicy _retryPolicy;

    public RetryDecorator(
        IMessageProcessor inner,
        ILogger<RetryDecorator> logger,
        IRetryPolicy? retryPolicy = null) : base(inner)
    {
        _logger = logger;
        _retryPolicy = retryPolicy ?? new ExponentialBackoffRetryPolicy();
    }

    public override async ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context, CancellationToken cancellationToken = default)
    {
        var retryCount = 0;
        var maxRetries = _retryPolicy.MaxRetries;
        Exception? lastException = null;

        while (retryCount <= maxRetries)
        {
            try
            {
                var result = await _inner.ProcessAsync(message, context, cancellationToken);
                
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
                
                await Task.Delay(delay, cancellationToken);
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
/// Exponential backoff retry policy with jitter
/// </summary>
public class ExponentialBackoffRetryPolicy : IRetryPolicy
{
    public int MaxRetries { get; }
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;
    private readonly double _jitterFactor;

    public ExponentialBackoffRetryPolicy(
        int maxRetries = 3,
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null,
        double jitterFactor = 0.3)
    {
        MaxRetries = maxRetries;
        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(30);
        _jitterFactor = jitterFactor;
    }

    public bool ShouldRetry(Exception? exception, int attemptNumber)
    {
        if (attemptNumber >= MaxRetries) return false;
        if (exception == null) return false;
        
        // Don't retry critical errors
        if (exception is OutOfMemoryException ||
            exception is StackOverflowException ||
            exception is AccessViolationException)
        {
            return false;
        }

        // Retry transient errors
        return IsTransientError(exception);
    }

    public TimeSpan GetRetryDelay(int attemptNumber)
    {
        var exponentialDelay = _baseDelay.TotalMilliseconds * Math.Pow(2, attemptNumber);
        var jitter = RandomHelper.Instance.NextDouble() * _jitterFactor;
        var delayWithJitter = exponentialDelay * (1 + jitter);
        var finalDelay = TimeSpan.FromMilliseconds(Math.Min(delayWithJitter, _maxDelay.TotalMilliseconds));
        
        return finalDelay;
    }

    private bool IsTransientError(Exception exception)
    {
        return exception is TimeoutException ||
               exception is TaskCanceledException ||
               exception is OperationCanceledException ||
               (exception.InnerException != null && IsTransientError(exception.InnerException));
    }
}
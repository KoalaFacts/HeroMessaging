namespace HeroMessaging.Abstractions.Policies;

/// <summary>
/// Defines retry policy behavior for message processing
/// </summary>
public interface IRetryPolicy
{
    /// <summary>
    /// Gets the maximum number of retry attempts
    /// </summary>
    int MaxRetries { get; }

    /// <summary>
    /// Determines whether to retry based on the exception and attempt number
    /// </summary>
    /// <param name="exception">The exception that occurred</param>
    /// <param name="attemptNumber">Current attempt number (0-based)</param>
    /// <returns>True if should retry, false otherwise</returns>
    bool ShouldRetry(Exception? exception, int attemptNumber);

    /// <summary>
    /// Gets the delay before the next retry attempt
    /// </summary>
    /// <param name="attemptNumber">Current attempt number (0-based)</param>
    /// <returns>Time to wait before next retry</returns>
    TimeSpan GetRetryDelay(int attemptNumber);
}
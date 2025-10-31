using HeroMessaging.Abstractions.Policies;

namespace HeroMessaging.Policies;

/// <summary>
/// Retry policy that never retries failed operations, immediately failing on the first error.
/// </summary>
/// <remarks>
/// This policy is useful for scenarios where:
/// - Immediate failure feedback is required
/// - Operations should never be retried (e.g., idempotent safety concerns)
/// - Testing error handling paths without retry delays
/// - Critical operations where retries could cause data inconsistency
/// - Time-sensitive operations where retry delays are unacceptable
///
/// Characteristics:
/// - MaxRetries: Always returns 0 (no retries)
/// - ShouldRetry: Always returns false (never retry)
/// - GetRetryDelay: Always returns TimeSpan.Zero (no delay)
///
/// Use cases:
/// - Financial transactions requiring immediate success/failure determination
/// - Operations with strict time constraints
/// - Testing scenarios to verify error handling without retry complexity
/// - Operations where side effects make retry unsafe
///
/// <code>
/// // Register NoRetryPolicy for specific scenarios
/// services.AddSingleton&lt;IRetryPolicy&gt;(new NoRetryPolicy());
///
/// // Use in pipeline
/// var pipeline = new MessageProcessingPipelineBuilder(serviceProvider)
///     .UseRetry(new NoRetryPolicy())
///     .Build(innerProcessor);
///
/// // Result: Any error will immediately fail without retry
/// </code>
/// </remarks>
public class NoRetryPolicy : IRetryPolicy
{
    /// <summary>
    /// Gets the maximum number of retry attempts.
    /// </summary>
    /// <value>Always returns 0, indicating no retries are permitted.</value>
    public int MaxRetries => 0;

    /// <summary>
    /// Determines whether an operation should be retried after a failure.
    /// </summary>
    /// <param name="exception">The exception that occurred. Ignored by this policy.</param>
    /// <param name="attemptNumber">The current attempt number. Ignored by this policy.</param>
    /// <returns>Always returns <c>false</c>, indicating the operation should never be retried.</returns>
    /// <remarks>
    /// This method always returns false regardless of the exception type or attempt number,
    /// ensuring that failed operations fail immediately without retry.
    /// </remarks>
    public bool ShouldRetry(Exception? exception, int attemptNumber)
    {
        return false;
    }

    /// <summary>
    /// Gets the delay before the next retry attempt.
    /// </summary>
    /// <param name="attemptNumber">The current attempt number. Ignored by this policy.</param>
    /// <returns>Always returns <see cref="TimeSpan.Zero"/>, indicating no delay.</returns>
    /// <remarks>
    /// Since this policy never retries, this method always returns zero delay.
    /// This method should never be called in practice since ShouldRetry always returns false.
    /// </remarks>
    public TimeSpan GetRetryDelay(int attemptNumber)
    {
        return TimeSpan.Zero;
    }
}
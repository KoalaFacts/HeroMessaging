using HeroMessaging.Abstractions.Policies;

namespace HeroMessaging.Policies;

/// <summary>
/// Retry policy that uses a fixed delay between retry attempts with configurable retryable exception types.
/// </summary>
/// <remarks>
/// This policy provides predictable, fixed-interval retry behavior suitable for:
/// - Services with consistent response times
/// - Scenarios where exponential backoff is not desired
/// - Testing with predictable retry timing
/// - Rate-limited APIs requiring fixed intervals
///
/// Features:
/// - Fixed delay between attempts (default: 1 second)
/// - Configurable maximum retry attempts (default: 3)
/// - Selective exception type retry (default: TimeoutException, TaskCanceledException)
/// - Automatic critical error detection (never retries OOM, stack overflow, access violation)
/// - Recursive inner exception checking for retryable types
///
/// Exception Filtering:
/// - Only specified exception types are retried
/// - Supports exception inheritance (base types match derived exceptions)
/// - Checks inner exceptions recursively
/// - Critical errors (OOM, StackOverflow, AccessViolation) are never retried
///
/// <code>
/// // Default configuration - retries timeouts 3 times with 1-second delay
/// var policy = new LinearRetryPolicy();
///
/// // Custom configuration - 5 retries with 2-second delay
/// var customPolicy = new LinearRetryPolicy(
///     maxRetries: 5,
///     delay: TimeSpan.FromSeconds(2)
/// );
///
/// // Retry specific exceptions only
/// var selectivePolicy = new LinearRetryPolicy(
///     maxRetries: 3,
///     delay: TimeSpan.FromSeconds(1),
///     retryableExceptions: new[]
///     {
///         typeof(HttpRequestException),
///         typeof(SqlException),
///         typeof(TimeoutException)
///     }
/// );
///
/// // Use in pipeline
/// var pipeline = new MessageProcessingPipelineBuilder(serviceProvider)
///     .UseRetry(customPolicy)
///     .Build(innerProcessor);
/// </code>
/// </remarks>
public class LinearRetryPolicy : IRetryPolicy
{
    /// <summary>
    /// Gets the maximum number of retry attempts.
    /// </summary>
    /// <value>The maximum number of retries configured for this policy.</value>
    public int MaxRetries { get; }

    private readonly TimeSpan _delay;
    private readonly HashSet<Type> _retryableExceptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinearRetryPolicy"/> class.
    /// </summary>
    /// <param name="maxRetries">Maximum number of retry attempts. Default is 3.</param>
    /// <param name="delay">Fixed delay between retry attempts. Default is 1 second.</param>
    /// <param name="retryableExceptions">
    /// Array of exception types that should trigger retries.
    /// If null or empty, defaults to TimeoutException and TaskCanceledException.
    /// </param>
    /// <remarks>
    /// The policy will only retry exceptions that match the specified types or their derived types.
    /// Critical exceptions (OutOfMemoryException, StackOverflowException, AccessViolationException)
    /// are never retried regardless of configuration.
    ///
    /// <code>
    /// // Retry all timeout-related exceptions
    /// var policy = new LinearRetryPolicy(
    ///     maxRetries: 5,
    ///     delay: TimeSpan.FromSeconds(2),
    ///     retryableExceptions: new[]
    ///     {
    ///         typeof(TimeoutException),
    ///         typeof(HttpRequestException)
    ///     }
    /// );
    ///
    /// // Default behavior - retry timeouts and cancellations
    /// var defaultPolicy = new LinearRetryPolicy();
    /// </code>
    /// </remarks>
    public LinearRetryPolicy(
        int maxRetries = 3,
        TimeSpan? delay = null,
        params Type[] retryableExceptions)
    {
        MaxRetries = maxRetries;
        _delay = delay ?? TimeSpan.FromSeconds(1);
        _retryableExceptions = retryableExceptions?.Length > 0
            ? new HashSet<Type>(retryableExceptions)
            : new HashSet<Type> { typeof(TimeoutException), typeof(TaskCanceledException) };
    }

    /// <summary>
    /// Determines whether an operation should be retried based on the exception type and attempt number.
    /// </summary>
    /// <param name="exception">The exception that occurred during the operation.</param>
    /// <param name="attemptNumber">The current attempt number (0-based: 0 = first attempt, 1 = first retry).</param>
    /// <returns>
    /// <c>true</c> if the operation should be retried; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// Retry Decision Logic:
    /// 1. Returns false if attemptNumber >= MaxRetries (retry limit reached)
    /// 2. Returns false if exception is null (no error to retry)
    /// 3. Returns false for critical exceptions (OOM, StackOverflow, AccessViolation)
    /// 4. Returns true if exception type matches retryable exceptions (includes inheritance and inner exceptions)
    /// 5. Returns false otherwise (exception type not in retryable list)
    ///
    /// Critical Exception Handling:
    /// The following exceptions are never retried as they indicate unrecoverable errors:
    /// - OutOfMemoryException: System has exhausted available memory
    /// - StackOverflowException: Call stack exceeded (often infinite recursion)
    /// - AccessViolationException: Attempted to read/write protected memory
    ///
    /// Exception Type Matching:
    /// - Checks exception type against configured retryable types
    /// - Supports exception inheritance (derived exceptions match base types)
    /// - Recursively checks inner exceptions
    ///
    /// <code>
    /// var policy = new LinearRetryPolicy(
    ///     maxRetries: 3,
    ///     retryableExceptions: new[] { typeof(TimeoutException) }
    /// );
    ///
    /// // Timeout exception - retryable
    /// bool shouldRetry1 = policy.ShouldRetry(new TimeoutException(), attemptNumber: 0);
    /// // shouldRetry1 == true
    ///
    /// // Max retries reached
    /// bool shouldRetry2 = policy.ShouldRetry(new TimeoutException(), attemptNumber: 3);
    /// // shouldRetry2 == false
    ///
    /// // Critical exception - never retry
    /// bool shouldRetry3 = policy.ShouldRetry(new OutOfMemoryException(), attemptNumber: 0);
    /// // shouldRetry3 == false
    ///
    /// // Non-retryable exception type
    /// bool shouldRetry4 = policy.ShouldRetry(new InvalidOperationException(), attemptNumber: 0);
    /// // shouldRetry4 == false
    /// </code>
    /// </remarks>
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

        // Check if exception type is retryable
        return IsRetryableException(exception);
    }

    /// <summary>
    /// Gets the fixed delay before the next retry attempt.
    /// </summary>
    /// <param name="attemptNumber">The current attempt number. Ignored by this policy as delay is constant.</param>
    /// <returns>The fixed delay configured for this policy (default: 1 second).</returns>
    /// <remarks>
    /// This policy uses a constant delay between all retry attempts, regardless of the attempt number.
    /// Unlike exponential backoff policies, the delay does not increase with each retry.
    ///
    /// <code>
    /// var policy = new LinearRetryPolicy(delay: TimeSpan.FromSeconds(2));
    ///
    /// var delay1 = policy.GetRetryDelay(0); // 2 seconds
    /// var delay2 = policy.GetRetryDelay(1); // 2 seconds
    /// var delay3 = policy.GetRetryDelay(2); // 2 seconds
    /// // All delays are the same
    /// </code>
    /// </remarks>
    public TimeSpan GetRetryDelay(int attemptNumber)
    {
        return _delay;
    }

    private bool IsRetryableException(Exception exception)
    {
        var exceptionType = exception.GetType();

        // Check if exception or any of its base types are in the retryable list
        while (exceptionType != null && exceptionType != typeof(object))
        {
            if (_retryableExceptions.Contains(exceptionType))
                return true;
            exceptionType = exceptionType.BaseType;
        }

        // Check inner exception
        if (exception.InnerException != null)
            return IsRetryableException(exception.InnerException);

        return false;
    }
}
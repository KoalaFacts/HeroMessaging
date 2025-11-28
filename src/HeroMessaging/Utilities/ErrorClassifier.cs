using System.Data.Common;

namespace HeroMessaging.Utilities;

/// <summary>
/// Centralized utility for classifying errors as transient (retryable) or critical (non-retryable).
/// Consolidates duplicate implementations from RetryDecorator, DefaultErrorHandler, and ConnectionResilienceDecorator.
/// </summary>
public static class ErrorClassifier
{
    /// <summary>
    /// Common SQL Server transient error codes.
    /// </summary>
    private static readonly int[] TransientSqlErrorCodes =
    [
        2,     // Timeout
        20,    // Instance failure
        64,    // Connection failure
        233,   // Connection reset
        10053, // Connection aborted
        10054, // Connection reset by peer
        40197, // Service busy
        40501, // Service busy
        40613, // Database not available
        49918, // Cannot process request
        49919, // Cannot process request
        49920  // Cannot process request
    ];

    /// <summary>
    /// Determines if an exception is transient and safe to retry.
    /// </summary>
    /// <param name="exception">The exception to classify</param>
    /// <param name="checkInnerException">Whether to recursively check inner exceptions. Default: true</param>
    /// <param name="treatCancellationAsTransient">Whether to treat TaskCanceledException/OperationCanceledException as transient. Default: true</param>
    /// <returns>True if the exception is transient and retryable</returns>
    public static bool IsTransient(Exception? exception, bool checkInnerException = true, bool treatCancellationAsTransient = true)
    {
        if (exception == null)
            return false;

        // Check direct transient types
        if (exception is TimeoutException)
            return true;

        // Cancellation handling - configurable
        if (exception is TaskCanceledException or OperationCanceledException)
            return treatCancellationAsTransient;

        // Check for database exceptions
        if (exception is DbException dbEx && IsTransientDbException(dbEx))
            return true;

        // Check message-based hints for specific patterns
        // Only check "timeout" for general exceptions (not "transient" - too prone to false positives)
        if (CompatibilityHelpers.Contains(exception.Message, "timeout", StringComparison.OrdinalIgnoreCase))
            return true;

        // InvalidOperationException with connection issues
        if (exception is InvalidOperationException &&
            CompatibilityHelpers.Contains(exception.Message, "connection", StringComparison.OrdinalIgnoreCase))
            return true;

        // Recursively check inner exception
        if (checkInnerException && exception.InnerException != null)
            return IsTransient(exception.InnerException, checkInnerException, treatCancellationAsTransient);

        return false;
    }

    /// <summary>
    /// Determines if an exception is critical and should not be retried.
    /// Critical errors indicate system-level issues that won't be resolved by retrying.
    /// </summary>
    /// <param name="exception">The exception to classify</param>
    /// <returns>True if the exception is critical</returns>
    public static bool IsCritical(Exception? exception)
    {
        if (exception == null)
            return false;

        return exception is OutOfMemoryException or
               StackOverflowException or
               AccessViolationException;
    }

    /// <summary>
    /// Determines if a database exception is transient based on error code and message.
    /// </summary>
    private static bool IsTransientDbException(DbException dbException)
    {
        // Check known transient error codes
        if (TransientSqlErrorCodes.Contains(dbException.ErrorCode))
            return true;

        // Check message-based hints
        return CompatibilityHelpers.Contains(dbException.Message, "timeout", StringComparison.OrdinalIgnoreCase) ||
               CompatibilityHelpers.Contains(dbException.Message, "connection", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines if retry should be attempted based on exception type and attempt count.
    /// Combines transient check with critical check.
    /// </summary>
    /// <param name="exception">The exception to evaluate</param>
    /// <param name="attemptNumber">Current attempt number (0-based)</param>
    /// <param name="maxRetries">Maximum number of retries allowed</param>
    /// <returns>True if retry should be attempted</returns>
    public static bool ShouldRetry(Exception? exception, int attemptNumber, int maxRetries)
    {
        if (exception == null)
            return false;

        if (attemptNumber >= maxRetries)
            return false;

        if (IsCritical(exception))
            return false;

        return IsTransient(exception);
    }
}

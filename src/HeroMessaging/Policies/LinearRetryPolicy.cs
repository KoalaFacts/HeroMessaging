using HeroMessaging.Abstractions.Policies;

namespace HeroMessaging.Policies;

/// <summary>
/// Linear retry policy with fixed delay between attempts
/// </summary>
public class LinearRetryPolicy : IRetryPolicy
{
    public int MaxRetries { get; }
    private readonly TimeSpan _delay;
    private readonly HashSet<Type> _retryableExceptions;
    
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
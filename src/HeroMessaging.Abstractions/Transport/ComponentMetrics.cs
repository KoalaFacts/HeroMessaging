
namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Base class for component metrics with common patterns
/// Consolidates success rate calculation and error tracking
/// Thread-safe for concurrent operations
/// </summary>
public abstract class ComponentMetricsBase
{
    private long _successfulOperations;
    private long _failedOperations;
    private readonly Lock _errorLock = new();

    /// <summary>
    /// Total number of successful operations
    /// </summary>
    public long SuccessfulOperations
    {
        get => Interlocked.Read(ref _successfulOperations);
        protected set => Interlocked.Exchange(ref _successfulOperations, value);
    }

    /// <summary>
    /// Total number of failed operations
    /// </summary>
    public long FailedOperations
    {
        get => Interlocked.Read(ref _failedOperations);
        protected set => Interlocked.Exchange(ref _failedOperations, value);
    }

    /// <summary>
    /// Last error message
    /// </summary>
    public string? LastError
    {
        get
        {
            lock (_errorLock)
            {
                return field;
            }
        }

        private set;
    }

    /// <summary>
    /// When the last error occurred
    /// </summary>
    public DateTimeOffset? LastErrorOccurredAt
    {
        get
        {
            lock (_errorLock)
            {
                return field;
            }
        }

        private set;
    }

    /// <summary>
    /// Calculate success rate (0.0 - 1.0)
    /// Returns 0 if no operations have been performed
    /// </summary>
    public double SuccessRate
    {
        get
        {
            var success = Interlocked.Read(ref _successfulOperations);
            var failed = Interlocked.Read(ref _failedOperations);
            var total = success + failed;
            return total > 0 ? (double)success / total : 0.0;
        }
    }

    /// <summary>
    /// Total operations (successful + failed)
    /// </summary>
    public long TotalOperations => Interlocked.Read(ref _successfulOperations) + Interlocked.Read(ref _failedOperations);

    /// <summary>
    /// Record a successful operation (thread-safe)
    /// </summary>
    public virtual void RecordSuccess()
    {
        Interlocked.Increment(ref _successfulOperations);
    }

    /// <summary>
    /// Record a failed operation (thread-safe)
    /// </summary>
    /// <param name="error">Error message or exception details</param>
    /// <param name="timeProvider">Optional time provider for testability. Uses system time if null.</param>
    public virtual void RecordFailure(string? error = null, TimeProvider? timeProvider = null)
    {
        Interlocked.Increment(ref _failedOperations);
        if (error != null)
        {
            lock (_errorLock)
            {
                LastError = error;
                LastErrorOccurredAt = (timeProvider ?? TimeProvider.System).GetUtcNow();
            }
        }
    }

    /// <summary>
    /// Reset all metrics (thread-safe)
    /// </summary>
    public virtual void Reset()
    {
        Interlocked.Exchange(ref _successfulOperations, 0);
        Interlocked.Exchange(ref _failedOperations, 0);
        lock (_errorLock)
        {
            LastError = null;
            LastErrorOccurredAt = null;
        }
    }
}

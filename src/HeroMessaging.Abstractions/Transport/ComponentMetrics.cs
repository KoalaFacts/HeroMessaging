namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Base class for component metrics with common patterns
/// Consolidates success rate calculation and error tracking
/// </summary>
public abstract class ComponentMetrics
{
    /// <summary>
    /// Total number of successful operations
    /// </summary>
    public long SuccessfulOperations { get; set; }

    /// <summary>
    /// Total number of failed operations
    /// </summary>
    public long FailedOperations { get; set; }

    /// <summary>
    /// Last error message
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// When the last error occurred
    /// </summary>
    public DateTime? LastErrorOccurredAt { get; set; }

    /// <summary>
    /// Calculate success rate (0.0 - 1.0)
    /// Returns 0 if no operations have been performed
    /// </summary>
    public double SuccessRate
    {
        get
        {
            var total = SuccessfulOperations + FailedOperations;
            return total > 0 ? (double)SuccessfulOperations / total : 0.0;
        }
    }

    /// <summary>
    /// Total operations (successful + failed)
    /// </summary>
    public long TotalOperations => SuccessfulOperations + FailedOperations;

    /// <summary>
    /// Record a successful operation
    /// </summary>
    public virtual void RecordSuccess()
    {
        SuccessfulOperations++;
    }

    /// <summary>
    /// Record a failed operation
    /// </summary>
    /// <param name="error">Error message or exception details</param>
    public virtual void RecordFailure(string? error = null)
    {
        FailedOperations++;
        if (error != null)
        {
            LastError = error;
            LastErrorOccurredAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Reset all metrics
    /// </summary>
    public virtual void Reset()
    {
        SuccessfulOperations = 0;
        FailedOperations = 0;
        LastError = null;
        LastErrorOccurredAt = null;
    }
}

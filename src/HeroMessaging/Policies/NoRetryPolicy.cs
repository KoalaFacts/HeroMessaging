using HeroMessaging.Abstractions.Policies;

namespace HeroMessaging.Policies;

/// <summary>
/// Policy that never retries - useful for testing or critical operations
/// </summary>
public class NoRetryPolicy : IRetryPolicy
{
    /// <summary>
    /// Gets max retries.
    /// </summary>
    public int MaxRetries => 0;
    /// <summary>
    /// Executes should retry.
    /// </summary>

    public bool ShouldRetry(Exception? exception, int attemptNumber)
    {
        return false;
    }
    /// <summary>
    /// Executes get retry delay.
    /// </summary>

    public TimeSpan GetRetryDelay(int attemptNumber)
    {
        return TimeSpan.Zero;
    }
}

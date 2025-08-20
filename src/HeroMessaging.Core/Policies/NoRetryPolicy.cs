using HeroMessaging.Abstractions.Policies;

namespace HeroMessaging.Core.Policies;

/// <summary>
/// Policy that never retries - useful for testing or critical operations
/// </summary>
public class NoRetryPolicy : IRetryPolicy
{
    public int MaxRetries => 0;
    
    public bool ShouldRetry(Exception? exception, int attemptNumber)
    {
        return false;
    }
    
    public TimeSpan GetRetryDelay(int attemptNumber)
    {
        return TimeSpan.Zero;
    }
}
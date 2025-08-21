using System.Collections.Concurrent;
using HeroMessaging.Abstractions.Policies;

namespace HeroMessaging.Policies;

/// <summary>
/// Circuit breaker retry policy that stops retrying after too many failures
/// </summary>
public class CircuitBreakerRetryPolicy : IRetryPolicy
{
    public int MaxRetries { get; }
    private readonly int _failureThreshold;
    private readonly TimeSpan _openCircuitDuration;
    private readonly TimeSpan _baseDelay;
    private readonly ConcurrentDictionary<string, CircuitState> _circuits = new();
    
    public CircuitBreakerRetryPolicy(
        int maxRetries = 3,
        int failureThreshold = 5,
        TimeSpan? openCircuitDuration = null,
        TimeSpan? baseDelay = null)
    {
        MaxRetries = maxRetries;
        _failureThreshold = failureThreshold;
        _openCircuitDuration = openCircuitDuration ?? TimeSpan.FromMinutes(1);
        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
    }
    
    public bool ShouldRetry(Exception? exception, int attemptNumber)
    {
        if (attemptNumber >= MaxRetries) return false;
        if (exception == null) return false;
        
        var circuitKey = GetCircuitKey(exception);
        var state = _circuits.GetOrAdd(circuitKey, _ => new CircuitState());
        
        // Check if circuit is open
        if (state.IsOpen)
        {
            if (DateTime.UtcNow - state.OpenedAt > _openCircuitDuration)
            {
                // Try to close the circuit
                state.Reset();
            }
            else
            {
                // Circuit is still open, don't retry
                return false;
            }
        }
        
        // Record failure
        state.RecordFailure();
        
        // Check if we should open the circuit
        if (state.FailureCount >= _failureThreshold)
        {
            state.Open();
            return false;
        }
        
        return true;
    }
    
    public TimeSpan GetRetryDelay(int attemptNumber)
    {
        return TimeSpan.FromMilliseconds(_baseDelay.TotalMilliseconds * (attemptNumber + 1));
    }
    
    private string GetCircuitKey(Exception exception)
    {
        // Create a key based on exception type and message
        return $"{exception.GetType().Name}:{exception.Message?.GetHashCode()}";
    }
    
    private class CircuitState
    {
        private int _failureCount;
        private DateTime _openedAt;
        private bool _isOpen;
        private readonly object _lock = new();
        
        public int FailureCount => _failureCount;
        public DateTime OpenedAt => _openedAt;
        public bool IsOpen => _isOpen;
        
        public void RecordFailure()
        {
            lock (_lock)
            {
                _failureCount++;
            }
        }
        
        public void Open()
        {
            lock (_lock)
            {
                _isOpen = true;
                _openedAt = DateTime.UtcNow;
            }
        }
        
        public void Reset()
        {
            lock (_lock)
            {
                _failureCount = 0;
                _isOpen = false;
                _openedAt = default;
            }
        }
    }
}
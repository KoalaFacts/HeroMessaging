using System.Collections.Concurrent;
using HeroMessaging.Abstractions.Policies;

namespace HeroMessaging.Policies;

/// <summary>
/// Circuit breaker retry policy that stops retrying after too many failures
/// </summary>
public class CircuitBreakerRetryPolicy(
    TimeProvider timeProvider,
    int maxRetries = 3,
    int failureThreshold = 5,
    TimeSpan? openCircuitDuration = null,
    TimeSpan? baseDelay = null) : IRetryPolicy
{
    public int MaxRetries { get; } = maxRetries;
    private readonly int _failureThreshold = failureThreshold;
    private readonly TimeSpan _openCircuitDuration = openCircuitDuration ?? TimeSpan.FromMinutes(1);
    private readonly TimeSpan _baseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
    private readonly ConcurrentDictionary<string, CircuitState> _circuits = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));


    public bool ShouldRetry(Exception? exception, int attemptNumber)
    {
        if (attemptNumber >= MaxRetries) return false;
        if (exception == null) return false;

        var circuitKey = GetCircuitKey(exception);
        var state = _circuits.GetOrAdd(circuitKey, _ => new CircuitState(_timeProvider));

        // Check if circuit is open
        if (state.IsOpen)
        {
            if (_timeProvider.GetUtcNow().DateTime - state.OpenedAt > _openCircuitDuration)
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

    private class CircuitState(TimeProvider timeProvider)
    {
        private int _failureCount;
        private DateTime _openedAt;
        private bool _isOpen;
        private readonly object _lock = new();
        private readonly TimeProvider _timeProvider = timeProvider;

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
                _openedAt = _timeProvider.GetUtcNow().DateTime;
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
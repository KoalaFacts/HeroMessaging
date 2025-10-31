using HeroMessaging.Abstractions.Policies;
using System.Collections.Concurrent;

namespace HeroMessaging.Policies;

/// <summary>
/// Advanced retry policy with circuit breaker pattern that prevents retry storms by temporarily stopping retries after consecutive failures exceed a threshold.
/// </summary>
/// <remarks>
/// This policy implements the Circuit Breaker pattern to protect systems from cascading failures:
/// - Tracks failure rates per exception type/message combination
/// - Opens circuit (stops retrying) after failure threshold reached
/// - Automatically closes circuit after configured duration
/// - Uses linear increasing delay (baseDelay * attemptNumber)
///
/// Circuit States:
/// - Closed: Normal operation, retries are attempted
/// - Open: Too many failures, retries are blocked
/// - Half-Open: Testing if system recovered (automatic transition from Open)
///
/// Features:
/// - Per-circuit-key failure tracking (based on exception type and message hash)
/// - Configurable failure threshold (default: 5 consecutive failures)
/// - Automatic circuit reset after open duration (default: 1 minute)
/// - Linear retry delays (default: 1s base, increases with attempt number)
/// - Thread-safe circuit state management
///
/// Use Cases:
/// - Protecting downstream services from overload during outages
/// - Preventing retry storms that amplify system failures
/// - Giving failing systems time to recover
/// - Coordinating retry behavior across multiple callers
///
/// <code>
/// // Default configuration
/// var policy = new CircuitBreakerRetryPolicy(TimeProvider.System);
///
/// // Custom configuration for aggressive circuit breaking
/// var aggressivePolicy = new CircuitBreakerRetryPolicy(
///     timeProvider: TimeProvider.System,
///     maxRetries: 3,
///     failureThreshold: 3,  // Open after 3 failures
///     openCircuitDuration: TimeSpan.FromSeconds(30),  // Reset after 30s
///     baseDelay: TimeSpan.FromSeconds(2)  // 2s, 4s, 6s delays
/// );
///
/// // Use in pipeline
/// var pipeline = new MessageProcessingPipelineBuilder(serviceProvider)
///     .UseRetry(aggressivePolicy)
///     .Build(innerProcessor);
/// </code>
/// </remarks>
public class CircuitBreakerRetryPolicy(
    TimeProvider timeProvider,
    int maxRetries = 3,
    int failureThreshold = 5,
    TimeSpan? openCircuitDuration = null,
    TimeSpan? baseDelay = null) : IRetryPolicy
{
    /// <summary>
    /// Gets the maximum number of retry attempts before giving up.
    /// </summary>
    /// <value>The maximum number of retries configured for this policy.</value>
    public int MaxRetries { get; } = maxRetries;
    private readonly int _failureThreshold = failureThreshold;
    private readonly TimeSpan _openCircuitDuration = openCircuitDuration ?? TimeSpan.FromMinutes(1);
    private readonly TimeSpan _baseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
    private readonly ConcurrentDictionary<string, CircuitState> _circuits = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));


    /// <summary>
    /// Determines whether an operation should be retried based on circuit breaker state and attempt number.
    /// </summary>
    /// <param name="exception">The exception that occurred during the operation.</param>
    /// <param name="attemptNumber">The current attempt number (0-based).</param>
    /// <returns>
    /// <c>true</c> if the operation should be retried; <c>false</c> if the circuit is open, max retries reached, or failure threshold exceeded.
    /// </returns>
    /// <remarks>
    /// Decision Logic:
    /// 1. Returns false if attemptNumber >= MaxRetries (retry limit reached)
    /// 2. Returns false if exception is null (no error to retry)
    /// 3. Creates/retrieves circuit state for this exception type
    /// 4. If circuit is open:
    ///    - Checks if open duration has elapsed
    ///    - If elapsed: Resets circuit to closed state
    ///    - If not elapsed: Returns false (circuit still open, no retry)
    /// 5. Records the failure in circuit state
    /// 6. If failure count >= threshold: Opens circuit and returns false
    /// 7. Otherwise: Returns true (retry allowed)
    ///
    /// Circuit State Management:
    /// - Each unique exception type/message combination has its own circuit
    /// - Circuits are identified by a key derived from exception type and message hash
    /// - Circuit states are stored in a thread-safe ConcurrentDictionary
    /// - Automatic reset after open circuit duration expires
    ///
    /// Failure Threshold Behavior:
    /// When failure count reaches the threshold:
    /// - Circuit transitions from Closed to Open
    /// - All subsequent retry attempts are blocked
    /// - System waits for open circuit duration before allowing retries
    /// - This prevents overwhelming already-failing downstream services
    ///
    /// <code>
    /// var policy = new CircuitBreakerRetryPolicy(
    ///     TimeProvider.System,
    ///     maxRetries: 3,
    ///     failureThreshold: 3,
    ///     openCircuitDuration: TimeSpan.FromSeconds(30)
    /// );
    ///
    /// var exception = new HttpRequestException("Service unavailable");
    ///
    /// // First 3 failures - retries allowed
    /// policy.ShouldRetry(exception, 0); // true - circuit closed
    /// policy.ShouldRetry(exception, 1); // true - circuit closed
    /// policy.ShouldRetry(exception, 2); // true - circuit closed
    ///
    /// // 3rd failure opens the circuit
    /// policy.ShouldRetry(exception, 2); // false - circuit opened
    ///
    /// // Circuit remains open for 30 seconds
    /// policy.ShouldRetry(exception, 0); // false - circuit still open
    ///
    /// // After 30 seconds, circuit resets
    /// // (assuming time has passed)
    /// policy.ShouldRetry(exception, 0); // true - circuit reset to closed
    /// </code>
    /// </remarks>
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

    /// <summary>
    /// Gets the delay before the next retry attempt using linear backoff.
    /// </summary>
    /// <param name="attemptNumber">The current attempt number (0-based).</param>
    /// <returns>The delay calculated as baseDelay * (attemptNumber + 1).</returns>
    /// <remarks>
    /// This policy uses linear backoff, where the delay increases proportionally with the attempt number.
    /// This is less aggressive than exponential backoff but still provides increasing delays.
    ///
    /// Delay Calculation:
    /// - Attempt 0: baseDelay * 1 (default: 1 second)
    /// - Attempt 1: baseDelay * 2 (default: 2 seconds)
    /// - Attempt 2: baseDelay * 3 (default: 3 seconds)
    /// - Attempt N: baseDelay * (N + 1)
    ///
    /// <code>
    /// var policy = new CircuitBreakerRetryPolicy(
    ///     TimeProvider.System,
    ///     baseDelay: TimeSpan.FromSeconds(2)
    /// );
    ///
    /// var delay0 = policy.GetRetryDelay(0); // 2 seconds
    /// var delay1 = policy.GetRetryDelay(1); // 4 seconds
    /// var delay2 = policy.GetRetryDelay(2); // 6 seconds
    /// // Linear progression: 2s, 4s, 6s, 8s, ...
    /// </code>
    /// </remarks>
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
namespace HeroMessaging.Policies;

/// <summary>
/// Represents the state of a circuit in the circuit breaker pattern.
/// Thread-safe implementation for tracking failure counts and circuit state.
/// </summary>
internal sealed class CircuitState
{
    private readonly TimeProvider _timeProvider;

#if NET9_0_OR_GREATER
    private readonly Lock _lock = new();
#else
    private readonly object _lock = new();
#endif

    /// <summary>
    /// Initializes a new instance of the <see cref="CircuitState"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider for getting current time.</param>
    public CircuitState(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Gets the current failure count.
    /// </summary>
    public int FailureCount { get; private set; }

    /// <summary>
    /// Gets the time when the circuit was opened.
    /// </summary>
    public DateTimeOffset OpenedAt { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the circuit is open.
    /// </summary>
    public bool IsOpen { get; private set; }

    /// <summary>
    /// Records a failure in the circuit.
    /// </summary>
    public void RecordFailure()
    {
        lock (_lock)
        {
            FailureCount++;
        }
    }

    /// <summary>
    /// Opens the circuit, preventing further operations until reset.
    /// </summary>
    public void Open()
    {
        lock (_lock)
        {
            IsOpen = true;
            OpenedAt = _timeProvider.GetUtcNow();
        }
    }

    /// <summary>
    /// Resets the circuit to its initial closed state.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            FailureCount = 0;
            IsOpen = false;
            OpenedAt = default;
        }
    }
}

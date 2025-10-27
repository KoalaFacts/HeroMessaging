namespace HeroMessaging.Abstractions.Sagas;

/// <summary>
/// Base class for saga instances providing common state management
/// Inherit from this class when implementing sagas
/// </summary>
public abstract class SagaBase : ISaga
{
    /// <summary>
    /// Unique identifier correlating all messages in this saga instance
    /// </summary>
    public Guid CorrelationId { get; set; }

    /// <summary>
    /// Current state of the saga
    /// </summary>
    public string CurrentState { get; set; } = "Initial";

    /// <summary>
    /// When this saga instance was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this saga instance was last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this saga has completed (successfully or failed)
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// Version for optimistic concurrency control
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Mark the saga as complete
    /// </summary>
    protected void Complete()
    {
        IsCompleted = true;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Transition to a new state
    /// </summary>
    protected void TransitionTo(string newState)
    {
        CurrentState = newState;
        UpdatedAt = DateTime.UtcNow;
    }
}

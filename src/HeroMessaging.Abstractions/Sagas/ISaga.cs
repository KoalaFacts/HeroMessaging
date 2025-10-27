namespace HeroMessaging.Abstractions.Sagas;

/// <summary>
/// Base interface for all sagas (process managers)
/// A saga coordinates long-running business processes with multiple steps
/// </summary>
public interface ISaga
{
    /// <summary>
    /// Unique identifier correlating all messages in this saga instance
    /// </summary>
    Guid CorrelationId { get; set; }

    /// <summary>
    /// Current state of the saga
    /// </summary>
    string CurrentState { get; set; }

    /// <summary>
    /// When this saga instance was created
    /// </summary>
    DateTime CreatedAt { get; set; }

    /// <summary>
    /// When this saga instance was last updated
    /// </summary>
    DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Whether this saga has completed (successfully or failed)
    /// </summary>
    bool IsCompleted { get; set; }

    /// <summary>
    /// Version for optimistic concurrency control
    /// </summary>
    int Version { get; set; }
}

/// <summary>
/// Base interface for saga state/data
/// Implementations should include all data needed to coordinate the workflow
/// </summary>
public interface ISagaData
{
    /// <summary>
    /// Correlation identifier linking this data to a saga instance
    /// </summary>
    Guid CorrelationId { get; set; }

    /// <summary>
    /// Current state identifier
    /// </summary>
    string CurrentState { get; set; }
}

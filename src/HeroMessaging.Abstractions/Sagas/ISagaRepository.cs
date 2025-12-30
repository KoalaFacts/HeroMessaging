namespace HeroMessaging.Abstractions.Sagas;

/// <summary>
/// Repository for persisting and retrieving saga instances
/// Supports optimistic concurrency control via versioning
/// </summary>
/// <typeparam name="TSaga">Type of saga to persist</typeparam>
public interface ISagaRepository<TSaga> where TSaga : class, ISaga
{
    /// <summary>
    /// Find a saga instance by its correlation ID
    /// </summary>
    /// <param name="correlationId">The correlation ID to search for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The saga instance if found, null otherwise</returns>
    Task<TSaga?> FindAsync(Guid correlationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find saga instances by their current state
    /// Useful for querying stuck or long-running sagas
    /// </summary>
    /// <param name="state">The state to search for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of saga instances in the specified state</returns>
    Task<IEnumerable<TSaga>> FindByStateAsync(string state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Save a new saga instance
    /// </summary>
    /// <param name="saga">The saga instance to save</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveAsync(TSaga saga, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing saga instance
    /// Implements optimistic concurrency - will throw if version mismatch
    /// </summary>
    /// <param name="saga">The saga instance to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="SagaConcurrencyException">Thrown when version mismatch occurs</exception>
    Task UpdateAsync(TSaga saga, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a completed saga instance
    /// Should only be called for cleanup of old completed sagas
    /// </summary>
    /// <param name="correlationId">The correlation ID of the saga to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DeleteAsync(Guid correlationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find sagas that have been active longer than a specified duration
    /// Useful for detecting stuck or timed-out sagas
    /// </summary>
    /// <param name="olderThan">Find sagas updated before this time</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of saga instances</returns>
    Task<IEnumerable<TSaga>> FindStaleAsync(TimeSpan olderThan, CancellationToken cancellationToken = default);
}

/// <summary>
/// Exception thrown when saga update fails due to concurrent modification.
/// </summary>
public class SagaConcurrencyException : Exception
{
    /// <summary>
    /// Gets the correlation ID of the saga that had a concurrency conflict.
    /// </summary>
    public Guid CorrelationId { get; }

    /// <summary>
    /// Gets the version that was expected when attempting the update.
    /// </summary>
    public int ExpectedVersion { get; }

    /// <summary>
    /// Gets the actual version found in storage.
    /// </summary>
    public int ActualVersion { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaConcurrencyException"/> class.
    /// </summary>
    /// <param name="correlationId">The correlation ID of the saga</param>
    /// <param name="expectedVersion">The expected version</param>
    /// <param name="actualVersion">The actual version found</param>
    public SagaConcurrencyException(Guid correlationId, int expectedVersion, int actualVersion)
        : base($"Saga {correlationId} concurrency conflict. Expected version {expectedVersion}, but found {actualVersion}.")
    {
        CorrelationId = correlationId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }
}

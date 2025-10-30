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
/// This typically occurs when two processes attempt to update the same saga instance simultaneously.
/// </summary>
/// <remarks>
/// This exception follows the optimistic concurrency pattern where saga instances have version numbers.
/// When a saga is updated, the expected version is checked against the actual version in storage.
/// If they don't match, it indicates another process has modified the saga, and this exception is thrown.
///
/// Best practices for handling:
/// - Retry the operation with fresh saga state
/// - Use exponential backoff for retries
/// - Log the conflict for diagnostics
/// - Consider idempotency to handle duplicate operations
///
/// Example:
/// <code>
/// try
/// {
///     await sagaRepository.SaveAsync(saga, expectedVersion, cancellationToken);
/// }
/// catch (SagaConcurrencyException ex)
/// {
///     logger.LogWarning("Saga {CorrelationId} concurrency conflict. Expected {Expected}, actual {Actual}",
///         ex.CorrelationId, ex.ExpectedVersion, ex.ActualVersion);
///
///     // Reload saga and retry
///     saga = await sagaRepository.GetAsync(ex.CorrelationId, cancellationToken);
///     await sagaRepository.SaveAsync(saga, saga.Version, cancellationToken);
/// }
/// </code>
/// </remarks>
public class SagaConcurrencyException : Exception
{
    /// <summary>
    /// Gets the correlation ID of the saga that experienced the concurrency conflict.
    /// </summary>
    public Guid CorrelationId { get; }

    /// <summary>
    /// Gets the version that was expected during the update attempt.
    /// </summary>
    public int ExpectedVersion { get; }

    /// <summary>
    /// Gets the actual version found in storage, indicating another process modified the saga.
    /// </summary>
    public int ActualVersion { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="SagaConcurrencyException"/> with default values.
    /// </summary>
    public SagaConcurrencyException()
        : base("Saga concurrency conflict occurred.")
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SagaConcurrencyException"/> with a custom message.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public SagaConcurrencyException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SagaConcurrencyException"/> with a custom message and inner exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public SagaConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SagaConcurrencyException"/> with detailed concurrency conflict information.
    /// </summary>
    /// <param name="correlationId">The correlation ID of the saga that experienced the conflict.</param>
    /// <param name="expectedVersion">The version that was expected during the update.</param>
    /// <param name="actualVersion">The actual version found in storage.</param>
    public SagaConcurrencyException(Guid correlationId, int expectedVersion, int actualVersion)
        : base($"Saga {correlationId} concurrency conflict. Expected version {expectedVersion}, but found {actualVersion}.")
    {
        CorrelationId = correlationId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }
}

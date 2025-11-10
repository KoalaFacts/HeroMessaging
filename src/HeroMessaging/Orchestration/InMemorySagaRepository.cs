using System.Collections.Concurrent;
using HeroMessaging.Abstractions.Sagas;

namespace HeroMessaging.Orchestration;

/// <summary>
/// In-memory saga repository for development and testing
/// Thread-safe implementation using ConcurrentDictionary
/// Tracks versions separately to support proper optimistic concurrency with reference types
/// </summary>
/// <typeparam name="TSaga">Type of saga being stored</typeparam>
public class InMemorySagaRepository<TSaga> : ISagaRepository<TSaga>
    where TSaga : class, ISaga
{
    private readonly ConcurrentDictionary<Guid, TSaga> _sagas = new();
    private readonly ConcurrentDictionary<Guid, int> _versions = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Constructor with required TimeProvider for testability
    /// </summary>
    /// <param name="timeProvider">TimeProvider instance for time-based operations</param>
    public InMemorySagaRepository(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Find a saga by correlation ID
    /// </summary>
    public Task<TSaga?> FindAsync(Guid correlationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _sagas.TryGetValue(correlationId, out var saga);
        return Task.FromResult(saga);
    }

    /// <summary>
    /// Find all sagas in a specific state
    /// </summary>
    public Task<IEnumerable<TSaga>> FindByStateAsync(string state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sagas = _sagas.Values
            .Where(s => s.CurrentState == state)
            .ToList();

        return Task.FromResult<IEnumerable<TSaga>>(sagas);
    }

    /// <summary>
    /// Save a new saga instance
    /// Sets CreatedAt and UpdatedAt timestamps automatically
    /// </summary>
    public Task SaveAsync(TSaga saga, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (saga == null)
            throw new ArgumentNullException(nameof(saga));

        if (_sagas.ContainsKey(saga.CorrelationId))
        {
            throw new InvalidOperationException(
                $"Saga with correlation ID {saga.CorrelationId} already exists. Use UpdateAsync to modify existing sagas.");
        }

        // Set timestamps for new saga
        var now = _timeProvider.GetUtcNow().DateTime;
        saga.CreatedAt = now;
        saga.UpdatedAt = now;

        if (!_sagas.TryAdd(saga.CorrelationId, saga))
        {
            throw new InvalidOperationException(
                $"Failed to save saga with correlation ID {saga.CorrelationId}");
        }

        // Track initial version separately
        _versions[saga.CorrelationId] = saga.Version;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Update an existing saga instance with optimistic concurrency control
    /// Note: This method increments the version internally. Callers should NOT increment version before calling this method.
    /// </summary>
    public Task UpdateAsync(TSaga saga, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (saga == null)
            throw new ArgumentNullException(nameof(saga));

        if (!_sagas.ContainsKey(saga.CorrelationId))
        {
            throw new InvalidOperationException(
                $"Saga with correlation ID {saga.CorrelationId} not found. Use SaveAsync to create new sagas.");
        }

        // Optimistic concurrency check using tracked version
        if (!_versions.TryGetValue(saga.CorrelationId, out var expectedVersion))
        {
            throw new InvalidOperationException($"Version tracking lost for saga {saga.CorrelationId}");
        }

        if (saga.Version != expectedVersion)
        {
            throw new SagaConcurrencyException(
                saga.CorrelationId,
                expectedVersion: expectedVersion,
                actualVersion: saga.Version);
        }

        // Increment version and update timestamp for this update
        saga.Version++;
        saga.UpdatedAt = _timeProvider.GetUtcNow().DateTime;

        // Update both saga and tracked version
        _sagas[saga.CorrelationId] = saga;
        _versions[saga.CorrelationId] = saga.Version;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Delete a saga instance
    /// </summary>
    public Task DeleteAsync(Guid correlationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _sagas.TryRemove(correlationId, out _);
        _versions.TryRemove(correlationId, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Find stale sagas that haven't been updated within the specified time
    /// Useful for timeout detection and saga cleanup
    /// </summary>
    public Task<IEnumerable<TSaga>> FindStaleAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cutoffTime = _timeProvider.GetUtcNow().DateTime - olderThan;
        var staleSagas = _sagas.Values
            .Where(s => !s.IsCompleted && s.UpdatedAt < cutoffTime)
            .ToList();

        return Task.FromResult<IEnumerable<TSaga>>(staleSagas);
    }

    /// <summary>
    /// Get all sagas (useful for testing and debugging)
    /// </summary>
    public IEnumerable<TSaga> GetAll()
    {
        return _sagas.Values.ToList();
    }

    /// <summary>
    /// Clear all sagas (useful for testing)
    /// </summary>
    public void Clear()
    {
        _sagas.Clear();
        _versions.Clear();
    }

    /// <summary>
    /// Get count of sagas in repository
    /// </summary>
    public int Count => _sagas.Count;
}

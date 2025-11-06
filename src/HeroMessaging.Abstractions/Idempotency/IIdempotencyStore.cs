using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Idempotency;

/// <summary>
/// Provides storage for idempotency responses to enable exactly-once processing semantics.
/// Implementations should ensure thread-safe operations and handle TTL expiration.
/// </summary>
/// <remarks>
/// The idempotency store caches processing results to prevent duplicate execution of messages.
/// This is essential for achieving exactly-once semantics in at-least-once delivery systems.
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>
    /// Retrieves a cached idempotency response if one exists and is not expired.
    /// </summary>
    /// <param name="idempotencyKey">The unique idempotency key identifying the operation.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>
    /// The cached response if found and not expired; otherwise null.
    /// Returns null if the key doesn't exist or has expired.
    /// </returns>
    ValueTask<IdempotencyResponse?> GetAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a successful processing result with the specified time-to-live.
    /// </summary>
    /// <param name="idempotencyKey">The unique idempotency key identifying the operation.</param>
    /// <param name="result">The successful result to cache. Can be null for operations without return values.</param>
    /// <param name="ttl">The time-to-live duration for this cached response.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// The stored result will be automatically expired after the TTL duration.
    /// Implementations should handle serialization of the result object.
    /// </remarks>
    ValueTask StoreSuccessAsync(
        string idempotencyKey,
        object? result,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a failed processing result with the specified time-to-live.
    /// </summary>
    /// <param name="idempotencyKey">The unique idempotency key identifying the operation.</param>
    /// <param name="exception">The exception that occurred during processing.</param>
    /// <param name="ttl">The time-to-live duration for this cached failure.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Only idempotent failures should be cached. Transient failures (timeouts, network issues)
    /// should not be stored as they may succeed on retry.
    /// </remarks>
    ValueTask StoreFailureAsync(
        string idempotencyKey,
        Exception exception,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an idempotency key exists in the store (regardless of expiration).
    /// </summary>
    /// <param name="idempotencyKey">The unique idempotency key to check.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>True if the key exists; otherwise false.</returns>
    /// <remarks>
    /// This method may return true for expired entries in some implementations.
    /// Use <see cref="GetAsync"/> to retrieve unexpired entries.
    /// </remarks>
    ValueTask<bool> ExistsAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes expired idempotency entries from the store.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The number of expired entries removed.</returns>
    /// <remarks>
    /// Implementations should call this periodically to prevent storage exhaustion.
    /// Consider running this as a background task for production systems.
    /// </remarks>
    ValueTask<int> CleanupExpiredAsync(CancellationToken cancellationToken = default);
}

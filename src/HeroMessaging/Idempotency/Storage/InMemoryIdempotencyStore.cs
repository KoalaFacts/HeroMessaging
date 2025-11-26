using System.Collections.Concurrent;
using HeroMessaging.Abstractions.Idempotency;

namespace HeroMessaging.Idempotency.Storage;

/// <summary>
/// In-memory implementation of <see cref="IIdempotencyStore"/> for testing and non-persistent scenarios.
/// </summary>
/// <remarks>
/// <para>
/// This implementation uses <see cref="ConcurrentDictionary{TKey,TValue}"/> for thread-safe operations.
/// It is suitable for:
/// </para>
/// <list type="bullet">
/// <item><description>Unit and integration testing</description></item>
/// <item><description>Development environments</description></item>
/// <item><description>Single-instance applications without persistence requirements</description></item>
/// <item><description>Short-lived processes where data loss on restart is acceptable</description></item>
/// </list>
/// <para>
/// <strong>Not recommended for production use</strong> in multi-instance or stateful systems,
/// as the cache is lost on process restart and is not shared across instances.
/// </para>
/// <para>
/// Performance characteristics:
/// </para>
/// <list type="bullet">
/// <item><description>Get: O(1) average case</description></item>
/// <item><description>Store: O(1) average case</description></item>
/// <item><description>Cleanup: O(n) where n is total entries</description></item>
/// </list>
/// </remarks>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, IdempotencyResponse> _cache = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryIdempotencyStore"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider for timestamp management and expiration checks.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider"/> is null.</exception>
    public InMemoryIdempotencyStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public ValueTask<IdempotencyResponse?> GetAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (idempotencyKey == null)
            throw new ArgumentNullException(nameof(idempotencyKey));
        if (string.IsNullOrEmpty(idempotencyKey))
            throw new ArgumentException("Idempotency key cannot be empty.", nameof(idempotencyKey));

        if (_cache.TryGetValue(idempotencyKey, out var response))
        {
            // Check if expired
            var now = _timeProvider.GetUtcNow();
            if (now >= response.ExpiresAt)
            {
                // Entry has expired, remove it and return null
                _cache.TryRemove(idempotencyKey, out _);
                return new ValueTask<IdempotencyResponse?>((IdempotencyResponse?)null);
            }

            return new ValueTask<IdempotencyResponse?>(response);
        }

        return new ValueTask<IdempotencyResponse?>((IdempotencyResponse?)null);
    }

    /// <inheritdoc />
    public ValueTask StoreSuccessAsync(
        string idempotencyKey,
        object? result,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        if (idempotencyKey == null)
            throw new ArgumentNullException(nameof(idempotencyKey));
        if (string.IsNullOrEmpty(idempotencyKey))
            throw new ArgumentException("Idempotency key cannot be empty.", nameof(idempotencyKey));

        var now = _timeProvider.GetUtcNow();
        var response = new IdempotencyResponse
        {
            IdempotencyKey = idempotencyKey,
            SuccessResult = result,
            Status = IdempotencyStatus.Success,
            StoredAt = now,
            ExpiresAt = now.Add(ttl)
        };

        _cache[idempotencyKey] = response;
        return default;
    }

    /// <inheritdoc />
    public ValueTask StoreFailureAsync(
        string idempotencyKey,
        Exception exception,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        if (idempotencyKey == null)
            throw new ArgumentNullException(nameof(idempotencyKey));
        if (string.IsNullOrEmpty(idempotencyKey))
            throw new ArgumentException("Idempotency key cannot be empty.", nameof(idempotencyKey));
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));

        var now = _timeProvider.GetUtcNow();
        var response = new IdempotencyResponse
        {
            IdempotencyKey = idempotencyKey,
            FailureType = exception.GetType().FullName,
            FailureMessage = exception.Message,
            FailureStackTrace = exception.StackTrace,
            Status = IdempotencyStatus.Failure,
            StoredAt = now,
            ExpiresAt = now.Add(ttl)
        };

        _cache[idempotencyKey] = response;
        return default;
    }

    /// <inheritdoc />
    public ValueTask<bool> ExistsAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (idempotencyKey == null)
            throw new ArgumentNullException(nameof(idempotencyKey));
        if (string.IsNullOrEmpty(idempotencyKey))
            throw new ArgumentException("Idempotency key cannot be empty.", nameof(idempotencyKey));

        if (_cache.TryGetValue(idempotencyKey, out var response))
        {
            // Check if expired
            var now = _timeProvider.GetUtcNow();
            if (now >= response.ExpiresAt)
            {
                // Entry has expired, remove it and return false
                _cache.TryRemove(idempotencyKey, out _);
                return new ValueTask<bool>(false);
            }

            return new ValueTask<bool>(true);
        }

        return new ValueTask<bool>(false);
    }

    /// <inheritdoc />
    public ValueTask<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var expiredKeys = _cache
            .Where(kvp => now >= kvp.Value.ExpiresAt)
            .Select(kvp => kvp.Key)
            .ToList();

        var removedCount = 0;
        foreach (var key in expiredKeys)
        {
            if (_cache.TryRemove(key, out _))
            {
                removedCount++;
            }
        }

        return new ValueTask<int>(removedCount);
    }
}

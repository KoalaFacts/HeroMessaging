using System.Collections.Concurrent;
using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Utilities;

namespace HeroMessaging.Idempotency;

/// <summary>
/// Idempotency store wrapper that holds success records back while a <see cref="IdempotencyCommitScope"/> is active,
/// so a cached success is only written after the surrounding business transaction has committed.
/// </summary>
/// <remarks>
/// Without an active scope every call passes straight through. Failure records are written immediately: an
/// idempotent failure rolls the transaction back, so caching it cannot hide committed work.
/// </remarks>
internal sealed class CommitDeferredIdempotencyStore(IIdempotencyStore inner) : IIdempotencyStore
{
    private readonly IIdempotencyStore _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public ValueTask<IdempotencyResponse?> GetAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        => _inner.GetAsync(idempotencyKey, cancellationToken);

    public ValueTask StoreSuccessAsync(string idempotencyKey, object? result, TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        if (IdempotencyCommitScope.Current is { } scope)
        {
            scope.Defer(ct => _inner.StoreSuccessAsync(idempotencyKey, result, ttl, ct));
            return default;
        }

        return _inner.StoreSuccessAsync(idempotencyKey, result, ttl, cancellationToken);
    }

    public ValueTask StoreFailureAsync(string idempotencyKey, Exception exception, TimeSpan ttl,
        CancellationToken cancellationToken = default)
        => _inner.StoreFailureAsync(idempotencyKey, exception, ttl, cancellationToken);

    public ValueTask<bool> ExistsAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        => _inner.ExistsAsync(idempotencyKey, cancellationToken);

    public ValueTask<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
        => _inner.CleanupExpiredAsync(cancellationToken);
}

/// <summary>
/// Ambient scope opened around a business transaction. Success records written through
/// <see cref="CommitDeferredIdempotencyStore"/> inside it are kept until <see cref="CompleteAsync"/> runs after commit;
/// if the transaction fails, they are dropped so a redelivery runs the handler again.
/// </summary>
/// <remarks>
/// Nested scopes (a handler sending another transactional command) join the outermost scope, so records are written
/// only after the outermost transaction commits.
/// </remarks>
internal sealed class IdempotencyCommitScope
{
    private static readonly AsyncLocal<IdempotencyCommitScope?> Ambient = new();
    private readonly ConcurrentQueue<Func<CancellationToken, ValueTask>> _pending = new();

    private IdempotencyCommitScope()
    {
    }

    /// <summary>The scope active in the current async flow, if any.</summary>
    public static IdempotencyCommitScope? Current => Ambient.Value;

    /// <summary>
    /// Opens a scope, or returns <see langword="null"/> when an outer scope is already active (the outer one owns the writes).
    /// </summary>
    public static IdempotencyCommitScope? Begin()
    {
        if (Ambient.Value is not null)
        {
            return null;
        }

        var scope = new IdempotencyCommitScope();
        Ambient.Value = scope;
        return scope;
    }

    /// <summary>Ends the scope for the current async flow without writing anything.</summary>
    public static void End(IdempotencyCommitScope? scope)
    {
        if (scope is not null && ReferenceEquals(Ambient.Value, scope))
        {
            Ambient.Value = null;
        }
    }

    internal void Defer(Func<CancellationToken, ValueTask> write) => _pending.Enqueue(write);

    /// <summary>
    /// Writes the held success records after commit. A store outage at this point is swallowed: the work is committed,
    /// and a missing record only means a redelivery runs the handler again (the at-least-once behaviour without idempotency).
    /// </summary>
    public async ValueTask CompleteAsync()
    {
        while (_pending.TryDequeue(out var write))
        {
            try
            {
                await write(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ErrorClassifier.IsCritical(ex))
            {
                // Best effort after commit; see remarks above.
            }
        }
    }
}

namespace HeroMessaging.Abstractions.Storage;

/// <summary>
/// Claims external outbox deliveries with a renewable, owner-checked lease.
/// </summary>
public interface IExternalOutboxStorage : IOutboxStorage
{
    /// <summary>Whether this instance can claim rows independently of a caller-owned transaction.</summary>
    bool SupportsExternalClaims { get; }

    /// <summary>Gets pending entries intended for in-process delivery.</summary>
    Task<IEnumerable<OutboxEntry>> GetLocalPendingAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Atomically claims due external entries across workers.</summary>
    Task<IReadOnlyList<OutboxLease>> ClaimExternalAsync(int limit, TimeSpan leaseDuration, CancellationToken cancellationToken = default);

    /// <summary>Extends a lease while its owner is still delivering.</summary>
    Task<bool> RenewExternalAsync(string entryId, Guid token, TimeSpan leaseDuration, CancellationToken cancellationToken = default);

    /// <summary>Completes a confirmed delivery owned by this lease.</summary>
    Task<bool> CompleteExternalAsync(string entryId, Guid token, CancellationToken cancellationToken = default);

    /// <summary>Releases a failed attempt for a later retry.</summary>
    Task<bool> RetryExternalAsync(string entryId, Guid token, int retryCount, DateTimeOffset nextRetry, string error, CancellationToken cancellationToken = default);

    /// <summary>Marks an exhausted delivery as failed.</summary>
    Task<bool> FailExternalAsync(string entryId, Guid token, int retryCount, string error, CancellationToken cancellationToken = default);
}

/// <summary>An outbox entry and the token that owns its current lease.</summary>
/// <param name="Entry">The claimed message.</param>
/// <param name="Token">The lease owner token.</param>
public sealed record OutboxLease(OutboxEntry Entry, Guid Token);

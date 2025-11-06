namespace HeroMessaging.Abstractions.Idempotency;

/// <summary>
/// Represents the status of an idempotency entry in the store.
/// </summary>
public enum IdempotencyStatus
{
    /// <summary>
    /// Processing completed successfully and the result was cached.
    /// </summary>
    Success = 0,

    /// <summary>
    /// Processing failed with an idempotent error and the failure was cached.
    /// Subsequent requests with the same idempotency key will return this cached failure.
    /// </summary>
    Failure = 1,

    /// <summary>
    /// Processing is currently in progress (optimistic lock).
    /// This status is used to prevent concurrent processing of the same operation.
    /// </summary>
    /// <remarks>
    /// If a processing lock remains in this state beyond the expected processing time,
    /// it may indicate a crashed worker or deadlock. Implementations should handle
    /// timeout of stale processing locks.
    /// </remarks>
    Processing = 2
}

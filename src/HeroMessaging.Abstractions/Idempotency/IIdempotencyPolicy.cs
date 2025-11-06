namespace HeroMessaging.Abstractions.Idempotency;

/// <summary>
/// Defines the policy for idempotency behavior including TTL, caching strategy, and failure classification.
/// </summary>
/// <remarks>
/// <para>
/// The policy controls how idempotency is applied to message processing:
/// </para>
/// <list type="bullet">
/// <item><description>TTL configuration: How long to cache successful and failed results</description></item>
/// <item><description>Failure handling: Which failures are idempotent and should be cached</description></item>
/// <item><description>Key generation: The strategy for generating idempotency keys</description></item>
/// </list>
/// <para>
/// Different message types or use cases may require different policies. For example:
/// </para>
/// <list type="bullet">
/// <item><description>Financial transactions: Long TTL (30 days), cache all failures</description></item>
/// <item><description>API calls: Medium TTL (24 hours), cache non-transient failures</description></item>
/// <item><description>Event processing: Short TTL (1 hour), cache validation failures only</description></item>
/// </list>
/// </remarks>
public interface IIdempotencyPolicy
{
    /// <summary>
    /// Gets the time-to-live duration for successfully processed results.
    /// </summary>
    /// <value>
    /// The TTL for success responses. Typically ranges from 1 hour to 30 days depending on the use case.
    /// Default recommendation: 24 hours for most scenarios.
    /// </value>
    /// <remarks>
    /// After this duration, the cached success result will expire and the operation could be reprocessed.
    /// Consider compliance and audit requirements when setting this value.
    /// </remarks>
    TimeSpan SuccessTtl { get; }

    /// <summary>
    /// Gets the time-to-live duration for failed processing results.
    /// </summary>
    /// <value>
    /// The TTL for failure responses. Typically shorter than success TTL, ranging from 10 minutes to 24 hours.
    /// Default recommendation: 1 hour for most scenarios.
    /// </value>
    /// <remarks>
    /// Failures are often cached with shorter TTL to allow for fixes and retries.
    /// Only applies to failures classified as idempotent by <see cref="IsIdempotentFailure"/>.
    /// </remarks>
    TimeSpan FailureTtl { get; }

    /// <summary>
    /// Gets a value indicating whether failed processing attempts should be cached.
    /// </summary>
    /// <value>
    /// True to cache failures; false to only cache successes.
    /// Default recommendation: true for most scenarios to prevent retry storms.
    /// </value>
    /// <remarks>
    /// <para>
    /// Caching failures prevents clients from overwhelming the system with retries of operations
    /// that will deterministically fail (e.g., validation errors, business rule violations).
    /// </para>
    /// <para>
    /// Only failures identified as idempotent by <see cref="IsIdempotentFailure"/> will be cached.
    /// Transient failures (timeouts, network errors) are not cached regardless of this setting.
    /// </para>
    /// </remarks>
    bool CacheFailures { get; }

    /// <summary>
    /// Gets the key generation strategy used to create idempotency keys.
    /// </summary>
    /// <value>
    /// An instance of <see cref="IIdempotencyKeyGenerator"/> that produces idempotency keys.
    /// </value>
    IIdempotencyKeyGenerator KeyGenerator { get; }

    /// <summary>
    /// Determines whether an exception represents an idempotent failure that should be cached.
    /// </summary>
    /// <param name="exception">The exception that occurred during processing.</param>
    /// <returns>
    /// True if the failure is idempotent and should be cached (when <see cref="CacheFailures"/> is true);
    /// false if the failure is transient and should be retried without caching.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Idempotent failures are deterministic errors that will consistently fail for the same input:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Validation errors (ArgumentException, ValidationException)</description></item>
    /// <item><description>Business rule violations (InvalidOperationException, DomainException)</description></item>
    /// <item><description>Authorization failures (UnauthorizedAccessException)</description></item>
    /// <item><description>Not found errors (NotFoundException, KeyNotFoundException)</description></item>
    /// </list>
    /// <para>
    /// Non-idempotent (transient) failures should NOT be cached as they may succeed on retry:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Network errors (HttpRequestException, SocketException)</description></item>
    /// <item><description>Timeout errors (TimeoutException, TaskCanceledException)</description></item>
    /// <item><description>Temporary resource unavailability (ServiceUnavailableException)</description></item>
    /// <item><description>Database deadlocks or connection failures</description></item>
    /// </list>
    /// <para>
    /// When in doubt, return false (don't cache) - this is the safer default.
    /// </para>
    /// </remarks>
    bool IsIdempotentFailure(Exception exception);
}

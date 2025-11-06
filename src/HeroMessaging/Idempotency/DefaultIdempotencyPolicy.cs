using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Idempotency.KeyGeneration;

namespace HeroMessaging.Idempotency;

/// <summary>
/// Default implementation of <see cref="IIdempotencyPolicy"/> with sensible defaults for most scenarios.
/// </summary>
/// <remarks>
/// <para>
/// This policy provides conservative, production-ready defaults:
/// </para>
/// <list type="bullet">
/// <item><description><strong>Success TTL</strong>: 24 hours - balances storage costs with audit requirements</description></item>
/// <item><description><strong>Failure TTL</strong>: 1 hour - shorter duration for failures to allow fixes and retries</description></item>
/// <item><description><strong>Cache Failures</strong>: Enabled - prevents retry storms on deterministic failures</description></item>
/// <item><description><strong>Key Generator</strong>: MessageId-based - simple and globally unique</description></item>
/// </list>
/// <para>
/// <strong>Exception Classification Strategy</strong>:
/// </para>
/// <para>
/// The policy classifies exceptions as idempotent (deterministic, safe to cache) or
/// non-idempotent (transient, should be retried). This conservative approach ensures
/// reliability while preventing unnecessary retries.
/// </para>
/// <para>
/// <strong>Idempotent Failures</strong> (cached):
/// </para>
/// <list type="bullet">
/// <item><description><see cref="ArgumentException"/> and derivatives - Invalid input parameters</description></item>
/// <item><description><see cref="InvalidOperationException"/> - Business rule violations</description></item>
/// <item><description><see cref="NotSupportedException"/> - Unsupported operations</description></item>
/// <item><description><see cref="FormatException"/> - Invalid data formats</description></item>
/// <item><description><see cref="UnauthorizedAccessException"/> - Authorization failures</description></item>
/// <item><description><see cref="KeyNotFoundException"/> - Missing required data</description></item>
/// </list>
/// <para>
/// <strong>Non-Idempotent Failures</strong> (not cached, should retry):
/// </para>
/// <list type="bullet">
/// <item><description><see cref="TimeoutException"/> - Temporary timeout conditions</description></item>
/// <item><description><see cref="TaskCanceledException"/> - Cancelled operations</description></item>
/// <item><description><see cref="OperationCanceledException"/> - Cancelled operations</description></item>
/// <item><description><see cref="System.IO.IOException"/> - I/O errors (network, disk)</description></item>
/// <item><description><see cref="HttpRequestException"/> - Network communication errors</description></item>
/// <item><description><see cref="System.Net.Sockets.SocketException"/> - Socket-level errors</description></item>
/// </list>
/// <para>
/// <strong>Unknown Exceptions</strong>: The policy takes a conservative approach and does NOT cache
/// unknown exception types by default. This prevents unintended caching of potentially transient errors.
/// </para>
/// <para>
/// <strong>Customization</strong>: For different behavior, create a custom <see cref="IIdempotencyPolicy"/>
/// implementation or extend this class and override <see cref="IsIdempotentFailure"/>.
/// </para>
/// </remarks>
public class DefaultIdempotencyPolicy : IIdempotencyPolicy
{
    /// <inheritdoc />
    public TimeSpan SuccessTtl { get; }

    /// <inheritdoc />
    public TimeSpan FailureTtl { get; }

    /// <inheritdoc />
    public bool CacheFailures { get; }

    /// <inheritdoc />
    public IIdempotencyKeyGenerator KeyGenerator { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultIdempotencyPolicy"/> class.
    /// </summary>
    /// <param name="successTtl">
    /// The time-to-live for successful responses. Defaults to 24 hours.
    /// Consider longer TTLs (7-30 days) for financial transactions or audit compliance.
    /// </param>
    /// <param name="failureTtl">
    /// The time-to-live for failed responses. Defaults to 1 hour.
    /// Shorter TTL allows for fixes and retries while preventing retry storms.
    /// </param>
    /// <param name="keyGenerator">
    /// The key generation strategy. Defaults to <see cref="MessageIdKeyGenerator"/>.
    /// </param>
    /// <param name="cacheFailures">
    /// Whether to cache idempotent failures. Defaults to true.
    /// Set to false if you want to retry all failures regardless of type.
    /// </param>
    public DefaultIdempotencyPolicy(
        TimeSpan? successTtl = null,
        TimeSpan? failureTtl = null,
        IIdempotencyKeyGenerator? keyGenerator = null,
        bool cacheFailures = true)
    {
        SuccessTtl = successTtl ?? TimeSpan.FromHours(24);
        FailureTtl = failureTtl ?? TimeSpan.FromHours(1);
        KeyGenerator = keyGenerator ?? new MessageIdKeyGenerator();
        CacheFailures = cacheFailures;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="exception"/> is null.</exception>
    public virtual bool IsIdempotentFailure(Exception exception)
    {
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));

        // Idempotent failures: These are deterministic errors that will consistently
        // fail for the same input. Safe to cache to prevent retry storms.
        return exception switch
        {
            // Cancellation - transient (user/system initiated)
            // Check these first as they may derive from OperationCanceledException
            TaskCanceledException => false,
            OperationCanceledException => false,

            // Timeout - transient (may succeed on retry)
            TimeoutException => false,

            // I/O errors - transient (network, disk issues)
            System.IO.IOException => false,

            // Network errors - transient
            HttpRequestException => false,
            System.Net.Sockets.SocketException => false,

            // Validation and argument errors - deterministic
            // ArgumentException covers ArgumentNullException, ArgumentOutOfRangeException, etc.
            ArgumentException => true,

            // Business logic and operational errors - deterministic
            InvalidOperationException => true,
            NotSupportedException => true,
            FormatException => true,

            // Authorization and access errors - deterministic
            UnauthorizedAccessException => true,

            // Data not found errors - deterministic
            KeyNotFoundException => true,

            // Unknown exceptions - conservative default (don't cache)
            // This prevents unintended caching of potentially transient errors
            _ => false
        };
    }
}

using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;

namespace HeroMessaging.Abstractions.Idempotency;

/// <summary>
/// Generates idempotency keys for messages to identify duplicate operations.
/// </summary>
/// <remarks>
/// <para>
/// Idempotency keys are used to detect and prevent duplicate processing of messages.
/// Different strategies can be employed based on requirements:
/// </para>
/// <list type="bullet">
/// <item><description>MessageId-based: Uses the unique message identifier (default, recommended)</description></item>
/// <item><description>Content-based: Hashes message content to detect semantic duplicates</description></item>
/// <item><description>Composite: Combines multiple fields (e.g., MessageId + UserId for multi-tenancy)</description></item>
/// <item><description>Custom: User-provided keys from message metadata</description></item>
/// </list>
/// <para>
/// Keys must be deterministic - the same message and context should always produce the same key.
/// </para>
/// </remarks>
public interface IIdempotencyKeyGenerator
{
    /// <summary>
    /// Generates a unique, deterministic idempotency key for the given message and context.
    /// </summary>
    /// <param name="message">The message to generate a key for.</param>
    /// <param name="context">The processing context containing additional metadata.</param>
    /// <returns>
    /// A unique idempotency key string. The same message and context must always produce the same key.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The key should be:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Deterministic: Same input always produces same key</description></item>
    /// <item><description>Unique: Different operations produce different keys</description></item>
    /// <item><description>Stable: Key format doesn't change across deployments</description></item>
    /// <item><description>Reasonable length: Typically 50-200 characters for storage efficiency</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
    string GenerateKey(IMessage message, ProcessingContext context);
}

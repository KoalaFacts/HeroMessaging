using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;

namespace HeroMessaging.Idempotency.KeyGeneration;

/// <summary>
/// Generates idempotency keys based on message IDs.
/// This is the default and recommended key generation strategy for most scenarios.
/// </summary>
/// <remarks>
/// <para>
/// This generator creates idempotency keys in the format: <c>idempotency:{MessageId}</c>
/// </para>
/// <para>
/// Advantages:
/// </para>
/// <list type="bullet">
/// <item><description>Simple and deterministic - same message always produces same key</description></item>
/// <item><description>Globally unique - leverages GUID uniqueness guarantees</description></item>
/// <item><description>No hash computation overhead - very fast (nanoseconds)</description></item>
/// <item><description>Easy to trace and debug - key contains the actual message ID</description></item>
/// </list>
/// <para>
/// Use cases:
/// </para>
/// <list type="bullet">
/// <item><description>Standard message deduplication where each message has a unique ID</description></item>
/// <item><description>At-least-once delivery scenarios requiring exactly-once semantics</description></item>
/// <item><description>Event sourcing where event IDs are guaranteed unique</description></item>
/// </list>
/// <para>
/// Limitations:
/// </para>
/// <list type="bullet">
/// <item><description>Does not detect semantically identical messages with different IDs</description></item>
/// <item><description>Requires message IDs to be properly generated and unique</description></item>
/// </list>
/// </remarks>
public sealed class MessageIdKeyGenerator : IIdempotencyKeyGenerator
{
    private const string Prefix = "idempotency";

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is null.</exception>
    public string GenerateKey(IMessage message, ProcessingContext context)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        // Format: idempotency:{MessageId}
        // Using string interpolation for optimal performance
        return $"{Prefix}:{message.MessageId}";
    }
}

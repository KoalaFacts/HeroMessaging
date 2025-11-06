using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Policies;
using HeroMessaging.Abstractions.Processing;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing.Decorators;

/// <summary>
/// Decorator that applies rate limiting to message processing.
/// Enforces throughput limits to protect downstream systems and comply with API quotas.
/// </summary>
/// <remarks>
/// The decorator acquires a permit from the rate limiter before processing each message.
/// If rate limited, returns a failed result with retry information.
/// Supports both global and scoped (per-message-type) rate limiting.
/// </remarks>
public class RateLimitingDecorator(
    IMessageProcessor inner,
    IRateLimiter rateLimiter,
    ILogger<RateLimitingDecorator> logger) : MessageProcessorDecorator(inner)
{
    private readonly IRateLimiter _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
    private readonly ILogger<RateLimitingDecorator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Processes a message with rate limiting enforcement.
    /// </summary>
    /// <param name="message">The message to process.</param>
    /// <param name="context">The processing context.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>A processing result indicating success or failure with rate limit details.</returns>
    public override async ValueTask<ProcessingResult> ProcessAsync(
        IMessage message,
        ProcessingContext context,
        CancellationToken cancellationToken = default)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        // Use message type as rate limit key for scoped limiting
        var messageType = message.GetType().Name;

        // Attempt to acquire rate limit permit
        var rateLimitResult = await _rateLimiter.AcquireAsync(
            key: messageType,
            permits: 1,
            cancellationToken);

        if (!rateLimitResult.IsAllowed)
        {
            // Rate limited - log and return failure
            _logger.LogWarning(
                "Message {MessageId} of type {MessageType} was rate limited. Retry after {RetryAfter}. Reason: {Reason}",
                message.MessageId,
                messageType,
                rateLimitResult.RetryAfter,
                rateLimitResult.ReasonPhrase);

            return ProcessingResult.Failed(
                new InvalidOperationException($"Rate limit exceeded: {rateLimitResult.ReasonPhrase}"),
                $"Rate limit exceeded for message type {messageType}. {rateLimitResult.ReasonPhrase}");
        }

        // Permit acquired - proceed with processing
        _logger.LogDebug(
            "Message {MessageId} of type {MessageType} acquired rate limit permit. Remaining: {RemainingPermits}",
            message.MessageId,
            messageType,
            rateLimitResult.RemainingPermits);

        // Process message through inner processor
        var result = await _inner.ProcessAsync(message, context, cancellationToken);

        return result;
    }
}

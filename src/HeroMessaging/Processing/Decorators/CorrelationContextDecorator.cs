using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Choreography;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing.Decorators;

/// <summary>
/// Decorator that sets up correlation context for message processing
/// Enables choreography pattern by propagating correlation and causation IDs through async operations
/// </summary>
public class CorrelationContextDecorator(
    IMessageProcessor inner,
    ILogger<CorrelationContextDecorator> logger) : MessageProcessorDecorator(inner)
{
    private readonly ILogger<CorrelationContextDecorator> _logger = logger;
    /// <summary>
    /// Executes process async.
    /// </summary>

    public override async ValueTask<ProcessingResult> ProcessAsync(
        IMessage message,
        ProcessingContext context,
        CancellationToken cancellationToken = default)
    {
        var messageId = message.MessageId.ToString();
        var correlationId = string.IsNullOrEmpty(message.CorrelationId) ? messageId : message.CorrelationId;
        using var correlationScope = CorrelationContext.BeginScope(correlationId, messageId);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug(
                "Processing message {MessageId} with CorrelationId={CorrelationId}, CausationId={CausationId}",
                message.MessageId,
                message.CorrelationId,
                message.CausationId);

        // Add correlation information to processing context metadata
        var enrichedContext = context with
        {
            Metadata = context.Metadata.SetItems(
            [
                new("CorrelationId", message.CorrelationId ?? messageId),
                new("CausationId", message.CausationId ?? string.Empty),
                new("MessageId", messageId)
            ])
        };

        // Process message with correlation context active
        var result = await _inner.ProcessAsync(message, enrichedContext, cancellationToken).ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace(
                "Completed processing message {MessageId} in correlation {CorrelationId}",
                message.MessageId,
                message.CorrelationId);
        }

        return result;
    }
}

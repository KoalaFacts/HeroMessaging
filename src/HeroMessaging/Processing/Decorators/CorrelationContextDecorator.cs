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

    public override async ValueTask<ProcessingResult> ProcessAsync(
        IMessage message,
        ProcessingContext context,
        CancellationToken cancellationToken = default)
    {
        // Set up correlation context for this message processing
        using var correlationScope = CorrelationContext.BeginScope(message);

        _logger.LogDebug(
            "Processing message {MessageId} with CorrelationId={CorrelationId}, CausationId={CausationId}",
            message.MessageId,
            message.CorrelationId,
            message.CausationId);

        // Add correlation information to processing context metadata
        var enrichedContext = context
            .WithMetadata("CorrelationId", message.CorrelationId ?? message.MessageId.ToString())
            .WithMetadata("CausationId", message.CausationId ?? string.Empty)
            .WithMetadata("MessageId", message.MessageId.ToString());

        // Process message with correlation context active
        var result = await _inner.ProcessAsync(message, enrichedContext, cancellationToken);

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

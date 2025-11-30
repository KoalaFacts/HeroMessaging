using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing.Decorators;

/// <summary>
/// Decorator that adds logging to message processing
/// </summary>
public class LoggingDecorator(
    IMessageProcessor inner,
    ILogger<LoggingDecorator> logger,
    TimeProvider timeProvider,
    LogLevel successLogLevel = LogLevel.Debug,
    bool logPayload = false) : MessageProcessorDecorator(inner)
{
    private readonly ILogger<LoggingDecorator> _logger = logger;
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly LogLevel _successLogLevel = successLogLevel;
    private readonly bool _logPayload = logPayload;

    public override async ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context, CancellationToken cancellationToken = default)
    {
        var startTime = _timeProvider.GetTimestamp();
        var messageType = message.GetType().Name;

        _logger.LogDebug("Processing {MessageType} with ID {MessageId} in component {Component}",
            messageType, message.MessageId, context.Component);

        if (_logPayload && _logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Message payload: {@Message}", message);
        }

        try
        {
            var result = await _inner.ProcessAsync(message, context, cancellationToken).ConfigureAwait(false);
            var elapsedMs = _timeProvider.GetElapsedTime(startTime).TotalMilliseconds;

            if (result.Success)
            {
                _logger.Log(_successLogLevel,
                    "Successfully processed {MessageType} with ID {MessageId} in {ElapsedMs}ms",
                    messageType, message.MessageId, elapsedMs);
            }
            else
            {
                _logger.LogWarning(result.Exception,
                    "Failed to process {MessageType} with ID {MessageId} in {ElapsedMs}ms: {Reason}",
                    messageType, message.MessageId, elapsedMs, result.Message);
            }

            // Note: Context is immutable, timing info can be passed via result if needed

            return result;
        }
        catch (Exception ex)
        {
            var elapsedMs = _timeProvider.GetElapsedTime(startTime).TotalMilliseconds;
            _logger.LogError(ex,
                "Exception processing {MessageType} with ID {MessageId} after {ElapsedMs}ms",
                messageType, message.MessageId, elapsedMs);
            throw;
        }
    }
}
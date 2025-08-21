using System.Diagnostics;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing.Decorators;

/// <summary>
/// Decorator that adds logging to message processing
/// </summary>
public class LoggingDecorator : MessageProcessorDecorator
{
    private readonly ILogger<LoggingDecorator> _logger;
    private readonly LogLevel _successLogLevel;
    private readonly bool _logPayload;

    public LoggingDecorator(
        IMessageProcessor inner,
        ILogger<LoggingDecorator> logger,
        LogLevel successLogLevel = LogLevel.Debug,
        bool logPayload = false) : base(inner)
    {
        _logger = logger;
        _successLogLevel = successLogLevel;
        _logPayload = logPayload;
    }

    public override async ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var messageType = message.GetType().Name;
        
        _logger.LogDebug("Processing {MessageType} with ID {MessageId} in component {Component}",
            messageType, message.MessageId, context.Component);

        if (_logPayload && _logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Message payload: {@Message}", message);
        }

        try
        {
            var result = await _inner.ProcessAsync(message, context, cancellationToken);
            stopwatch.Stop();

            if (result.Success)
            {
                _logger.Log(_successLogLevel, 
                    "Successfully processed {MessageType} with ID {MessageId} in {ElapsedMs}ms",
                    messageType, message.MessageId, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogWarning(result.Exception,
                    "Failed to process {MessageType} with ID {MessageId} in {ElapsedMs}ms: {Reason}",
                    messageType, message.MessageId, stopwatch.ElapsedMilliseconds, result.Message);
            }

            // Note: Context is immutable, timing info can be passed via result if needed
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Exception processing {MessageType} with ID {MessageId} after {ElapsedMs}ms",
                messageType, message.MessageId, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
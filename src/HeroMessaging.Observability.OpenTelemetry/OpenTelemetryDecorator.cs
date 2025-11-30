using System.Diagnostics;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing.Decorators;

namespace HeroMessaging.Observability.OpenTelemetry;

/// <summary>
/// Decorator that adds OpenTelemetry instrumentation to message processing
/// </summary>
public class OpenTelemetryDecorator(IMessageProcessor inner, TimeProvider timeProvider) : MessageProcessorDecorator(inner)
{
    private readonly IMessageProcessor _innerProcessor = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public override async ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context, CancellationToken cancellationToken = default)
    {
        var messageType = message.GetType().Name;
        var startTime = _timeProvider.GetTimestamp();

        // Start OpenTelemetry activity for message processing
        using var activity = HeroMessagingInstrumentation.StartProcessActivity(message, context.Component);

        // Add retry count if applicable
        if (context.RetryCount > 0 && activity != null)
        {
            activity.SetTag("messaging.retry_count", context.RetryCount);
        }

        try
        {
            var result = await _innerProcessor.ProcessAsync(message, context, cancellationToken).ConfigureAwait(false);
            var elapsedMs = _timeProvider.GetElapsedTime(startTime).TotalMilliseconds;

            // Record processing duration metric
            HeroMessagingInstrumentation.RecordProcessingDuration(messageType, elapsedMs);

            if (!result.Success)
            {
                // Record failure and set activity status
                var reason = result.Message ?? result.Exception?.Message ?? "Unknown";
                HeroMessagingInstrumentation.RecordMessageFailed(messageType, reason);

                if (result.Exception != null)
                {
                    HeroMessagingInstrumentation.SetError(activity, result.Exception);
                }
                else
                {
                    activity?.SetStatus(ActivityStatusCode.Error, reason);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            var elapsedMs = _timeProvider.GetElapsedTime(startTime).TotalMilliseconds;

            // Record failure and set activity error status
            HeroMessagingInstrumentation.RecordMessageFailed(messageType, ex.Message);
            HeroMessagingInstrumentation.SetError(activity, ex);

            // Record processing duration even for failures
            HeroMessagingInstrumentation.RecordProcessingDuration(messageType, elapsedMs);

            throw;
        }
    }
}

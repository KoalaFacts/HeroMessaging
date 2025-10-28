using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing.Decorators;
using System.Diagnostics;

namespace HeroMessaging.Observability.OpenTelemetry;

/// <summary>
/// Decorator that adds OpenTelemetry instrumentation to message processing
/// </summary>
public class OpenTelemetryDecorator(IMessageProcessor inner) : MessageProcessorDecorator(inner)
{
    public override async ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context, CancellationToken cancellationToken = default)
    {
        var messageType = message.GetType().Name;
        var stopwatch = Stopwatch.StartNew();

        // Start OpenTelemetry activity for message processing
        using var activity = HeroMessagingInstrumentation.StartProcessActivity(message, context.Component);

        // Add retry count if applicable
        if (context.RetryCount > 0 && activity != null)
        {
            activity.SetTag("messaging.retry_count", context.RetryCount);
        }

        try
        {
            var result = await _inner.ProcessAsync(message, context, cancellationToken);
            stopwatch.Stop();

            // Record processing duration metric
            HeroMessagingInstrumentation.RecordProcessingDuration(messageType, stopwatch.Elapsed.TotalMilliseconds);

            if (!result.Success)
            {
                // Record failure and set activity status
                var reason = result.Message ?? result.Exception?.Message ?? "Unknown";
                HeroMessagingInstrumentation.RecordMessageFailed(messageType, reason);

                if (result.Exception != null)
                {
                    HeroMessagingInstrumentation.SetError(activity, result.Exception);
                }
                else if (activity != null)
                {
                    activity.SetStatus(ActivityStatusCode.Error, reason);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // Record failure and set activity error status
            HeroMessagingInstrumentation.RecordMessageFailed(messageType, ex.Message);
            HeroMessagingInstrumentation.SetError(activity, ex);

            // Record processing duration even for failures
            HeroMessagingInstrumentation.RecordProcessingDuration(messageType, stopwatch.Elapsed.TotalMilliseconds);

            throw;
        }
    }
}

using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing.Decorators;
using System.Diagnostics;

namespace HeroMessaging.Observability.OpenTelemetry;

/// <summary>
/// Decorator that adds OpenTelemetry distributed tracing and metrics instrumentation to message processing.
/// Creates spans for each message operation and records performance metrics.
/// </summary>
/// <remarks>
/// This decorator integrates OpenTelemetry observability into the message processing pipeline by:
/// - Creating a distributed trace span for each message processing operation
/// - Recording processing duration metrics for performance analysis
/// - Tracking failure counts and error details
/// - Adding contextual tags (message type, retry count, component name)
/// - Setting appropriate activity status codes (OK, Error)
/// - Capturing exceptions with full stack traces
///
/// The decorator uses the HeroMessagingInstrumentation class to emit telemetry data
/// to the configured OpenTelemetry activity source and meter.
///
/// Spans created by this decorator include:
/// - Operation name: "process_message"
/// - Tags: messaging.message_type, messaging.component, messaging.retry_count (if applicable)
/// - Status: OK on success, Error on failure
/// - Events: Exception details on errors
///
/// Metrics recorded:
/// - messaging.process.duration: Histogram of processing times by message type
/// - messaging.process.failed: Counter of failed messages by type and reason
///
/// Position in pipeline:
/// Add this decorator to the processing pipeline using UseOpenTelemetry() on the pipeline builder.
/// Typically placed early in the pipeline to capture the full processing time including other decorators.
///
/// Example pipeline configuration:
/// <code>
/// builder.ConfigureProcessing(pipeline =>
/// {
///     pipeline.UseOpenTelemetry()  // First: captures total processing time
///             .UseRetry()           // Subsequent decorators are traced
///             .UseValidation();
/// });
/// </code>
/// </remarks>
public class OpenTelemetryDecorator(IMessageProcessor inner) : MessageProcessorDecorator(inner)
{
    private readonly IMessageProcessor _innerProcessor = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>
    /// Processes a message with OpenTelemetry instrumentation, creating a trace span and recording metrics.
    /// </summary>
    /// <param name="message">The message to process</param>
    /// <param name="context">The processing context containing metadata about the current operation</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation, containing the processing result</returns>
    /// <remarks>
    /// This method:
    /// 1. Creates a new OpenTelemetry activity (span) for the message processing operation
    /// 2. Adds relevant tags to the span (message type, component, retry count)
    /// 3. Delegates to the inner processor to perform the actual processing
    /// 4. Records processing duration metrics on completion
    /// 5. Records failure metrics and sets error status on failures
    /// 6. Ensures metrics are recorded even when exceptions are thrown
    ///
    /// The activity is automatically propagated to child operations via the ambient Activity.Current,
    /// enabling distributed tracing across service boundaries.
    /// </remarks>
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
            var result = await _innerProcessor.ProcessAsync(message, context, cancellationToken);
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

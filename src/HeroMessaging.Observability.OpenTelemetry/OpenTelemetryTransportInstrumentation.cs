using System.Diagnostics;
using HeroMessaging.Abstractions.Observability;
using HeroMessaging.Abstractions.Transport;

namespace HeroMessaging.Observability.OpenTelemetry;

/// <summary>
/// OpenTelemetry implementation of transport instrumentation
/// </summary>
public sealed class OpenTelemetryTransportInstrumentation : ITransportInstrumentation
{
    /// <summary>
    /// Singleton instance for easy access
    /// </summary>
    public static readonly OpenTelemetryTransportInstrumentation Instance = new();

    /// <inheritdoc />
    public Activity? StartSendActivity(TransportEnvelope envelope, string destination, string transportName)
    {
        return TransportInstrumentation.StartSendActivity(envelope, destination, transportName);
    }

    /// <inheritdoc />
    public Activity? StartPublishActivity(TransportEnvelope envelope, string destination, string transportName)
    {
        return TransportInstrumentation.StartPublishActivity(envelope, destination, transportName);
    }

    /// <inheritdoc />
    public Activity? StartReceiveActivity(
        TransportEnvelope envelope,
        string source,
        string transportName,
        string consumerId,
        ActivityContext parentContext = default)
    {
        return TransportInstrumentation.StartReceiveActivity(
            envelope, source, transportName, consumerId, parentContext);
    }

    /// <inheritdoc />
    public void RecordSendDuration(string transportName, string destination, string messageType, double durationMs)
    {
        TransportInstrumentation.RecordTransportSendDuration(transportName, destination, messageType, durationMs);
    }

    /// <inheritdoc />
    public void RecordReceiveDuration(string transportName, string source, string messageType, double durationMs)
    {
        TransportInstrumentation.RecordTransportReceiveDuration(transportName, source, messageType, durationMs);
    }

    /// <inheritdoc />
    public void RecordSerializationDuration(string transportName, string messageType, string operation, double durationMs)
    {
        TransportInstrumentation.RecordSerializationDuration(transportName, messageType, operation, durationMs);
    }

    /// <inheritdoc />
    public void RecordOperation(string transportName, string operation, string status)
    {
        TransportInstrumentation.RecordTransportOperation(transportName, operation, status);
    }

    /// <inheritdoc />
    public void RecordError(Activity? activity, Exception exception)
    {
        TransportInstrumentation.SetError(activity, exception);
    }

    /// <inheritdoc />
    public void AddEvent(Activity? activity, string eventName, IEnumerable<KeyValuePair<string, object?>>? attributes = null)
    {
        TransportInstrumentation.AddActivityEvent(activity, eventName, attributes);
    }

    /// <inheritdoc />
    public TransportEnvelope InjectTraceContext(TransportEnvelope envelope, Activity? activity)
    {
        return TraceContextPropagator.Inject(envelope, activity);
    }

    /// <inheritdoc />
    public ActivityContext ExtractTraceContext(TransportEnvelope envelope)
    {
        return TraceContextPropagator.Extract(envelope);
    }
}

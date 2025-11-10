using System.Diagnostics;
using HeroMessaging.Abstractions.Transport;

namespace HeroMessaging.Abstractions.Observability;

/// <summary>
/// Interface for transport layer instrumentation
/// Allows transport implementations to report telemetry in a standardized way
/// </summary>
public interface ITransportInstrumentation
{
    /// <summary>
    /// Start a send activity for outgoing messages
    /// </summary>
    /// <param name="envelope">Message envelope being sent</param>
    /// <param name="destination">Destination address</param>
    /// <param name="transportName">Name of the transport implementation</param>
    /// <returns>Activity instance or null if tracing is disabled</returns>
    Activity? StartSendActivity(TransportEnvelope envelope, string destination, string transportName);

    /// <summary>
    /// Start a publish activity for outgoing messages (pub/sub)
    /// </summary>
    /// <param name="envelope">Message envelope being published</param>
    /// <param name="destination">Destination topic/exchange</param>
    /// <param name="transportName">Name of the transport implementation</param>
    /// <returns>Activity instance or null if tracing is disabled</returns>
    Activity? StartPublishActivity(TransportEnvelope envelope, string destination, string transportName);

    /// <summary>
    /// Start a receive activity for incoming messages
    /// </summary>
    /// <param name="envelope">Message envelope being received</param>
    /// <param name="source">Source address</param>
    /// <param name="transportName">Name of the transport implementation</param>
    /// <param name="consumerId">Consumer identifier</param>
    /// <param name="parentContext">Parent activity context extracted from message headers</param>
    /// <returns>Activity instance or null if tracing is disabled</returns>
    Activity? StartReceiveActivity(
        TransportEnvelope envelope,
        string source,
        string transportName,
        string consumerId,
        ActivityContext parentContext = default);

    /// <summary>
    /// Record a send operation duration
    /// </summary>
    /// <param name="transportName">Name of the transport implementation</param>
    /// <param name="destination">Destination address</param>
    /// <param name="messageType">Message type</param>
    /// <param name="durationMs">Duration in milliseconds</param>
    void RecordSendDuration(string transportName, string destination, string messageType, double durationMs);

    /// <summary>
    /// Record a receive operation duration
    /// </summary>
    /// <param name="transportName">Name of the transport implementation</param>
    /// <param name="source">Source address</param>
    /// <param name="messageType">Message type</param>
    /// <param name="durationMs">Duration in milliseconds</param>
    void RecordReceiveDuration(string transportName, string source, string messageType, double durationMs);

    /// <summary>
    /// Record serialization/deserialization duration
    /// </summary>
    /// <param name="transportName">Name of the transport implementation</param>
    /// <param name="messageType">Message type</param>
    /// <param name="operation">Operation type (serialize/deserialize)</param>
    /// <param name="durationMs">Duration in milliseconds</param>
    void RecordSerializationDuration(string transportName, string messageType, string operation, double durationMs);

    /// <summary>
    /// Record a transport operation result
    /// </summary>
    /// <param name="transportName">Name of the transport implementation</param>
    /// <param name="operation">Operation type (send/receive/publish)</param>
    /// <param name="status">Operation status (success/failure/timeout)</param>
    void RecordOperation(string transportName, string operation, string status);

    /// <summary>
    /// Record an error during transport operations
    /// </summary>
    /// <param name="activity">Current activity</param>
    /// <param name="exception">Exception that occurred</param>
    void RecordError(Activity? activity, Exception exception);

    /// <summary>
    /// Add an event to an activity
    /// </summary>
    /// <param name="activity">Activity to add event to</param>
    /// <param name="eventName">Event name</param>
    /// <param name="attributes">Event attributes</param>
    void AddEvent(Activity? activity, string eventName, IEnumerable<KeyValuePair<string, object?>>? attributes = null);

    /// <summary>
    /// Inject trace context into envelope headers
    /// </summary>
    /// <param name="envelope">Envelope to inject context into</param>
    /// <param name="activity">Activity containing trace context</param>
    /// <returns>Updated envelope with trace context</returns>
    TransportEnvelope InjectTraceContext(TransportEnvelope envelope, Activity? activity);

    /// <summary>
    /// Extract trace context from envelope headers
    /// </summary>
    /// <param name="envelope">Envelope containing trace context</param>
    /// <returns>Extracted activity context</returns>
    ActivityContext ExtractTraceContext(TransportEnvelope envelope);
}

/// <summary>
/// No-op implementation of ITransportInstrumentation for cases where telemetry is disabled
/// </summary>
public sealed class NoOpTransportInstrumentation : ITransportInstrumentation
{
    /// <summary>
    /// Singleton instance
    /// </summary>
    public static readonly NoOpTransportInstrumentation Instance = new();

    private NoOpTransportInstrumentation() { }

    /// <inheritdoc />
    public Activity? StartSendActivity(TransportEnvelope envelope, string destination, string transportName) => null;

    /// <inheritdoc />
    public Activity? StartPublishActivity(TransportEnvelope envelope, string destination, string transportName) => null;

    /// <inheritdoc />
    public Activity? StartReceiveActivity(
        TransportEnvelope envelope,
        string source,
        string transportName,
        string consumerId,
        ActivityContext parentContext = default) => null;

    /// <inheritdoc />
    public void RecordSendDuration(string transportName, string destination, string messageType, double durationMs) { }

    /// <inheritdoc />
    public void RecordReceiveDuration(string transportName, string source, string messageType, double durationMs) { }

    /// <inheritdoc />
    public void RecordSerializationDuration(string transportName, string messageType, string operation, double durationMs) { }

    /// <inheritdoc />
    public void RecordOperation(string transportName, string operation, string status) { }

    /// <inheritdoc />
    public void RecordError(Activity? activity, Exception exception) { }

    /// <inheritdoc />
    public void AddEvent(Activity? activity, string eventName, IEnumerable<KeyValuePair<string, object?>>? attributes = null) { }

    /// <inheritdoc />
    public TransportEnvelope InjectTraceContext(TransportEnvelope envelope, Activity? activity) => envelope;

    /// <inheritdoc />
    public ActivityContext ExtractTraceContext(TransportEnvelope envelope) => default;
}

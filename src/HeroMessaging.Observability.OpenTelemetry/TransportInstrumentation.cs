using System.Diagnostics;
using System.Diagnostics.Metrics;
using HeroMessaging.Abstractions.Transport;

namespace HeroMessaging.Observability.OpenTelemetry;

/// <summary>
/// OpenTelemetry instrumentation for HeroMessaging transport layer
/// Provides activities and metrics for send/receive operations at the transport boundary
/// </summary>
public static class TransportInstrumentation
{
    /// <summary>
    /// Activity source name for transport operations
    /// </summary>
    public const string ActivitySourceName = "HeroMessaging.Transport";

    /// <summary>
    /// Meter name for transport metrics
    /// </summary>
    public const string MeterName = "HeroMessaging.Transport.Metrics";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
    private static readonly Meter Meter = new(MeterName, "1.0.0");

    // Transport-level metrics
    private static readonly Histogram<double> TransportSendDuration = Meter.CreateHistogram<double>(
        "heromessaging_transport_send_duration_ms",
        unit: "ms",
        description: "Transport send operation duration in milliseconds");

    private static readonly Histogram<double> TransportReceiveDuration = Meter.CreateHistogram<double>(
        "heromessaging_transport_receive_duration_ms",
        unit: "ms",
        description: "Transport receive operation duration in milliseconds");

    private static readonly Histogram<double> SerializationDuration = Meter.CreateHistogram<double>(
        "heromessaging_transport_serialization_duration_ms",
        unit: "ms",
        description: "Message serialization/deserialization duration in milliseconds");

    private static readonly Histogram<long> PayloadSize = Meter.CreateHistogram<long>(
        "heromessaging_transport_payload_size_bytes",
        unit: "bytes",
        description: "Message payload size in bytes");

    private static readonly Counter<long> TransportOperations = Meter.CreateCounter<long>(
        "heromessaging_transport_operations_total",
        description: "Total number of transport operations");

    private static readonly Counter<long> PublisherConfirms = Meter.CreateCounter<long>(
        "heromessaging_transport_publisher_confirms_total",
        description: "Total number of publisher confirms (RabbitMQ)");

    private static readonly Counter<long> Acknowledgments = Meter.CreateCounter<long>(
        "heromessaging_transport_acknowledgments_total",
        description: "Total number of message acknowledgments");

    /// <summary>
    /// Start a new activity for sending a message
    /// </summary>
    /// <param name="envelope">Transport envelope containing the message</param>
    /// <param name="destination">Destination queue/topic name</param>
    /// <param name="transportName">Transport implementation name (e.g., "rabbitmq", "inmemory")</param>
    /// <param name="additionalTags">Additional tags to add to the activity</param>
    /// <returns>Activity instance or null if tracing is disabled</returns>
    public static Activity? StartSendActivity(
        TransportEnvelope envelope,
        string destination,
        string transportName,
        IEnumerable<KeyValuePair<string, object?>>? additionalTags = null)
    {
        var activity = ActivitySource.StartActivity(
            "HeroMessaging.Transport.Send",
            ActivityKind.Producer,
            Activity.Current?.Context ?? default);

        if (activity != null)
        {
            SetCommonTags(activity, envelope, transportName);
            activity.SetTag("messaging.destination", destination);
            activity.SetTag("messaging.operation", "send");

            if (additionalTags != null)
            {
                foreach (var tag in additionalTags)
                {
                    activity.SetTag(tag.Key, tag.Value);
                }
            }
        }

        return activity;
    }

    /// <summary>
    /// Start a new activity for publishing a message
    /// </summary>
    /// <param name="envelope">Transport envelope containing the message</param>
    /// <param name="destination">Destination topic/exchange name</param>
    /// <param name="transportName">Transport implementation name</param>
    /// <param name="additionalTags">Additional tags to add to the activity</param>
    /// <returns>Activity instance or null if tracing is disabled</returns>
    public static Activity? StartPublishActivity(
        TransportEnvelope envelope,
        string destination,
        string transportName,
        IEnumerable<KeyValuePair<string, object?>>? additionalTags = null)
    {
        var activity = ActivitySource.StartActivity(
            "HeroMessaging.Transport.Publish",
            ActivityKind.Producer,
            Activity.Current?.Context ?? default);

        if (activity != null)
        {
            SetCommonTags(activity, envelope, transportName);
            activity.SetTag("messaging.destination", destination);
            activity.SetTag("messaging.operation", "publish");

            if (additionalTags != null)
            {
                foreach (var tag in additionalTags)
                {
                    activity.SetTag(tag.Key, tag.Value);
                }
            }
        }

        return activity;
    }

    /// <summary>
    /// Start a new activity for receiving a message
    /// </summary>
    /// <param name="envelope">Transport envelope containing the message</param>
    /// <param name="source">Source queue/topic name</param>
    /// <param name="transportName">Transport implementation name</param>
    /// <param name="consumerId">Consumer identifier</param>
    /// <param name="parentContext">Parent activity context extracted from message headers</param>
    /// <param name="additionalTags">Additional tags to add to the activity</param>
    /// <returns>Activity instance or null if tracing is disabled</returns>
    public static Activity? StartReceiveActivity(
        TransportEnvelope envelope,
        string source,
        string transportName,
        string consumerId,
        ActivityContext parentContext = default,
        IEnumerable<KeyValuePair<string, object?>>? additionalTags = null)
    {
        var activity = ActivitySource.StartActivity(
            "HeroMessaging.Transport.Receive",
            ActivityKind.Consumer,
            parentContext);

        if (activity != null)
        {
            SetCommonTags(activity, envelope, transportName);
            activity.SetTag("messaging.source", source);
            activity.SetTag("messaging.consumer_id", consumerId);
            activity.SetTag("messaging.operation", "receive");

            if (additionalTags != null)
            {
                foreach (var tag in additionalTags)
                {
                    activity.SetTag(tag.Key, tag.Value);
                }
            }
        }

        return activity;
    }

    /// <summary>
    /// Record transport send operation duration
    /// </summary>
    public static void RecordTransportSendDuration(
        string transportName,
        string destination,
        string messageType,
        double durationMs)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("transport", transportName),
            new KeyValuePair<string, object?>("destination", destination),
            new KeyValuePair<string, object?>("message_type", messageType)
        };

        TransportSendDuration.Record(durationMs, tags);
    }

    /// <summary>
    /// Record transport receive operation duration
    /// </summary>
    public static void RecordTransportReceiveDuration(
        string transportName,
        string source,
        string messageType,
        double durationMs)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("transport", transportName),
            new KeyValuePair<string, object?>("source", source),
            new KeyValuePair<string, object?>("message_type", messageType)
        };

        TransportReceiveDuration.Record(durationMs, tags);
    }

    /// <summary>
    /// Record serialization/deserialization duration
    /// </summary>
    public static void RecordSerializationDuration(
        string transportName,
        string messageType,
        string operation,
        double durationMs)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("transport", transportName),
            new KeyValuePair<string, object?>("message_type", messageType),
            new KeyValuePair<string, object?>("operation", operation)
        };

        SerializationDuration.Record(durationMs, tags);
    }

    /// <summary>
    /// Record message payload size
    /// </summary>
    public static void RecordPayloadSize(
        string transportName,
        string messageType,
        string operation,
        long sizeBytes)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("transport", transportName),
            new KeyValuePair<string, object?>("message_type", messageType),
            new KeyValuePair<string, object?>("operation", operation)
        };

        PayloadSize.Record(sizeBytes, tags);
    }

    /// <summary>
    /// Record transport operation (success, failure, timeout)
    /// </summary>
    public static void RecordTransportOperation(
        string transportName,
        string operation,
        string status)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("transport", transportName),
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("status", status)
        };

        TransportOperations.Add(1, tags);
    }

    /// <summary>
    /// Record publisher confirm (RabbitMQ)
    /// </summary>
    public static void RecordPublisherConfirm(
        string transportName,
        string status)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("transport", transportName),
            new KeyValuePair<string, object?>("status", status)
        };

        PublisherConfirms.Add(1, tags);
    }

    /// <summary>
    /// Record message acknowledgment
    /// </summary>
    public static void RecordAcknowledgment(
        string transportName,
        string operation)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("transport", transportName),
            new KeyValuePair<string, object?>("operation", operation)
        };

        Acknowledgments.Add(1, tags);
    }

    /// <summary>
    /// Add an event to an activity with optional attributes
    /// </summary>
    public static void AddActivityEvent(
        Activity? activity,
        string eventName,
        IEnumerable<KeyValuePair<string, object?>>? attributes = null)
    {
        if (activity == null)
        {
            return;
        }

        if (attributes != null)
        {
            activity.AddEvent(new ActivityEvent(eventName, tags: new ActivityTagsCollection(attributes)));
        }
        else
        {
            activity.AddEvent(new ActivityEvent(eventName));
        }
    }

    /// <summary>
    /// Set error status on activity
    /// </summary>
    public static void SetError(Activity? activity, Exception exception)
    {
        if (activity != null)
        {
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity.AddException(exception);
        }
    }

    private static void SetCommonTags(Activity activity, TransportEnvelope envelope, string transportName)
    {
        activity.SetTag("messaging.system", "heromessaging");
        activity.SetTag("messaging.transport", transportName);
        activity.SetTag("messaging.message_id", envelope.MessageId);
        activity.SetTag("messaging.message_type", envelope.MessageType);

        if (!string.IsNullOrEmpty(envelope.CorrelationId))
        {
            activity.SetTag("messaging.correlation_id", envelope.CorrelationId);
        }

        if (!string.IsNullOrEmpty(envelope.CausationId))
        {
            activity.SetTag("messaging.causation_id", envelope.CausationId);
        }

        if (!string.IsNullOrEmpty(envelope.ConversationId))
        {
            activity.SetTag("messaging.conversation_id", envelope.ConversationId);
        }

        // Record payload size
        if (envelope.Body.Length > 0)
        {
            activity.SetTag("messaging.message_payload_size_bytes", envelope.Body.Length);
        }
    }
}

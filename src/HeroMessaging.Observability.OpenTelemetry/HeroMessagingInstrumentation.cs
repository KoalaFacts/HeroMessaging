using System.Diagnostics;
using System.Diagnostics.Metrics;
using HeroMessaging.Abstractions.Messages;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace HeroMessaging.Observability.OpenTelemetry;

/// <summary>
/// OpenTelemetry instrumentation for HeroMessaging
/// </summary>
public static class HeroMessagingInstrumentation
{
    public const string ActivitySourceName = "HeroMessaging";
    public const string MeterName = "HeroMessaging.Metrics";
    
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
    private static readonly Meter Meter = new(MeterName, "1.0.0");
    
    // Metrics
    private static readonly Counter<long> MessagesSentCounter = Meter.CreateCounter<long>(
        "heromessaging_messages_sent_total",
        description: "Total number of messages sent");
    
    private static readonly Counter<long> MessagesReceivedCounter = Meter.CreateCounter<long>(
        "heromessaging_messages_received_total",
        description: "Total number of messages received");
    
    private static readonly Counter<long> MessagesFailedCounter = Meter.CreateCounter<long>(
        "heromessaging_messages_failed_total",
        description: "Total number of failed messages");
    
    private static readonly Histogram<double> MessageProcessingDuration = Meter.CreateHistogram<double>(
        "heromessaging_message_processing_duration_ms",
        unit: "ms",
        description: "Message processing duration in milliseconds");
    
    private static readonly Histogram<long> MessageSize = Meter.CreateHistogram<long>(
        "heromessaging_message_size_bytes",
        unit: "bytes",
        description: "Message size in bytes");
    
    // UpDownCounter not available in older versions - using Counter instead for now
    private static readonly Counter<int> QueueSize = Meter.CreateCounter<int>(
        "heromessaging_queue_operations",
        description: "Queue operations counter");
    
    /// <summary>
    /// Start a new activity for sending a message
    /// </summary>
    public static Activity? StartSendActivity(IMessage message, string destination)
    {
        var activity = ActivitySource.StartActivity(
            "HeroMessaging.Send",
            ActivityKind.Producer,
            Activity.Current?.Context ?? default);
        
        if (activity != null)
        {
            activity.SetTag("messaging.system", "heromessaging");
            activity.SetTag("messaging.destination", destination);
            activity.SetTag("messaging.message_id", message.MessageId.ToString());
            activity.SetTag("messaging.message_type", message.GetType().Name);
            
            if (message.Metadata != null)
            {
                foreach (var kvp in message.Metadata)
                {
                    activity.SetTag($"messaging.metadata.{kvp.Key}", kvp.Value?.ToString());
                }
            }
        }
        
        return activity;
    }
    
    /// <summary>
    /// Start a new activity for receiving a message
    /// </summary>
    public static Activity? StartReceiveActivity(IMessage message, string source)
    {
        var activity = ActivitySource.StartActivity(
            "HeroMessaging.Receive",
            ActivityKind.Consumer,
            Activity.Current?.Context ?? default);
        
        if (activity != null)
        {
            activity.SetTag("messaging.system", "heromessaging");
            activity.SetTag("messaging.source", source);
            activity.SetTag("messaging.message_id", message.MessageId.ToString());
            activity.SetTag("messaging.message_type", message.GetType().Name);
        }
        
        return activity;
    }
    
    /// <summary>
    /// Start a new activity for processing a message
    /// </summary>
    public static Activity? StartProcessActivity(IMessage message, string processor)
    {
        var activity = ActivitySource.StartActivity(
            "HeroMessaging.Process",
            ActivityKind.Internal,
            Activity.Current?.Context ?? default);
        
        if (activity != null)
        {
            activity.SetTag("messaging.system", "heromessaging");
            activity.SetTag("messaging.processor", processor);
            activity.SetTag("messaging.message_id", message.MessageId.ToString());
            activity.SetTag("messaging.message_type", message.GetType().Name);
        }
        
        return activity;
    }
    
    /// <summary>
    /// Record that a message was sent
    /// </summary>
    public static void RecordMessageSent(string messageType, string destination, long sizeInBytes = 0)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("message_type", messageType),
            new KeyValuePair<string, object?>("destination", destination)
        };
        
        MessagesSentCounter.Add(1, tags);
        
        if (sizeInBytes > 0)
        {
            MessageSize.Record(sizeInBytes, tags);
        }
    }
    
    /// <summary>
    /// Record that a message was received
    /// </summary>
    public static void RecordMessageReceived(string messageType, string source)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("message_type", messageType),
            new KeyValuePair<string, object?>("source", source)
        };
        
        MessagesReceivedCounter.Add(1, tags);
    }
    
    /// <summary>
    /// Record that a message failed
    /// </summary>
    public static void RecordMessageFailed(string messageType, string reason)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("message_type", messageType),
            new KeyValuePair<string, object?>("reason", reason)
        };
        
        MessagesFailedCounter.Add(1, tags);
    }
    
    /// <summary>
    /// Record message processing duration
    /// </summary>
    public static void RecordProcessingDuration(string messageType, double durationMs)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("message_type", messageType)
        };
        
        MessageProcessingDuration.Record(durationMs, tags);
    }
    
    /// <summary>
    /// Update queue size
    /// </summary>
    public static void UpdateQueueSize(string queueName, int delta)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("queue_name", queueName)
        };
        
        QueueSize.Add(delta, tags);
    }
    
    /// <summary>
    /// Set error on activity
    /// </summary>
    public static void SetError(Activity? activity, Exception exception)
    {
        if (activity != null)
        {
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity.AddException(exception);
        }
    }
}
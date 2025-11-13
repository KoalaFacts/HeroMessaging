using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Observability.OpenTelemetry;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Xunit;

namespace HeroMessaging.Observability.OpenTelemetry.Tests.Unit;

/// <summary>
/// Unit tests for HeroMessagingInstrumentation
/// Testing activity creation and metrics recording for message processing
/// </summary>
public class HeroMessagingInstrumentationTests : IDisposable
{
    private readonly ActivityListener _activityListener;
    private readonly List<Activity> _activities;
    private readonly MeterListener _meterListener;
    private readonly Dictionary<string, List<Measurement<long>>> _longMeasurements;
    private readonly Dictionary<string, List<Measurement<double>>> _doubleMeasurements;
    private readonly Dictionary<string, List<Measurement<int>>> _intMeasurements;

    public HeroMessagingInstrumentationTests()
    {
        _activities = new List<Activity>();
        _longMeasurements = new Dictionary<string, List<Measurement<long>>>();
        _doubleMeasurements = new Dictionary<string, List<Measurement<double>>>();
        _intMeasurements = new Dictionary<string, List<Measurement<int>>>();

        // Set up activity listener to capture activities
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == HeroMessagingInstrumentation.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => _activities.Add(activity)
        };
        ActivitySource.AddActivityListener(_activityListener);

        // Set up meter listener to capture metrics
        _meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == HeroMessagingInstrumentation.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };

        _meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (!_longMeasurements.ContainsKey(instrument.Name))
            {
                _longMeasurements[instrument.Name] = new List<Measurement<long>>();
            }
            _longMeasurements[instrument.Name].Add(measurement);
        });

        _meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (!_doubleMeasurements.ContainsKey(instrument.Name))
            {
                _doubleMeasurements[instrument.Name] = new List<Measurement<double>>();
            }
            _doubleMeasurements[instrument.Name].Add(measurement);
        });

        _meterListener.SetMeasurementEventCallback<int>((instrument, measurement, tags, state) =>
        {
            if (!_intMeasurements.ContainsKey(instrument.Name))
            {
                _intMeasurements[instrument.Name] = new List<Measurement<int>>();
            }
            _intMeasurements[instrument.Name].Add(measurement);
        });

        _meterListener.Start();
    }

    public void Dispose()
    {
        _activityListener?.Dispose();
        _meterListener?.Dispose();
        foreach (var activity in _activities)
        {
            activity?.Dispose();
        }
    }

    #region StartSendActivity Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void StartSendActivity_CreatesProducerActivityWithCorrectTags()
    {
        // Arrange
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid()
        };
        var destination = "test-queue";

        // Act
        var activity = HeroMessagingInstrumentation.StartSendActivity(message, destination);

        // Assert
        Assert.NotNull(activity);
        Assert.Equal("HeroMessaging.Send", activity.OperationName);
        Assert.Equal(ActivityKind.Producer, activity.Kind);
        Assert.Equal("heromessaging", activity.GetTagItem("messaging.system"));
        Assert.Equal(destination, activity.GetTagItem("messaging.destination"));
        Assert.Equal(message.MessageId.ToString(), activity.GetTagItem("messaging.message_id"));
        Assert.Equal("TestMessage", activity.GetTagItem("messaging.message_type"));

        activity?.Dispose();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void StartSendActivity_WithMetadata_IncludesMetadataAsTags()
    {
        // Arrange
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            Metadata = new Dictionary<string, object>
            {
                ["tenant_id"] = "tenant-123",
                ["priority"] = "high"
            }
        };
        var destination = "test-queue";

        // Act
        var activity = HeroMessagingInstrumentation.StartSendActivity(message, destination);

        // Assert
        Assert.NotNull(activity);
        Assert.Equal("tenant-123", activity.GetTagItem("messaging.metadata.tenant_id"));
        Assert.Equal("high", activity.GetTagItem("messaging.metadata.priority"));

        activity?.Dispose();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void StartSendActivity_WithNullMetadata_DoesNotThrow()
    {
        // Arrange
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            Metadata = null
        };

        // Act
        var activity = HeroMessagingInstrumentation.StartSendActivity(message, "queue");

        // Assert
        Assert.NotNull(activity);

        activity?.Dispose();
    }

    #endregion

    #region StartReceiveActivity Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void StartReceiveActivity_CreatesConsumerActivityWithCorrectTags()
    {
        // Arrange
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid()
        };
        var source = "test-queue";

        // Act
        var activity = HeroMessagingInstrumentation.StartReceiveActivity(message, source);

        // Assert
        Assert.NotNull(activity);
        Assert.Equal("HeroMessaging.Receive", activity.OperationName);
        Assert.Equal(ActivityKind.Consumer, activity.Kind);
        Assert.Equal("heromessaging", activity.GetTagItem("messaging.system"));
        Assert.Equal(source, activity.GetTagItem("messaging.source"));
        Assert.Equal(message.MessageId.ToString(), activity.GetTagItem("messaging.message_id"));
        Assert.Equal("TestMessage", activity.GetTagItem("messaging.message_type"));

        activity?.Dispose();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void StartReceiveActivity_WithParentContext_CreatesChildActivity()
    {
        // Arrange
        using var parentActivity = new Activity("parent");
        parentActivity.Start();
        var parentContext = parentActivity.Context;

        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var source = "test-queue";

        // Act - Note: StartReceiveActivity doesn't accept parent context, so it will use Activity.Current
        var activity = HeroMessagingInstrumentation.StartReceiveActivity(message, source);

        // Assert
        Assert.NotNull(activity);
        Assert.Equal(parentContext.TraceId, activity.TraceId);
        Assert.Equal(parentContext.SpanId, activity.ParentSpanId);

        activity?.Dispose();
    }

    #endregion

    #region StartProcessActivity Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void StartProcessActivity_CreatesInternalActivityWithCorrectTags()
    {
        // Arrange
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid()
        };
        var processor = "MessageHandler";

        // Act
        var activity = HeroMessagingInstrumentation.StartProcessActivity(message, processor);

        // Assert
        Assert.NotNull(activity);
        Assert.Equal("HeroMessaging.Process", activity.OperationName);
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.Equal("heromessaging", activity.GetTagItem("messaging.system"));
        Assert.Equal(processor, activity.GetTagItem("messaging.processor"));
        Assert.Equal(message.MessageId.ToString(), activity.GetTagItem("messaging.message_id"));
        Assert.Equal("TestMessage", activity.GetTagItem("messaging.message_type"));

        activity?.Dispose();
    }

    #endregion

    #region Metrics Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordMessageSent_IncrementsCounter()
    {
        // Arrange
        var messageType = "TestMessage";
        var destination = "test-queue";

        // Act
        HeroMessagingInstrumentation.RecordMessageSent(messageType, destination);

        // Assert
        Assert.True(_longMeasurements.ContainsKey("heromessaging_messages_sent_total"));
        var measurements = _longMeasurements["heromessaging_messages_sent_total"];
        Assert.Single(measurements);
        Assert.Equal(1, measurements[0].Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordMessageSent_WithSize_RecordsBothCounterAndHistogram()
    {
        // Arrange
        var messageType = "TestMessage";
        var destination = "test-queue";
        var sizeInBytes = 1024L;

        // Act
        HeroMessagingInstrumentation.RecordMessageSent(messageType, destination, sizeInBytes);

        // Assert
        Assert.True(_longMeasurements.ContainsKey("heromessaging_messages_sent_total"));
        Assert.True(_longMeasurements.ContainsKey("heromessaging_message_size_bytes"));

        var counterMeasurements = _longMeasurements["heromessaging_messages_sent_total"];
        Assert.Single(counterMeasurements);
        Assert.Equal(1, counterMeasurements[0].Value);

        var histogramMeasurements = _longMeasurements["heromessaging_message_size_bytes"];
        Assert.Single(histogramMeasurements);
        Assert.Equal(sizeInBytes, histogramMeasurements[0].Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordMessageSent_WithZeroSize_DoesNotRecordHistogram()
    {
        // Arrange
        var messageType = "TestMessage";
        var destination = "test-queue";

        // Act
        HeroMessagingInstrumentation.RecordMessageSent(messageType, destination, 0);

        // Assert
        Assert.True(_longMeasurements.ContainsKey("heromessaging_messages_sent_total"));
        // Size histogram should not be recorded for zero size
        if (_longMeasurements.ContainsKey("heromessaging_message_size_bytes"))
        {
            Assert.Empty(_longMeasurements["heromessaging_message_size_bytes"]);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordMessageReceived_IncrementsCounter()
    {
        // Arrange
        var messageType = "TestMessage";
        var source = "test-queue";

        // Act
        HeroMessagingInstrumentation.RecordMessageReceived(messageType, source);

        // Assert
        Assert.True(_longMeasurements.ContainsKey("heromessaging_messages_received_total"));
        var measurements = _longMeasurements["heromessaging_messages_received_total"];
        Assert.Single(measurements);
        Assert.Equal(1, measurements[0].Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordMessageFailed_IncrementsCounter()
    {
        // Arrange
        var messageType = "TestMessage";
        var reason = "Validation failed";

        // Act
        HeroMessagingInstrumentation.RecordMessageFailed(messageType, reason);

        // Assert
        Assert.True(_longMeasurements.ContainsKey("heromessaging_messages_failed_total"));
        var measurements = _longMeasurements["heromessaging_messages_failed_total"];
        Assert.Single(measurements);
        Assert.Equal(1, measurements[0].Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordProcessingDuration_RecordsHistogram()
    {
        // Arrange
        var messageType = "TestMessage";
        var durationMs = 42.5;

        // Act
        HeroMessagingInstrumentation.RecordProcessingDuration(messageType, durationMs);

        // Assert
        Assert.True(_doubleMeasurements.ContainsKey("heromessaging_message_processing_duration_ms"));
        var measurements = _doubleMeasurements["heromessaging_message_processing_duration_ms"];
        Assert.Single(measurements);
        Assert.Equal(durationMs, measurements[0].Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void UpdateQueueSize_RecordsCounter()
    {
        // Arrange
        var queueName = "test-queue";
        var delta = 5;

        // Act
        HeroMessagingInstrumentation.UpdateQueueSize(queueName, delta);

        // Assert
        Assert.True(_intMeasurements.ContainsKey("heromessaging_queue_operations"));
        var measurements = _intMeasurements["heromessaging_queue_operations"];
        Assert.Single(measurements);
        Assert.Equal(delta, measurements[0].Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void UpdateQueueSize_WithNegativeDelta_RecordsCorrectly()
    {
        // Arrange
        var queueName = "test-queue";
        var delta = -3;

        // Act
        HeroMessagingInstrumentation.UpdateQueueSize(queueName, delta);

        // Assert
        Assert.True(_intMeasurements.ContainsKey("heromessaging_queue_operations"));
        var measurements = _intMeasurements["heromessaging_queue_operations"];
        Assert.Single(measurements);
        Assert.Equal(delta, measurements[0].Value);
    }

    #endregion

    #region SetError Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void SetError_WithActivity_SetsErrorStatus()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        using var activity = HeroMessagingInstrumentation.StartProcessActivity(message, "processor");
        var exception = new InvalidOperationException("Test error");

        // Act
        HeroMessagingInstrumentation.SetError(activity, exception);

        // Assert
        Assert.NotNull(activity);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Contains("Test error", activity.StatusDescription ?? "");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SetError_WithNullActivity_DoesNotThrow()
    {
        // Arrange
        var exception = new InvalidOperationException("Test error");

        // Act & Assert
        var recordedException = Record.Exception(() =>
            HeroMessagingInstrumentation.SetError(null, exception));

        Assert.Null(recordedException);
    }

    #endregion

    #region Multiple Operations Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordMessageSent_MultipleTimes_IncrementsCounterCorrectly()
    {
        // Arrange
        var messageType = "TestMessage";
        var destination = "test-queue";

        // Act
        HeroMessagingInstrumentation.RecordMessageSent(messageType, destination);
        HeroMessagingInstrumentation.RecordMessageSent(messageType, destination);
        HeroMessagingInstrumentation.RecordMessageSent(messageType, destination);

        // Assert
        var measurements = _longMeasurements["heromessaging_messages_sent_total"];
        Assert.Equal(3, measurements.Count);
        Assert.All(measurements, m => Assert.Equal(1, m.Value));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordProcessingDuration_MultipleTimes_RecordsAllMeasurements()
    {
        // Arrange
        var messageType = "TestMessage";
        var durations = new[] { 10.5, 20.3, 15.7 };

        // Act
        foreach (var duration in durations)
        {
            HeroMessagingInstrumentation.RecordProcessingDuration(messageType, duration);
        }

        // Assert
        var measurements = _doubleMeasurements["heromessaging_message_processing_duration_ms"];
        Assert.Equal(3, measurements.Count);
        for (int i = 0; i < durations.Length; i++)
        {
            Assert.Equal(durations[i], measurements[i].Value);
        }
    }

    #endregion

    #region Constants Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ActivitySourceName_IsCorrect()
    {
        // Assert
        Assert.Equal("HeroMessaging", HeroMessagingInstrumentation.ActivitySourceName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MeterName_IsCorrect()
    {
        // Assert
        Assert.Equal("HeroMessaging.Metrics", HeroMessagingInstrumentation.MeterName);
    }

    #endregion

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; }
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public IDictionary<string, object>? Metadata { get; set; }
    }
}

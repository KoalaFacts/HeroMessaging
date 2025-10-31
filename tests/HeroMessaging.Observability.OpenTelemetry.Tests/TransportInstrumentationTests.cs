using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Observability.OpenTelemetry;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Xunit;

namespace HeroMessaging.Observability.OpenTelemetry.Tests;

public class TransportInstrumentationTests : IDisposable
{
    private readonly ActivityListener _activityListener;
    private readonly List<Activity> _activities;
    private readonly MeterListener _meterListener;
    private readonly Dictionary<string, List<Measurement<long>>> _longMeasurements;
    private readonly Dictionary<string, List<Measurement<double>>> _doubleMeasurements;

    public TransportInstrumentationTests()
    {
        _activities = new List<Activity>();
        _longMeasurements = new Dictionary<string, List<Measurement<long>>>();
        _doubleMeasurements = new Dictionary<string, List<Measurement<double>>>();

        // Set up activity listener to capture activities
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TransportInstrumentation.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => _activities.Add(activity)
        };
        ActivitySource.AddActivityListener(_activityListener);

        // Set up meter listener to capture metrics
        _meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == TransportInstrumentation.MeterName)
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

        _meterListener.Start();
    }

    public void Dispose()
    {
        _activityListener?.Dispose();
        _meterListener?.Dispose();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void StartSendActivity_CreatesProducerActivityWithCorrectTags()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 },
            messageId: "test-123",
            correlationId: "corr-456");
        var destination = "test-queue";
        var transportName = "rabbitmq";

        // Act
        var activity = TransportInstrumentation.StartSendActivity(envelope, destination, transportName);

        // Assert
        Assert.NotNull(activity);
        Assert.Equal("HeroMessaging.Transport.Send", activity.OperationName);
        Assert.Equal(ActivityKind.Producer, activity.Kind);
        Assert.Equal("heromessaging", activity.GetTagItem("messaging.system"));
        Assert.Equal(destination, activity.GetTagItem("messaging.destination"));
        Assert.Equal(transportName, activity.GetTagItem("messaging.transport"));
        Assert.Equal("test-123", activity.GetTagItem("messaging.message_id"));
        Assert.Equal("TestMessage", activity.GetTagItem("messaging.message_type"));
        Assert.Equal("corr-456", activity.GetTagItem("messaging.correlation_id"));
        Assert.Equal("send", activity.GetTagItem("messaging.operation"));

        activity?.Dispose();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void StartSendActivity_WithNullCorrelationId_DoesNotSetTag()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 },
            messageId: "test-123");
        var destination = "test-queue";
        var transportName = "inmemory";

        // Act
        var activity = TransportInstrumentation.StartSendActivity(envelope, destination, transportName);

        // Assert
        Assert.NotNull(activity);
        Assert.Null(activity.GetTagItem("messaging.correlation_id"));

        activity?.Dispose();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void StartPublishActivity_CreatesProducerActivityWithPublishOperation()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestEvent",
            body: new byte[] { 1, 2, 3 },
            messageId: "test-123");
        var destination = "test-topic";
        var transportName = "rabbitmq";

        // Act
        var activity = TransportInstrumentation.StartPublishActivity(envelope, destination, transportName);

        // Assert
        Assert.NotNull(activity);
        Assert.Equal("HeroMessaging.Transport.Publish", activity.OperationName);
        Assert.Equal(ActivityKind.Producer, activity.Kind);
        Assert.Equal("publish", activity.GetTagItem("messaging.operation"));
        Assert.Equal(destination, activity.GetTagItem("messaging.destination"));

        activity?.Dispose();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void StartReceiveActivity_CreatesConsumerActivityWithCorrectTags()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 },
            messageId: "test-123",
            correlationId: "corr-456");
        var source = "test-queue";
        var transportName = "rabbitmq";
        var consumerId = "consumer-1";

        // Act
        var activity = TransportInstrumentation.StartReceiveActivity(envelope, source, transportName, consumerId);

        // Assert
        Assert.NotNull(activity);
        Assert.Equal("HeroMessaging.Transport.Receive", activity.OperationName);
        Assert.Equal(ActivityKind.Consumer, activity.Kind);
        Assert.Equal("heromessaging", activity.GetTagItem("messaging.system"));
        Assert.Equal(source, activity.GetTagItem("messaging.source"));
        Assert.Equal(transportName, activity.GetTagItem("messaging.transport"));
        Assert.Equal("test-123", activity.GetTagItem("messaging.message_id"));
        Assert.Equal("TestMessage", activity.GetTagItem("messaging.message_type"));
        Assert.Equal(consumerId, activity.GetTagItem("messaging.consumer_id"));
        Assert.Equal("receive", activity.GetTagItem("messaging.operation"));

        activity?.Dispose();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void StartReceiveActivity_WithParentContext_UsesProvidedParent()
    {
        // Arrange
        using var parentActivity = new Activity("parent");
        parentActivity.Start();
        var parentContext = parentActivity.Context;

        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 },
            messageId: "test-123");
        var source = "test-queue";
        var transportName = "rabbitmq";

        // Act
        var activity = TransportInstrumentation.StartReceiveActivity(
            envelope, source, transportName, "consumer-1", parentContext);

        // Assert
        Assert.NotNull(activity);
        Assert.Equal(parentContext.TraceId, activity.TraceId);
        Assert.Equal(parentContext.SpanId, activity.ParentSpanId);

        activity?.Dispose();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordTransportSendDuration_RecordsHistogramMeasurement()
    {
        // Arrange
        var transportName = "rabbitmq";
        var destination = "test-queue";
        var messageType = "TestMessage";
        var durationMs = 12.5;

        // Act
        TransportInstrumentation.RecordTransportSendDuration(
            transportName, destination, messageType, durationMs);

        // Assert
        Assert.True(_doubleMeasurements.ContainsKey("heromessaging_transport_send_duration_ms"));
        var measurements = _doubleMeasurements["heromessaging_transport_send_duration_ms"];
        Assert.Single(measurements);
        Assert.Equal(durationMs, measurements[0].Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordTransportReceiveDuration_RecordsHistogramMeasurement()
    {
        // Arrange
        var transportName = "rabbitmq";
        var source = "test-queue";
        var messageType = "TestMessage";
        var durationMs = 8.3;

        // Act
        TransportInstrumentation.RecordTransportReceiveDuration(
            transportName, source, messageType, durationMs);

        // Assert
        Assert.True(_doubleMeasurements.ContainsKey("heromessaging_transport_receive_duration_ms"));
        var measurements = _doubleMeasurements["heromessaging_transport_receive_duration_ms"];
        Assert.Single(measurements);
        Assert.Equal(durationMs, measurements[0].Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordTransportOperation_SuccessStatus_IncrementsCounter()
    {
        // Arrange
        var transportName = "rabbitmq";
        var operation = "send";
        var status = "success";

        // Act
        TransportInstrumentation.RecordTransportOperation(transportName, operation, status);

        // Assert
        Assert.True(_longMeasurements.ContainsKey("heromessaging_transport_operations_total"));
        var measurements = _longMeasurements["heromessaging_transport_operations_total"];
        Assert.Single(measurements);
        Assert.Equal(1, measurements[0].Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordTransportOperation_FailureStatus_IncrementsCounter()
    {
        // Arrange
        var transportName = "rabbitmq";
        var operation = "send";
        var status = "failure";

        // Act
        TransportInstrumentation.RecordTransportOperation(transportName, operation, status);

        // Assert
        Assert.True(_longMeasurements.ContainsKey("heromessaging_transport_operations_total"));
        var measurements = _longMeasurements["heromessaging_transport_operations_total"];
        Assert.Single(measurements);
        Assert.Equal(1, measurements[0].Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordSerializationDuration_RecordsHistogramMeasurement()
    {
        // Arrange
        var transportName = "rabbitmq";
        var messageType = "TestMessage";
        var operation = "serialize";
        var durationMs = 2.1;

        // Act
        TransportInstrumentation.RecordSerializationDuration(
            transportName, messageType, operation, durationMs);

        // Assert
        Assert.True(_doubleMeasurements.ContainsKey("heromessaging_transport_serialization_duration_ms"));
        var measurements = _doubleMeasurements["heromessaging_transport_serialization_duration_ms"];
        Assert.Single(measurements);
        Assert.Equal(durationMs, measurements[0].Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordPayloadSize_RecordsHistogramMeasurement()
    {
        // Arrange
        var transportName = "rabbitmq";
        var messageType = "TestMessage";
        var operation = "send";
        var sizeBytes = 1024L;

        // Act
        TransportInstrumentation.RecordPayloadSize(
            transportName, messageType, operation, sizeBytes);

        // Assert
        Assert.True(_longMeasurements.ContainsKey("heromessaging_transport_payload_size_bytes"));
        var measurements = _longMeasurements["heromessaging_transport_payload_size_bytes"];
        Assert.Single(measurements);
        Assert.Equal(sizeBytes, measurements[0].Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddActivityEvent_WithActivity_AddsEvent()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 },
            messageId: "test-123");
        using var activity = TransportInstrumentation.StartSendActivity(envelope, "queue", "rabbitmq");

        // Act
        TransportInstrumentation.AddActivityEvent(activity, "test.event", new Dictionary<string, object>
        {
            ["key1"] = "value1",
            ["key2"] = 42
        });

        // Assert
        Assert.NotNull(activity);
        var events = activity.Events.ToList();
        Assert.Single(events);
        Assert.Equal("test.event", events[0].Name);
        var tags = events[0].Tags.ToList();
        Assert.Contains(tags, t => t.Key == "key1" && (string?)t.Value == "value1");
        Assert.Contains(tags, t => t.Key == "key2" && (int?)t.Value == 42);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddActivityEvent_WithNullActivity_DoesNotThrow()
    {
        // Act & Assert
        var exception = Record.Exception(() =>
            TransportInstrumentation.AddActivityEvent(null, "test.event"));

        Assert.Null(exception);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SetError_WithActivity_SetsErrorStatus()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 },
            messageId: "test-123");
        using var activity = TransportInstrumentation.StartSendActivity(envelope, "queue", "rabbitmq");
        var exception = new InvalidOperationException("Test error");

        // Act
        TransportInstrumentation.SetError(activity, exception);

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
            TransportInstrumentation.SetError(null, exception));

        Assert.Null(recordedException);
    }
}

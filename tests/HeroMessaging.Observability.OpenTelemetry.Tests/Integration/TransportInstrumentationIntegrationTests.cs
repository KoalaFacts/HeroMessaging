using HeroMessaging.Abstractions.Observability;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Observability.OpenTelemetry;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Xunit;

namespace HeroMessaging.Observability.OpenTelemetry.Tests;

public class TransportInstrumentationIntegrationTests : IDisposable
{
    private readonly ActivityListener _activityListener;
    private readonly List<Activity> _activities;
    private readonly MeterListener _meterListener;
    private readonly Dictionary<string, List<Measurement<long>>> _longMeasurements;
    private readonly Dictionary<string, List<Measurement<double>>> _doubleMeasurements;
    private readonly ITransportInstrumentation _instrumentation;

    public TransportInstrumentationIntegrationTests()
    {
        _activities = new List<Activity>();
        _longMeasurements = new Dictionary<string, List<Measurement<long>>>();
        _doubleMeasurements = new Dictionary<string, List<Measurement<double>>>();
        _instrumentation = OpenTelemetryTransportInstrumentation.Instance;

        // Set up activity listener
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == TransportInstrumentation.ActivitySourceName ||
                source.Name == HeroMessagingInstrumentation.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => _activities.Add(activity)
        };
        ActivitySource.AddActivityListener(_activityListener);

        // Set up meter listener
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

        // Dispose all captured activities
        foreach (var activity in _activities)
        {
            activity?.Dispose();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void EndToEnd_SendReceive_CreatesLinkedActivities()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 },
            messageId: "test-123",
            correlationId: "corr-456");
        var destination = "test-queue";
        var transportName = "rabbitmq";

        // Act - Send side
        using var sendActivity = _instrumentation.StartSendActivity(envelope, destination, transportName);
        Assert.NotNull(sendActivity);

        // Inject trace context
        var envelopeWithContext = _instrumentation.InjectTraceContext(envelope, sendActivity);
        Assert.True(envelopeWithContext.HasHeader(TraceContextPropagator.TraceParentHeaderName));

        // Simulate transport
        sendActivity.Dispose();

        // Act - Receive side
        var parentContext = _instrumentation.ExtractTraceContext(envelopeWithContext);
        Assert.NotEqual(default, parentContext);

        using var receiveActivity = _instrumentation.StartReceiveActivity(
            envelopeWithContext, destination, transportName, "consumer-1", parentContext);
        Assert.NotNull(receiveActivity);

        // Assert
        Assert.Equal(2, _activities.Count);

        var send = _activities[0];
        var receive = _activities[1];

        // Verify send activity
        Assert.Equal("HeroMessaging.Transport.Send", send.OperationName);
        Assert.Equal(ActivityKind.Producer, send.Kind);
        Assert.Equal(destination, send.GetTagItem("messaging.destination"));

        // Verify receive activity
        Assert.Equal("HeroMessaging.Transport.Receive", receive.OperationName);
        Assert.Equal(ActivityKind.Consumer, receive.Kind);
        Assert.Equal(destination, receive.GetTagItem("messaging.source"));

        // Verify trace context propagation
        Assert.Equal(send.TraceId, receive.TraceId);
        Assert.Equal(send.SpanId, receive.ParentSpanId);
        Assert.True(receive.HasRemoteParent);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void EndToEnd_PublishReceive_CreatesLinkedActivitiesWithTraceState()
    {
        // Arrange
        using var rootActivity = new Activity("root");
        rootActivity.SetIdFormat(ActivityIdFormat.W3C);
        rootActivity.TraceStateString = "vendor=test-value";
        rootActivity.Start();

        var envelope = new TransportEnvelope(
            messageType: "TestEvent",
            body: new byte[] { 1, 2, 3 });
        var topic = "test-topic";
        var transportName = "rabbitmq";

        // Act - Publish side
        using var publishActivity = _instrumentation.StartPublishActivity(envelope, topic, transportName);
        Assert.NotNull(publishActivity);

        var envelopeWithContext = _instrumentation.InjectTraceContext(envelope, publishActivity);
        publishActivity.Dispose();

        // Act - Receive side
        var parentContext = _instrumentation.ExtractTraceContext(envelopeWithContext);
        using var receiveActivity = _instrumentation.StartReceiveActivity(
            envelopeWithContext, topic, transportName, "consumer-1", parentContext);

        // Assert
        Assert.NotNull(receiveActivity);
        Assert.Equal(publishActivity.TraceId, receiveActivity.TraceId);
        Assert.Equal("vendor=test-value", receiveActivity.TraceStateString);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void TransportOperations_RecordMetrics_AllMetricsRecorded()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3, 4, 5 });
        var destination = "test-queue";
        var transportName = "rabbitmq";

        // Act
        using var sendActivity = _instrumentation.StartSendActivity(envelope, destination, transportName);

        // Record serialization
        _instrumentation.RecordSerializationDuration(transportName, envelope.MessageType, "serialize", 1.5);

        // Record send
        _instrumentation.RecordSendDuration(transportName, destination, envelope.MessageType, 10.2);
        _instrumentation.RecordOperation(transportName, "send", "success");

        sendActivity?.Dispose();

        // Receive side
        var parentContext = _instrumentation.ExtractTraceContext(
            _instrumentation.InjectTraceContext(envelope, sendActivity));
        using var receiveActivity = _instrumentation.StartReceiveActivity(
            envelope, destination, transportName, "consumer-1", parentContext);

        // Record deserialization
        _instrumentation.RecordSerializationDuration(transportName, envelope.MessageType, "deserialize", 0.8);

        // Record receive
        _instrumentation.RecordReceiveDuration(transportName, destination, envelope.MessageType, 5.3);
        _instrumentation.RecordOperation(transportName, "receive", "success");

        // Assert
        Assert.True(_doubleMeasurements.ContainsKey("heromessaging_transport_send_duration_ms"));
        Assert.True(_doubleMeasurements.ContainsKey("heromessaging_transport_receive_duration_ms"));
        Assert.True(_doubleMeasurements.ContainsKey("heromessaging_transport_serialization_duration_ms"));
        Assert.True(_longMeasurements.ContainsKey("heromessaging_transport_operations_total"));

        var sendDurations = _doubleMeasurements["heromessaging_transport_send_duration_ms"];
        Assert.Single(sendDurations);
        Assert.Equal(10.2, sendDurations[0].Value);

        var receiveDurations = _doubleMeasurements["heromessaging_transport_receive_duration_ms"];
        Assert.Single(receiveDurations);
        Assert.Equal(5.3, receiveDurations[0].Value);

        var serializationDurations = _doubleMeasurements["heromessaging_transport_serialization_duration_ms"];
        Assert.Equal(2, serializationDurations.Count); // serialize + deserialize

        var operations = _longMeasurements["heromessaging_transport_operations_total"];
        Assert.Equal(2, operations.Count); // send + receive
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void TransportError_RecordsErrorInActivity()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 });
        var destination = "test-queue";
        var transportName = "rabbitmq";

        // Act
        using var sendActivity = _instrumentation.StartSendActivity(envelope, destination, transportName);
        Assert.NotNull(sendActivity);

        var exception = new InvalidOperationException("Connection failed");
        _instrumentation.RecordError(sendActivity, exception);
        _instrumentation.RecordOperation(transportName, "send", "failure");

        // Assert
        Assert.Equal(ActivityStatusCode.Error, sendActivity.Status);
        Assert.Contains("Connection failed", sendActivity.StatusDescription ?? "");

        var operations = _longMeasurements["heromessaging_transport_operations_total"];
        Assert.Single(operations);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void ActivityEvents_RecordedCorrectly()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 });
        var destination = "test-queue";
        var transportName = "rabbitmq";

        // Act
        using var sendActivity = _instrumentation.StartSendActivity(envelope, destination, transportName);
        Assert.NotNull(sendActivity);

        _instrumentation.AddEvent(sendActivity, "serialization.start");
        _instrumentation.AddEvent(sendActivity, "serialization.complete", new[]
        {
            new KeyValuePair<string, object?>("size_bytes", 3)
        });
        _instrumentation.AddEvent(sendActivity, "publish.start");
        _instrumentation.AddEvent(sendActivity, "publish.confirmed");

        // Assert
        var events = sendActivity.Events.ToList();
        Assert.Equal(4, events.Count);
        Assert.Equal("serialization.start", events[0].Name);
        Assert.Equal("serialization.complete", events[1].Name);
        Assert.Equal("publish.start", events[2].Name);
        Assert.Equal("publish.confirmed", events[3].Name);

        var sizeTag = events[1].Tags.FirstOrDefault(t => t.Key == "size_bytes");
        Assert.Equal(3, sizeTag.Value);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void NoOpInstrumentation_DoesNotCreateActivities()
    {
        // Arrange
        var noOp = NoOpTransportInstrumentation.Instance;
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 });

        // Act
        var sendActivity = noOp.StartSendActivity(envelope, "queue", "rabbitmq");
        var receiveActivity = noOp.StartReceiveActivity(envelope, "queue", "rabbitmq", "consumer-1");

        // Assert
        Assert.Null(sendActivity);
        Assert.Null(receiveActivity);

        // Operations should not throw
        noOp.RecordSendDuration("rabbitmq", "queue", "TestMessage", 1.0);
        noOp.RecordOperation("rabbitmq", "send", "success");
        noOp.RecordError(null, new Exception("test"));
        noOp.AddEvent(null, "test");

        var envelopeResult = noOp.InjectTraceContext(envelope, null);
        Assert.Equal(envelope, envelopeResult);

        var context = noOp.ExtractTraceContext(envelope);
        Assert.Equal(default, context);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void MultipleHops_MaintainsTraceContext()
    {
        // Arrange - Service A sends message
        var originalEnvelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 },
            messageId: "msg-1");

        // Act - Service A: Send
        using var serviceASend = _instrumentation.StartSendActivity(originalEnvelope, "queue-1", "rabbitmq");
        var envelopeHop1 = _instrumentation.InjectTraceContext(originalEnvelope, serviceASend);
        serviceASend?.Dispose();

        // Service B: Receive and process
        var contextHop1 = _instrumentation.ExtractTraceContext(envelopeHop1);
        using var serviceBReceive = _instrumentation.StartReceiveActivity(
            envelopeHop1, "queue-1", "rabbitmq", "consumer-b", contextHop1);

        // Service B: Send to Service C
        var nextEnvelope = new TransportEnvelope(
            messageType: "ProcessedMessage",
            body: new byte[] { 4, 5, 6 },
            messageId: "msg-2");
        using var serviceBSend = _instrumentation.StartSendActivity(nextEnvelope, "queue-2", "rabbitmq");
        var envelopeHop2 = _instrumentation.InjectTraceContext(nextEnvelope, serviceBSend);
        serviceBSend?.Dispose();
        serviceBReceive?.Dispose();

        // Service C: Receive
        var contextHop2 = _instrumentation.ExtractTraceContext(envelopeHop2);
        using var serviceCReceive = _instrumentation.StartReceiveActivity(
            envelopeHop2, "queue-2", "rabbitmq", "consumer-c", contextHop2);

        // Assert - All activities share the same trace ID
        Assert.NotNull(serviceASend);
        Assert.NotNull(serviceBReceive);
        Assert.NotNull(serviceBSend);
        Assert.NotNull(serviceCReceive);

        Assert.Equal(serviceASend.TraceId, serviceBReceive.TraceId);
        Assert.Equal(serviceASend.TraceId, serviceBSend.TraceId);
        Assert.Equal(serviceASend.TraceId, serviceCReceive.TraceId);

        // Verify parent-child relationships
        Assert.Equal(serviceASend.SpanId, serviceBReceive.ParentSpanId);
        Assert.Equal(serviceBSend.SpanId, serviceCReceive.ParentSpanId);
    }
}

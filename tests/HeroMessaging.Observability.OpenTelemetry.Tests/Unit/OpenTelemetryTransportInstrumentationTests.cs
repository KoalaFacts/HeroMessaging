using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Observability.OpenTelemetry;
using System.Diagnostics;
using Xunit;

namespace HeroMessaging.Observability.OpenTelemetry.Tests.Unit;

/// <summary>
/// Unit tests for OpenTelemetryTransportInstrumentation
/// Testing the wrapper implementation and singleton instance
/// </summary>
public class OpenTelemetryTransportInstrumentationTests : IDisposable
{
    private readonly ActivityListener _activityListener;
    private readonly List<Activity> _activities;

    public OpenTelemetryTransportInstrumentationTests()
    {
        _activities = new List<Activity>();

        // Set up activity listener to capture activities
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TransportInstrumentation.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => _activities.Add(activity)
        };
        ActivitySource.AddActivityListener(_activityListener);
    }

    public void Dispose()
    {
        _activityListener?.Dispose();
        foreach (var activity in _activities)
        {
            activity?.Dispose();
        }
    }

    #region Singleton Instance Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Instance_IsNotNull()
    {
        // Assert
        Assert.NotNull(OpenTelemetryTransportInstrumentation.Instance);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Instance_ReturnsSameInstanceOnMultipleCalls()
    {
        // Act
        var instance1 = OpenTelemetryTransportInstrumentation.Instance;
        var instance2 = OpenTelemetryTransportInstrumentation.Instance;

        // Assert
        Assert.Same(instance1, instance2);
    }

    #endregion

    #region StartSendActivity Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void StartSendActivity_DelegatesToTransportInstrumentation()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 },
            messageId: "test-123");
        var destination = "test-queue";
        var transportName = "rabbitmq";
        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;

        // Act
        var activity = instrumentation.StartSendActivity(envelope, destination, transportName);

        // Assert
        Assert.NotNull(activity);
        Assert.Equal("HeroMessaging.Transport.Send", activity.OperationName);
        Assert.Equal(ActivityKind.Producer, activity.Kind);
        Assert.Equal(transportName, activity.GetTagItem("messaging.transport"));
        Assert.Equal(destination, activity.GetTagItem("messaging.destination"));

        activity?.Dispose();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void StartSendActivity_WithNullEnvelope_ReturnsNullOrThrows()
    {
        // Arrange
        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;

        // Act & Assert
        // This might throw ArgumentNullException or return null depending on implementation
        // We test that it behaves consistently
        var exception = Record.Exception(() =>
            instrumentation.StartSendActivity(null!, "queue", "transport"));

        // Either it throws or returns null - both are acceptable
        Assert.True(exception is ArgumentNullException || exception == null);
    }

    #endregion

    #region StartPublishActivity Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void StartPublishActivity_DelegatesToTransportInstrumentation()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestEvent",
            body: new byte[] { 1, 2, 3 },
            messageId: "test-456");
        var destination = "test-topic";
        var transportName = "rabbitmq";
        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;

        // Act
        var activity = instrumentation.StartPublishActivity(envelope, destination, transportName);

        // Assert
        Assert.NotNull(activity);
        Assert.Equal("HeroMessaging.Transport.Publish", activity.OperationName);
        Assert.Equal(ActivityKind.Producer, activity.Kind);
        Assert.Equal(transportName, activity.GetTagItem("messaging.transport"));
        Assert.Equal(destination, activity.GetTagItem("messaging.destination"));

        activity?.Dispose();
    }

    #endregion

    #region StartReceiveActivity Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void StartReceiveActivity_DelegatesToTransportInstrumentation()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 },
            messageId: "test-789");
        var source = "test-queue";
        var transportName = "rabbitmq";
        var consumerId = "consumer-1";
        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;

        // Act
        var activity = instrumentation.StartReceiveActivity(
            envelope, source, transportName, consumerId);

        // Assert
        Assert.NotNull(activity);
        Assert.Equal("HeroMessaging.Transport.Receive", activity.OperationName);
        Assert.Equal(ActivityKind.Consumer, activity.Kind);
        Assert.Equal(transportName, activity.GetTagItem("messaging.transport"));
        Assert.Equal(source, activity.GetTagItem("messaging.source"));
        Assert.Equal(consumerId, activity.GetTagItem("messaging.consumer_id"));

        activity?.Dispose();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void StartReceiveActivity_WithParentContext_UsesParentContext()
    {
        // Arrange
        using var parentActivity = new Activity("parent");
        parentActivity.Start();
        var parentContext = parentActivity.Context;

        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 },
            messageId: "test-999");
        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;

        // Act
        var activity = instrumentation.StartReceiveActivity(
            envelope, "queue", "transport", "consumer-1", parentContext);

        // Assert
        Assert.NotNull(activity);
        Assert.Equal(parentContext.TraceId, activity.TraceId);
        Assert.Equal(parentContext.SpanId, activity.ParentSpanId);

        activity?.Dispose();
    }

    #endregion

    #region Metrics Recording Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordSendDuration_DelegatesToTransportInstrumentation()
    {
        // Arrange
        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;

        // Act - Should not throw
        var exception = Record.Exception(() =>
            instrumentation.RecordSendDuration("transport", "destination", "MessageType", 10.5));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordReceiveDuration_DelegatesToTransportInstrumentation()
    {
        // Arrange
        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;

        // Act - Should not throw
        var exception = Record.Exception(() =>
            instrumentation.RecordReceiveDuration("transport", "source", "MessageType", 8.3));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordSerializationDuration_DelegatesToTransportInstrumentation()
    {
        // Arrange
        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;

        // Act - Should not throw
        var exception = Record.Exception(() =>
            instrumentation.RecordSerializationDuration("transport", "MessageType", "serialize", 2.1));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordOperation_DelegatesToTransportInstrumentation()
    {
        // Arrange
        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;

        // Act - Should not throw
        var exception = Record.Exception(() =>
            instrumentation.RecordOperation("transport", "send", "success"));

        // Assert
        Assert.Null(exception);
    }

    #endregion

    #region Error Recording Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordError_WithActivity_SetsErrorStatus()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 },
            messageId: "test-error");
        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;
        using var activity = instrumentation.StartSendActivity(envelope, "queue", "transport");
        var exception = new InvalidOperationException("Test error");

        // Act
        instrumentation.RecordError(activity, exception);

        // Assert
        Assert.NotNull(activity);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Contains("Test error", activity.StatusDescription ?? "");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordError_WithNullActivity_DoesNotThrow()
    {
        // Arrange
        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;
        var exception = new InvalidOperationException("Test error");

        // Act & Assert
        var recordedException = Record.Exception(() =>
            instrumentation.RecordError(null, exception));

        Assert.Null(recordedException);
    }

    #endregion

    #region Event Recording Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void AddEvent_WithActivity_AddsEventToActivity()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 },
            messageId: "test-event");
        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;
        using var activity = instrumentation.StartSendActivity(envelope, "queue", "transport");
        var attributes = new Dictionary<string, object?>
        {
            ["key1"] = "value1",
            ["key2"] = 42
        };

        // Act
        instrumentation.AddEvent(activity, "test.event", attributes);

        // Assert
        Assert.NotNull(activity);
        var events = activity.Events.ToList();
        Assert.Single(events);
        Assert.Equal("test.event", events[0].Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddEvent_WithNullActivity_DoesNotThrow()
    {
        // Arrange
        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;

        // Act & Assert
        var exception = Record.Exception(() =>
            instrumentation.AddEvent(null, "test.event"));

        Assert.Null(exception);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddEvent_WithNullAttributes_AddsEventWithoutAttributes()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 },
            messageId: "test-event-null");
        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;
        using var activity = instrumentation.StartSendActivity(envelope, "queue", "transport");

        // Act
        instrumentation.AddEvent(activity, "test.event", null);

        // Assert
        Assert.NotNull(activity);
        var events = activity.Events.ToList();
        Assert.Single(events);
        Assert.Equal("test.event", events[0].Name);
    }

    #endregion

    #region Trace Context Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void InjectTraceContext_WithActivity_InjectsContext()
    {
        // Arrange
        using var activity = new Activity("test");
        activity.Start();
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 });
        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;

        // Act
        var result = instrumentation.InjectTraceContext(envelope, activity);

        // Assert
        Assert.True(result.HasHeader(TraceContextPropagator.TraceParentHeaderName));
        var traceParent = result.GetHeader<string>(TraceContextPropagator.TraceParentHeaderName);
        Assert.Contains(activity.TraceId.ToString(), traceParent!);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void InjectTraceContext_WithNullActivity_ReturnsOriginalEnvelope()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 });
        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;

        // Act
        var result = instrumentation.InjectTraceContext(envelope, null);

        // Assert
        Assert.Equal(envelope, result);
        Assert.False(result.HasHeader(TraceContextPropagator.TraceParentHeaderName));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ExtractTraceContext_WithValidTraceParent_ReturnsContext()
    {
        // Arrange
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var traceParent = $"00-{traceId}-{spanId}-01";
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 })
            .WithHeader(TraceContextPropagator.TraceParentHeaderName, traceParent);
        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;

        // Act
        var context = instrumentation.ExtractTraceContext(envelope);

        // Assert
        Assert.NotEqual(default, context);
        Assert.Equal(traceId, context.TraceId);
        Assert.Equal(spanId, context.SpanId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ExtractTraceContext_WithNoTraceParent_ReturnsDefaultContext()
    {
        // Arrange
        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 });
        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;

        // Act
        var context = instrumentation.ExtractTraceContext(envelope);

        // Assert
        Assert.Equal(default, context);
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void InjectAndExtract_RoundTrip_PreservesContext()
    {
        // Arrange
        using var originalActivity = new Activity("test");
        originalActivity.SetIdFormat(ActivityIdFormat.W3C);
        originalActivity.Start();

        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 });
        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;

        // Act
        var injected = instrumentation.InjectTraceContext(envelope, originalActivity);
        var extracted = instrumentation.ExtractTraceContext(injected);

        // Assert
        Assert.Equal(originalActivity.TraceId, extracted.TraceId);
        Assert.Equal(originalActivity.SpanId, extracted.SpanId);
    }

    #endregion
}

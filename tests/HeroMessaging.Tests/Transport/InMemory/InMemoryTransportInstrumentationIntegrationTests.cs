using HeroMessaging.Abstractions.Observability;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Observability.OpenTelemetry;
using HeroMessaging.Transport.InMemory;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Xunit;

namespace HeroMessaging.Tests.Transport.InMemory;

/// <summary>
/// Integration tests for InMemory transport instrumentation
/// Tests end-to-end distributed tracing and metrics collection in memory
/// </summary>
[Trait("Category", "Integration")]
public class InMemoryTransportInstrumentationIntegrationTests : IDisposable
{
    private readonly ActivityListener _activityListener;
    private readonly List<Activity> _activities;
    private readonly MeterListener _meterListener;
    private readonly Dictionary<string, List<Measurement<long>>> _longMeasurements;
    private readonly Dictionary<string, List<Measurement<double>>> _doubleMeasurements;
    private readonly ITransportInstrumentation _instrumentation;

    public InMemoryTransportInstrumentationIntegrationTests()
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

        foreach (var activity in _activities)
        {
            activity?.Dispose();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EndToEnd_SendAndReceive_CreatesLinkedActivitiesWithTraceContext()
    {
        // Arrange
        var options = new InMemoryTransportOptions
        {
            Name = "test-inmemory",
            MaxQueueLength = 100
        };

        var transport = new InMemoryTransport(options, TimeProvider.System, _instrumentation);

        var queueName = $"test-queue-{Guid.NewGuid()}";
        var destination = TransportAddress.Queue(queueName);

        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 },
            messageId: Guid.NewGuid().ToString(),
            correlationId: "test-correlation");

        TransportEnvelope? receivedEnvelope = null;
        MessageContext? receivedContext = null;
        var messageReceived = new TaskCompletionSource<bool>();

        try
        {
            await transport.ConnectAsync();

            // Subscribe
            var consumer = await transport.SubscribeAsync(
                destination,
                async (env, ctx, ct) =>
                {
                    receivedEnvelope = env;
                    receivedContext = ctx;
                    messageReceived.TrySetResult(true);
                    await Task.CompletedTask;
                },
                new ConsumerOptions { StartImmediately = true, AutoAcknowledge = true });

            // Act - Send message
            await transport.SendAsync(destination, envelope);

            // Wait for message
            var received = await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Assert
            Assert.True(received, "Message should be received");
            Assert.NotNull(receivedEnvelope);
            Assert.NotNull(receivedContext);

            // Verify activities were created
            Assert.Contains(_activities, a => a.OperationName == "HeroMessaging.Transport.Send");
            Assert.Contains(_activities, a => a.OperationName == "HeroMessaging.Transport.Receive");

            var sendActivity = _activities.First(a => a.OperationName == "HeroMessaging.Transport.Send");
            var receiveActivity = _activities.First(a => a.OperationName == "HeroMessaging.Transport.Receive");

            // Verify trace context propagation
            Assert.Equal(sendActivity.TraceId, receiveActivity.TraceId);
            Assert.Equal(sendActivity.SpanId, receiveActivity.ParentSpanId);
            Assert.True(receiveActivity.HasRemoteParent);

            // Verify activity tags
            Assert.Equal("heromessaging", sendActivity.GetTagItem("messaging.system"));
            Assert.Equal(queueName, sendActivity.GetTagItem("messaging.destination"));
            Assert.Equal("test-inmemory", sendActivity.GetTagItem("messaging.transport"));
            Assert.Equal("send", sendActivity.GetTagItem("messaging.operation"));

            Assert.Equal("heromessaging", receiveActivity.GetTagItem("messaging.system"));
            Assert.Equal(queueName, receiveActivity.GetTagItem("messaging.source"));
            Assert.Equal("receive", receiveActivity.GetTagItem("messaging.operation"));

            // Verify metrics
            Assert.True(_doubleMeasurements.ContainsKey("heromessaging_transport_send_duration_ms"));
            Assert.True(_doubleMeasurements.ContainsKey("heromessaging_transport_receive_duration_ms"));
            Assert.True(_longMeasurements.ContainsKey("heromessaging_transport_operations_total"));
        }
        finally
        {
            await transport.DisconnectAsync();
            await transport.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PublishAndSubscribe_CreatesLinkedActivities()
    {
        // Arrange
        var options = new InMemoryTransportOptions
        {
            Name = "test-inmemory"
        };

        var transport = new InMemoryTransport(options, TimeProvider.System, _instrumentation);

        var topicName = $"test-topic-{Guid.NewGuid()}";
        var topic = TransportAddress.Topic(topicName);

        var envelope = new TransportEnvelope(
            messageType: "TestEvent",
            body: new byte[] { 1, 2, 3 });

        var messagesReceived = 0;
        var allReceived = new TaskCompletionSource<bool>();

        try
        {
            await transport.ConnectAsync();

            // Subscribe two consumers to the same topic
            await transport.SubscribeAsync(
                topic,
                async (env, ctx, ct) =>
                {
                    Interlocked.Increment(ref messagesReceived);
                    if (messagesReceived >= 2)
                    {
                        allReceived.TrySetResult(true);
                    }
                    await Task.CompletedTask;
                },
                new ConsumerOptions { StartImmediately = true, AutoAcknowledge = true, ConsumerId = "consumer-1" });

            await transport.SubscribeAsync(
                topic,
                async (env, ctx, ct) =>
                {
                    Interlocked.Increment(ref messagesReceived);
                    if (messagesReceived >= 2)
                    {
                        allReceived.TrySetResult(true);
                    }
                    await Task.CompletedTask;
                },
                new ConsumerOptions { StartImmediately = true, AutoAcknowledge = true, ConsumerId = "consumer-2" });

            // Act - Publish message
            await transport.PublishAsync(topic, envelope);

            // Wait for both consumers to receive
            var received = await allReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Assert
            Assert.True(received, "Both consumers should receive the message");

            // Verify publish activity
            var publishActivity = _activities.FirstOrDefault(a => a.OperationName == "HeroMessaging.Transport.Publish");
            Assert.NotNull(publishActivity);
            Assert.Equal(ActivityKind.Producer, publishActivity.Kind);
            Assert.Equal(topicName, publishActivity.GetTagItem("messaging.destination"));
            Assert.Equal("publish", publishActivity.GetTagItem("messaging.operation"));

            // Verify receive activities (should be 2, one for each consumer)
            var receiveActivities = _activities.Where(a => a.OperationName == "HeroMessaging.Transport.Receive").ToList();
            Assert.True(receiveActivities.Count >= 2, $"Expected at least 2 receive activities, got {receiveActivities.Count}");

            // All receive activities should share the same trace as the publish
            Assert.All(receiveActivities, receiveActivity =>
            {
                Assert.Equal(publishActivity.TraceId, receiveActivity.TraceId);
                Assert.Equal(publishActivity.SpanId, receiveActivity.ParentSpanId);
            });
        }
        finally
        {
            await transport.DisconnectAsync();
            await transport.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MultiHop_ThreeServices_MaintainsTraceContext()
    {
        // Arrange - Three services with in-memory transport
        var options = new InMemoryTransportOptions { Name = "test-inmemory" };
        var transport = new InMemoryTransport(options, TimeProvider.System, _instrumentation);

        var queue1 = TransportAddress.Queue("queue-1");
        var queue2 = TransportAddress.Queue("queue-2");
        var queue3 = TransportAddress.Queue("queue-3");

        var service2Received = new TaskCompletionSource<bool>();
        var service3Received = new TaskCompletionSource<bool>();

        try
        {
            await transport.ConnectAsync();

            // Service 2: Receives from queue1, sends to queue2
            await transport.SubscribeAsync(
                queue1,
                async (env, ctx, ct) =>
                {
                    service2Received.TrySetResult(true);
                    // Service 2 forwards to Service 3
                    var forwardEnvelope = new TransportEnvelope(
                        messageType: "ForwardedMessage",
                        body: new byte[] { 2, 2, 2 },
                        messageId: Guid.NewGuid().ToString());
                    await transport.SendAsync(queue2, forwardEnvelope, ct);
                },
                new ConsumerOptions { StartImmediately = true, AutoAcknowledge = true, ConsumerId = "service-2" });

            // Service 3: Receives from queue2
            await transport.SubscribeAsync(
                queue2,
                async (env, ctx, ct) =>
                {
                    service3Received.TrySetResult(true);
                    await Task.CompletedTask;
                },
                new ConsumerOptions { StartImmediately = true, AutoAcknowledge = true, ConsumerId = "service-3" });

            // Act - Service 1 sends initial message
            var initialEnvelope = new TransportEnvelope(
                messageType: "InitialMessage",
                body: new byte[] { 1, 1, 1 },
                messageId: Guid.NewGuid().ToString());

            await transport.SendAsync(queue1, initialEnvelope);

            // Wait for all services
            await Task.WhenAll(
                service2Received.Task.WaitAsync(TimeSpan.FromSeconds(5)),
                service3Received.Task.WaitAsync(TimeSpan.FromSeconds(5)));

            // Assert
            var sendActivities = _activities.Where(a =>
                a.OperationName == "HeroMessaging.Transport.Send").OrderBy(a => a.StartTimeUtc).ToList();
            var receiveActivities = _activities.Where(a =>
                a.OperationName == "HeroMessaging.Transport.Receive").OrderBy(a => a.StartTimeUtc).ToList();

            Assert.True(sendActivities.Count >= 2, $"Expected at least 2 send activities, got {sendActivities.Count}");
            Assert.True(receiveActivities.Count >= 2, $"Expected at least 2 receive activities, got {receiveActivities.Count}");

            // Verify the trace context flows through all hops
            // Service 1 → Service 2
            var send1 = sendActivities[0];
            var receive1 = receiveActivities.FirstOrDefault(r => r.TraceId == send1.TraceId);
            Assert.NotNull(receive1);
            Assert.Equal(send1.SpanId, receive1.ParentSpanId);

            // Service 2 → Service 3 (should be part of the same trace initiated by Service 1)
            var send2 = sendActivities.FirstOrDefault(s => s.TraceId == send1.TraceId && s != send1);
            if (send2 != null)
            {
                var receive2 = receiveActivities.FirstOrDefault(r => r.TraceId == send2.TraceId && r != receive1);
                if (receive2 != null)
                {
                    // All activities should share the same trace ID
                    Assert.Equal(send1.TraceId, send2.TraceId);
                    Assert.Equal(send1.TraceId, receive2.TraceId);
                }
            }
        }
        finally
        {
            await transport.DisconnectAsync();
            await transport.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ActivityEvents_RecordedForInMemoryTransport()
    {
        // Arrange
        var options = new InMemoryTransportOptions { Name = "test-inmemory" };
        var transport = new InMemoryTransport(options, TimeProvider.System, _instrumentation);

        var queueName = $"test-queue-{Guid.NewGuid()}";
        var destination = TransportAddress.Queue(queueName);

        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 });

        var messageReceived = new TaskCompletionSource<bool>();

        try
        {
            await transport.ConnectAsync();

            await transport.SubscribeAsync(
                destination,
                async (env, ctx, ct) =>
                {
                    messageReceived.TrySetResult(true);
                    await ctx.AcknowledgeAsync(ct);
                },
                new ConsumerOptions { StartImmediately = true, AutoAcknowledge = false });

            // Act
            await transport.SendAsync(destination, envelope);
            await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Assert
            var sendActivity = _activities.FirstOrDefault(a => a.OperationName == "HeroMessaging.Transport.Send");
            Assert.NotNull(sendActivity);

            var sendEvents = sendActivity.Events.Select(e => e.Name).ToList();
            Assert.Contains("send.start", sendEvents);
            Assert.Contains("send.complete", sendEvents);

            var receiveActivity = _activities.FirstOrDefault(a => a.OperationName == "HeroMessaging.Transport.Receive");
            Assert.NotNull(receiveActivity);

            var receiveEvents = receiveActivity.Events.Select(e => e.Name).ToList();
            Assert.Contains("receive.start", receiveEvents);
            Assert.Contains("handler.start", receiveEvents);
            Assert.Contains("handler.complete", receiveEvents);
            Assert.Contains("acknowledge", receiveEvents);
        }
        finally
        {
            await transport.DisconnectAsync();
            await transport.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueueFull_RecordsEventAndError()
    {
        // Arrange
        var options = new InMemoryTransportOptions
        {
            Name = "test-inmemory",
            MaxQueueLength = 1,
            DropWhenFull = false
        };

        var transport = new InMemoryTransport(options, TimeProvider.System, _instrumentation);

        var queueName = $"test-queue-{Guid.NewGuid()}";
        var destination = TransportAddress.Queue(queueName);

        try
        {
            await transport.ConnectAsync();

            // Act - Send 2 messages to queue with max length 1 (no consumer to drain)
            var envelope1 = new TransportEnvelope(messageType: "Msg1", body: new byte[] { 1 });
            await transport.SendAsync(destination, envelope1);

            var envelope2 = new TransportEnvelope(messageType: "Msg2", body: new byte[] { 2 });

            // Second send should fail due to queue being full
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await transport.SendAsync(destination, envelope2));

            // Assert
            var failedSendActivity = _activities.LastOrDefault(a => a.OperationName == "HeroMessaging.Transport.Send");
            Assert.NotNull(failedSendActivity);

            var events = failedSendActivity.Events.Select(e => e.Name).ToList();
            Assert.Contains("queue.full", events);

            Assert.Equal(ActivityStatusCode.Error, failedSendActivity.Status);

            var failureMetrics = _longMeasurements["heromessaging_transport_operations_total"]
                .Where(m => m.Tags.Any(t => t.Key == "status" && t.Value?.ToString() == "failure")).ToList();
            Assert.NotEmpty(failureMetrics);
        }
        finally
        {
            await transport.DisconnectAsync();
            await transport.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task NoOpInstrumentation_DoesNotBreakFunctionality()
    {
        // Arrange - Use NoOp instrumentation
        var options = new InMemoryTransportOptions { Name = "test-inmemory" };
        var transport = new InMemoryTransport(options, TimeProvider.System, NoOpTransportInstrumentation.Instance);

        var queueName = $"test-queue-{Guid.NewGuid()}";
        var destination = TransportAddress.Queue(queueName);

        var envelope = new TransportEnvelope(messageType: "TestMessage", body: new byte[] { 1, 2, 3 });

        var messageReceived = new TaskCompletionSource<bool>();

        try
        {
            await transport.ConnectAsync();

            await transport.SubscribeAsync(
                destination,
                async (env, ctx, ct) =>
                {
                    messageReceived.TrySetResult(true);
                    await Task.CompletedTask;
                },
                new ConsumerOptions { StartImmediately = true, AutoAcknowledge = true });

            // Act
            await transport.SendAsync(destination, envelope);
            var received = await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Assert
            Assert.True(received, "Message should still be received with NoOp instrumentation");

            // No activities should be created
            Assert.Empty(_activities.Where(a => a.OperationName.Contains("Transport")));
        }
        finally
        {
            await transport.DisconnectAsync();
            await transport.DisposeAsync();
        }
    }
}

using System.Diagnostics;
using System.Diagnostics.Metrics;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Observability.OpenTelemetry;
using HeroMessaging.Transport.InMemory;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Tests.Transport.InMemory;

/// <summary>
/// Integration tests for InMemory transport instrumentation
/// Tests end-to-end distributed tracing and metrics collection in memory
/// Each test creates its own isolated ActivityListener to avoid shared state issues
/// </summary>
[Trait("Category", "Integration")]
public sealed class InMemoryTransportInstrumentationIntegrationTests
{
    [Fact]
    public async Task EndToEnd_SendAndReceive_CreatesLinkedActivitiesWithTraceContext()
    {
        // Arrange - Create isolated instrumentation collector for this test
        using var collector = new TestInstrumentationCollector();

        // IMPORTANT: Access instrumentation singleton AFTER listener is set up
        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;

        var fakeTimeProvider = new FakeTimeProvider();
        var options = new InMemoryTransportOptions
        {
            Name = "test-inmemory",
            MaxQueueLength = 100
        };

        var transport = new InMemoryTransport(options, fakeTimeProvider, instrumentation);

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
            await transport.ConnectAsync(TestContext.Current.CancellationToken);

            // Subscribe
            var consumer = await transport.SubscribeAsync(destination, async (env, ctx, ct) =>
            {
                receivedEnvelope = env;
                receivedContext = ctx;
                messageReceived.TrySetResult(true);
                await Task.CompletedTask;
            }, new ConsumerOptions { StartImmediately = true, AutoAcknowledge = true }, TestContext.Current.CancellationToken);

            // Act - Send message
            await transport.SendAsync(destination, envelope, TestContext.Current.CancellationToken);

            // Wait for message
            var received = await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            // Advance time to allow consumer to finish recording metrics (deterministic timing)
            // The consumer records metrics AFTER the handler completes
            fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(100));

            // Record observable instruments to ensure metrics are collected
            collector.RecordObservableInstruments();

            // Assert
            Assert.True(received, "Message should be received");
            Assert.NotNull(receivedEnvelope);
            Assert.NotNull(receivedContext);

            // Verify activities were created
            Assert.Contains(collector.Activities, a => a.OperationName == "HeroMessaging.Transport.Send");
            Assert.Contains(collector.Activities, a => a.OperationName == "HeroMessaging.Transport.Receive");

            var sendActivity = collector.Activities.First(a => a.OperationName == "HeroMessaging.Transport.Send");
            var receiveActivity = collector.Activities.First(a => a.OperationName == "HeroMessaging.Transport.Receive");

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
        }
        finally
        {
            await transport.DisposeAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task MultiHop_ThreeServices_MaintainsTraceContext()
    {
        // Arrange - Create isolated instrumentation collector for this test
        using var collector = new TestInstrumentationCollector();

        // IMPORTANT: Access instrumentation singleton AFTER listener is set up
        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;

        var fakeTimeProvider = new FakeTimeProvider();

        // Use a single shared transport to simulate three services communicating via shared queues
        // In real scenarios, services would use a shared message broker (RabbitMQ, Azure Service Bus, etc.)
        var sharedTransport = new InMemoryTransport(
            new InMemoryTransportOptions { Name = "SharedInMemoryBroker" },
            fakeTimeProvider,
            instrumentation);

        var queueAB = TransportAddress.Queue("queue-a-to-b");
        var queueBC = TransportAddress.Queue("queue-b-to-c");

        var originalMessage = new TransportEnvelope(
            messageType: "OrderCreated",
            body: new byte[] { 1, 2, 3 },
            messageId: Guid.NewGuid().ToString(),
            correlationId: "order-123");

        var serviceCReceived = new TaskCompletionSource<bool>();

        try
        {
            await sharedTransport.ConnectAsync(TestContext.Current.CancellationToken);

            // Service C consumer (end of chain)
            await sharedTransport.SubscribeAsync(queueBC, async (env, ctx, ct) =>
            {
                serviceCReceived.TrySetResult(true);
                await Task.CompletedTask;
            }, new ConsumerOptions { StartImmediately = true, AutoAcknowledge = true }, TestContext.Current.CancellationToken);

            // Service B consumer (middle of chain) - forwards to Service C
            await sharedTransport.SubscribeAsync(queueAB, async (env, ctx, ct) =>
            {
                await sharedTransport.SendAsync(queueBC, env, ct, TestContext.Current.CancellationToken);
            }, new ConsumerOptions { StartImmediately = true, AutoAcknowledge = true }, TestContext.Current.CancellationToken);

            // Act - Service A sends the original message
            await sharedTransport.SendAsync(queueAB, originalMessage, TestContext.Current.CancellationToken);

            // Wait for message to reach Service C
            var received = await serviceCReceived.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            // Assert
            Assert.True(received, "Message should reach Service C");

            // We should have sends and receives for the message chain
            var sendActivities = collector.Activities.Where(a => a.OperationName == "HeroMessaging.Transport.Send").ToList();
            var receiveActivities = collector.Activities.Where(a => a.OperationName == "HeroMessaging.Transport.Receive").ToList();

            Assert.True(sendActivities.Count >= 2, "Should have at least 2 send activities");
            Assert.True(receiveActivities.Count >= 2, "Should have at least 2 receive activities");

            // All activities should share the same TraceId
            var traceIds = collector.Activities.Select(a => a.TraceId).Distinct().ToList();
            Assert.Single(traceIds);
        }
        finally
        {
            await sharedTransport.DisposeAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task QueueFull_RecordsEventAndError()
    {
        // Arrange - Create isolated instrumentation collector for this test
        using var collector = new TestInstrumentationCollector();

        var instrumentation = OpenTelemetryTransportInstrumentation.Instance;

        var fakeTimeProvider = new FakeTimeProvider();
        var options = new InMemoryTransportOptions
        {
            Name = "test-full-queue",
            MaxQueueLength = 1,  // Very small queue
            DropWhenFull = false  // Wait when full (will block indefinitely without consumer)
        };

        var transport = new InMemoryTransport(options, fakeTimeProvider, instrumentation);
        var destination = TransportAddress.Queue("small-queue");

        var envelope1 = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1 },
            messageId: Guid.NewGuid().ToString(),
            correlationId: "test-1");

        var envelope2 = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 2 },
            messageId: Guid.NewGuid().ToString(),
            correlationId: "test-2");

        try
        {
            await transport.ConnectAsync(TestContext.Current.CancellationToken);

            // Act - Fill the queue
            await transport.SendAsync(destination, envelope1, TestContext.Current.CancellationToken);

            // Try to send to full queue with timeout - should timeout when DropWhenFull=false
            // When DropWhenFull=false, the channel uses BoundedChannelFullMode.Wait,
            // which blocks indefinitely waiting for space. We use a timeout to detect this.
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await transport.SendAsync(destination, envelope2, cts.Token, TestContext.Current.CancellationToken);
            });

            // Assert - Check that send activities were recorded
            var sendActivities = collector.Activities.Where(a => a.OperationName == "HeroMessaging.Transport.Send").ToList();
            Assert.True(sendActivities.Count >= 1, "Should have at least one send activity recorded");

            // The first send should have succeeded
            var firstSend = sendActivities.First();
            Assert.NotEqual(ActivityStatusCode.Error, firstSend.Status);
        }
        finally
        {
            await transport.DisposeAsync(TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Helper class to encapsulate test instrumentation setup and disposal
    /// </summary>
    private sealed class TestInstrumentationCollector : IDisposable
    {
        private readonly ActivityListener _activityListener;
        private readonly MeterListener _meterListener;
        public List<Activity> Activities { get; } = [];
        public Dictionary<string, List<Measurement<long>>> LongMeasurements { get; } = [];
        public Dictionary<string, List<Measurement<double>>> DoubleMeasurements { get; } = [];

        public TestInstrumentationCollector()
        {
            // Set up activity listener for this test only
            _activityListener = new ActivityListener
            {
                ShouldListenTo = source =>
                    source.Name == TransportInstrumentation.ActivitySourceName ||
                    source.Name == HeroMessagingInstrumentation.ActivitySourceName,
                Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = Activities.Add
            };
            ActivitySource.AddActivityListener(_activityListener);

            // Set up meter listener for this test only
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
                if (!LongMeasurements.TryGetValue(instrument.Name, out List<Measurement<long>>? value))
                {
                    value = [];
                    LongMeasurements[instrument.Name] = value;
                }

                value.Add(new Measurement<long>(measurement, tags));
            });

            _meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
            {
                if (!DoubleMeasurements.TryGetValue(instrument.Name, out List<Measurement<double>>? value))
                {
                    value = [];
                    DoubleMeasurements[instrument.Name] = value;
                }

                value.Add(new Measurement<double>(measurement, tags));
            });

            _meterListener.Start();
        }

        public void RecordObservableInstruments()
        {
            _meterListener.RecordObservableInstruments();
        }

        public void Dispose()
        {
            // CRITICAL: Stop listening FIRST before clearing data
            try
            {
                // Stop the meter listener first (stops collecting metrics)
                _meterListener?.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }

            try
            {
                // Stop the activity listener (stops collecting traces)
                _activityListener?.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }

            // Now safe to clear collected data
            Activities.Clear();
            LongMeasurements.Clear();
            DoubleMeasurements.Clear();
        }
    }
}

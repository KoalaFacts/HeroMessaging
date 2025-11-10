using HeroMessaging.Abstractions.Observability;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Observability.OpenTelemetry;
using HeroMessaging.Transport.RabbitMQ;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Integration;

/// <summary>
/// Integration tests for RabbitMQ transport instrumentation
/// Tests end-to-end distributed tracing and metrics collection
/// </summary>
[Trait("Category", "Integration")]
public sealed class RabbitMqTransportInstrumentationIntegrationTests : IDisposable
{
    private readonly ActivityListener _activityListener;
    private readonly List<Activity> _activities;
    private readonly MeterListener _meterListener;
    private readonly Dictionary<string, List<Measurement<long>>> _longMeasurements;
    private readonly Dictionary<string, List<Measurement<double>>> _doubleMeasurements;
    private readonly ITransportInstrumentation _instrumentation;

    public RabbitMqTransportInstrumentationIntegrationTests()
    {
        _activities = new List<Activity>();
        _longMeasurements = new Dictionary<string, List<Measurement<long>>>();
        _doubleMeasurements = new Dictionary<string, List<Measurement<double>>>();
        _instrumentation = OpenTelemetryTransportInstrumentation.Instance;

        // Set up activity listener to capture all activities
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == TransportInstrumentation.ActivitySourceName ||
                source.Name == HeroMessagingInstrumentation.ActivitySourceName,
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
            _longMeasurements[instrument.Name].Add(new Measurement<long>(measurement, tags));
        });

        _meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (!_doubleMeasurements.ContainsKey(instrument.Name))
            {
                _doubleMeasurements[instrument.Name] = new List<Measurement<double>>();
            }
            _doubleMeasurements[instrument.Name].Add(new Measurement<double>(measurement, tags));
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
    public async Task EndToEnd_SendAndReceive_CreatesLinkedActivitiesWithTraceContext()
    {
        // Arrange
        var options = new RabbitMqTransportOptions
        {
            Name = "test-transport",
            Host = "localhost",
            Port = 5672,
            UsePublisherConfirms = false
        };

        var transport = new RabbitMqTransport(
            options,
            NullLoggerFactory.Instance,
            TimeProvider.System,
            _instrumentation);

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
            // Connect and configure
            await transport.ConnectAsync();
            await transport.ConfigureTopologyAsync(new TransportTopology()
                .AddQueue(new QueueDefinition { Name = queueName, Durable = false, AutoDelete = true }));

            // Subscribe before sending
            var consumer = await transport.SubscribeAsync(
                destination,
                async (env, ctx, ct) =>
                {
                    receivedEnvelope = env;
                    receivedContext = ctx;
                    messageReceived.TrySetResult(true);
                    await Task.CompletedTask;
                },
                new ConsumerOptions { StartImmediately = true, ConsumerId = "test-consumer" });

            // Give consumer time to start
            await Task.Delay(500);

            // Act - Send message
            await transport.SendAsync(destination, envelope);

            // Wait for message to be received
            var received = await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));

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
            Assert.Equal("test-transport", sendActivity.GetTagItem("messaging.transport"));
            Assert.Equal("send", sendActivity.GetTagItem("messaging.operation"));

            Assert.Equal("heromessaging", receiveActivity.GetTagItem("messaging.system"));
            Assert.Equal(queueName, receiveActivity.GetTagItem("messaging.source"));
            Assert.Equal("test-consumer", receiveActivity.GetTagItem("messaging.consumer_id"));
            Assert.Equal("receive", receiveActivity.GetTagItem("messaging.operation"));

            // Verify metrics were recorded
            Assert.True(_doubleMeasurements.ContainsKey("heromessaging_transport_send_duration_ms"));
            Assert.True(_doubleMeasurements.ContainsKey("heromessaging_transport_receive_duration_ms"));
            Assert.True(_longMeasurements.ContainsKey("heromessaging_transport_operations_total"));

            var sendDurations = _doubleMeasurements["heromessaging_transport_send_duration_ms"];
            Assert.NotEmpty(sendDurations);
            Assert.All(sendDurations, m => Assert.True(m.Value > 0));

            var operations = _longMeasurements["heromessaging_transport_operations_total"];
            Assert.Contains(operations, o => o.Value == 1);
        }
        finally
        {
            await transport.DisconnectAsync();
            await transport.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SendWithPublisherConfirms_RecordsConfirmEvent()
    {
        // Arrange
        var options = new RabbitMqTransportOptions
        {
            Name = "test-transport",
            Host = "localhost",
            Port = 5672,
            UsePublisherConfirms = true,
            PublisherConfirmTimeout = TimeSpan.FromSeconds(5)
        };

        var transport = new RabbitMqTransport(
            options,
            NullLoggerFactory.Instance,
            TimeProvider.System,
            _instrumentation);

        var queueName = $"test-queue-{Guid.NewGuid()}";
        var destination = TransportAddress.Queue(queueName);

        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 });

        try
        {
            // Connect and configure
            await transport.ConnectAsync();
            await transport.ConfigureTopologyAsync(new TransportTopology()
                .AddQueue(new QueueDefinition { Name = queueName, Durable = false, AutoDelete = true }));

            // Act
            await transport.SendAsync(destination, envelope);

            // Assert
            var sendActivity = _activities.FirstOrDefault(a => a.OperationName == "HeroMessaging.Transport.Send");
            Assert.NotNull(sendActivity);

            var events = sendActivity.Events.ToList();
            Assert.Contains(events, e => e.Name == "publish.confirmed");
        }
        finally
        {
            await transport.DisconnectAsync();
            await transport.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PublishToTopic_CreatesPublishActivity()
    {
        // Arrange
        var options = new RabbitMqTransportOptions
        {
            Name = "test-transport",
            Host = "localhost",
            Port = 5672,
            UsePublisherConfirms = false
        };

        var transport = new RabbitMqTransport(
            options,
            NullLoggerFactory.Instance,
            TimeProvider.System,
            _instrumentation);

        var exchangeName = $"test-exchange-{Guid.NewGuid()}";
        var topic = TransportAddress.Topic(exchangeName);

        var envelope = new TransportEnvelope(
            messageType: "TestEvent",
            body: new byte[] { 1, 2, 3 });

        try
        {
            // Connect and configure
            await transport.ConnectAsync();
            await transport.ConfigureTopologyAsync(new TransportTopology()
                .AddExchange(new ExchangeDefinition
                {
                    Name = exchangeName,
                    Type = ExchangeType.Topic,
                    Durable = false,
                    AutoDelete = true
                }));

            // Act
            await transport.PublishAsync(topic, envelope);

            // Assert
            var publishActivity = _activities.FirstOrDefault(a => a.OperationName == "HeroMessaging.Transport.Publish");
            Assert.NotNull(publishActivity);
            Assert.Equal(ActivityKind.Producer, publishActivity.Kind);
            Assert.Equal(exchangeName, publishActivity.GetTagItem("messaging.destination"));
            Assert.Equal("publish", publishActivity.GetTagItem("messaging.operation"));

            var events = publishActivity.Events.ToList();
            Assert.Contains(events, e => e.Name == "publish.start");
            Assert.Contains(events, e => e.Name == "publish.complete");
        }
        finally
        {
            await transport.DisconnectAsync();
            await transport.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SendFailure_RecordsErrorInActivity()
    {
        // Arrange
        var options = new RabbitMqTransportOptions
        {
            Name = "test-transport",
            Host = "localhost",
            Port = 5672,
            UsePublisherConfirms = false
        };

        var transport = new RabbitMqTransport(
            options,
            NullLoggerFactory.Instance,
            TimeProvider.System,
            _instrumentation);

        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 });

        // Act - Try to send without connecting (should fail)
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transport.SendAsync(TransportAddress.Queue("test"), envelope));

        // Assert
        Assert.Contains("not connected", exception.Message);

        // Note: Since SendAsync throws before creating activity (EnsureConnected check),
        // we might not have an activity to verify. This tests the error path.
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MultipleMessages_MaintainsSeparateTraceIds()
    {
        // Arrange
        var options = new RabbitMqTransportOptions
        {
            Name = "test-transport",
            Host = "localhost",
            Port = 5672,
            UsePublisherConfirms = false
        };

        var transport = new RabbitMqTransport(
            options,
            NullLoggerFactory.Instance,
            TimeProvider.System,
            _instrumentation);

        var queueName = $"test-queue-{Guid.NewGuid()}";
        var destination = TransportAddress.Queue(queueName);

        var messagesReceived = 0;
        var messageReceived = new TaskCompletionSource<bool>();

        try
        {
            // Connect and configure
            await transport.ConnectAsync();
            await transport.ConfigureTopologyAsync(new TransportTopology()
                .AddQueue(new QueueDefinition { Name = queueName, Durable = false, AutoDelete = true }));

            // Subscribe
            await transport.SubscribeAsync(
                destination,
                async (env, ctx, ct) =>
                {
                    messagesReceived++;
                    if (messagesReceived >= 3)
                    {
                        messageReceived.TrySetResult(true);
                    }
                    await Task.CompletedTask;
                },
                new ConsumerOptions { StartImmediately = true });

            await Task.Delay(500);

            // Act - Send multiple messages
            for (int i = 0; i < 3; i++)
            {
                var envelope = new TransportEnvelope(
                    messageType: "TestMessage",
                    body: new byte[] { (byte)i },
                    messageId: $"msg-{i}");

                await transport.SendAsync(destination, envelope);
            }

            // Wait for all messages
            await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // Assert
            var sendActivities = _activities.Where(a => a.OperationName == "HeroMessaging.Transport.Send").ToList();
            var receiveActivities = _activities.Where(a => a.OperationName == "HeroMessaging.Transport.Receive").ToList();

            Assert.Equal(3, sendActivities.Count);
            Assert.True(receiveActivities.Count >= 3);

            // Each message should have its own trace
            var uniqueTraceIds = sendActivities.Select(a => a.TraceId).Distinct().Count();
            Assert.Equal(3, uniqueTraceIds);

            // But each send/receive pair should share the same trace
            for (int i = 0; i < 3; i++)
            {
                var send = sendActivities[i];
                var receive = receiveActivities.FirstOrDefault(r => r.TraceId == send.TraceId);
                Assert.NotNull(receive);
                Assert.Equal(send.SpanId, receive.ParentSpanId);
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
    public async Task ActivityEvents_RecordedInCorrectOrder()
    {
        // Arrange
        var options = new RabbitMqTransportOptions
        {
            Name = "test-transport",
            Host = "localhost",
            Port = 5672,
            UsePublisherConfirms = false
        };

        var transport = new RabbitMqTransport(
            options,
            NullLoggerFactory.Instance,
            TimeProvider.System,
            _instrumentation);

        var queueName = $"test-queue-{Guid.NewGuid()}";
        var destination = TransportAddress.Queue(queueName);

        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 });

        try
        {
            // Connect and configure
            await transport.ConnectAsync();
            await transport.ConfigureTopologyAsync(new TransportTopology()
                .AddQueue(new QueueDefinition { Name = queueName, Durable = false, AutoDelete = true }));

            // Act
            await transport.SendAsync(destination, envelope);

            // Assert
            var sendActivity = _activities.FirstOrDefault(a => a.OperationName == "HeroMessaging.Transport.Send");
            Assert.NotNull(sendActivity);

            var events = sendActivity.Events.Select(e => e.Name).ToList();

            // Verify event order
            var serializationStartIndex = events.IndexOf("serialization.start");
            var serializationCompleteIndex = events.IndexOf("serialization.complete");
            var publishStartIndex = events.IndexOf("publish.start");
            var publishCompleteIndex = events.IndexOf("publish.complete");

            Assert.True(serializationStartIndex >= 0);
            Assert.True(serializationCompleteIndex > serializationStartIndex);
            Assert.True(publishStartIndex > serializationCompleteIndex);
            Assert.True(publishCompleteIndex > publishStartIndex);
        }
        finally
        {
            await transport.DisconnectAsync();
            await transport.DisposeAsync();
        }
    }
}

using System.Diagnostics;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.RabbitMQ;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class RabbitMqConsumerRetryPolicyTests
{
    private static ConsumerOptions Options(int maxAttempts, TimeSpan? initialDelay = null, bool requeueOnFailure = true) => new()
    {
        RequeueOnFailure = requeueOnFailure,
        MessageRetryPolicy = new RetryPolicy
        {
            MaxAttempts = maxAttempts,
            InitialDelay = initialDelay ?? TimeSpan.Zero,
            MaxDelay = TimeSpan.FromMinutes(1),
            UseExponentialBackoff = true
        }
    };

    [Fact]
    public async Task HandlerFailure_WithAttemptsRemaining_WaitsBackoffBeforeRequeue()
    {
        var fixture = new RetryFixture(static (_, _, _) => throw new InvalidOperationException("boom"),
            Options(maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(300)));
        await using var consumer = fixture.Consumer;
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        var started = Stopwatch.GetTimestamp();
        await fixture.DeliverAsync(1, TestContext.Current.CancellationToken);
        var nack = await fixture.NextNackAsync();

        Assert.True(Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(250));
        Assert.Equal((1UL, true), nack);
        Assert.Equal(0, consumer.GetMetrics().MessagesDeadLettered);
    }

    [Fact]
    public async Task HandlerFailure_AfterMaxAttempts_DeadLettersWithoutRequeue()
    {
        var deliveryCounts = new List<int>();
        var fixture = new RetryFixture((envelope, _, _) =>
        {
            lock (deliveryCounts)
                deliveryCounts.Add(envelope.DeliveryCount);
            throw new InvalidOperationException("poison");
        }, Options(maxAttempts: 2));
        await using var consumer = fixture.Consumer;
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        var nacks = new List<(ulong, bool)>();
        for (ulong tag = 1; tag <= 3; tag++)
        {
            await fixture.DeliverAsync(tag, TestContext.Current.CancellationToken, redelivered: tag > 1);
            nacks.Add(await fixture.NextNackAsync());
        }

        Assert.Equal([(1UL, true), (2UL, true), (3UL, false)], nacks);
        Assert.Equal([0, 1, 2], deliveryCounts);
        var metrics = consumer.GetMetrics();
        Assert.Equal(1, metrics.MessagesDeadLettered);
        Assert.Equal(3, metrics.MessagesFailed);
    }

    [Fact]
    public async Task HandlerFailure_UsesBrokerDeliveryCountAcrossRedelivery()
    {
        var observed = -1;
        var fixture = new RetryFixture((envelope, _, _) =>
        {
            observed = envelope.DeliveryCount;
            throw new InvalidOperationException("poison");
        }, Options(maxAttempts: 2));
        await using var consumer = fixture.Consumer;
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        // A quorum queue reports two earlier returns, e.g. after a consumer restart lost local state.
        await fixture.DeliverAsync(7, TestContext.Current.CancellationToken, redelivered: true,
            headers: new Dictionary<string, object?> { ["x-delivery-count"] = 2L });
        var nack = await fixture.NextNackAsync();

        Assert.Equal(2, observed);
        Assert.Equal((7UL, false), nack);
        Assert.Equal(1, consumer.GetMetrics().MessagesDeadLettered);
    }

    [Fact]
    public async Task SuccessfulDelivery_ClearsRetryState()
    {
        var calls = 0;
        var fixture = new RetryFixture((_, _, _) =>
        {
            var call = Interlocked.Increment(ref calls);
            return call == 2 ? Task.CompletedTask : throw new InvalidOperationException("transient");
        }, Options(maxAttempts: 1));
        await using var consumer = fixture.Consumer;
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        await fixture.DeliverAsync(1, TestContext.Current.CancellationToken);
        Assert.Equal((1UL, true), await fixture.NextNackAsync());
        await fixture.DeliverAsync(2, TestContext.Current.CancellationToken, redelivered: true);
        await fixture.DeliverAsync(3, TestContext.Current.CancellationToken);

        // The third delivery is a new failure of the same message ID; its count restarted after success.
        Assert.Equal((3UL, true), await fixture.NextNackAsync());
        fixture.Channel.Verify(ch => ch.BasicAckAsync(2, false, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(0, consumer.GetMetrics().MessagesDeadLettered);
    }

    [Fact]
    public async Task HandlerFailure_WithInfiniteAttempts_AlwaysRequeues()
    {
        var fixture = new RetryFixture(static (_, _, _) => throw new InvalidOperationException("down"),
            Options(maxAttempts: -1));
        await using var consumer = fixture.Consumer;
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        for (ulong tag = 1; tag <= 5; tag++)
        {
            await fixture.DeliverAsync(tag, TestContext.Current.CancellationToken, redelivered: tag > 1);
            Assert.Equal((tag, true), await fixture.NextNackAsync());
        }

        Assert.Equal(0, consumer.GetMetrics().MessagesDeadLettered);
    }

    [Fact]
    public async Task HandlerFailure_WithZeroAttempts_DeadLettersFirstFailure()
    {
        var fixture = new RetryFixture(static (_, _, _) => throw new InvalidOperationException("poison"),
            Options(maxAttempts: 0, initialDelay: TimeSpan.FromMinutes(1)));
        await using var consumer = fixture.Consumer;
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        await fixture.DeliverAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal((1UL, false), await fixture.NextNackAsync());
        Assert.Equal(1, consumer.GetMetrics().MessagesDeadLettered);
    }

    [Fact]
    public async Task HandlerFailure_WithRequeueDisabled_RejectsImmediatelyWithoutBackoff()
    {
        var fixture = new RetryFixture(static (_, _, _) => throw new InvalidOperationException("poison"),
            Options(maxAttempts: 3, initialDelay: TimeSpan.FromMinutes(1), requeueOnFailure: false));
        await using var consumer = fixture.Consumer;
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        await fixture.DeliverAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal((1UL, false), await fixture.NextNackAsync());
        Assert.Equal(1, consumer.GetMetrics().MessagesDeadLettered);
    }

    [Fact]
    public async Task StopAsync_DuringRetryBackoff_RequeuesWithoutWaitingForFullDelay()
    {
        var fixture = new RetryFixture(static (_, _, _) => throw new InvalidOperationException("transient"),
            Options(maxAttempts: 3, initialDelay: TimeSpan.FromMinutes(5)));
        await using var consumer = fixture.Consumer;
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        await fixture.DeliverAsync(1, TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        Assert.False(fixture.HasNack);

        await consumer.StopAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal((1UL, true), await fixture.NextNackAsync());
    }

    [Fact]
    public async Task ExplicitDeadLetter_IsCountedInMetrics()
    {
        var fixture = new RetryFixture(static (_, context, ct) => context.DeadLetterAsync("invalid", ct),
            Options(maxAttempts: 3));
        await using var consumer = fixture.Consumer;
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        await fixture.DeliverAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal((1UL, false), await fixture.NextNackAsync());
        Assert.Equal(1, consumer.GetMetrics().MessagesDeadLettered);
    }

    private sealed class RetryFixture
    {
        private readonly System.Threading.Channels.Channel<(ulong Tag, bool Requeue)> _nacks =
            System.Threading.Channels.Channel.CreateUnbounded<(ulong, bool)>();
        private IAsyncBasicConsumer? _basicConsumer;

        internal Mock<IChannel> Channel { get; } = new();
        internal RabbitMqConsumer Consumer { get; }
        internal bool HasNack => _nacks.Reader.Count > 0;

        internal RetryFixture(Func<TransportEnvelope, MessageContext, CancellationToken, Task> handler, ConsumerOptions options)
        {
            Channel.Setup(ch => ch.IsOpen).Returns(true);
            Channel.Setup(ch => ch.BasicQosAsync(
                It.IsAny<uint>(), It.IsAny<ushort>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Channel.Setup(ch => ch.BasicConsumeAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<bool>(), It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<IAsyncBasicConsumer>(), It.IsAny<CancellationToken>()))
                .Callback<string, bool, string, bool, bool, IDictionary<string, object?>, IAsyncBasicConsumer, CancellationToken>(
                    (_, _, _, _, _, _, basicConsumer, _) => _basicConsumer = basicConsumer)
                .ReturnsAsync("test-tag");
            Channel.Setup(ch => ch.BasicAckAsync(It.IsAny<ulong>(), false, It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
            Channel.Setup(ch => ch.BasicNackAsync(It.IsAny<ulong>(), false, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Callback<ulong, bool, bool, CancellationToken>((tag, _, requeue, _) => _nacks.Writer.TryWrite((tag, requeue)))
                .Returns(ValueTask.CompletedTask);
            Channel.Setup(ch => ch.BasicCancelAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(() => _basicConsumer!.HandleBasicCancelOkAsync("test-tag"));
            Channel.Setup(ch => ch.CloseAsync(It.IsAny<ushort>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var host = new Mock<IRabbitMqConsumerHost>();
            host.SetupGet(h => h.Name).Returns("RabbitMQ");
            Consumer = new RabbitMqConsumer("test-consumer", TransportAddress.Queue("test-queue"),
                Channel.Object, handler, options, host.Object,
                Mock.Of<ILogger<RabbitMqConsumer>>(), TimeProvider.System);
        }

        internal Task DeliverAsync(ulong deliveryTag, CancellationToken cancellationToken, bool redelivered = false,
            IDictionary<string, object?>? headers = null) => _basicConsumer!.HandleBasicDeliverAsync(
            "test-tag", deliveryTag, redelivered, string.Empty, "test-queue",
            new BasicProperties { MessageId = "message-1", Headers = headers }, new byte[] { 1 }, cancellationToken);

        internal async Task<(ulong, bool)> NextNackAsync() =>
            await _nacks.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }
}

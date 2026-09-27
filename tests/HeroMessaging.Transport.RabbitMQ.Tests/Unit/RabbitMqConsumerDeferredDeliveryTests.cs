using System.Diagnostics;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.RabbitMQ;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class RabbitMqConsumerDeferredDeliveryTests
{
    [Theory]
    [InlineData(1, 10, 1)]
    [InlineData(3, 10, 3)]
    [InlineData(10, 2, 2)]
    [InlineData(1, 0, 1)]
    public async Task StartAsync_LimitsPrefetchToConcurrentMessageLimit(
        int concurrentMessageLimit, ushort configuredPrefetch, ushort expectedPrefetch)
    {
        var fixture = new ConsumerFixture(
            static (_, _, _) => Task.CompletedTask,
            options: new ConsumerOptions
            {
                ConcurrentMessageLimit = concurrentMessageLimit,
                PrefetchCount = configuredPrefetch
            });
        await using var consumer = fixture.Consumer;

        await consumer.StartAsync(TestContext.Current.CancellationToken);

        fixture.Channel.Verify(ch => ch.BasicQosAsync(
            0, expectedPrefetch, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeferAsync_WithDelay_WaitsBeforeRequeueing()
    {
        var elapsed = TimeSpan.Zero;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fixture = new ConsumerFixture(async (_, context, ct) =>
        {
            var started = Stopwatch.GetTimestamp();
            await context.DeferAsync(TimeSpan.FromMilliseconds(200), ct);
            elapsed = Stopwatch.GetElapsedTime(started);
            completed.TrySetResult();
        });
        await using var consumer = fixture.Consumer;

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        await fixture.DeliverAsync(TestContext.Current.CancellationToken);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(elapsed >= TimeSpan.FromMilliseconds(150));
        fixture.Channel.Verify(ch => ch.BasicNackAsync(1, false, true, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Channel.Verify(ch => ch.BasicAckAsync(1, false, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StopAsync_DuringDefer_RequeuesWithoutWaitingForFullDelay()
    {
        var deferred = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fixture = new ConsumerFixture(async (_, context, ct) =>
        {
            deferred.TrySetResult();
            await context.DeferAsync(TimeSpan.FromMinutes(1), ct);
        });
        await using var consumer = fixture.Consumer;

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        var delivery = fixture.DeliverAsync(TestContext.Current.CancellationToken);
        await deferred.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await delivery.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        fixture.Channel.Verify(ch => ch.BasicNackAsync(1, false, true, It.IsAny<CancellationToken>()), Times.Never);

        await consumer.StopAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await delivery.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        fixture.Channel.Verify(ch => ch.BasicNackAsync(1, false, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopAsync_DoesNotRequeueUntilConsumerIsCancelled()
    {
        var deferred = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCancel = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fixture = new ConsumerFixture(async (_, context, ct) =>
        {
            deferred.TrySetResult();
            await context.DeferAsync(TimeSpan.FromMinutes(1), ct);
        }, cancelStarted, releaseCancel.Task);
        await using var consumer = fixture.Consumer;

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        var delivery = fixture.DeliverAsync(TestContext.Current.CancellationToken);
        await deferred.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await delivery.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var stopping = consumer.StopAsync(TestContext.Current.CancellationToken);
        try
        {
            await cancelStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
            fixture.Channel.Verify(ch => ch.BasicNackAsync(1, false, true, It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            releaseCancel.TrySetResult();
        }

        await stopping.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await delivery.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.Channel.Verify(ch => ch.BasicNackAsync(1, false, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeferAsync_NegativeDelay_DoesNotClaimDisposition()
    {
        var fixture = new ConsumerFixture(async (_, context, ct) =>
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => context.DeferAsync(TimeSpan.FromMilliseconds(-1), ct));
            await context.AcknowledgeAsync(ct);
        });
        await using var consumer = fixture.Consumer;

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        await fixture.DeliverAsync(TestContext.Current.CancellationToken);

        fixture.Channel.Verify(ch => ch.BasicAckAsync(1, false, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Channel.Verify(ch => ch.BasicNackAsync(1, false, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeferAsync_AfterConsumerRecovery_HonorsDelay()
    {
        var elapsed = TimeSpan.Zero;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fixture = new ConsumerFixture(async (_, context, ct) =>
        {
            var started = Stopwatch.GetTimestamp();
            await context.DeferAsync(TimeSpan.FromMilliseconds(200), ct);
            elapsed = Stopwatch.GetElapsedTime(started);
            completed.TrySetResult();
        });
        await using var consumer = fixture.Consumer;

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        await fixture.SimulateRecoveryAsync(TestContext.Current.CancellationToken);
        await fixture.DeliverAsync(TestContext.Current.CancellationToken);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(elapsed >= TimeSpan.FromMilliseconds(150));
        fixture.Channel.Verify(ch => ch.BasicNackAsync(1, false, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeferAsync_WhenCallerCancels_RequeuesPromptly()
    {
        using var deferCancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        var fixture = new ConsumerFixture(async (_, context, _) =>
        {
            entered.TrySetResult();
            try
            {
                await context.DeferAsync(TimeSpan.FromMinutes(1), deferCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            completed.TrySetResult();
        });
        await using var consumer = fixture.Consumer;

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        var delivery = fixture.DeliverAsync(TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        deferCancellation.Cancel();
        await delivery.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(cancelled);
        fixture.Channel.Verify(ch => ch.BasicNackAsync(1, false, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class ConsumerFixture
    {
        private IAsyncBasicConsumer? _basicConsumer;

        internal Mock<IChannel> Channel { get; } = new();
        internal RabbitMqConsumer Consumer { get; }

        internal ConsumerFixture(
            Func<TransportEnvelope, MessageContext, CancellationToken, Task> handler,
            TaskCompletionSource? cancelStarted = null,
            Task? cancelRelease = null,
            ConsumerOptions? options = null)
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
                .Returns(ValueTask.CompletedTask);
            Channel.Setup(ch => ch.BasicCancelAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    cancelStarted?.TrySetResult();
                    if (cancelRelease is not null)
                        await cancelRelease;
                    await _basicConsumer!.HandleBasicCancelOkAsync("test-tag");
                });
            Channel.Setup(ch => ch.CloseAsync(It.IsAny<ushort>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var host = new Mock<IRabbitMqConsumerHost>();
            host.SetupGet(h => h.Name).Returns("RabbitMQ");
            Consumer = new RabbitMqConsumer("test-consumer", TransportAddress.Queue("test-queue"),
                Channel.Object, handler, options ?? new ConsumerOptions(), host.Object,
                Mock.Of<ILogger<RabbitMqConsumer>>(), TimeProvider.System);
        }

        internal Task DeliverAsync(CancellationToken cancellationToken) => _basicConsumer!.HandleBasicDeliverAsync(
            "test-tag", 1, false, string.Empty, "test-queue",
            new BasicProperties { MessageId = "message-1" }, new byte[] { 1 }, cancellationToken);

        internal async Task SimulateRecoveryAsync(CancellationToken cancellationToken)
        {
            await _basicConsumer!.HandleBasicCancelOkAsync("test-tag", cancellationToken);
            await _basicConsumer.HandleBasicConsumeOkAsync("test-tag", cancellationToken);
        }
    }
}

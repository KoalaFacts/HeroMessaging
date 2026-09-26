using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.InMemory;
using Xunit;

namespace HeroMessaging.Tests.Unit.Transport;

[Trait("Category", "Unit")]
public sealed class InMemoryConsumerRequeueShutdownTests
{
    [Fact]
    public async Task StopAsync_RepeatedManualRequeue_TerminatesDelivery()
    {
        await using var transport = new InMemoryTransport(new InMemoryTransportOptions(), TimeProvider.System);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var source = TransportAddress.Queue("requeue-shutdown");
        var firstRejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? shutdownRequeueError = null;
        var attempts = 0;
        var consumer = await transport.SubscribeAsync(source, async (_, context, ct) =>
        {
            switch (Interlocked.Increment(ref attempts))
            {
                case 1:
                    await context.RejectAsync(requeue: true, ct);
                    firstRejected.TrySetResult();
                    await releaseFirst.Task.WaitAsync(ct);
                    break;
                case 2:
                    shutdownRequeueError = await Record.ExceptionAsync(() => context.RejectAsync(requeue: true, ct));
                    break;
                default:
                    await context.AcknowledgeAsync(ct);
                    break;
            }
        }, new ConsumerOptions(), TestContext.Current.CancellationToken);

        try
        {
            await transport.SendAsync(source,
                new TransportEnvelope("RequeueTest", ReadOnlyMemory<byte>.Empty), TestContext.Current.CancellationToken);
            await firstRejected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            var stopping = consumer.StopAsync(TestContext.Current.CancellationToken);
            releaseFirst.TrySetResult();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.IsType<InvalidOperationException>(shutdownRequeueError);
            Assert.Equal(2, Volatile.Read(ref attempts));
            Assert.Equal(1, consumer.GetMetrics().MessagesDeadLettered);
        }
        finally
        {
            releaseFirst.TrySetResult();
            await consumer.DisposeAsync();
        }
    }
}

using System.Diagnostics;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.InMemory;
using Xunit;

namespace HeroMessaging.Tests.Unit.Transport;

[Trait("Category", "Unit")]
public sealed class InMemoryConsumerRetryShutdownTests
{
    [Theory]
    [InlineData(TransportAddressType.Queue, true)]
    [InlineData(TransportAddressType.Queue, false)]
    [InlineData(TransportAddressType.Topic, true)]
    [InlineData(TransportAddressType.Topic, false)]
    public async Task DeferAsync_RedeliversAfterRequestedOrDefaultDelay(
        TransportAddressType addressType, bool explicitDelay)
    {
        await using var transport = new InMemoryTransport(new InMemoryTransportOptions(), TimeProvider.System);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var source = addressType == TransportAddressType.Queue
            ? TransportAddress.Queue("deferred-delivery")
            : TransportAddress.Topic("deferred-delivery");
        var redelivered = new TaskCompletionSource<(int DeliveryCount, TimeSpan Elapsed)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var firstDeliveryStarted = 0L;
        var consumer = await transport.SubscribeAsync(source, async (envelope, context, ct) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                firstDeliveryStarted = Stopwatch.GetTimestamp();
                await context.DeferAsync(explicitDelay ? TimeSpan.FromMilliseconds(200) : null, ct);
                return;
            }

            redelivered.TrySetResult((envelope.DeliveryCount, Stopwatch.GetElapsedTime(firstDeliveryStarted)));
        }, new ConsumerOptions
        {
            MessageRetryPolicy = RetryPolicy.Linear(1, TimeSpan.FromMilliseconds(200))
        }, TestContext.Current.CancellationToken);

        var envelope = new TransportEnvelope("DeferredTest", ReadOnlyMemory<byte>.Empty);
        if (addressType == TransportAddressType.Queue)
            await transport.SendAsync(source, envelope, TestContext.Current.CancellationToken);
        else
            await transport.PublishAsync(source, envelope, TestContext.Current.CancellationToken);

        var (deliveryCount, elapsed) = await redelivered.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await consumer.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(elapsed >= TimeSpan.FromMilliseconds(150));
        Assert.Equal(1, deliveryCount);
        Assert.Equal(2, Volatile.Read(ref attempts));
        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task StopAsync_CompletesDeferredDeliveryBeforeReturning()
    {
        await using var transport = new InMemoryTransport(new InMemoryTransportOptions(), TimeProvider.System);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var source = TransportAddress.Queue("defer-shutdown");
        var deferred = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var consumer = await transport.SubscribeAsync(source, async (_, context, ct) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                await context.DeferAsync(TimeSpan.FromMinutes(1), ct);
                deferred.TrySetResult();
            }
        }, new ConsumerOptions(), TestContext.Current.CancellationToken);

        await transport.SendAsync(source,
            new TransportEnvelope("DeferredTest", ReadOnlyMemory<byte>.Empty), TestContext.Current.CancellationToken);
        await deferred.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await consumer.StopAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(2, Volatile.Read(ref attempts));
        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task DeferAsync_HandlerThrowsAfterDeferring_StillRedelivers()
    {
        await using var transport = new InMemoryTransport(new InMemoryTransportOptions(), TimeProvider.System);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var source = TransportAddress.Queue("defer-then-throw");
        var firstAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var consumer = await transport.SubscribeAsync(source, async (_, context, ct) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                await context.DeferAsync(TimeSpan.FromMinutes(1), ct);
                firstAttempt.TrySetResult();
                throw new InvalidOperationException("Failure after deferring");
            }
        }, new ConsumerOptions(), TestContext.Current.CancellationToken);

        await transport.SendAsync(source,
            new TransportEnvelope("DeferredTest", ReadOnlyMemory<byte>.Empty), TestContext.Current.CancellationToken);
        await firstAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await consumer.StopAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(2, Volatile.Read(ref attempts));
        Assert.Equal(1, consumer.GetMetrics().MessagesFailed);
        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task DeferAsync_RejectAfterDeferral_DoesNotCreateSecondRedelivery()
    {
        await using var transport = new InMemoryTransport(new InMemoryTransportOptions(), TimeProvider.System);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var source = TransportAddress.Queue("defer-then-reject");
        var firstAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? dispositionError = null;
        var attempts = 0;
        var consumer = await transport.SubscribeAsync(source, async (_, context, ct) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                await context.DeferAsync(TimeSpan.FromMinutes(1), ct);
                dispositionError = await Record.ExceptionAsync(() => context.RejectAsync(true, ct));
                firstAttempt.TrySetResult();
            }
        }, new ConsumerOptions(), TestContext.Current.CancellationToken);

        await transport.SendAsync(source,
            new TransportEnvelope("DeferredTest", ReadOnlyMemory<byte>.Empty), TestContext.Current.CancellationToken);
        await firstAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await consumer.StopAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.IsType<InvalidOperationException>(dispositionError);
        Assert.Equal(2, Volatile.Read(ref attempts));
        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task DeferAsync_LongDelay_RemainsAcceptedUntilShutdown()
    {
        await using var transport = new InMemoryTransport(new InMemoryTransportOptions(), TimeProvider.System);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var source = TransportAddress.Queue("long-defer");
        var firstAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var consumer = await transport.SubscribeAsync(source, async (_, context, ct) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                await context.DeferAsync(TimeSpan.FromDays(50), ct);
                firstAttempt.TrySetResult();
            }
        }, new ConsumerOptions(), TestContext.Current.CancellationToken);

        await transport.SendAsync(source,
            new TransportEnvelope("DeferredTest", ReadOnlyMemory<byte>.Empty), TestContext.Current.CancellationToken);
        await firstAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        await consumer.StopAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(2, Volatile.Read(ref attempts));
        Assert.Equal(0, consumer.GetMetrics().MessagesDeadLettered);
        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task StopAsync_RepeatedDeferral_DoesNotLoopForever()
    {
        await using var transport = new InMemoryTransport(new InMemoryTransportOptions(), TimeProvider.System);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var source = TransportAddress.Queue("repeated-defer-shutdown");
        var firstAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? shutdownDeferralError = null;
        var attempts = 0;
        var consumer = await transport.SubscribeAsync(source, async (_, context, ct) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                await context.DeferAsync(TimeSpan.FromMinutes(1), ct);
                firstAttempt.TrySetResult();
            }
            else if (Volatile.Read(ref attempts) == 2)
            {
                shutdownDeferralError = await Record.ExceptionAsync(() => context.DeferAsync(TimeSpan.FromMinutes(1), ct));
            }
        }, new ConsumerOptions(), TestContext.Current.CancellationToken);

        await transport.SendAsync(source,
            new TransportEnvelope("DeferredTest", ReadOnlyMemory<byte>.Empty), TestContext.Current.CancellationToken);
        await firstAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await consumer.StopAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.IsType<InvalidOperationException>(shutdownDeferralError);
        Assert.Equal(2, Volatile.Read(ref attempts));
        Assert.Equal(1, consumer.GetMetrics().MessagesDeadLettered);
        await consumer.DisposeAsync();
    }

    [Theory]
    [InlineData(TransportAddressType.Queue)]
    [InlineData(TransportAddressType.Topic)]
    public async Task StopAsync_CompletesDelayedRetryBeforeReturning(TransportAddressType addressType)
    {
        await using var transport = new InMemoryTransport(new InMemoryTransportOptions(), TimeProvider.System);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var source = addressType == TransportAddressType.Queue
            ? TransportAddress.Queue("retry-shutdown")
            : TransportAddress.Topic("retry-shutdown");
        var firstAttempt = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var consumer = await transport.SubscribeAsync(source, (_, _, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                firstAttempt.TrySetResult(true);
                throw new InvalidOperationException("Transient failure");
            }

            return Task.CompletedTask;
        }, new ConsumerOptions
        {
            MessageRetryPolicy = RetryPolicy.Linear(1, TimeSpan.FromMinutes(1))
        }, TestContext.Current.CancellationToken);

        var envelope = new TransportEnvelope("RetryTest", ReadOnlyMemory<byte>.Empty);
        if (addressType == TransportAddressType.Queue)
            await transport.SendAsync(source, envelope, TestContext.Current.CancellationToken);
        else
            await transport.PublishAsync(source, envelope, TestContext.Current.CancellationToken);

        await firstAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await consumer.StopAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(2, Volatile.Read(ref attempts));
        Assert.Equal(1, consumer.GetMetrics().MessagesFailed);
        Assert.Equal(1, consumer.GetMetrics().MessagesProcessed);
        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task StopAsync_CanceledWait_DoesNotCancelAcceptedRetry()
    {
        await using var transport = new InMemoryTransport(new InMemoryTransportOptions(), TimeProvider.System);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var source = TransportAddress.Queue("retry-canceled-wait");
        var firstAttempt = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAttempt = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var consumer = await transport.SubscribeAsync(source, async (_, _, ct) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                firstAttempt.TrySetResult(true);
                throw new InvalidOperationException("Transient failure");
            }

            secondAttempt.TrySetResult(true);
            await release.Task.WaitAsync(ct);
        }, new ConsumerOptions
        {
            MessageRetryPolicy = RetryPolicy.Linear(1, TimeSpan.FromMinutes(1))
        }, TestContext.Current.CancellationToken);

        try
        {
            await transport.SendAsync(source,
                new TransportEnvelope("RetryTest", ReadOnlyMemory<byte>.Empty), TestContext.Current.CancellationToken);
            await firstAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            using var stopCancellation = new CancellationTokenSource();
            var stopping = consumer.StopAsync(stopCancellation.Token);
            await secondAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            stopCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await stopping);

            release.TrySetResult(true);
            await consumer.StopAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.Equal(2, Volatile.Read(ref attempts));
            Assert.Equal(1, consumer.GetMetrics().MessagesProcessed);
        }
        finally
        {
            release.TrySetResult(true);
            await consumer.DisposeAsync();
        }
    }

    [Fact]
    public async Task StopAsync_DrainsRetryWhenPrefetchBufferIsFull()
    {
        await using var transport = new InMemoryTransport(new InMemoryTransportOptions(), TimeProvider.System);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var source = TransportAddress.Queue("retry-full-prefetch");
        var firstAttempt = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queuedStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseQueued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryAttempts = 0;
        var processed = 0;
        var consumer = (InMemoryConsumer)await transport.SubscribeAsync(source, async (envelope, _, ct) =>
        {
            if (envelope.MessageType == "retry" && Interlocked.Increment(ref retryAttempts) == 1)
            {
                firstAttempt.TrySetResult(true);
                throw new InvalidOperationException("Transient failure");
            }

            if (envelope.MessageType == "queued")
            {
                queuedStarted.TrySetResult(true);
                await releaseQueued.Task.WaitAsync(ct);
            }

            Interlocked.Increment(ref processed);
        }, new ConsumerOptions
        {
            StartImmediately = false,
            PrefetchCount = 1,
            MessageRetryPolicy = RetryPolicy.Linear(1, TimeSpan.FromMinutes(1))
        }, TestContext.Current.CancellationToken);

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await consumer.DeliverMessageAsync(
                new TransportEnvelope("retry", ReadOnlyMemory<byte>.Empty), TestContext.Current.CancellationToken);
            await firstAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await consumer.DeliverMessageAsync(
                new TransportEnvelope("queued", ReadOnlyMemory<byte>.Empty), TestContext.Current.CancellationToken);
            await queuedStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await consumer.DeliverMessageAsync(
                new TransportEnvelope("last", ReadOnlyMemory<byte>.Empty), TestContext.Current.CancellationToken);

            var stopping = consumer.StopAsync(TestContext.Current.CancellationToken);
            Assert.False(stopping.IsCompleted);
            releaseQueued.TrySetResult(true);
            await stopping.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.Equal(3, Volatile.Read(ref processed));
            Assert.Equal(2, Volatile.Read(ref retryAttempts));
            Assert.Equal(3, consumer.GetMetrics().MessagesProcessed);
        }
        finally
        {
            releaseQueued.TrySetResult(true);
            await consumer.DisposeAsync();
        }
    }
}

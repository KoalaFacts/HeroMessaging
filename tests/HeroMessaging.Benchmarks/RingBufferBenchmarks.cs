using BenchmarkDotNet.Attributes;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.RingBuffer.WaitStrategies;
using HeroMessaging.Transport.InMemory;

namespace HeroMessaging.Benchmarks;

public class WaitStrategyBenchmarks
{
    private readonly SleepingWaitStrategy _sleeping = new();
    private readonly YieldingWaitStrategy _yielding = new();

    [Benchmark(Baseline = true)]
    public long Sleeping() => _sleeping.WaitFor(42);

    [Benchmark]
    public long Yielding() => _yielding.WaitFor(42);
}

public class RingBufferEnqueueBenchmarks
{
    private static readonly TransportEnvelope[] Envelopes = [.. Enumerable.Range(0, 1000)
        .Select(index => new TransportEnvelope(
            messageType: "TestMessage",
            body: BitConverter.GetBytes(index),
            messageId: $"msg-{index}"))];

    [Benchmark(OperationsPerInvoke = 1000)]
    public long EnqueueBatch()
    {
        using var queue = new RingBufferQueue(new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 2048,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Single
        });

        foreach (var envelope in Envelopes)
        {
            if (!queue.EnqueueAsync(envelope).GetAwaiter().GetResult())
            {
                throw new InvalidOperationException("Ring buffer enqueue failed.");
            }
        }

        return queue.MessageCount;
    }
}

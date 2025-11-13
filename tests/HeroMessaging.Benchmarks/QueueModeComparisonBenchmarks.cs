using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.InMemory;

namespace HeroMessaging.Benchmarks;

/// <summary>
/// Direct comparison benchmarks between Channel and RingBuffer queue modes.
/// Measures relative performance, allocation differences, and throughput.
/// </summary>
[MemoryDiagnoser]
[ThreadingDiagnoser]
[MarkdownExporter]
[HtmlExporter]
[CategoriesColumn]
public class QueueModeComparisonBenchmarks
{
    private InMemoryQueue? _channelQueue;
    private RingBufferQueue? _ringBufferQueue;
    private TransportEnvelope _testEnvelope = null!;
    private List<TransportEnvelope> _testEnvelopes = null!;

    [Params(1024, 4096)]
    public int BufferSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Create test envelope
        _testEnvelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[100],
            messageId: Guid.NewGuid().ToString());

        // Pre-create envelopes for bulk tests
        _testEnvelopes = new List<TransportEnvelope>(10_000);
        for (int i = 0; i < 10_000; i++)
        {
            _testEnvelopes.Add(new TransportEnvelope(
                messageType: "TestMessage",
                body: BitConverter.GetBytes(i),
                messageId: $"msg-{i}"));
        }

        // Setup Channel-based queue
        _channelQueue = new InMemoryQueue(BufferSize, dropWhenFull: false);

        // Setup RingBuffer-based queue with Sleeping strategy (balanced default)
        var ringBufferOptions = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = BufferSize,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Single
        };
        _ringBufferQueue = new RingBufferQueue(ringBufferOptions);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        _channelQueue?.Dispose();
        if (_ringBufferQueue != null)
        {
            await _ringBufferQueue.DisposeAsync();
        }
    }

    // ==================== Single Message Latency ====================

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("SingleMessage", "Latency")]
    public async Task<bool> Channel_SingleMessage()
    {
        return await _channelQueue!.EnqueueAsync(_testEnvelope);
    }

    [Benchmark]
    [BenchmarkCategory("SingleMessage", "Latency")]
    public async Task<bool> RingBuffer_SingleMessage()
    {
        return await _ringBufferQueue!.EnqueueAsync(_testEnvelope);
    }

    // ==================== Throughput - 100 Messages ====================

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Throughput", "100Messages")]
    public async Task Channel_100Messages()
    {
        for (int i = 0; i < 100; i++)
        {
            await _channelQueue!.EnqueueAsync(_testEnvelopes[i]);
        }
    }

    [Benchmark]
    [BenchmarkCategory("Throughput", "100Messages")]
    public async Task RingBuffer_100Messages()
    {
        for (int i = 0; i < 100; i++)
        {
            await _ringBufferQueue!.EnqueueAsync(_testEnvelopes[i]);
        }
    }

    // ==================== Throughput - 1000 Messages ====================

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Throughput", "1000Messages")]
    public async Task Channel_1000Messages()
    {
        for (int i = 0; i < 1000; i++)
        {
            await _channelQueue!.EnqueueAsync(_testEnvelopes[i]);
        }
    }

    [Benchmark]
    [BenchmarkCategory("Throughput", "1000Messages")]
    public async Task RingBuffer_1000Messages()
    {
        for (int i = 0; i < 1000; i++)
        {
            await _ringBufferQueue!.EnqueueAsync(_testEnvelopes[i]);
        }
    }

    // ==================== Throughput - 10000 Messages ====================

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Throughput", "10000Messages")]
    public async Task Channel_10000Messages()
    {
        for (int i = 0; i < 10_000; i++)
        {
            await _channelQueue!.EnqueueAsync(_testEnvelopes[i]);
        }
    }

    [Benchmark]
    [BenchmarkCategory("Throughput", "10000Messages")]
    public async Task RingBuffer_10000Messages()
    {
        for (int i = 0; i < 10_000; i++)
        {
            await _ringBufferQueue!.EnqueueAsync(_testEnvelopes[i]);
        }
    }

    // ==================== Memory Allocation Comparison ====================

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Allocation")]
    public async Task Channel_AllocationTest_1000Messages()
    {
        // Measures allocations during steady-state operation
        for (int i = 0; i < 1000; i++)
        {
            await _channelQueue!.EnqueueAsync(_testEnvelopes[i]);
        }
    }

    [Benchmark]
    [BenchmarkCategory("Allocation")]
    public async Task RingBuffer_AllocationTest_1000Messages()
    {
        // RingBuffer should show significantly lower allocations
        for (int i = 0; i < 1000; i++)
        {
            await _ringBufferQueue!.EnqueueAsync(_testEnvelopes[i]);
        }
    }
}

/// <summary>
/// Benchmarks comparing Channel vs RingBuffer with consumers actively processing messages.
/// Measures end-to-end throughput including consumer processing.
/// </summary>
[MemoryDiagnoser]
[MarkdownExporter]
[HtmlExporter]
public class QueueModeWithConsumerBenchmarks
{
    private InMemoryQueue? _channelQueue;
    private RingBufferQueue? _ringBufferQueue;
    private InMemoryConsumer? _channelConsumer;
    private InMemoryConsumer? _ringBufferConsumer;
    private List<TransportEnvelope> _testEnvelopes = null!;
    private int _channelProcessedCount;
    private int _ringBufferProcessedCount;

    [Params(1000, 5000)]
    public int MessageCount { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        // Pre-create envelopes
        _testEnvelopes = new List<TransportEnvelope>(MessageCount);
        for (int i = 0; i < MessageCount; i++)
        {
            _testEnvelopes.Add(new TransportEnvelope(
                messageType: "TestMessage",
                body: BitConverter.GetBytes(i),
                messageId: $"msg-{i}"));
        }

        // Setup Channel queue with consumer
        _channelQueue = new InMemoryQueue(1024, dropWhenFull: false);
        _channelConsumer = CreateConsumer("channel-consumer", ref _channelProcessedCount);
        await _channelConsumer.StartAsync();
        _channelQueue.AddConsumer(_channelConsumer);

        // Setup RingBuffer queue with consumer
        var ringBufferOptions = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 1024,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Single
        };
        _ringBufferQueue = new RingBufferQueue(ringBufferOptions);
        _ringBufferConsumer = CreateConsumer("ringbuffer-consumer", ref _ringBufferProcessedCount);
        await _ringBufferConsumer.StartAsync();
        _ringBufferQueue.AddConsumer(_ringBufferConsumer);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_channelConsumer != null)
        {
            await _channelConsumer.StopAsync();
        }

        if (_ringBufferConsumer != null)
        {
            await _ringBufferConsumer.StopAsync();
        }

        _channelQueue?.Dispose();

        if (_ringBufferQueue != null)
        {
            await _ringBufferQueue.DisposeAsync();
        }
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("EndToEnd")]
    public async Task Channel_WithConsumer()
    {
        _channelProcessedCount = 0;

        for (int i = 0; i < MessageCount; i++)
        {
            await _channelQueue!.EnqueueAsync(_testEnvelopes[i]);
        }

        // Wait for all messages to be processed
        var timeout = DateTime.UtcNow.AddSeconds(30);
        while (_channelProcessedCount < MessageCount && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }
    }

    [Benchmark]
    [BenchmarkCategory("EndToEnd")]
    public async Task RingBuffer_WithConsumer()
    {
        _ringBufferProcessedCount = 0;

        for (int i = 0; i < MessageCount; i++)
        {
            await _ringBufferQueue!.EnqueueAsync(_testEnvelopes[i]);
        }

        // Wait for all messages to be processed
        var timeout = DateTime.UtcNow.AddSeconds(30);
        while (_ringBufferProcessedCount < MessageCount && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }
    }

    private InMemoryConsumer CreateConsumer(string consumerId, ref int processedCount)
    {
        var transport = new InMemoryTransport("test", TimeProvider.System);
        var source = new TransportAddress("queue", TransportAddressType.Queue);
        var options = new ConsumerOptions { AutoAcknowledge = true };

        return new InMemoryConsumer(
            consumerId,
            source,
            async (envelope, context, ct) =>
            {
                Interlocked.Increment(ref processedCount);
                await Task.CompletedTask;
            },
            options,
            transport,
            TimeProvider.System);
    }
}

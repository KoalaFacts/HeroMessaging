using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.InMemory;

namespace HeroMessaging.Benchmarks;

/// <summary>
/// Benchmarks comparing Channel-based vs RingBuffer-based InMemoryQueue performance.
/// Measures latency, throughput, and memory allocations.
/// </summary>
[MemoryDiagnoser]
[ThreadingDiagnoser]
[MarkdownExporter]
[HtmlExporter]
public class QueueModeBenchmarks
{
    private InMemoryQueue? _channelQueue;
    private RingBufferQueue? _ringBufferQueue;
    private TransportEnvelope _testEnvelope;

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

        // Setup Channel-based queue
        _channelQueue = new InMemoryQueue(BufferSize, dropWhenFull: false);

        // Setup RingBuffer-based queue
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
    public void Cleanup()
    {
        _channelQueue?.Dispose();
        _ringBufferQueue?.Dispose();
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Latency")]
    public async Task<bool> Channel_SingleMessage_Latency()
    {
        return await _channelQueue!.EnqueueAsync(_testEnvelope);
    }

    [Benchmark]
    [BenchmarkCategory("Latency")]
    public async Task<bool> RingBuffer_SingleMessage_Latency()
    {
        return await _ringBufferQueue!.EnqueueAsync(_testEnvelope);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Throughput")]
    public async Task Channel_1000Messages_Throughput()
    {
        for (int i = 0; i < 1000; i++)
        {
            await _channelQueue!.EnqueueAsync(_testEnvelope);
        }
    }

    [Benchmark]
    [BenchmarkCategory("Throughput")]
    public async Task RingBuffer_1000Messages_Throughput()
    {
        for (int i = 0; i < 1000; i++)
        {
            await _ringBufferQueue!.EnqueueAsync(_testEnvelope);
        }
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Throughput")]
    public async Task Channel_10000Messages_Throughput()
    {
        for (int i = 0; i < 10_000; i++)
        {
            await _channelQueue!.EnqueueAsync(_testEnvelope);
        }
    }

    [Benchmark]
    [BenchmarkCategory("Throughput")]
    public async Task RingBuffer_10000Messages_Throughput()
    {
        for (int i = 0; i < 10_000; i++)
        {
            await _ringBufferQueue!.EnqueueAsync(_testEnvelope);
        }
    }
}

/// <summary>
/// Benchmarks for different wait strategies in RingBuffer mode.
/// Measures the impact of wait strategy on latency.
/// </summary>
[MemoryDiagnoser]
[MarkdownExporter]
public class WaitStrategyBenchmarks
{
    private RingBufferQueue? _queue;
    private TransportEnvelope _testEnvelope;

    [Params(WaitStrategy.Sleeping, WaitStrategy.Yielding, WaitStrategy.Blocking)]
    public WaitStrategy Strategy { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _testEnvelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[100],
            messageId: Guid.NewGuid().ToString());

        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 1024,
            WaitStrategy = Strategy,
            ProducerMode = ProducerMode.Single
        };

        _queue = new RingBufferQueue(options);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _queue?.Dispose();
    }

    [Benchmark]
    public async Task<bool> EnqueueMessage()
    {
        return await _queue!.EnqueueAsync(_testEnvelope);
    }
}

/// <summary>
/// Benchmarks comparing single vs multi-producer performance in RingBuffer mode.
/// </summary>
[MemoryDiagnoser]
[MarkdownExporter]
public class ProducerModeBenchmarks
{
    private RingBufferQueue? _singleProducerQueue;
    private RingBufferQueue? _multiProducerQueue;
    private TransportEnvelope _testEnvelope;

    [GlobalSetup]
    public void Setup()
    {
        _testEnvelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[100],
            messageId: Guid.NewGuid().ToString());

        // Single producer
        var singleOptions = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 1024,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Single
        };
        _singleProducerQueue = new RingBufferQueue(singleOptions);

        // Multi producer
        var multiOptions = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 1024,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Multi
        };
        _multiProducerQueue = new RingBufferQueue(multiOptions);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _singleProducerQueue?.Dispose();
        _multiProducerQueue?.Dispose();
    }

    [Benchmark(Baseline = true)]
    public async Task<bool> SingleProducer_EnqueueMessage()
    {
        return await _singleProducerQueue!.EnqueueAsync(_testEnvelope);
    }

    [Benchmark]
    public async Task<bool> MultiProducer_EnqueueMessage()
    {
        return await _multiProducerQueue!.EnqueueAsync(_testEnvelope);
    }

    [Benchmark(Baseline = true)]
    public async Task SingleProducer_1000Messages()
    {
        for (int i = 0; i < 1000; i++)
        {
            await _singleProducerQueue!.EnqueueAsync(_testEnvelope);
        }
    }

    [Benchmark]
    public async Task MultiProducer_1000Messages()
    {
        for (int i = 0; i < 1000; i++)
        {
            await _multiProducerQueue!.EnqueueAsync(_testEnvelope);
        }
    }
}

/// <summary>
/// Benchmarks for different buffer sizes to find optimal configuration.
/// </summary>
[MemoryDiagnoser]
[MarkdownExporter]
public class BufferSizeBenchmarks
{
    private RingBufferQueue? _queue;
    private TransportEnvelope _testEnvelope;

    [Params(256, 512, 1024, 2048, 4096, 8192)]
    public int BufferSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _testEnvelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[100],
            messageId: Guid.NewGuid().ToString());

        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = BufferSize,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Single
        };

        _queue = new RingBufferQueue(options);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _queue?.Dispose();
    }

    [Benchmark]
    public async Task Enqueue1000Messages()
    {
        for (int i = 0; i < 1000; i++)
        {
            await _queue!.EnqueueAsync(_testEnvelope);
        }
    }
}

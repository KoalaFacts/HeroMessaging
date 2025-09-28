using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Engines;
using System;
using System.Threading.Tasks;
using System.Threading;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Tests.TestUtilities;

namespace HeroMessaging.Benchmarks;

/// <summary>
/// Performance benchmarks for message processing
/// Target: <1ms p99 latency, >100K msg/s throughput
/// </summary>
[SimpleJob(RuntimeMoniker.Net80)]
[MemoryDiagnoser]
[IterationCount(100)]
[WarmupCount(10)]
[BenchmarkCategory("MessageProcessing")]
public class MessageProcessingBenchmarks
{
    private IMessage _testMessage = null!;
    private ProcessingContext _context;
    private MockMessageProcessor _processor = null!;

    [GlobalSetup]
    public void Setup()
    {
        _testMessage = TestMessageBuilder.CreateValidMessage("Benchmark test message");
        _context = new ProcessingContext("benchmark-component");
        _processor = new MockMessageProcessor();
    }

    [Benchmark]
    [BenchmarkCategory("Latency")]
    public async ValueTask<ProcessingResult> ProcessMessage_Latency()
    {
        return await _processor.ProcessAsync(_testMessage, _context);
    }

    [Benchmark]
    [BenchmarkCategory("Throughput")]
    public async ValueTask ProcessMessages_Throughput()
    {
        for (int i = 0; i < 1000; i++)
        {
            await _processor.ProcessAsync(_testMessage, _context);
        }
    }

    [Benchmark]
    [BenchmarkCategory("Memory")]
    public async ValueTask<ProcessingResult> ProcessMessage_MemoryAllocation()
    {
        var message = TestMessageBuilder.CreateValidMessage($"Message content {DateTime.UtcNow.Ticks}");
        var context = new ProcessingContext("memory-test");
        return await _processor.ProcessAsync(message, context);
    }

    [Benchmark]
    [BenchmarkCategory("LargeMessage")]
    public async ValueTask<ProcessingResult> ProcessLargeMessage()
    {
        var largeMessage = TestMessageBuilder.CreateLargeMessage(50000); // 50KB
        return await _processor.ProcessAsync(largeMessage, _context);
    }

    /// <summary>
    /// Mock message processor for benchmarking
    /// </summary>
    public class MockMessageProcessor : IMessageProcessor
    {
        public ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context, CancellationToken cancellationToken = default)
        {
            // Simulate minimal processing work
            var result = ProcessingResult.Successful("Processed successfully", message);
            return ValueTask.FromResult(result);
        }
    }
}
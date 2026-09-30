using System.Diagnostics;
using System.Globalization;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Processing;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroMessaging.Benchmarks;

internal static class ConcurrentInProcessPipelineBenchmark
{
    private const string Usage = "Usage: --inprocess-concurrent <message-count> <handler-count> <producer-count> <capacity> <parallelism> <noop|cpu|async|delay>";

    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 6)
            throw new ArgumentException(Usage);

        var count = ParsePositive(args[0], "message-count");
        var handlerCount = ParsePositive(args[1], "handler-count");
        var producerCount = ParsePositive(args[2], "producer-count");
        var capacity = ParsePositive(args[3], "capacity");
        var parallelism = ParsePositive(args[4], "parallelism");
        var workload = args[5] switch
        {
            "noop" => InProcessWorkload.Noop,
            "cpu" => InProcessWorkload.Cpu,
            "async" => InProcessWorkload.Async,
            "delay" => InProcessWorkload.Delay,
            _ => throw new ArgumentException(Usage)
        };

        if (producerCount > count)
            throw new ArgumentException("producer-count must not exceed message-count.");
        if ((long)count * handlerCount > int.MaxValue)
            throw new ArgumentException("The total number of handler deliveries must fit in an array.");

        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<InProcessSink>();
        for (var handlerIndex = 0; handlerIndex < handlerCount; handlerIndex++)
        {
            var index = handlerIndex;
            services.AddSingleton<IEventHandler<InProcessEvent>>(provider =>
                new InProcessHandler(provider.GetRequiredService<InProcessSink>(), index, workload));
        }

        using var provider = services.BuildServiceProvider();
        await using var bus = new EventBus(provider, NullLogger<EventBus>.Instance, new EventBusOptions
        {
            BoundedCapacity = capacity,
            MaxDegreeOfParallelism = parallelism
        });
        var sink = provider.GetRequiredService<InProcessSink>();

        await RunBatchAsync(bus, sink, Math.Max(producerCount, Math.Min(1_000, count)), handlerCount, producerCount);
        Console.WriteLine($"Scenario: messages={count}, handlers={handlerCount}, producers={producerCount}, capacity={capacity}, parallelism={parallelism}, workload={workload}, deliveries={count * (long)handlerCount}");
        for (var run = 1; run <= 3; run++)
        {
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var gen0Before = GC.CollectionCount(0);
            var gen1Before = GC.CollectionCount(1);
            var gen2Before = GC.CollectionCount(2);
            var result = await RunBatchAsync(bus, sink, count, handlerCount, producerCount);
            var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            var completeRate = count / result.CompletionSeconds;
            Console.WriteLine($"Run {run}: publish={count / result.PublishSeconds:F0} events/s, complete={completeRate:F0} events/s ({completeRate * handlerCount:F0} deliveries/s), steady={InProcessPipelineBenchmark.SteadyRate(result.CompletionTimestamps, result.Started, result.CompletionSeconds)}, " +
                $"pending-publish={result.PendingPublishes * 100.0 / count:F1}%, accept p95/p99={InProcessPipelineBenchmark.Percentile(result.AcceptLatencies, 0.95):F2}/{InProcessPipelineBenchmark.Percentile(result.AcceptLatencies, 0.99):F2}ms, " +
                $"first-handler p95/p99={InProcessPipelineBenchmark.Percentile(result.FirstHandlerLatencies, 0.95):F2}/{InProcessPipelineBenchmark.Percentile(result.FirstHandlerLatencies, 0.99):F2}ms, " +
                $"all-handlers p50/p95/p99={InProcessPipelineBenchmark.Percentile(result.CompletionLatencies, 0.50):F2}/{InProcessPipelineBenchmark.Percentile(result.CompletionLatencies, 0.95):F2}/{InProcessPipelineBenchmark.Percentile(result.CompletionLatencies, 0.99):F2}ms, " +
                $"allocated={allocatedBytes / (double)count:F0} B/event, GC gen0/1/2={GC.CollectionCount(0) - gen0Before}/{GC.CollectionCount(1) - gen1Before}/{GC.CollectionCount(2) - gen2Before}");
        }
    }

    private static async Task<BatchResult> RunBatchAsync(EventBus bus, InProcessSink sink, int count, int handlerCount, int producerCount)
    {
        sink.Start(count, handlerCount);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new Task[producerCount];
        var readyCount = 0;
        var pendingPublishes = 0;

        for (var producerIndex = 0; producerIndex < producerCount; producerIndex++)
        {
            var index = producerIndex;
            tasks[index] = Task.Run(async () =>
            {
                if (Interlocked.Increment(ref readyCount) == producerCount)
                    ready.TrySetResult();
                await start.Task.ConfigureAwait(false);

                for (var sequence = index; sequence < count; sequence += producerCount)
                {
                    sink.RecordPublishing(sequence);
                    var publish = bus.PublishAsync(new InProcessEvent { Sequence = sequence });
                    if (!publish.IsCompleted)
                        Interlocked.Increment(ref pendingPublishes);
                    await publish.ConfigureAwait(false);
                    sink.RecordAccepted(sequence);
                }
            });
        }

        await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var started = Stopwatch.GetTimestamp();
        start.TrySetResult();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromMinutes(2));
        var publishSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        await sink.Completion.WaitAsync(TimeSpan.FromMinutes(2));
        var completionSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        var (accept, firstHandler, completion, completionTimestamps) = sink.GetLatencies();
        Array.Sort(accept);
        Array.Sort(firstHandler);
        Array.Sort(completion);
        return new BatchResult(started, publishSeconds, completionSeconds, pendingPublishes, accept, firstHandler, completion, completionTimestamps);
    }

    private static int ParsePositive(string value, string name)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result == 0)
            throw new ArgumentException($"{name} must be a positive integer.");
        return result;
    }

    private sealed record BatchResult(long Started, double PublishSeconds, double CompletionSeconds, int PendingPublishes,
        double[] AcceptLatencies, double[] FirstHandlerLatencies, double[] CompletionLatencies, long[] CompletionTimestamps);
}

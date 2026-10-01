using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Processing;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroMessaging.Benchmarks;

internal static class ConcurrentInProcessPipelineBenchmark
{
    private const string Usage = "Usage: --inprocess-concurrent <message-count> <handler-count> <producer-count> <capacity> <parallelism> <noop|cpu|async|delay> [publish|receipt] [warmup-count] [runs] [json-output]";

    public static async Task RunAsync(string[] args)
    {
        if (args.Length is < 6 or > 10)
            throw new ArgumentException(Usage);

        var count = ParsePositive(args[0], "message-count");
        var handlerCount = ParsePositive(args[1], "handler-count");
        var producerCount = ParsePositive(args[2], "producer-count");
        var capacity = ParsePositive(args[3], "capacity");
        var parallelism = ParsePositive(args[4], "parallelism");
        var mode = args.Length > 6 ? args[6] : "publish";
        if (mode is not ("publish" or "receipt"))
            throw new ArgumentException(Usage);
        var warmupCount = args.Length > 7 ? ParsePositive(args[7], "warmup-count") : Math.Min(1_000, count);
        warmupCount = Math.Max(producerCount, warmupCount);
        var runs = args.Length > 8 ? ParsePositive(args[8], "runs") : 3;
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
        if ((long)Math.Max(count, warmupCount) * handlerCount > int.MaxValue)
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
        var receiptMethod = typeof(EventBus).GetMethod("PublishAndWaitAsync", [typeof(IEvent), typeof(CancellationToken)]);
        Func<IEvent, CancellationToken, Task>? publishReceipt = null;
        Func<Task, bool>? receiptSucceeded = null;
        if (mode == "receipt")
        {
            if (receiptMethod is null)
                throw new NotSupportedException("This library revision has no PublishAndWaitAsync API.");

            // Bind once so the same harness builds against revisions without receipt models.
            publishReceipt = receiptMethod.CreateDelegate<Func<IEvent, CancellationToken, Task>>(bus);
            var task = Expression.Parameter(typeof(Task));
            var receipt = Expression.Property(Expression.Convert(task, receiptMethod.ReturnType), "Result");
            receiptSucceeded = Expression.Lambda<Func<Task, bool>>(Expression.Property(receipt, "Success"), task).Compile();
        }

        await RunBatchAsync(bus, sink, warmupCount, handlerCount, producerCount, publishReceipt, receiptSucceeded, 0);
        List<InProcessComparisonSample> samples = [];
        Console.WriteLine($"Scenario: messages={count}, handlers={handlerCount}, producers={producerCount}, capacity={capacity}, parallelism={parallelism}, workload={workload}, mode={mode}, deliveries={count * (long)handlerCount}");
        for (var run = 1; run <= runs; run++)
        {
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var gen0Before = GC.CollectionCount(0);
            var gen1Before = GC.CollectionCount(1);
            var gen2Before = GC.CollectionCount(2);
            var result = await RunBatchAsync(bus, sink, count, handlerCount, producerCount, publishReceipt, receiptSucceeded, run);
            var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            var completeRate = count / result.CompletionSeconds;
            var sample = new InProcessComparisonSample(
                completeRate, InProcessPipelineBenchmark.SteadyRate(result.CompletionTimestamps, result.Started, result.CompletionSeconds),
                InProcessPipelineBenchmark.Percentile(result.AcceptLatencies, 0.95),
                InProcessPipelineBenchmark.Percentile(result.AcceptLatencies, 0.99),
                InProcessPipelineBenchmark.Percentile(result.CompletionLatencies, 0.95),
                InProcessPipelineBenchmark.Percentile(result.CompletionLatencies, 0.99),
                allocatedBytes / (double)count, GC.CollectionCount(0) - gen0Before,
                GC.CollectionCount(1) - gen1Before, GC.CollectionCount(2) - gen2Before,
                result.CpuSeconds * 1_000_000 / count, result.Contentions / (double)count,
                result.CompletedWorkItems / (double)count);
            samples.Add(sample);
            var returnLabel = mode == "receipt" ? "receipt-return" : "accept";
            Console.WriteLine($"Run {run}: publish={count / result.PublishSeconds:F0} events/s, complete={completeRate:F0} events/s ({completeRate * handlerCount:F0} deliveries/s), steady={InProcessPipelineBenchmark.SteadyRate(result.CompletionTimestamps, result.Started, result.CompletionSeconds)}, " +
                $"pending-publish={result.PendingPublishes * 100.0 / count:F1}%, {returnLabel} p95/p99={InProcessPipelineBenchmark.Percentile(result.AcceptLatencies, 0.95):F2}/{InProcessPipelineBenchmark.Percentile(result.AcceptLatencies, 0.99):F2}ms, " +
                $"first-handler p95/p99={InProcessPipelineBenchmark.Percentile(result.FirstHandlerLatencies, 0.95):F2}/{InProcessPipelineBenchmark.Percentile(result.FirstHandlerLatencies, 0.99):F2}ms, " +
                $"all-handlers p50/p95/p99={InProcessPipelineBenchmark.Percentile(result.CompletionLatencies, 0.50):F2}/{InProcessPipelineBenchmark.Percentile(result.CompletionLatencies, 0.95):F2}/{InProcessPipelineBenchmark.Percentile(result.CompletionLatencies, 0.99):F2}ms, " +
                $"allocated={allocatedBytes / (double)count:F0} B/event, GC gen0/1/2={GC.CollectionCount(0) - gen0Before}/{GC.CollectionCount(1) - gen1Before}/{GC.CollectionCount(2) - gen2Before}, " +
                $"cpu={result.CpuSeconds:F2} core-s ({result.CpuSeconds / result.CompletionSeconds:F2} average cores, {result.CpuSeconds * 1_000_000 / count:F2} us/event), " +
                $"contentions={result.Contentions / (double)count:F4}/event, work-items={result.CompletedWorkItems / (double)count:F2}/event");
        }

        if (args.Length > 9)
        {
            var output = Path.GetFullPath(args[9]);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var report = new InProcessComparisonResult(1, count, handlerCount, producerCount, capacity, parallelism,
                args[5], mode, warmupCount, receiptMethod is not null, samples);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        }
    }

    private static async Task<BatchResult> RunBatchAsync(EventBus bus, InProcessSink sink, int count, int handlerCount, int producerCount,
        Func<IEvent, CancellationToken, Task>? publishReceipt, Func<Task, bool>? receiptSucceeded, int batchId)
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
                    var message = new InProcessEvent { Sequence = sequence };
                    var publish = publishReceipt is null ? bus.PublishAsync(message) : publishReceipt(message, CancellationToken.None);
                    if (!publish.IsCompleted)
                        Interlocked.Increment(ref pendingPublishes);
                    await publish.ConfigureAwait(false);
                    if (receiptSucceeded is not null && !receiptSucceeded(publish))
                        throw new InvalidOperationException("A benchmark publication returned an unsuccessful receipt.");
                    sink.RecordAccepted(sequence);
                }
            });
        }

        await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var contentionsBefore = Monitor.LockContentionCount;
        var workItemsBefore = ThreadPool.CompletedWorkItemCount;
        InProcessBenchmarkEvents.Log.BatchStart(InProcessBenchmarkEvents.SchemaVersion, count, handlerCount, producerCount, batchId);
        var started = Stopwatch.GetTimestamp();
        start.TrySetResult();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromMinutes(2));
        var publishSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        await sink.Completion.WaitAsync(TimeSpan.FromMinutes(2));
        var completionSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        InProcessBenchmarkEvents.Log.BatchStop(InProcessBenchmarkEvents.SchemaVersion, batchId);
        var cpuSeconds = (process.TotalProcessorTime - cpuBefore).TotalSeconds;
        var contentions = Monitor.LockContentionCount - contentionsBefore;
        var completedWorkItems = ThreadPool.CompletedWorkItemCount - workItemsBefore;
        var (accept, firstHandler, completion, completionTimestamps) = sink.GetLatencies();
        Array.Sort(accept);
        Array.Sort(firstHandler);
        Array.Sort(completion);
        return new BatchResult(started, publishSeconds, completionSeconds, pendingPublishes, accept, firstHandler, completion, completionTimestamps,
            cpuSeconds, contentions, completedWorkItems);
    }

    private static int ParsePositive(string value, string name)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result == 0)
            throw new ArgumentException($"{name} must be a positive integer.");
        return result;
    }

    private sealed record BatchResult(long Started, double PublishSeconds, double CompletionSeconds, int PendingPublishes,
        double[] AcceptLatencies, double[] FirstHandlerLatencies, double[] CompletionLatencies, long[] CompletionTimestamps,
        double CpuSeconds, long Contentions, long CompletedWorkItems);
}

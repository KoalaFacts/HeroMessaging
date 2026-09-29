using System.Diagnostics;
using System.Globalization;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Processing;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroMessaging.Benchmarks;

internal static class InProcessPipelineBenchmark
{
    public static async Task RunAsync(string[] args)
    {
        if (args.Length > 3 || args.Take(2).Any(arg => !int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 1))
            throw new ArgumentException("Usage: --inprocess [message-count] [handler-count] [noop|cpu|async]");

        var count = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 10_000;
        var handlerCount = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 1;
        var workload = args.Length > 2 ? args[2] switch
        {
            "noop" => InProcessWorkload.Noop,
            "cpu" => InProcessWorkload.Cpu,
            "async" => InProcessWorkload.Async,
            _ => throw new ArgumentException("Usage: --inprocess [message-count] [handler-count] [noop|cpu|async]")
        } : InProcessWorkload.Noop;
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
        await using var bus = new EventBus(provider, NullLogger<EventBus>.Instance);
        var sink = provider.GetRequiredService<InProcessSink>();

        await RunBatchAsync(bus, sink, 1_000, handlerCount);
        Console.WriteLine($"Scenario: messages={count}, handlers={handlerCount}, workload={workload}, deliveries={count * (long)handlerCount}");
        for (var run = 1; run <= 3; run++)
        {
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var result = await RunBatchAsync(bus, sink, count, handlerCount);
            var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            var completedEventsPerSecond = count / result.CompletionSeconds;
            var completedDeliveriesPerSecond = completedEventsPerSecond * handlerCount;
            Console.WriteLine($"Run {run}: publish={count / result.PublishSeconds:F0} events/s, complete={completedEventsPerSecond:F0} events/s ({completedDeliveriesPerSecond:F0} deliveries/s), steady={result.SteadyRate}, " +
                $"accept p95/p99={Percentile(result.AcceptLatencies, 0.95):F2}/{Percentile(result.AcceptLatencies, 0.99):F2}ms, first-handler p95/p99={Percentile(result.FirstHandlerLatencies, 0.95):F2}/{Percentile(result.FirstHandlerLatencies, 0.99):F2}ms, " +
                $"all-handlers p50/p95/p99={Percentile(result.CompletionLatencies, 0.50):F2}/{Percentile(result.CompletionLatencies, 0.95):F2}/{Percentile(result.CompletionLatencies, 0.99):F2}ms, allocated={allocatedBytes / (double)count:F0} B/event");
        }
    }

    private static async Task<BatchResult> RunBatchAsync(EventBus bus, InProcessSink sink, int count, int handlerCount)
    {
        sink.Start(count, handlerCount);
        var started = Stopwatch.GetTimestamp();
        for (var sequence = 0; sequence < count; sequence++)
        {
            sink.RecordPublishing(sequence);
            await bus.PublishAsync(new InProcessEvent { Sequence = sequence });
            sink.RecordAccepted(sequence);
        }

        var publishSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        await sink.Completion.WaitAsync(TimeSpan.FromMinutes(2));
        var completionSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        var (accept, firstHandler, completion, completionTimestamps) = sink.GetLatencies();
        Array.Sort(accept);
        Array.Sort(firstHandler);
        Array.Sort(completion);
        return new BatchResult(publishSeconds, completionSeconds, accept, firstHandler,
            completion, SteadyRate(completionTimestamps, started, completionSeconds));
    }

    private static string SteadyRate(long[] completionTimestamps, long started, double durationSeconds)
    {
        var fullSeconds = (int)Math.Floor(durationSeconds);
        if (fullSeconds < 3)
            return "n/a (<3s)";

        var perSecond = new int[fullSeconds - 1];
        foreach (var timestamp in completionTimestamps)
        {
            var second = (int)Stopwatch.GetElapsedTime(started, timestamp).TotalSeconds;
            if (second > 0 && second < fullSeconds)
                perSecond[second - 1]++;
        }

        Array.Sort(perSecond);
        return $"{perSecond[0]}/{perSecond[perSecond.Length / 2]}/{perSecond[^1]} events/s min/median/max";
    }

    private static double Percentile(double[] sorted, double percentile)
        => sorted[(int)Math.Ceiling(sorted.Length * percentile) - 1];

    private sealed record BatchResult(double PublishSeconds, double CompletionSeconds, double[] AcceptLatencies,
        double[] FirstHandlerLatencies, double[] CompletionLatencies, string SteadyRate);
}

internal sealed class InProcessEvent : IEvent
{
    public Guid MessageId { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; set; }
    public string? CausationId { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public int Sequence { get; set; }
}

internal enum InProcessWorkload { Noop, Cpu, Async }

internal sealed class InProcessHandler(InProcessSink sink, int handlerIndex, InProcessWorkload workload) : IEventHandler<InProcessEvent>
{
    private int _checksum;

    public Task HandleAsync(InProcessEvent message, CancellationToken cancellationToken = default)
    {
        if (workload == InProcessWorkload.Async)
            return HandleWithYieldAsync(message);

        if (workload == InProcessWorkload.Cpu)
        {
            var hash = message.Sequence;
            for (var round = 0; round < 1_024; round++)
                hash = unchecked((hash * 16_777_619) ^ round);
            Volatile.Write(ref _checksum, hash);
        }

        sink.RecordHandlerCompleted(message.Sequence, handlerIndex);
        return Task.CompletedTask;
    }

    private async Task HandleWithYieldAsync(InProcessEvent message)
    {
        await Task.Yield();
        sink.RecordHandlerCompleted(message.Sequence, handlerIndex);
    }
}

internal sealed class InProcessSink
{
    private long[] _publishing = [];
    private long[] _accepted = [];
    private long[] _firstHandler = [];
    private long[] _completed = [];
    private long[] _handlerCompleted = [];
    private int[] _handlerCounts = [];
    private int _handlerCount;
    private int _completedEvents;
    private TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Completion => _completion.Task;

    public void Start(int count, int handlerCount)
    {
        _publishing = new long[count];
        _accepted = new long[count];
        _firstHandler = new long[count];
        _completed = new long[count];
        _handlerCompleted = new long[checked(count * handlerCount)];
        _handlerCounts = new int[count];
        _handlerCount = handlerCount;
        _completedEvents = 0;
        _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void RecordPublishing(int sequence) => _publishing[sequence] = Stopwatch.GetTimestamp();

    public void RecordAccepted(int sequence) => _accepted[sequence] = Stopwatch.GetTimestamp();

    public void RecordHandlerCompleted(int sequence, int handlerIndex)
    {
        var timestamp = Stopwatch.GetTimestamp();
        if (Interlocked.CompareExchange(ref _handlerCompleted[sequence * _handlerCount + handlerIndex], timestamp, 0) != 0)
            throw new InvalidOperationException("A benchmark handler ran more than once for an event.");

        Interlocked.CompareExchange(ref _firstHandler[sequence], timestamp, 0);
        if (Interlocked.Increment(ref _handlerCounts[sequence]) != _handlerCount)
            return;

        _completed[sequence] = Stopwatch.GetTimestamp();
        if (Interlocked.Increment(ref _completedEvents) == _completed.Length)
            _completion.TrySetResult();
    }

    public (double[] Accept, double[] FirstHandler, double[] Completion, long[] CompletionTimestamps) GetLatencies()
    {
        if (Volatile.Read(ref _completedEvents) != _publishing.Length ||
            _handlerCounts.Any(count => count != _handlerCount))
            throw new InvalidOperationException("Not all benchmark handlers completed.");

        var accept = new double[_publishing.Length];
        var firstHandler = new double[_publishing.Length];
        var completion = new double[_publishing.Length];
        for (var sequence = 0; sequence < accept.Length; sequence++)
        {
            accept[sequence] = Stopwatch.GetElapsedTime(_publishing[sequence], _accepted[sequence]).TotalMilliseconds;
            firstHandler[sequence] = Stopwatch.GetElapsedTime(_publishing[sequence], _firstHandler[sequence]).TotalMilliseconds;
            completion[sequence] = Stopwatch.GetElapsedTime(_publishing[sequence], _completed[sequence]).TotalMilliseconds;
        }

        return (accept, firstHandler, completion, _completed);
    }
}

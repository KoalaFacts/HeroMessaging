using System.Diagnostics;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Processing;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroMessaging.Benchmarks;

internal static class InProcessPipelineBenchmark
{
    public static async Task RunAsync(string[] args)
    {
        if (args.Length > 1 || args.Length == 1 && (!int.TryParse(args[0], out var parsedCount) || parsedCount < 1))
            throw new ArgumentException("Usage: --inprocess [message-count]");

        var count = args.Length == 0 ? 10_000 : int.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture);
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<InProcessSink>();
        services.AddSingleton<IEventHandler<InProcessEvent>, InProcessHandler>();
        using var provider = services.BuildServiceProvider();
        await using var bus = new EventBus(provider, NullLogger<EventBus>.Instance);
        var sink = provider.GetRequiredService<InProcessSink>();

        await RunBatchAsync(bus, sink, 1_000);
        for (var run = 1; run <= 3; run++)
        {
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var result = await RunBatchAsync(bus, sink, count);
            var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            Console.WriteLine($"Run {run}: messages={count}, publish={result.PublishSeconds:F3}s, handler throughput={count / result.HandlerSeconds:F0}/s, p50={Percentile(result.Latencies, 0.50):F2}ms, p95={Percentile(result.Latencies, 0.95):F2}ms, p99={Percentile(result.Latencies, 0.99):F2}ms, allocated={allocatedBytes / (double)count:F0} B/message");
        }
    }

    private static async Task<BatchResult> RunBatchAsync(EventBus bus, InProcessSink sink, int count)
    {
        sink.Start(count);
        var started = Stopwatch.GetTimestamp();
        for (var sequence = 0; sequence < count; sequence++)
        {
            sink.RecordPublished(sequence);
            await bus.PublishAsync(new InProcessEvent { Sequence = sequence });
        }

        var publishSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        await sink.Completion.WaitAsync(TimeSpan.FromMinutes(2));
        var handlerSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        var latencies = sink.GetLatencies();
        Array.Sort(latencies);
        return new BatchResult(publishSeconds, handlerSeconds, latencies);
    }

    private static double Percentile(double[] sorted, double percentile)
        => sorted[(int)Math.Ceiling(sorted.Length * percentile) - 1];

    private sealed record BatchResult(double PublishSeconds, double HandlerSeconds, double[] Latencies);
}

public sealed class InProcessEvent : IEvent
{
    public Guid MessageId { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; set; }
    public string? CausationId { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public int Sequence { get; set; }
}

public sealed class InProcessHandler(InProcessSink sink) : IEventHandler<InProcessEvent>
{
    public Task HandleAsync(InProcessEvent message, CancellationToken cancellationToken = default)
    {
        sink.RecordHandled(message.Sequence);
        return Task.CompletedTask;
    }
}

public sealed class InProcessSink
{
    private long[] _published = [];
    private long[] _handled = [];
    private int _completed;
    private TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Completion => _completion.Task;

    public void Start(int count)
    {
        _published = new long[count];
        _handled = new long[count];
        _completed = 0;
        _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void RecordPublished(int sequence) => _published[sequence] = Stopwatch.GetTimestamp();

    public void RecordHandled(int sequence)
    {
        if (Interlocked.CompareExchange(ref _handled[sequence], Stopwatch.GetTimestamp(), 0) != 0)
            throw new InvalidOperationException("The handler ran more than once for a benchmark event.");
        if (Interlocked.Increment(ref _completed) == _handled.Length)
            _completion.TrySetResult();
    }

    public double[] GetLatencies()
    {
        if (Volatile.Read(ref _completed) != _published.Length)
            throw new InvalidOperationException("Not all benchmark events were handled.");

        var latencies = new double[_published.Length];
        for (var sequence = 0; sequence < latencies.Length; sequence++)
            latencies[sequence] = Stopwatch.GetElapsedTime(_published[sequence], _handled[sequence]).TotalMilliseconds;
        return latencies;
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Text.Json;

namespace HeroMessaging.Benchmarks;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var selfTest = args is ["--self-test"];
        if (!selfTest && args.Length != 1)
            throw new ArgumentException("Usage: <new-audit-json> | --self-test");
        if (!selfTest && (!OperatingSystem.IsLinux() || Environment.ProcessorCount != 4))
            throw new InvalidOperationException("The native boundary probe requires isolated four-CPU Linux.");

        var source = InProcessBenchmarkEvents.Log;
        if (!selfTest)
        {
            var timeout = Stopwatch.StartNew();
            while (!source.IsEnabled(EventLevel.Informational, (EventKeywords)(-1)))
            {
                if (timeout.Elapsed > TimeSpan.FromSeconds(30))
                    throw new TimeoutException("Marker provider was not enabled before any local observer existed.");
                await Task.Delay(10);
            }
        }
        var enabledBeforeObserver = source.IsEnabled();
        // Local observation follows native enablement; it does not prove perf readiness or delivery.
        using var observer = new MarkerObserver(source);
        using var cancellation = new CancellationTokenSource();
        var workers = selfTest ? [] : Enumerable.Range(0, 4).Select(_ => Task.Factory.StartNew(() =>
        {
            var value = 1;
            while (!cancellation.IsCancellationRequested)
            {
                for (var index = 0; index < 1000; index++)
                    value = unchecked(value * 31 + index);
                Thread.Yield();
            }
            GC.KeepAlive(value);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        List<MarkerCall> calls = [];
        try
        {
            for (var batch = 1; batch <= 3; batch++)
            {
                Emit(1, batch);
                await Task.Delay(selfTest ? TimeSpan.FromMilliseconds(20) : TimeSpan.FromSeconds(12));
                Emit(2, batch);
            }
        }
        finally
        {
            cancellation.Cancel();
            await Task.WhenAll(workers);
        }

        var observed = observer.Records;
        var audit = new
        {
            schemaVersion = 1,
            processId = Environment.ProcessId,
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            enabledBeforeObserver,
            stopwatchFrequency = Stopwatch.Frequency,
            calls,
            observed,
            batchSeconds = Enumerable.Range(0, 3).Select(index =>
                (calls[index * 2 + 1].Timestamp - calls[index * 2].Timestamp) / (double)Stopwatch.Frequency).ToArray(),
            interpretation = "Synthetic marker boundary probe, not message throughput or performance evidence. Payload count=1, handlers=3, producers=32 are fixed wire-schema sentinels, not actual message deliveries. Local observation cannot prove native emission, perf readiness, or loss-free collection."
        };
        if (!selfTest)
        {
            await using var output = new FileStream(args[0], FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(output, audit, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        }
        if (observed.Length != 6 || observed.Any(entry => entry.EventId is not (1 or 2) || entry.Payload.Length == 0) ||
            !observed.Select(entry => (entry.EventId, entry.Payload[^1])).SequenceEqual(calls.Select(call => (call.EventId, call.BatchId))))
            throw new InvalidOperationException("Local observation is incomplete or contains EventSource errors.");
        if (selfTest)
            Console.WriteLine("Marker observer self-test passed; no native collection was performed.");

        void Emit(int eventId, int batchId)
        {
            calls.Add(new MarkerCall(eventId, batchId, Stopwatch.GetTimestamp(), source.IsEnabled()));
            if (eventId == 1)
                source.BatchStart(InProcessBenchmarkEvents.SchemaVersion, 1, 3, 32, batchId);
            else
                source.BatchStop(InProcessBenchmarkEvents.SchemaVersion, batchId);
        }
    }

    private sealed record MarkerCall(int EventId, int BatchId, long Timestamp, bool ProviderEnabled);
    private sealed record MarkerObservation(int EventId, long Timestamp, int[] Payload);

    private sealed class MarkerObserver : EventListener
    {
        private readonly ConcurrentQueue<MarkerObservation> _records = new();

        internal MarkerObserver(EventSource source)
        {
            EnableEvents(source, EventLevel.Informational, (EventKeywords)(-1));
        }

        internal MarkerObservation[] Records => [.. _records];

        protected override void OnEventWritten(EventWrittenEventArgs eventData) => _records.Enqueue(
            new MarkerObservation(eventData.EventId, Stopwatch.GetTimestamp(),
                eventData.Payload?.Select(value => value is int number ? number : -1).ToArray() ?? []));
    }
}

using System.Diagnostics.Tracing;

namespace HeroMessaging.Benchmarks;

[EventSource(Name = "HeroMessaging-InProcessBenchmark")]
internal sealed class InProcessBenchmarkEvents : EventSource
{
    public static readonly InProcessBenchmarkEvents Log = new();

    private InProcessBenchmarkEvents()
    {
    }

    [Event(1, Version = 1, Level = EventLevel.Informational)]
    public void BatchStart(int messages, int handlers, int producers, int batchId) => WriteEvent(1, messages, handlers, producers, batchId);

    [Event(2, Version = 1, Level = EventLevel.Informational)]
    public void BatchStop(int batchId) => WriteEvent(2, batchId);
}

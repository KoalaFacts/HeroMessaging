using System.Diagnostics.Tracing;

namespace HeroMessaging.Benchmarks;

[EventSource(Name = "HeroMessaging-InProcessBenchmark")]
internal sealed class InProcessBenchmarkEvents : EventSource
{
    public static readonly InProcessBenchmarkEvents Log = new();

    private InProcessBenchmarkEvents()
    {
    }

    [Event(1, Level = EventLevel.Informational)]
    public void BatchStart(int messages, int handlers, int producers) => WriteEvent(1, messages, handlers, producers);

    [Event(2, Level = EventLevel.Informational)]
    public void BatchStop() => WriteEvent(2);
}

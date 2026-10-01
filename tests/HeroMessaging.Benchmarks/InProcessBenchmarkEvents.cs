using System.Diagnostics.Tracing;

namespace HeroMessaging.Benchmarks;

[EventSource(Name = "HeroMessaging-InProcessBenchmark")]
internal sealed class InProcessBenchmarkEvents : EventSource
{
    internal const int SchemaVersion = 2;
    public static readonly InProcessBenchmarkEvents Log = new();

    private InProcessBenchmarkEvents()
    {
    }

    [Event(1, Version = SchemaVersion, Level = EventLevel.Informational)]
    public void BatchStart(int schemaVersion, int messages, int handlers, int producers, int batchId) =>
        WriteEvent(1, schemaVersion, messages, handlers, producers, batchId);

    [Event(2, Version = SchemaVersion, Level = EventLevel.Informational)]
    public void BatchStop(int schemaVersion, int batchId) => WriteEvent(2, schemaVersion, batchId);
}

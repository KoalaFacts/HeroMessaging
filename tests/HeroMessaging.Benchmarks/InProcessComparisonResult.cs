namespace HeroMessaging.Benchmarks;

internal sealed record InProcessComparisonResult(int SchemaVersion, int Count, int HandlerCount, int ProducerCount,
    int Capacity, int Parallelism, string Workload, string Mode, int WarmupCount, bool ReceiptSupported,
    IReadOnlyList<InProcessComparisonSample> Samples);

internal sealed record InProcessComparisonSample(double CompleteEventsPerSecond, string SteadyRate,
    double PublishReturnP95Ms, double PublishReturnP99Ms, double AllHandlersP95Ms, double AllHandlersP99Ms,
    double AllocatedBytesPerEvent, int Gen0, int Gen1, int Gen2, double CpuMicrosecondsPerEvent,
    double ContentionsPerEvent, double WorkItemsPerEvent);

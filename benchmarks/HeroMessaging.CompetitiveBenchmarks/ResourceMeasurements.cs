using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;

namespace HeroMessaging.CompetitiveBenchmarks;

internal sealed record ResourceOptions(string Provider, int Count, int LatencyCount, int Warmup)
{
    public static ResourceOptions Parse(string[] args)
    {
        if (args.Length < 2 || args.Length % 2 != 0)
            throw new ArgumentException("Use --resource hero|masstransit [--count N] [--latency-count N] [--warmup N].");

        ProviderHarness.ValidateName(args[1]);
        var values = new Dictionary<string, int> { ["--count"] = 100_000, ["--latency-count"] = 10_000, ["--warmup"] = 10_000 };
        var seen = new HashSet<string>();
        for (var index = 2; index < args.Length; index += 2)
        {
            if (!values.ContainsKey(args[index]) || !seen.Add(args[index])
                || !int.TryParse(args[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                throw new ArgumentException($"Invalid or duplicate resource option: {args[index]}.");

            values[args[index]] = value;
        }

        RequireRange(values["--count"], 1, 1_000_000, "--count");
        RequireRange(values["--latency-count"], 1, 100_000, "--latency-count");
        RequireRange(values["--warmup"], 0, 100_000, "--warmup");
        return new ResourceOptions(args[1], values["--count"], values["--latency-count"], values["--warmup"]);
    }

    private static void RequireRange(int value, int minimum, int maximum, string name)
    {
        if (value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(name, $"Expected {minimum} through {maximum}.");
    }
}

internal sealed record ResourceBatchResult(
    long Completed, long ExpectedCompleted, long Checksum, long ExpectedChecksum,
    double ElapsedMilliseconds, double CompletedPerSecond, double CpuMilliseconds,
    double CpuNanosecondsPerCompletedCommand, long AllocatedBytes,
    double AllocatedBytesPerCompletedCommand, int[] GcCollections);

internal sealed record LatencyBatchResult(
    long Completed, long ExpectedCompleted, long Checksum, long ExpectedChecksum,
    double P50Nanoseconds, double P95Nanoseconds, double P99Nanoseconds);

internal static class ResourceMeasurements
{
    public static async Task<object> RunAsync(ResourceOptions options)
    {
        RequireConsistentProcessView();
        await using var harness = ProviderHarness.Create(options.Provider);
        using var process = Process.GetCurrentProcess();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var command = new CompetitiveCommand();
        var beforeWarmup = ObserveWorkingSet(process);
        for (var index = 0; index < options.Warmup; index++)
            await harness.SendAsync(command, deadline.Token).ConfigureAwait(false);

        harness.Work.RequireCompleted(default, options.Warmup);
        var afterWarmup = ObserveWorkingSet(process);
        var resourceBatch = await MeasureResourcesAsync(harness, command, options.Count, process, deadline.Token).ConfigureAwait(false);
        var afterResourceBatch = ObserveWorkingSet(process);
        var latencyBatch = await MeasureLatencyAsync(harness, command, options.LatencyCount, deadline.Token).ConfigureAwait(false);
        var afterLatencyBatch = ObserveWorkingSet(process);
        return new
        {
            Scope = "In-process command dispatch; one caller; synchronous CPU handler; singleton handler/consumer; no broker, serialization, or durability.",
            Evidence = "Observations only; no superiority verdict. Shared-host smoke runs are not performance acceptance evidence.",
            Metadata = CreateMetadata(harness),
            Options = options,
            ResourceBatch = resourceBatch,
            LatencyBatch = latencyBatch,
            WorkingSetObservations = new
            {
                BeforeWarmupBytes = beforeWarmup,
                AfterWarmupBytes = afterWarmup,
                AfterResourceBatchBytes = afterResourceBatch,
                AfterLatencyBatchBytes = afterLatencyBatch,
                ProcessLifetimePeakBytesObservedAtEnd = process.PeakWorkingSet64,
                Interpretation = "Whole process samples and lifetime peak; not retained heap, steady-state memory, or library-attributable memory."
            }
        };
    }

    private static async Task<ResourceBatchResult> MeasureResourcesAsync(
        ProviderHarness harness, CompetitiveCommand command, int count, Process process, CancellationToken cancellationToken)
    {
        var before = harness.Work.Snapshot;
        var collectionsBefore = ReadCollections();
        var cpuBefore = process.TotalProcessorTime;
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var started = Stopwatch.GetTimestamp();
        for (var index = 0; index < count; index++)
            await harness.SendAsync(command, cancellationToken).ConfigureAwait(false);

        var ended = Stopwatch.GetTimestamp();
        var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var cpuElapsed = process.TotalProcessorTime - cpuBefore;
        var collections = ReadCollections();
        for (var generation = 0; generation < collections.Length; generation++)
            collections[generation] -= collectionsBefore[generation];

        var actual = harness.Work.RequireCompleted(before, count);
        var elapsed = Stopwatch.GetElapsedTime(started, ended);
        return new ResourceBatchResult(actual.Completed, count, actual.Checksum, unchecked(BenchmarkWork.ExpectedPerCommandChecksum * count),
            elapsed.TotalMilliseconds, actual.Completed / elapsed.TotalSeconds, cpuElapsed.TotalMilliseconds,
            cpuElapsed.TotalMilliseconds * 1_000_000 / actual.Completed, allocatedBytes,
            (double)allocatedBytes / actual.Completed, collections);
    }

    private static async Task<LatencyBatchResult> MeasureLatencyAsync(
        ProviderHarness harness, CompetitiveCommand command, int count, CancellationToken cancellationToken)
    {
        var samples = new long[count];
        var before = harness.Work.Snapshot;
        for (var index = 0; index < count; index++)
        {
            var started = Stopwatch.GetTimestamp();
            await harness.SendAsync(command, cancellationToken).ConfigureAwait(false);
            samples[index] = Stopwatch.GetTimestamp() - started;
        }

        var actual = harness.Work.RequireCompleted(before, count);
        Array.Sort(samples);
        return new LatencyBatchResult(actual.Completed, count, actual.Checksum, unchecked(BenchmarkWork.ExpectedPerCommandChecksum * count),
            PercentileNanoseconds(samples, 0.50), PercentileNanoseconds(samples, 0.95), PercentileNanoseconds(samples, 0.99));
    }

    private static double PercentileNanoseconds(long[] sortedSamples, double percentile) =>
        sortedSamples[(int)Math.Ceiling(sortedSamples.Length * percentile) - 1] * (1_000_000_000.0 / Stopwatch.Frequency);

    private static int[] ReadCollections() => [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];

    private static void RequireConsistentProcessView()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var stat = File.ReadAllText("/proc/self/stat");
        var procPid = int.Parse(stat.AsSpan(0, stat.IndexOf(' ')), CultureInfo.InvariantCulture);
        if (procPid != Environment.ProcessId)
            throw new InvalidOperationException("Process metrics unavailable: the Linux PID namespace and /proc mount use different process IDs. Run with matching PID namespace and proc views to avoid reporting another process's CPU or memory.");
    }

    private static long ObserveWorkingSet(Process process)
    {
        process.Refresh();
        return process.WorkingSet64;
    }

    private static object CreateMetadata(ProviderHarness harness) => new
    {
        Provider = harness.Name,
        ObservedUtc = DateTimeOffset.UtcNow,
        Runtime = RuntimeInformation.FrameworkDescription,
        RuntimeVersion = Environment.Version.ToString(),
        RuntimeInformation.OSDescription,
        OSArchitecture = RuntimeInformation.OSArchitecture.ToString(),
        ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
        Environment.ProcessId,
        AvailableProcessorCount = Environment.ProcessorCount,
        GCSettings.IsServerGC,
        GcLatencyMode = GCSettings.LatencyMode.ToString(),
        Stopwatch.Frequency,
        BuildConfiguration = typeof(Program).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
        HarnessAssembly = InformationalVersion(typeof(Program).Assembly),
        SelectedProviderAssembly = DescribeAssembly(harness.Hero?.GetType().Assembly ?? harness.Mediator!.GetType().Assembly),
        PinnedPackages = new { MassTransit = "8.5.12", BenchmarkDotNet = "0.15.8", MicrosoftExtensionsDependencyInjection = "10.0.13" }
    };

    private static string? InformationalVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    private static object DescribeAssembly(Assembly assembly) => new
    {
        assembly.GetName().Name,
        InformationalVersion = InformationalVersion(assembly)
    };
}

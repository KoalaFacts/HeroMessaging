using System.Globalization;
using System.Text.Json;
using HeroMessaging.Benchmarks;
using Microsoft.Diagnostics.Tracing.Etlx;

if (args.Length != 3)
    throw new ArgumentException("Usage: <native-nettrace> <target-pid> <measured-message-count>");
var processId = int.Parse(args[1], CultureInfo.InvariantCulture);
var messageCount = int.Parse(args[2], CultureInfo.InvariantCulture);
var cache = Path.ChangeExtension(args[0], ".etlx");
TraceLog.CreateFromEventPipeDataFile(args[0], cache, new TraceLogOptions { ConversionLog = Console.Error });
using var log = new TraceLog(cache);
var collector = new BatchWindowCollector();
var targetThreads = new HashSet<int>();
var eventKinds = new Dictionary<string, int>();
var batchMarkers = new List<object>();
foreach (var data in log.Events)
{
    var kind = data.ProviderName + "/" + data.EventName;
    eventKinds[kind] = eventKinds.GetValueOrDefault(kind) + 1;
    if (data.ProviderName == "HeroMessaging-InProcessBenchmark")
        batchMarkers.Add(new
        {
            id = (int)data.ID,
            data.EventName,
            data.ProcessID,
            data.ThreadID,
            data.TimeStampRelativeMSec,
            data.Version,
            data.ProviderGuid,
            rawPayload = Convert.ToHexString(data.EventData()),
            payload = data.PayloadNames.ToDictionary(name => name, data.PayloadByName)
        });
    if (data.ProcessID != processId)
        continue;
    if (data.ThreadID > 0)
        targetThreads.Add(data.ThreadID);
    if (data.ProviderGuid != BatchMarkerPayload.ProviderId)
        continue;
    // Decode our exact versioned wire schema; native dynamic metadata can be unavailable.
    collector.Observe(data.ProviderGuid, (int)data.ID, data.Version, data.EventData(), messageCount, data.TimeStampRelativeMSec);
}
var windows = collector.Windows;
var nativeCpuSamples = 0;
var schedulerEvents = 0;
var targetSchedulerEvents = 0;
var missingStacks = 0;
var unresolvedLeaves = 0;
var exclusive = new Dictionary<string, int>();
var monitorCallers = new Dictionary<string, int>();
foreach (var data in log.Events.Where(data => windows.Any(window =>
    data.TimeStampRelativeMSec >= window.Start && data.TimeStampRelativeMSec <= window.Stop)))
{
    var isSwitch = (data.ProviderName == "Universal.Events" && data.EventName == "cswitch") ||
        data.EventName.Contains("sched_switch", StringComparison.Ordinal);
    if (isSwitch)
    {
        schedulerEvents++;
        var matchesTarget = data.ProcessID == processId || data.PayloadNames
            .Where(name => name is "prev_pid" or "next_pid")
            .Any(field => int.TryParse(Convert.ToString(data.PayloadByName(field), CultureInfo.InvariantCulture), out var threadId) &&
                targetThreads.Contains(threadId));
        if (matchesTarget)
            targetSchedulerEvents++;
    }
    if (data.ProcessID != processId || data.ProviderName != "Universal.Events" || data.EventName != "cpu")
        continue;
    nativeCpuSamples++;
    var stack = data.CallStack();
    if (stack is null)
    {
        missingStacks++;
        continue;
    }
    var method = stack.CodeAddress.Method?.FullMethodName;
    var leaf = method ?? $"{stack.CodeAddress.ModuleFile?.Name ?? "?"}!0x{stack.CodeAddress.Address:x}";
    exclusive[leaf] = exclusive.GetValueOrDefault(leaf) + 1;
    if (method is null)
        unresolvedLeaves++;
    if (leaf.Contains("Monitor", StringComparison.Ordinal))
    {
        for (var caller = stack.Caller; caller is not null; caller = caller.Caller)
        {
            var name = caller.CodeAddress.Method?.FullMethodName;
            if (name is not null && (name.StartsWith("System.", StringComparison.Ordinal) || name.StartsWith("HeroMessaging.", StringComparison.Ordinal)))
            {
                monitorCallers[name] = monitorCallers.GetValueOrDefault(name) + 1;
                break;
            }
        }
    }
}
Console.WriteLine(JsonSerializer.Serialize(new
{
    processId,
    messageCount,
    completeWindows = windows.Select(window => new { batchId = window.BatchId, startMilliseconds = window.Start, stopMilliseconds = window.Stop, seconds = (window.Stop - window.Start) / 1000 }),
    nativeCpuSamples,
    schedulerEvents,
    targetSchedulerEvents,
    eventLossReportedByTraceLog = log.EventsLost,
    missingStacks,
    unresolvedLeaves,
    eventKinds,
    batchMarkers,
    exclusiveSamples = exclusive.OrderByDescending(pair => pair.Value).Take(30),
    monitorCallerSamples = monitorCallers.OrderByDescending(pair => pair.Value).Take(20),
    interpretation = "Sample counts, not exclusive instruction costs. Inlined handler code can be attributed to its caller. Unresolved frames and native loss reporting require review. Scheduler event counts do not prove ready-time or queue causality."
}, new JsonSerializerOptions { WriteIndented = true }));

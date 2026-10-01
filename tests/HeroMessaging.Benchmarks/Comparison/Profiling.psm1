Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Report.psm1') -Force

function Get-InProcessProfileConfiguration {
    param([ValidateSet('publish', 'receipt')][string]$Mode, [int]$Count = 500000)
    return @{
        schemaVersion = 1; count = $Count; handlerCount = 3; producerCount = 32
        capacity = 16; parallelism = 4; workload = 'cpu'; mode = $Mode
        warmupCount = 100000; runs = 3
    }
}

function Get-InProcessNativeTraceArguments {
    param([int]$ProcessId, [string]$Output, [int]$Seconds = 60)
    if ($ProcessId -le 0 -or $Seconds -lt 1 -or $Seconds -gt 180 -or !$Output) {
        throw 'A target PID, output path and bounded duration are required.'
    }
    return @('collect-linux', '--process-id', "$ProcessId", '--profile', 'dotnet-common,cpu-sampling,thread-time',
        '--providers', 'HeroMessaging-InProcessBenchmark:0xFFFFFFFFFFFFFFFF:4',
        '--perf-events', 'sched:sched_switch,sched:sched_wakeup,sched:sched_wakeup_new',
        '--duration', ([TimeSpan]::FromSeconds($Seconds).ToString('dd\:hh\:mm\:ss')), '--output', $Output)
}

function Assert-InProcessNativeSummary {
    param([object]$Summary, [int]$ProcessId, [int]$MessageCount, [double[]]$BatchSeconds)
    if ($Summary.processId -ne $ProcessId -or $Summary.messageCount -ne $MessageCount -or
        @($Summary.completeWindows).Count -eq 0 -or $Summary.nativeCpuSamples -le 0 -or
        $Summary.targetSchedulerEvents -le 0 -or $Summary.eventLossReportedByTraceLog -ne 0 -or
        @($Summary.completeWindows | Where-Object { ![double]::IsFinite([double]$_.seconds) -or $_.seconds -lt 10 }).Count -gt 0) {
        throw 'Native diagnostic data must contain target CPU and scheduler events in complete measured windows, without reported event loss.'
    }
    if ($BatchSeconds.Count -ne 3 -or @($BatchSeconds | Where-Object { ![double]::IsFinite($_) -or $_ -le 0 }).Count -gt 0) {
        throw 'All three benchmark batch durations are required to verify native windows.'
    }
    $previousId = 0
    foreach ($window in $Summary.completeWindows) {
        $id = [double]$window.batchId
        if (![double]::IsFinite($id) -or $id -ne [Math]::Truncate($id) -or $id -le $previousId -or $id -gt $BatchSeconds.Count) {
            throw 'Native windows must have unique, increasing measured batch identities.'
        }
        $expectedSeconds = $BatchSeconds[[int]$id - 1]
        $seconds = [double]$window.seconds
        $start = [double]$window.startMilliseconds
        $stop = [double]$window.stopMilliseconds
        if (![double]::IsFinite($start) -or ![double]::IsFinite($stop) -or $start -lt 0 -or $stop -le $start -or
            [Math]::Abs(($stop - $start) / 1000 - $seconds) -gt 0.000001 -or
            [Math]::Abs($seconds - $expectedSeconds) -gt [Math]::Max(0.1, $expectedSeconds * 0.01)) {
            throw 'Native window does not match the duration of its benchmark batch.'
        }
        $previousId = [int]$id
    }
}

function Read-InProcessProfileControl {
    param([string]$Path, [hashtable]$Expected)
    # Profiled batches are diagnostic only; sustained controls use the unchanged comparison guard.
    return Read-InProcessResult $Path $Expected -MinimumBatchSeconds 10
}

Export-ModuleMember -Function Get-InProcessProfileConfiguration, Get-InProcessNativeTraceArguments, Read-InProcessProfileControl, Assert-InProcessNativeSummary

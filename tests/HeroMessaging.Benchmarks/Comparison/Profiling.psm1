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
        '--duration', ([TimeSpan]::FromSeconds($Seconds).ToString('dd\:hh\:mm\:ss')), '--output', $Output)
}

function Read-InProcessProfileControl {
    param([string]$Path, [hashtable]$Expected)
    # Profiled batches are diagnostic only; sustained controls use the unchanged comparison guard.
    return Read-InProcessResult $Path $Expected -MinimumBatchSeconds 10
}

Export-ModuleMember -Function Get-InProcessProfileConfiguration, Get-InProcessNativeTraceArguments, Read-InProcessProfileControl

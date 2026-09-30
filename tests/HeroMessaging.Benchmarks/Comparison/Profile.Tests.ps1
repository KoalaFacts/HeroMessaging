#requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Profiling.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Report.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Calibration.psm1') -Force
foreach ($command in @('Get-InProcessBatchSeconds', 'Get-InProcessCalibrationObservation',
    'Get-InProcessCalibrationCount', 'Read-InProcessResult', 'Read-InProcessProfileControl')) {
    if (!(Get-Command $command -ErrorAction SilentlyContinue)) { throw "Profile import lost required command: $command" }
}

function Assert-Throws {
    param([scriptblock]$Action)
    $threw = $false
    try { & $Action | Out-Null } catch { $threw = $true }
    if (!$threw) { throw 'Expected profiling validation to fail.' }
}

foreach ($mode in @('publish', 'receipt')) {
    $config = Get-InProcessProfileConfiguration $mode
    if ($config.handlerCount -ne 3 -or $config.producerCount -ne 32 -or $config.capacity -ne 16 -or
        $config.parallelism -ne 4 -or $config.warmupCount -ne 100000 -or $config.runs -ne 3 -or
        $config.workload -ne 'cpu' -or $config.mode -ne $mode) { throw 'Profile configuration drifted from CI.' }
}
$arguments = Get-InProcessNativeTraceArguments 123 'capture.nettrace'
if (($arguments -join ' ') -ne ('collect-linux --process-id 123 --profile dotnet-common,cpu-sampling,thread-time ' +
    '--providers HeroMessaging-InProcessBenchmark:0xFFFFFFFFFFFFFFFF:4,Microsoft-Windows-DotNETRuntime:0x100003C01D:4 ' +
    '--perf-events sched:sched_switch,sched:sched_wakeup,sched:sched_wakeup_new --duration 00:00:01:00 --output capture.nettrace')) {
    throw 'Native profile lost PID scoping, kernel sampling, scheduler events or batch markers.'
}
Assert-Throws { Get-InProcessNativeTraceArguments 0 'capture.nettrace' }
Assert-Throws { Get-InProcessNativeTraceArguments 123 '' }
Assert-Throws { Get-InProcessNativeTraceArguments 123 'capture.nettrace' 181 }
$maximum = Get-InProcessNativeTraceArguments 123 'capture.nettrace' 180
if ($maximum[10] -ne '00:00:03:00') { throw 'Capture duration is not a valid bounded timespan.' }
$summary = [PSCustomObject]@{
    processId = 123; messageCount = 500000; completeWindows = @([PSCustomObject]@{ seconds = 12.5 })
    nativeCpuSamples = 1000; targetSchedulerEvents = 10; eventLossReportedByTraceLog = 0
}
Assert-InProcessNativeSummary $summary 123 500000
foreach ($property in @('nativeCpuSamples', 'targetSchedulerEvents')) {
    $value = $summary.$property
    $summary.$property = 0
    Assert-Throws { Assert-InProcessNativeSummary $summary 123 500000 }
    $summary.$property = $value
}
$summary.eventLossReportedByTraceLog = 1
Assert-Throws { Assert-InProcessNativeSummary $summary 123 500000 }
$summary.eventLossReportedByTraceLog = 0
Assert-Throws { Assert-InProcessNativeSummary $summary 999 500000 }
Assert-Throws { Assert-InProcessNativeSummary $summary 123 999 }
foreach ($seconds in @(9, [double]::NaN, [double]::PositiveInfinity)) {
    $summary.completeWindows[0].seconds = $seconds
    Assert-Throws { Assert-InProcessNativeSummary $summary 123 500000 }
}
$summary.completeWindows = @()
Assert-Throws { Assert-InProcessNativeSummary $summary 123 500000 }

$fixture = Join-Path ([IO.Path]::GetTempPath()) "HeroMessaging-Profile-$([Guid]::NewGuid().ToString('N')).json"
try {
    $expected = Get-InProcessProfileConfiguration 'publish' 500000
    $result = $expected.Clone()
    $result.Remove('runs')
    $result.receiptSupported = $true
    $result.samples = @(1..3 | ForEach-Object { @{
        completeEventsPerSecond = 40000; steadyRate = '39000/40000/41000 events/s min/median/max'
        publishReturnP95Ms = 0.5; publishReturnP99Ms = 1.0; allHandlersP95Ms = 0.2; allHandlersP99Ms = 0.3
        allocatedBytesPerEvent = 4096; gen0 = 1; gen1 = 0; gen2 = 0; cpuMicrosecondsPerEvent = 20
        contentionsPerEvent = 0.0; workItemsPerEvent = 2.0
    } })
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fixture
    Read-InProcessProfileControl $fixture $expected | Out-Null
    $observation = Get-InProcessCalibrationObservation ([PSCustomObject]$result) -Runs 3
    if ($observation.fastestSeconds -ne 12.5 -or (Get-InProcessCalibrationCount 500000 $observation.fastestSeconds 10 10000000) -ne 500000) {
        throw 'Actual profiling import order broke calibration execution.'
    }
    $result.samples[2].completeEventsPerSecond = 60000
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fixture
    Assert-Throws { Read-InProcessProfileControl $fixture $expected }
    $result.samples[2].completeEventsPerSecond = 40000
    $result.capacity = 32
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fixture
    Assert-Throws { Read-InProcessProfileControl $fixture $expected }
    Write-Host 'Native profiling configuration and control tests passed.'
}
finally { if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture } }

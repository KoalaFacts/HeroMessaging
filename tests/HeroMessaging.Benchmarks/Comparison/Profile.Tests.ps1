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
    param([scriptblock]$Action, [string]$ExpectedMessage)
    $threw = $false
    try { & $Action | Out-Null } catch {
        if ($ExpectedMessage -and $_.Exception.Message -ne $ExpectedMessage) { throw }
        $threw = $true
    }
    if (!$threw) { throw 'Expected profiling validation to fail.' }
}

foreach ($mode in @('publish', 'receipt')) {
    $config = Get-InProcessProfileConfiguration $mode
    if ($config.handlerCount -ne 3 -or $config.producerCount -ne 32 -or $config.capacity -ne 16 -or
        $config.parallelism -ne 4 -or $config.warmupCount -ne 100000 -or $config.runs -ne 3 -or
        $config.workload -ne 'cpu' -or $config.mode -ne $mode) { throw 'Profile configuration drifted from CI.' }
}
$arguments = Get-InProcessNativeTraceArguments 123 'capture.nettrace'
if (($arguments -join ' ') -ne ('collect-linux --process-id 123 --profile cpu-sampling ' +
    '--providers HeroMessaging-InProcessBenchmark:0xFFFFFFFFFFFFFFFF:4 ' +
    '--perf-events sched:sched_switch,sched:sched_wakeup,sched:sched_wakeup_new --duration 00:00:01:00 --output capture.nettrace')) {
    throw 'Native profile lost required data or reintroduced redundant high-volume providers.'
}
Assert-Throws { Get-InProcessNativeTraceArguments 0 'capture.nettrace' }
Assert-Throws { Get-InProcessNativeTraceArguments 123 '' }
Assert-Throws { Get-InProcessNativeTraceArguments 123 'capture.nettrace' 181 }
$maximum = Get-InProcessNativeTraceArguments 123 'capture.nettrace' 180
if ($maximum[10] -ne '00:00:03:00') { throw 'Capture duration is not a valid bounded timespan.' }
$summary = [PSCustomObject]@{
    processId = 123; messageCount = 500000
    completeWindows = @(
        [PSCustomObject]@{ batchId = 1; startMilliseconds = 0; stopMilliseconds = 12500; seconds = 12.5 }
        [PSCustomObject]@{ batchId = 2; startMilliseconds = 13000; stopMilliseconds = 26000; seconds = 13.0 }
        [PSCustomObject]@{ batchId = 3; startMilliseconds = 26500; stopMilliseconds = 40000; seconds = 13.5 }
    )
    nativeCpuSamples = 1000; targetSchedulerEvents = 10; eventLossReportedByTraceLog = 0
}
$batchSeconds = @(12.5, 13.0, 13.5)
Assert-InProcessNativeSummary $summary 123 500000 $batchSeconds
$validWindows = $summary.completeWindows
# Every requested batch is required, including captures missing only the first, middle or last stop.
foreach ($ids in @(@(1, 2), @(1, 3), @(2, 3), @(1), @(2), @(3))) {
    $summary.completeWindows = @($validWindows | Where-Object { $_.batchId -in $ids })
    Assert-Throws { Assert-InProcessNativeSummary $summary 123 500000 $batchSeconds } `
        'Native diagnostics must contain a complete window for every measured benchmark batch.'
}
$summary.completeWindows = $validWindows
foreach ($property in @('nativeCpuSamples', 'targetSchedulerEvents')) {
    $value = $summary.$property
    $summary.$property = 0
    Assert-Throws { Assert-InProcessNativeSummary $summary 123 500000 $batchSeconds }
    $summary.$property = $value
}
$summary.eventLossReportedByTraceLog = 1
Assert-Throws { Assert-InProcessNativeSummary $summary 123 500000 $batchSeconds }
$summary.eventLossReportedByTraceLog = 0
Assert-Throws { Assert-InProcessNativeSummary $summary 999 500000 $batchSeconds }
Assert-Throws { Assert-InProcessNativeSummary $summary 123 999 $batchSeconds }
foreach ($seconds in @(9, [double]::NaN, [double]::PositiveInfinity)) {
    $summary.completeWindows[0].seconds = $seconds
    Assert-Throws { Assert-InProcessNativeSummary $summary 123 500000 $batchSeconds }
}
$summary.completeWindows[0].seconds = 12.5
foreach ($id in @(0, -1, 4, 1.5, [double]::NaN, [double]::PositiveInfinity)) {
    $summary.completeWindows[0].batchId = $id
    Assert-Throws { Assert-InProcessNativeSummary $summary 123 500000 $batchSeconds }
}
$summary.completeWindows[0].batchId = 1
$summary.completeWindows = @($validWindows[0], $validWindows[0], $validWindows[2])
Assert-Throws { Assert-InProcessNativeSummary $summary 123 500000 $batchSeconds }
$summary.completeWindows = @($validWindows[1], $validWindows[0], $validWindows[2])
Assert-Throws { Assert-InProcessNativeSummary $summary 123 500000 $batchSeconds }
$summary.completeWindows = @([PSCustomObject]@{ seconds = 12.5 }, $validWindows[1], $validWindows[2])
Assert-Throws { Assert-InProcessNativeSummary $summary 123 500000 $batchSeconds }
# Reproduce the green capture's erroneous start 2 -> stop 3 concatenation.
$summary.completeWindows = @($validWindows[0],
    [PSCustomObject]@{ batchId = 2; startMilliseconds = 15934; stopMilliseconds = 43290; seconds = 27.356 },
    $validWindows[2])
Assert-Throws { Assert-InProcessNativeSummary $summary 123 500000 $batchSeconds }
$summary.completeWindows = @([PSCustomObject]@{ batchId = 3; startMilliseconds = 29475; stopMilliseconds = 42975; seconds = 13.5 })
Assert-Throws { Assert-InProcessNativeSummary $summary 123 500000 $batchSeconds }
$summary.completeWindows = $validWindows
Assert-Throws { Assert-InProcessNativeSummary $summary 123 500000 @(12.5, 13.0) }
Assert-Throws { Assert-InProcessNativeSummary $summary 123 500000 @(12.5, 13.0, [double]::NaN) }
$summary.completeWindows = $validWindows
foreach ($stop in @(-1, [double]::NaN, [double]::PositiveInfinity, 13500)) {
    $summary.completeWindows[0].stopMilliseconds = $stop
    Assert-Throws { Assert-InProcessNativeSummary $summary 123 500000 $batchSeconds }
}
$summary.completeWindows = @()
Assert-Throws { Assert-InProcessNativeSummary $summary 123 500000 $batchSeconds }

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

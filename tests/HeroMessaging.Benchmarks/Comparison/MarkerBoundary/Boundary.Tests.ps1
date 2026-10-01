#requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Boundary.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'NativeLogging.psm1') -Force
function New-Audit {
    $calls = @(0..5 | ForEach-Object { @{ eventId = 1 + ($_ % 2); batchId = 1 + [int][Math]::Floor($_ / 2); timestamp = 1000 + $_ * 12000; providerEnabled = $true } })
    return [PSCustomObject]@{
        schemaVersion = 1; processId = 123; enabledBeforeObserver = $true; stopwatchFrequency = 1000; calls = $calls; batchSeconds = @(12, 12, 12)
        observed = @($calls | ForEach-Object { @{ eventId = $_.eventId; timestamp = $_.timestamp + 1; payload = if ($_.eventId -eq 1) { @(2, 1, 3, 32, $_.batchId) } else { @(2, $_.batchId) } } })
    }
}
function Assert-Rejected {
    param([object]$Audit, [int]$ProcessId = 123)
    $rejected = $false
    try { Assert-InProcessMarkerAudit $Audit $ProcessId } catch { $rejected = $true }
    if (!$rejected) { throw 'Invalid marker boundary audit accepted.' }
}
Assert-InProcessMarkerAudit (New-Audit) 123
Assert-Rejected (New-Audit) 999
foreach ($field in @('calls', 'observed', 'batchSeconds')) {
    $audit = New-Audit
    $audit.$field = @($audit.$field | Select-Object -Skip 1)
    Assert-Rejected $audit
}
$audit = New-Audit; $audit.enabledBeforeObserver = $false; Assert-Rejected $audit
$audit = New-Audit; $audit.calls[2].providerEnabled = $false; Assert-Rejected $audit
$audit = New-Audit; $audit.calls[2].batchId = 1; Assert-Rejected $audit
$audit = New-Audit; $audit.observed[2].eventId = 0; Assert-Rejected $audit
$audit = New-Audit; $audit.observed[2].payload[0] = 1; Assert-Rejected $audit
$audit = New-Audit; $audit.observed[2].payload[-1] = 1; Assert-Rejected $audit
$audit = New-Audit; $audit.calls[2].timestamp = [double]::NaN; Assert-Rejected $audit
$audit = New-Audit; $audit.observed[2].timestamp = 0; Assert-Rejected $audit
$audit = New-Audit; $audit.observed[2].timestamp = $audit.calls[3].timestamp + 1; Assert-Rejected $audit
$audit = New-Audit; $audit.stopwatchFrequency = [double]::PositiveInfinity; Assert-Rejected $audit
$audit = New-Audit; $audit.batchSeconds[2] = 1; Assert-Rejected $audit
$statuses = @("Tgid:`t123`nPid:`t123`nTracerPid:`t456", "Tgid:`t123`nPid:`t124`nTracerPid:`t456")
if (!(Test-InProcessNativeWriteObserverAttachment $statuses 123 456)) { throw 'Attached thread snapshot rejected.' }
foreach ($invalid in @(
    @{ statuses = @(); processId = 123; observerId = 456 },
    @{ statuses = $statuses; processId = 0; observerId = 456 },
    @{ statuses = $statuses; processId = 123; observerId = 0 },
    @{ statuses = @($statuses[0], $statuses[0]); processId = 123; observerId = 456 },
    @{ statuses = @($statuses[0], "Tgid:`t999`nPid:`t124`nTracerPid:`t456"); processId = 123; observerId = 456 },
    @{ statuses = @($statuses[0], "Tgid:`t123`nPid:`t124`nTracerPid:`t0"); processId = 123; observerId = 456 },
    @{ statuses = @($statuses[0], "Tgid:`t123`nPid:`t124`nTracerPid:`t789"); processId = 123; observerId = 456 },
    @{ statuses = @($statuses[0], "Tgid:`t123`nPid:`t124"); processId = 123; observerId = 456 },
    @{ statuses = @($statuses[1]); processId = 123; observerId = 456 },
    @{ statuses = @("Tgid:`t123`nPid:`t123`nTracerPid:`t456`nTracerPid:`t456"); processId = 123; observerId = 456 }
)) {
    if (Test-InProcessNativeWriteObserverAttachment $invalid.statuses $invalid.processId $invalid.observerId) {
        throw 'Incomplete, duplicate or foreign observer attachment accepted.'
    }
}
$config = Get-InProcessNativeLoggingConfiguration 123 'native.nettrace' 'native.script' 'native.log'
$expected = @(
    'let HeroMessaging_InProcessBenchmark_flags = new_dotnet_provider_flags();',
    'HeroMessaging_InProcessBenchmark_flags.with_callstacks();',
    'record_dotnet_provider("HeroMessaging-InProcessBenchmark", 0xFFFFFFFFFFFFFFFF, 4, HeroMessaging_InProcessBenchmark_flags);', '',
    'let sched_switch = event_from_tracefs("sched", "sched_switch");', 'record_event(sched_switch);', '',
    'let sched_wakeup = event_from_tracefs("sched", "sched_wakeup");', 'record_event(sched_wakeup);', '',
    'let sched_wakeup_new = event_from_tracefs("sched", "sched_wakeup_new");', 'record_event(sched_wakeup_new);', '', ''
) -join "`n"
if ($config.script -cne $expected -or
    $config.command -cne '--on-cpu --pid 123 --out native.nettrace --script-file native.script --log-filter debug --log-path native.log --log-mode file') {
    throw 'Native logging adapter changed the characterized script or arguments.'
}
$ringConfig = Get-InProcessNativeLoggingConfiguration 123 'native.nettrace' 'native.script' 'native.log' -RingTrace
$ringFilter = 'debug,one_collect::perf_event::rb=trace,one_collect::perf_event::rb::source=trace'
if ($ringConfig.script -cne $config.script -or
    $ringConfig.command -cne $config.command.Replace('--log-filter debug ', "--log-filter $ringFilter ") -or
    ($ringConfig.arguments -join ' ') -cne $ringConfig.command) {
    throw 'Ring trace must change only the characterized logging filter, preserving global debug.'
}
Assert-InProcessNativeRingTraceLibrary 'FCA0D0DAB5CDF81CC156A15BCF40ACA77E2B65744C11CC5DECC74E769A262860'
$rejected = $false
try { Assert-InProcessNativeRingTraceLibrary 'uncharacterized' } catch { $rejected = $true }
if (!$rejected) { throw 'Ring trace accepted an uncharacterized native binary.' }
$rejected = $false
try {
    & (Join-Path $PSScriptRoot 'Profile-Boundary.ps1') -Assembly unused -TraceTool unused -Analyzer unused -OutputDirectory unused -RingTrace
} catch { $rejected = $_.Exception.Message -ceq 'Ring trace requires the isolated native logging adapter.' }
if (!$rejected) { throw 'Ring trace must fail before startup without native logging.' }
foreach ($path in @('', 'two words', 'two"quotes', "two`nlines", 'two\slashes', '-option')) {
    $rejected = $false
    try { $null = Get-InProcessNativeLoggingConfiguration 123 $path 'native.script' 'native.log' } catch { $rejected = $true }
    if (!$rejected) { throw 'Ambiguous native argument accepted.' }
}
$rejected = $false
try { $null = Get-InProcessNativeLoggingConfiguration 0 'native.nettrace' 'native.script' 'native.log' } catch { $rejected = $true }
if (!$rejected) { throw 'Invalid native target accepted.' }
Assert-InProcessNativeRuntimeSupport "pid,processName,supportsCollectLinux`n123,dotnet,true" 123
$probeArguments = Get-InProcessNativeRuntimeProbeArguments 123 'runtime-support.csv'
if (($probeArguments -join ' ') -cne 'collect-linux --probe --process-id 123 --output runtime-support.csv') {
    throw 'Runtime support probe must write machine-readable evidence, not parse console banners.'
}
$nativeTestLog = Get-InProcessNativeLoggingSelfTestLogPath 'diagnostic-results'
if ([IO.Path]::GetFileName($nativeTestLog) -cne 'native-logging-self-test-native.log' -or
    $nativeTestLog -ceq (Join-Path 'diagnostic-results' 'native-logging-self-test.log')) {
    throw 'Native self-test log collides with owned child stdout.'
}
foreach ($invalid in @(@{ processId = 0; path = 'runtime-support.csv' }, @{ processId = 123; path = '' }, @{ processId = 123; path = 'stdout' })) {
    $rejected = $false
    try { $null = Get-InProcessNativeRuntimeProbeArguments $invalid.processId $invalid.path } catch { $rejected = $true }
    if (!$rejected) { throw 'Invalid or console-only runtime probe accepted.' }
}
Assert-InProcessNativeLoggingTool '10.0.745401+cef304c50763bf24f99566cb31d55540842e7ae9'
$rejected = $false
try { Assert-InProcessNativeLoggingTool '10.0.745402+unreviewed' } catch { $rejected = $true }
if (!$rejected) { throw 'Uncharacterized collector version accepted.' }
foreach ($csv in @(
    "pid,processName,supportsCollectLinux`n124,dotnet,true",
    "pid,processName,supportsCollectLinux`n123,dotnet,false",
    "pid,processName,supportsCollectLinux`n123,dotnet,unknown",
    "pid,processName,supportsCollectLinux`n123,dotnet,true`n123,dotnet,true",
    "preview banner`npid,processName,supportsCollectLinux`n123,dotnet,true",
    "pid,processName,supportsCollectLinux,unexpected`n123,dotnet,true,ignored",
    "pid,processName,supportsCollectLinux"
)) {
    $rejected = $false
    try { Assert-InProcessNativeRuntimeSupport $csv 123 } catch { $rejected = $true }
    if (!$rejected) { throw 'Invalid native runtime support accepted.' }
}
Write-Host 'Marker boundary, observer attachment and native logging configuration guards passed; no native capture performed.'

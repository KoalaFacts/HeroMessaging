#requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Boundary.psm1') -Force
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
Write-Host 'Marker boundary and observer attachment guards passed; no native capture performed.'

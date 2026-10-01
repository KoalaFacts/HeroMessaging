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
Write-Host 'Marker boundary audit guards passed; no native capture performed.'

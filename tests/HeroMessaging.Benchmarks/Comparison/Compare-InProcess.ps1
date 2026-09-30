#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaselineRoot,
    [string]$CandidateRoot = (Join-Path $PSScriptRoot '../../..'),
    [string]$OutputDirectory = 'artifacts/ci-inprocess',
    [ValidateRange(1, 4)][int]$Rounds = 1,
    [ValidateRange(1, 10)][int]$Runs = 3,
    [ValidateRange(32, 10000000)][int]$NoopMessages = 2000000,
    [ValidateRange(32, 10000000)][int]$MultiHandlerMessages = 500000,
    [ValidateRange(32, 1000000)][int]$WarmupCount = 100000,
    [ValidateRange(0, 30)][int]$MinimumBatchSeconds = 10,
    [ValidateRange(32, 10000000)][int]$MaximumMessages = 10000000
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Report.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Calibration.psm1') -Force
if ($MinimumBatchSeconds -gt 0 -and $MinimumBatchSeconds -lt 3) {
    throw 'MinimumBatchSeconds must be zero for a smoke test, or at least three for steady windows.'
}
if ([Math]::Max($NoopMessages, $MultiHandlerMessages) -gt $MaximumMessages) {
    throw 'Initial scenario counts must not exceed MaximumMessages.'
}

function Invoke-LoggedDotNet {
    param([string[]]$Arguments, [string]$Log)
    & dotnet @Arguments *> $Log
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $Log | ForEach-Object { Write-Host $_ }
        throw "dotnet failed with exit code $LASTEXITCODE; see $Log"
    }
}

function Get-Commit {
    param([string]$Root)
    $topLevel = & git -C $Root rev-parse --show-toplevel
    if ($LASTEXITCODE -ne 0 -or (Resolve-Path -LiteralPath $topLevel).Path -ne $Root) {
        throw 'Source roots must identify complete Git checkouts, not subdirectories.'
    }
    $commit = & git -C $Root rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[a-f0-9]{40}$') { throw "Cannot resolve source commit: $Root" }
    return $commit
}

function Test-DirtySource {
    param([string]$Root)
    $changes = @(& git -C $Root status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw "Cannot inspect source status: $Root" }
    return $changes.Count -gt 0
}

$baseline = (Resolve-Path -LiteralPath $BaselineRoot).Path
$candidate = (Resolve-Path -LiteralPath $CandidateRoot).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'OutputDirectory must be new; existing evidence is not overwritten.' }
New-Item -ItemType Directory -Path $output | Out-Null
$manifestPath = Join-Path $output 'manifest.json'
$manifest = [ordered]@{
    schemaVersion = 1; status = 'running'; baselineCommit = Get-Commit $baseline
    candidateCommit = Get-Commit $candidate; baselineDirty = Test-DirtySource $baseline
    candidateDirty = Test-DirtySource $candidate; rounds = $Rounds; runs = $Runs; warmupCount = $WarmupCount
    os = [Environment]::OSVersion.VersionString; processorCount = [Environment]::ProcessorCount
    processOrder = 'baseline, candidate, candidate, baseline; reverse order on alternating rounds'
    minimumBatchSeconds = $MinimumBatchSeconds; maximumMessages = $MaximumMessages
    binaries = @(); calibrations = @(); configurations = @(); executions = @()
}
try {
    if ($manifest.baselineDirty) { throw 'Baseline source must be clean.' }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    Invoke-LoggedDotNet @('--info') (Join-Path $output 'dotnet-info.log')
    $project = Join-Path $candidate 'tests/HeroMessaging.Benchmarks/Comparison/HeroMessaging.InProcessBenchmarks.csproj'
    $assemblies = @{}
    foreach ($variant in @('baseline', 'candidate')) {
        $source = if ($variant -eq 'baseline') { $baseline } else { $candidate }
        $binaryDirectory = Join-Path $output "binaries/$variant"
        Invoke-LoggedDotNet @('build', $project, '-c', 'Release', '-f', 'net10.0', '-warnaserror',
            '-o', $binaryDirectory, "-p:HeroMessagingSourceRoot=$source", '-v:minimal') (Join-Path $output "build-$variant.log")
        $assemblies[$variant] = Join-Path $binaryDirectory 'HeroMessaging.InProcessBenchmarks.dll'
        $manifest.binaries += [ordered]@{
            variant = $variant
            librarySha256 = (Get-FileHash -LiteralPath (Join-Path $binaryDirectory 'HeroMessaging.dll')).Hash
            harnessSha256 = (Get-FileHash -LiteralPath $assemblies[$variant]).Hash
        }
    }

    $scenarios = @(
        @{ workload = 'noop'; count = $NoopMessages; handlerCount = 1; capacity = 64; parallelism = 24 },
        @{ workload = 'cpu'; count = $MultiHandlerMessages; handlerCount = 3; capacity = 16; parallelism = 4 },
        @{ workload = 'async'; count = $MultiHandlerMessages; handlerCount = 3; capacity = 16; parallelism = 4 }
    )
    $results = [Collections.Generic.List[object]]::new()
    $supported = @{}
    function Invoke-InProcessExecution {
        param([string]$Variant, [string]$Name, [hashtable]$Expected, [int]$MinimumSeconds = 0)
        $json = Join-Path $output "$Name.json"
        Write-Host "Running $Name (messages=$($Expected.count))"
        Invoke-LoggedDotNet @($assemblies[$Variant], "$($Expected.count)", "$($Expected.handlerCount)",
            "$($Expected.producerCount)", "$($Expected.capacity)", "$($Expected.parallelism)", $Expected.workload,
            $Expected.mode, "$($Expected.warmupCount)", "$($Expected.runs)", $json) (Join-Path $output "$Name.log")
        $result = Read-InProcessResult $json $Expected -MinimumBatchSeconds $MinimumSeconds
        if ($supported.ContainsKey($Variant) -and $supported[$Variant] -ne $result.receiptSupported) {
            throw 'Receipt availability changed between executions of the same binary.'
        }
        $supported[$Variant] = $result.receiptSupported
        return $result
    }
    foreach ($mode in @('publish', 'receipt')) {
        if ($mode -eq 'receipt' -and $supported.baseline -and !$supported.candidate) {
            throw 'The candidate removed the receipt API present in the baseline.'
        }
        $variants = @('baseline', 'candidate' | Where-Object { $mode -eq 'publish' -or $supported[$_] })
        if ($variants.Count -eq 0) { continue }
        foreach ($scenario in $scenarios) {
            $expected = @{
                schemaVersion = 1; count = $scenario.count; handlerCount = $scenario.handlerCount
                producerCount = 32; capacity = $scenario.capacity; parallelism = $scenario.parallelism
                workload = $scenario.workload; mode = $mode; warmupCount = $WarmupCount; runs = $Runs
            }
            $calibrated = $MinimumBatchSeconds -eq 0
            for ($attempt = 1; $attempt -le 4 -and !$calibrated; $attempt++) {
                $pilot = $expected.Clone()
                $durations = @(
                    foreach ($variant in $variants) {
                        $name = "calibrate-$mode-$($scenario.workload)-$attempt-$variant"
                        $result = Invoke-InProcessExecution $variant $name $pilot
                        $observation = Get-InProcessCalibrationObservation $result -Runs $Runs
                        $manifest.calibrations += [ordered]@{
                            execution = $name; variant = $variant; mode = $mode; workload = $scenario.workload
                            count = $pilot.count; seconds = $observation.fastestSeconds
                            batchSeconds = $observation.batchSeconds
                        }
                        $observation.fastestSeconds
                    }
                )
                $fastest = ($durations | Measure-Object -Minimum).Minimum
                $nextCount = Get-InProcessCalibrationCount $expected.count $fastest $MinimumBatchSeconds $MaximumMessages -Attempt $attempt
                $calibrated = $nextCount -eq $expected.count
                $expected.count = $nextCount
            }
            if (!$calibrated) { throw "Calibration did not converge within four attempts for $mode/$($scenario.workload)." }
            $manifest.configurations += [ordered]@{ mode = $mode; workload = $scenario.workload; messages = $expected.count }
            for ($round = 1; $round -le $Rounds; $round++) {
                $order = if ($round % 2 -eq 1) { @('baseline', 'candidate', 'candidate', 'baseline') }
                    else { @('candidate', 'baseline', 'baseline', 'candidate') }
                for ($index = 0; $index -lt $order.Count; $index++) {
                    $variant = $order[$index]
                    if ($mode -eq 'receipt' -and !$supported[$variant]) { continue }
                    $name = "$mode-$($scenario.workload)-$round-$index-$variant"
                    $result = Invoke-InProcessExecution $variant $name $expected $MinimumBatchSeconds
                    $results.Add([PSCustomObject]@{ Variant = $variant; Mode = $mode; Workload = $scenario.workload; Result = $result })
                    $manifest.executions += $name
                }
            }
        }
    }
    $report = New-InProcessReport $results.ToArray() $manifest.baselineCommit $manifest.candidateCommit $MinimumBatchSeconds
    if (!$supported.candidate) { $report += "`n`nReceipt API is unavailable in the candidate; no receipt measurements were run." }
    $report | Set-Content -LiteralPath (Join-Path $output 'report.md') -Encoding utf8
    if ($env:GITHUB_STEP_SUMMARY) { $report | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8 }
    $manifest.status = 'completed'
    Write-Host "Comparison complete: $OutputDirectory/report.md"
}
catch {
    $manifest.status = 'failed'
    throw
}
finally {
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
}

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
    [ValidateRange(32, 1000000)][int]$WarmupCount = 100000
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Report.psm1') -Force

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
    binaries = @(); executions = @()
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
    foreach ($mode in @('publish', 'receipt')) {
        if ($mode -eq 'receipt' -and $supported.baseline -and !$supported.candidate) {
            throw 'The candidate removed the receipt API present in the baseline.'
        }
        foreach ($scenario in $scenarios) {
            $expected = @{
                schemaVersion = 1; count = $scenario.count; handlerCount = $scenario.handlerCount
                producerCount = 32; capacity = $scenario.capacity; parallelism = $scenario.parallelism
                workload = $scenario.workload; mode = $mode; warmupCount = $WarmupCount; runs = $Runs
            }
            for ($round = 1; $round -le $Rounds; $round++) {
                $order = if ($round % 2 -eq 1) { @('baseline', 'candidate', 'candidate', 'baseline') }
                    else { @('candidate', 'baseline', 'baseline', 'candidate') }
                for ($index = 0; $index -lt $order.Count; $index++) {
                    $variant = $order[$index]
                    if ($mode -eq 'receipt' -and !$supported[$variant]) { continue }
                    $name = "$mode-$($scenario.workload)-$round-$index-$variant"
                    $json = Join-Path $output "$name.json"
                    Write-Host "Running $name"
                    Invoke-LoggedDotNet @($assemblies[$variant], "$($scenario.count)", "$($scenario.handlerCount)",
                        '32', "$($scenario.capacity)", "$($scenario.parallelism)", $scenario.workload,
                        $mode, "$WarmupCount", "$Runs", $json) (Join-Path $output "$name.log")
                    $result = Read-InProcessResult $json $expected
                    if ($supported.ContainsKey($variant) -and $supported[$variant] -ne $result.receiptSupported) {
                        throw 'Receipt availability changed between executions of the same binary.'
                    }
                    $supported[$variant] = $result.receiptSupported
                    $results.Add([PSCustomObject]@{ Variant = $variant; Mode = $mode; Workload = $scenario.workload; Result = $result })
                    $manifest.executions += $name
                }
            }
        }
    }
    $report = New-InProcessReport $results.ToArray() $manifest.baselineCommit $manifest.candidateCommit
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

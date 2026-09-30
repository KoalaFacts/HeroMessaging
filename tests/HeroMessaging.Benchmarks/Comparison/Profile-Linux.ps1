#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Assembly,
    [Parameter(Mandatory)][string]$TraceTool,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(30, 180)][int]$CaptureSeconds = 60,
    [switch]$ValidateOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Profiling.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Report.psm1') -Force
# Report reloads calibration in its private scope; expose calibration after that reload.
Import-Module (Join-Path $PSScriptRoot 'Calibration.psm1') -Force

if (!$IsLinux) { throw 'Native CI profiling requires Linux; managed thread-time sampling is not a CPU fallback.' }
if ([Environment]::ProcessorCount -ne 4) { throw 'This profile requires the CI runner with four available processors.' }
$uid = & id -u
if ($LASTEXITCODE -ne 0 -or $uid -ne '0') { throw 'Run the collector as root on an isolated disposable runner.' }
if (!(Test-Path -LiteralPath '/sys/kernel/tracing/user_events_data')) {
    throw 'The kernel must support user_events with tracefs mounted; no fallback trace was started.'
}
$binary = (Resolve-Path -LiteralPath $Assembly).Path
$trace = (Resolve-Path -LiteralPath $TraceTool).Path
$dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$version = & $trace --version
if ($LASTEXITCODE -ne 0) { throw 'The native trace tool is unavailable.' }
$profiles = & $trace list-profiles
if ($LASTEXITCODE -ne 0 -or ($profiles -join "`n") -notmatch 'cpu-sampling' -or ($profiles -join "`n") -notmatch 'thread-time') {
    throw 'The trace tool does not support native CPU and scheduler profiles.'
}
if ($ValidateOnly) { Write-Host 'Linux native profiling prerequisites passed; no workload or trace was started.'; return }

$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'OutputDirectory must be new; previous evidence is not overwritten.' }
$null = New-Item -ItemType Directory -Path $output
$manifestPath = Join-Path $output 'manifest.json'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$commit = & git -c "safe.directory=$repository" -C $repository rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the profiled source revision.' }
$manifest = [ordered]@{
    schemaVersion = 1; status = 'running'; analysisStatus = 'pending'; commit = $commit
    os = [Environment]::OSVersion.VersionString; processorCount = [Environment]::ProcessorCount
    traceToolVersion = $version -join ' '; captureSeconds = $CaptureSeconds
    librarySha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $binary) 'HeroMessaging.dll')).Hash
    harnessSha256 = (Get-FileHash -LiteralPath $binary).Hash
    profiles = 'dotnet-common,cpu-sampling,thread-time'; executions = @(); calibrations = @()
    interpretation = 'Diagnostic capture only. Verify loss, symbols, target PID and complete measured batch windows before attribution. Profiled rates are not optimization evidence.'
}

function Start-Child {
    param([string]$Executable, [string[]]$Arguments)
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = [Diagnostics.ProcessStartInfo]::new($Executable)
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $process.StartInfo.ArgumentList.Add($argument) }
    if (!$process.Start()) { $process.Dispose(); throw 'Cannot start diagnostic child process.' }
    return [PSCustomObject]@{ process = $process; stdout = $process.StandardOutput.ReadToEndAsync(); stderr = $process.StandardError.ReadToEndAsync() }
}

function Stop-Child {
    param($Child, [string]$Name)
    try {
        if (!$Child.process.HasExited) {
            $Child.process.Kill($true)
            if (!$Child.process.WaitForExit(10000)) { throw "Owned process $Name did not exit after termination." }
        }
        if (![Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($Child.stdout, $Child.stderr), 10000)) {
            throw "Owned process $Name did not close its output streams."
        }
        $Child.stdout.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $output "$Name.log")
        $Child.stderr.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $output "$Name-error.log")
    }
    finally { $Child.process.Dispose() }
}

function Invoke-Execution {
    param([string]$Name, [hashtable]$Expected, [switch]$NativeTrace, [switch]$Control)
    $json = Join-Path $output "$Name.json"
    $arguments = @($binary, "$($Expected.count)", '3', '32', '16', '4', 'cpu', $Expected.mode, '100000', '3', $json)
    $benchmark = $null
    $collector = $null
    $record = [ordered]@{ name = $Name; mode = $Expected.mode; count = $Expected.count; nativeTrace = [bool]$NativeTrace; status = 'running' }
    $manifest.executions += $record
    try {
        $benchmark = Start-Child $dotnet $arguments
        $record.processId = $benchmark.process.Id
        if ($NativeTrace) {
            $tracePath = Join-Path $output "$Name.nettrace"
            $collector = Start-Child $trace (Get-InProcessNativeTraceArguments $record.processId $tracePath $CaptureSeconds)
            $record.collectorProcessId = $collector.process.Id
            if (!$collector.process.WaitForExit(($CaptureSeconds + 120) * 1000)) { throw 'Native collector exceeded its bounded save timeout.' }
            if ($collector.process.ExitCode -ne 0) { throw 'Native collection failed; inspect the collector log. No managed fallback is used.' }
            if (!(Test-Path -LiteralPath $tracePath) -or (Get-Item -LiteralPath $tracePath).Length -eq 0) { throw 'No native trace was saved.' }
        }
        if (!$benchmark.process.WaitForExit(180000)) { throw 'Benchmark exceeded its diagnostic timeout.' }
        if ($benchmark.process.ExitCode -ne 0) { throw 'The profiled workload failed; this capture is not a passing workload.' }
        $result = if ($Control) { Read-InProcessProfileControl $json $Expected } else { Read-InProcessResult $json $Expected }
        $record.batchSeconds = @(Get-InProcessBatchSeconds $result)
        $record.status = 'completed'
        return $result
    }
    catch { $record.status = 'failed'; throw }
    finally {
        try { if ($null -ne $collector) { Stop-Child $collector "$Name-collector" } }
        finally { if ($null -ne $benchmark) { Stop-Child $benchmark $Name } }
        $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath
    }
}

try {
    & dotnet --info | Set-Content -LiteralPath (Join-Path $output 'dotnet-info.log')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot record SDK/runtime provenance.' }
    & uname -a | Set-Content -LiteralPath (Join-Path $output 'kernel.log')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot record native kernel provenance.' }
    foreach ($mode in @('publish', 'receipt')) {
        $expected = Get-InProcessProfileConfiguration $mode
        $calibrated = $false
        for ($attempt = 1; $attempt -le 4 -and !$calibrated; $attempt++) {
            $result = Invoke-Execution "calibrate-$mode-$attempt" $expected
            $observation = Get-InProcessCalibrationObservation $result -Runs 3
            $manifest.calibrations += @{ mode = $mode; count = $expected.count; batchSeconds = $observation.batchSeconds }
            $next = Get-InProcessCalibrationCount $expected.count $observation.fastestSeconds 10 10000000 -Attempt $attempt
            $calibrated = $next -eq $expected.count
            $expected.count = $next
        }
        if (!$calibrated) { throw 'Profile control calibration did not converge within four attempts.' }
        $null = Invoke-Execution "$mode-control-before" $expected -Control
        $null = Invoke-Execution "$mode-native" $expected -NativeTrace
        $null = Invoke-Execution "$mode-control-after" $expected -Control
    }
    $manifest.status = 'captured'
    Write-Host 'Native traces captured. Attribution remains pending loss, symbols and measured-window analysis.'
}
catch { $manifest.status = 'failed'; throw }
finally { $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath }

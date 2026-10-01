#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Assembly, [Parameter(Mandatory)][string]$TraceTool,
    [Parameter(Mandatory)][string]$Analyzer, [Parameter(Mandatory)][string]$OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../Profiling.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Boundary.psm1') -Force
if (!$IsLinux -or [Environment]::ProcessorCount -ne 4) { throw 'Boundary capture requires isolated four-CPU Linux.' }
if ((& id -u) -ne '0' -or !(Test-Path -LiteralPath '/sys/kernel/tracing/user_events_data')) {
    throw 'Boundary capture requires root and kernel user_events; no fallback.'
}
$binary = (Resolve-Path -LiteralPath $Assembly).Path
$trace = (Resolve-Path -LiteralPath $TraceTool).Path
$analyzerBinary = (Resolve-Path -LiteralPath $Analyzer).Path
$dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Boundary evidence directory must be new.' }
$null = New-Item -ItemType Directory -Path $output
$version = & $trace --version
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify native collector.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$commit = & git -c "safe.directory=$repository" -C $repository rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify probe revision.' }
$manifest = [ordered]@{
    schemaVersion = 1; status = 'running'; commit = $commit; traceToolVersion = $version -join ' '
    probeSha256 = (Get-FileHash $binary).Hash; analyzerSha256 = (Get-FileHash $analyzerBinary).Hash
    processorCount = [Environment]::ProcessorCount; captureSeconds = 90
    interpretation = 'Synthetic marker boundary diagnostic, not a performance comparison or repair. Local observation does not prove native emission or perf readiness. Required CPU/scheduler/three-window guards remain intact.'
}
$children = [Collections.Generic.List[object]]::new()
function Start-Child {
    param([string]$Executable, [string[]]$Arguments, [string]$Name)
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = [Diagnostics.ProcessStartInfo]::new($Executable)
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $process.StartInfo.ArgumentList.Add($argument) }
    if (!$process.Start()) { $process.Dispose(); throw 'Cannot start owned boundary child.' }
    $child = [PSCustomObject]@{ process = $process; stdout = $process.StandardOutput.ReadToEndAsync(); stderr = $process.StandardError.ReadToEndAsync(); name = $Name }
    $children.Add($child)
    return $process
}
try {
    $auditPath = Join-Path $output 'marker-audit.json'
    $capture = Join-Path $output 'native.nettrace'
    $probe = Start-Child $dotnet @($binary, $auditPath) 'probe'
    $manifest.processId = $probe.Id
    $collector = Start-Child $trace (Get-InProcessNativeTraceArguments $probe.Id $capture 90) 'collector'
    if (!$probe.WaitForExit(70000) -or $probe.ExitCode -ne 0) { throw 'Marker probe failed or exceeded its bounded timeout.' }
    if (!$collector.WaitForExit(210000) -or $collector.ExitCode -ne 0) { throw 'Boundary native collection failed or exceeded its bounded save timeout.' }
    $audit = Get-Content -LiteralPath $auditPath -Raw | ConvertFrom-Json
    Assert-InProcessMarkerAudit $audit $manifest.processId
    $summaryPath = Join-Path $output 'native-summary.json'
    & $dotnet $analyzerBinary $capture "$($manifest.processId)" '1' 1> $summaryPath 2> (Join-Path $output 'analysis.log')
    if ($LASTEXITCODE -ne 0) { throw 'Boundary native decoding failed.' }
    $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
    Assert-InProcessNativeSummary $summary $manifest.processId 1 $audit.batchSeconds
    if ($summary.missingStacks -ne 0) { throw 'Boundary CPU stacks are missing.' }
    $manifest.status = 'captured'
}
catch { $manifest.status = 'failed'; throw }
finally {
    $cleanupFailure = $null
    try {
        foreach ($child in $children) {
            try {
                if (!$child.process.HasExited) {
                    $child.process.Kill($true)
                    if (!$child.process.WaitForExit(10000)) { throw 'Owned boundary child did not terminate.' }
                }
                if (![Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($child.stdout, $child.stderr), 10000)) {
                    throw 'Owned boundary output streams did not close.'
                }
                $child.stdout.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $output "$($child.name).log")
                $child.stderr.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $output "$($child.name)-error.log")
            }
            catch { $cleanupFailure = $_; $manifest.status = 'failed' }
            finally { $child.process.Dispose() }
        }
    }
    finally { $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'manifest.json') }
    if ($null -ne $cleanupFailure) { throw $cleanupFailure }
}

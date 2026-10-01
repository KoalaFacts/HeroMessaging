#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Assembly, [Parameter(Mandatory)][string]$TraceTool,
    [Parameter(Mandatory)][string]$Analyzer, [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$ObserveNativeWrites, [switch]$NativeLogging, [switch]$RingTrace)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../Profiling.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Boundary.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'NativeLogging.psm1') -Force
if ($RingTrace -and !$NativeLogging) { throw 'Ring trace requires the isolated native logging adapter.' }
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
    observeNativeWrites = [bool]$ObserveNativeWrites
    nativeLogging = [bool]$NativeLogging
    ringTrace = [bool]$RingTrace
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
    if ($NativeLogging) {
        Assert-InProcessNativeLoggingTool $manifest.traceToolVersion
        $library = Get-InProcessNativeLoggingLibrary $trace
        $manifest.nativeLibrarySha256 = (Get-FileHash $library).Hash
        if ($RingTrace) {
            Assert-InProcessNativeRingTraceLibrary $manifest.nativeLibrarySha256
            $manifest.ringTraceInterpretation = 'Adds only characterized ring-reader TRACE targets while preserving global debug/WARN/ERROR. Logging perturbs pressure; compiled sites and parser acceptance do not prove reachable records, marker identity or loss-free collection.'
        }
        $manifest.nativeLibraryLength = (Get-Item -LiteralPath $library).Length
        $manifest.nativeLibraryPath = $library
        $manifest.traceToolSha256 = (Get-FileHash $trace).Hash
        $toolAssembly = [IO.Path]::GetFullPath((Join-Path (Split-Path $library -Parent) '../../../dotnet-trace.dll'))
        $manifest.traceToolAssemblySha256 = (Get-FileHash -LiteralPath $toolAssembly).Hash
        $toolDependencies = [IO.Path]::ChangeExtension($toolAssembly, '.deps.json')
        $manifest.traceToolDependenciesSha256 = (Get-FileHash -LiteralPath $toolDependencies).Hash
        $manifest.kernelRelease = (& uname -r) -join ' '
        if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the capture kernel.' }
        $manifest.kernelVersion = Get-Content -LiteralPath '/proc/version' -Raw
        $manifest.nativeLoggingInterpretation = 'Explicit installed-library Cdecl invocation with characterized wrapper configuration and callback duration; adds debug logging, not a durable collector repair or performance proof. Source-template equivalence is not historical wrapper-script byte equivalence.'
        $selfTestLog = Get-InProcessNativeLoggingSelfTestLogPath $output
        $selfTest = Start-Child $dotnet @($binary, '--native-logging-self-test', $library, $selfTestLog) 'native-logging-self-test'
        if (!$selfTest.WaitForExit(15000) -or $selfTest.ExitCode -ne 0) { throw 'Native logging adapter self-test failed or exceeded its timeout.' }
    }
    $auditPath = Join-Path $output 'marker-audit.json'
    $capture = Join-Path $output 'native.nettrace'
    $probe = Start-Child $dotnet @($binary, $auditPath) 'probe'
    $manifest.processId = $probe.Id
    if ($NativeLogging) {
        $runtimeSupport = Join-Path $output 'runtime-support.csv'
        $runtimeProbe = Start-Child $trace (Get-InProcessNativeRuntimeProbeArguments $probe.Id $runtimeSupport) 'runtime-probe'
        if (!$runtimeProbe.WaitForExit(10000) -or $runtimeProbe.ExitCode -ne 0) { throw 'Native runtime support probe failed or timed out.' }
        Assert-InProcessNativeRuntimeSupport (Get-Content -LiteralPath $runtimeSupport -Raw) $probe.Id
        $maps = Get-Content -LiteralPath "/proc/$($probe.Id)/maps"
        $maps | Set-Content -LiteralPath (Join-Path $output 'target-runtime-maps.log')
        $runtimeLibraries = @($maps | ForEach-Object {
            if ($_ -match '\s(/\S*/lib(coreclr|clrjit)\.so)$') { $Matches[1] }
        } | Sort-Object -Unique)
        if ($runtimeLibraries.Count -ne 2) { throw 'Cannot identify both loaded target runtime libraries.' }
        $manifest.targetRuntimeLibraries = @($runtimeLibraries | ForEach-Object { @{ path = $_; sha256 = (Get-FileHash -LiteralPath $_).Hash } })
        $scriptPath = Join-Path $output 'native.script'
        $nativeLog = Join-Path $output 'native-collector.log'
        $config = Get-InProcessNativeLoggingConfiguration $probe.Id $capture $scriptPath $nativeLog -RingTrace:$RingTrace
        [IO.File]::WriteAllText($scriptPath, $config.script, [Text.UTF8Encoding]::new($false))
        $commandPath = Join-Path $output 'native-command.txt'
        [IO.File]::WriteAllText($commandPath, $config.command, [Text.UTF8Encoding]::new($false))
        $manifest.nativeArguments = $config.arguments
        $manifest.nativeScriptSha256 = (Get-FileHash $scriptPath).Hash
    }
    if ($ObserveNativeWrites) {
        $syscallTool = (Get-Command strace -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        $manifest.syscallToolVersion = (& $syscallTool --version) -join ' '
        if ($LASTEXITCODE -ne 0) { throw 'Cannot identify native syscall observer.' }
        $manifest.syscallToolSha256 = (Get-FileHash $syscallTool).Hash
        $syscallLog = Join-Path $output 'native-writev.log'
        $manifest.syscallArguments = @('-f', '-ttt', '-yy', '-xx', '-v', '-s', '4096', '-e', 'trace=writev', '-o', $syscallLog, '-p', "$($probe.Id)")
        $observer = Start-Child $syscallTool $manifest.syscallArguments 'syscall-observer'
        $manifest.syscallObserverId = $observer.Id
        $attachment = [Diagnostics.Stopwatch]::StartNew()
        do {
            if ($observer.HasExited -or $probe.HasExited -or $attachment.Elapsed.TotalSeconds -ge 10) {
                throw 'Native syscall observer did not attach before provider enablement.'
            }
            $statuses = @(Get-ChildItem -LiteralPath "/proc/$($probe.Id)/task" -Directory |
                ForEach-Object { Get-Content -LiteralPath (Join-Path $_.FullName 'status') -Raw })
            $attached = Test-InProcessNativeWriteObserverAttachment $statuses $probe.Id $observer.Id
            if (!$attached) { Start-Sleep -Milliseconds 10 }
        } while (!$attached)
        $statuses | Set-Content -LiteralPath (Join-Path $output 'observer-attachment.log')
        $manifest.observerAttachedBeforeCollector = $true
        $manifest.observerInterpretation = 'ptrace changes scheduling and syscall cost. Inspect native writev descriptor, event identity, payload and result manually, including unfinished/resumed calls. Wall-clock strace times are not monotonic audit times. This is not performance or loss-free native proof.'
    }
    $collector = if ($NativeLogging) {
        Start-Child $dotnet @($binary, '--native-collector', $library, $commandPath, '90') 'collector'
    } else {
        Start-Child $trace (Get-InProcessNativeTraceArguments $probe.Id $capture 90) 'collector'
    }
    if (!$probe.WaitForExit(70000) -or $probe.ExitCode -ne 0) { throw 'Marker probe failed or exceeded its bounded timeout.' }
    if ($ObserveNativeWrites) {
        if (!$observer.WaitForExit(10000) -or $observer.ExitCode -ne 0 -or
            !(Test-Path -LiteralPath $syscallLog) -or (Get-Item -LiteralPath $syscallLog).Length -eq 0) {
            throw 'Native syscall observation failed or its saved log is empty.'
        }
    }
    if (!$collector.WaitForExit(210000) -or $collector.ExitCode -ne 0) { throw 'Boundary native collection failed or exceeded its bounded save timeout.' }
    if ($NativeLogging -and (!(Test-Path -LiteralPath $nativeLog) -or (Get-Item -LiteralPath $nativeLog).Length -eq 0 -or
        (Get-FileHash $library).Hash -cne $manifest.nativeLibrarySha256)) { throw 'Native collector log or binary identity is invalid.' }
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

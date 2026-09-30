#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('noop', 'cpu', 'async')]
    [string]$Scenario = 'noop',
    [ValidateRange(1, 180)]
    [int]$TimeoutSeconds = 90,
    [switch]$ValidateOnly
)

# Windows PowerShell is the default terminal on many hosts; keep the collector on PowerShell 7.
if ($PSVersionTable.PSVersion.Major -lt 7) {
    $command = Get-Command pwsh.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $command) { throw 'PowerShell 7 is required. Install it or enable pwsh.exe on PATH, then retry.' }
    $pwsh = $command.Source
    $versionOutput = & $pwsh -NoLogo -NoProfile -Command '$PSVersionTable.PSVersion.ToString()'
    $runtimeVersion = $null
    if ($LASTEXITCODE -ne 0 -or -not [Version]::TryParse(($versionOutput -join '').Trim(), [ref]$runtimeVersion) -or $runtimeVersion.Major -lt 7) {
        throw 'The resolved pwsh.exe is not a usable PowerShell 7 runtime. No recording was started.'
    }
    $arguments = @('-NoLogo', '-NoProfile', '-File', $PSCommandPath, '-Scenario', $Scenario, '-TimeoutSeconds', [string]$TimeoutSeconds)
    if ($ValidateOnly) { $arguments += '-ValidateOnly' }
    Write-Host "Forwarding to PowerShell $runtimeVersion; elevation is inherited, not requested."
    & $pwsh @arguments
    if ($LASTEXITCODE -ne 0) { throw "PowerShell 7 collector failed ($LASTEXITCODE)." }
    return
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$benchmark = Join-Path $PSScriptRoot 'bin/Release/net10.0/HeroMessaging.Benchmarks.dll'
$profile = Join-Path $PSScriptRoot 'InProcess.wprp'
$wpr = (Get-Command wpr.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$dotnet = (Get-Command dotnet.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
if (-not (Test-Path -LiteralPath $benchmark -PathType Leaf)) {
    throw 'Build the Release net10.0 benchmark before recording. This script never builds during capture.'
}

# Validate the profile without starting a system-wide recording or creating artifacts.
& $wpr -profiles $profile
if ($LASTEXITCODE -ne 0) { throw "WPR profile validation failed ($LASTEXITCODE)." }
& $wpr -profiledetails CPU -filemode
if ($LASTEXITCODE -ne 0) { throw "WPR CPU profile is unavailable ($LASTEXITCODE)." }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$workload = if ($Scenario -eq 'noop') { @('2000000', '1', '32', '64', '24', 'noop') }
    else { @('500000', '3', '32', '16', '4', $Scenario) }
if ($ValidateOnly) {
    Write-Host "Validation passed. Administrator=$isAdministrator. TimeoutSeconds=$TimeoutSeconds. Workload: $($workload -join ' ')"
    return
}
if (-not $isAdministrator) {
    throw 'Recording requires an elevated PowerShell session. No recording or workload was started.'
}

$instance = 'HeroMessaging-' + [Guid]::NewGuid().ToString('N')
$directory = Join-Path $repository ('artifacts/wpr-inprocess/' +
    [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + $Scenario + '-' + $instance.Substring(14, 8))
$null = New-Item -ItemType Directory -Path $directory
$etl = Join-Path $directory 'capture.etl'
$wprLog = Join-Path $directory 'wpr.log'

function Invoke-RecordingCommand {
    param([string[]]$Arguments)
    & $wpr @Arguments -instancename $instance 2>&1 | Tee-Object -FilePath $wprLog -Append
    if ($LASTEXITCODE -ne 0) { throw "WPR failed ($LASTEXITCODE): $($Arguments -join ' ')" }
}

$process = [Diagnostics.Process]::new()
$process.StartInfo = [Diagnostics.ProcessStartInfo]::new($dotnet)
$process.StartInfo.UseShellExecute = $false
$process.StartInfo.CreateNoWindow = $true
$process.StartInfo.RedirectStandardOutput = $true
$process.StartInfo.RedirectStandardError = $true
$process.StartInfo.WorkingDirectory = $repository
foreach ($argument in @($benchmark, '--inprocess-concurrent') + $workload) {
    $process.StartInfo.ArgumentList.Add($argument)
}
$startedRecording = $false
$attemptedRecording = $false
$startedProcess = $false
$savedRecording = $false
$stdout = $null
$stderr = $null
$failure = $null
Write-Host 'WPR captures system-wide activity. Keep ETL files private; do not run builds/tests concurrently.'
Write-Host "Recording instance: $instance"
try {
    $attemptedRecording = $true
    Invoke-RecordingCommand -Arguments @('-start', 'CPU', '-start', "$profile!InProcess", '-filemode', '-recordtempto', $directory)
    $startedRecording = $true
    if (-not $process.Start()) { throw 'Failed to start the benchmark.' }
    $startedProcess = $true
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    "instance=$instance`nprocessId=$($process.Id)`nscenario=$Scenario`narguments=$($workload -join ' ')" |
        Set-Content -LiteralPath (Join-Path $directory 'session.txt')
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        throw "Benchmark exceeded $TimeoutSeconds seconds; the partial trace is diagnostic only."
    }
    if ($process.ExitCode -ne 0) { throw "Benchmark failed ($($process.ExitCode)); do not treat this capture as a passing workload." }
}
catch {
    $failure = $_
}
finally {
    try {
        if ($startedProcess) {
            if (-not $process.HasExited) {
                $process.Kill($true)
                if (-not $process.WaitForExit(10000)) { throw 'The benchmark did not exit after termination; inspect the PID in session.txt.' }
            }
            if (-not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdout, $stderr), 10000)) {
                throw 'Benchmark output did not close within ten seconds; continuing with recording cleanup.'
            }
            $stdout.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $directory 'benchmark.log')
            $stderr.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $directory 'benchmark-error.log')
        }
    }
    catch { if ($null -eq $failure) { $failure = $_ } }
    if ($startedRecording) {
        try {
            Invoke-RecordingCommand -Arguments @('-status', 'collectors', '-details')
        }
        catch { Write-Warning "Could not save pre-stop collector status: $_" }
        try {
            Write-Host 'Saving and merging ETL, including managed symbols. WPR may remain at 0% for several minutes; do not interrupt it.'
            Write-Host 'The benchmark timeout does not apply to this save phase.'
            Invoke-RecordingCommand -Arguments @('-stop', $etl, "HeroMessaging in-process $Scenario CPU and scheduling")
            $savedRecording = $true
        }
        catch {
            if ($null -eq $failure) { $failure = $_ }
            # Never cancel the default instance or another operator's recording.
            & $wpr -cancel -instancename $instance 2>&1 | Tee-Object -FilePath $wprLog -Append
            if ($LASTEXITCODE -ne 0) { Write-Warning "Cleanup failed. Inspect only recording instance $instance." }
        }
    }
    elseif ($attemptedRecording) {
        # A failed start may have created part of this uniquely named session.
        & $wpr -cancel -instancename $instance 2>&1 | Tee-Object -FilePath $wprLog -Append
    }
    $process.Dispose()
}
if ($savedRecording) { Write-Host "Saved private trace: $etl" }
if ($null -ne $failure) { throw $failure }
if (-not $savedRecording -or -not (Test-Path -LiteralPath $etl -PathType Leaf)) {
    throw 'No ETL was saved; this is not a completed recording.'
}
Write-Host 'Capture complete. Check lost events, symbols, benchmark PID, and measured batch windows in WPA before drawing conclusions.'

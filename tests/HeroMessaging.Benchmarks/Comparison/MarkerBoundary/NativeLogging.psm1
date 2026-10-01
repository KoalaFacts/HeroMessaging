Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-InProcessNativeLoggingConfiguration {
    param([int]$ProcessId, [string]$OutputPath, [string]$ScriptPath, [string]$LogPath)
    if ($ProcessId -le 0) { throw 'Native logging requires a positive target PID.' }
    foreach ($path in @($OutputPath, $ScriptPath, $LogPath)) {
        if ([string]::IsNullOrWhiteSpace($path) -or $path -match '[\s"\\]' -or $path.StartsWith('-')) {
            throw 'Native logging paths must be unambiguous native command tokens.'
        }
    }
    # Matches the pinned collect-linux provider/perf-event templates, without extra CLR streams.
    $script = 'let HeroMessaging_InProcessBenchmark_flags = new_dotnet_provider_flags();' + "`n" +
        'HeroMessaging_InProcessBenchmark_flags.with_callstacks();' + "`n" +
        'record_dotnet_provider("HeroMessaging-InProcessBenchmark", 0xFFFFFFFFFFFFFFFF, 4, HeroMessaging_InProcessBenchmark_flags);' + "`n`n"
    foreach ($event in @('sched_switch', 'sched_wakeup', 'sched_wakeup_new')) {
        $script += "let $event = event_from_tracefs(`"sched`", `"$event`");`nrecord_event($event);`n`n"
    }
    $arguments = @('--on-cpu', '--pid', "$ProcessId", '--out', $OutputPath, '--script-file', $ScriptPath,
        '--log-filter', 'debug', '--log-path', $LogPath, '--log-mode', 'file')
    return [PSCustomObject]@{ script = $script; arguments = $arguments; command = $arguments -join ' ' }
}

function Get-InProcessNativeLoggingLibrary {
    param([string]$TraceTool)
    $store = Join-Path (Split-Path $TraceTool -Parent) '.store/dotnet-trace'
    $candidates = @(Get-ChildItem -LiteralPath $store -Filter librecordtrace.so -File -Recurse |
        Where-Object { $_.FullName -match '/runtimes/linux-x64/native/librecordtrace\.so$' })
    if ($candidates.Count -ne 1) { throw 'Cannot uniquely identify the installed Linux x64 native collector.' }
    return $candidates[0].FullName
}

function Assert-InProcessNativeRuntimeSupport {
    param([string]$Csv, [int]$ProcessId)
    $rows = @($Csv | ConvertFrom-Csv)
    if ($ProcessId -le 0 -or $rows.Count -ne 1 -or $rows[0].pid -cne "$ProcessId" -or
        $rows[0].supportsCollectLinux -cne 'true') {
        throw 'Native runtime probe did not confirm support for the exact target PID.'
    }
}

function Assert-InProcessNativeLoggingTool {
    param([string]$Version)
    if ($Version -cne '10.0.745401+cef304c50763bf24f99566cb31d55540842e7ae9') {
        throw 'Installed collector changed; re-characterize its native configuration before capture.'
    }
}

Export-ModuleMember -Function Get-InProcessNativeLoggingConfiguration, Get-InProcessNativeLoggingLibrary, Assert-InProcessNativeRuntimeSupport, Assert-InProcessNativeLoggingTool

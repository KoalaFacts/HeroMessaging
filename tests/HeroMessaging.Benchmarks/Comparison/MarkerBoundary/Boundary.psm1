Set-StrictMode -Version Latest

function Assert-InProcessMarkerAudit {
    param([object]$Audit, [int]$ProcessId)
    $frequency = [double]$Audit.stopwatchFrequency
    if ($Audit.schemaVersion -ne 1 -or $Audit.processId -ne $ProcessId -or $ProcessId -le 0 -or
        $Audit.enabledBeforeObserver -ne $true -or ![double]::IsFinite($frequency) -or $frequency -le 0 -or
        @($Audit.calls).Count -ne 6 -or @($Audit.observed).Count -ne 6 -or @($Audit.batchSeconds).Count -ne 3) {
        throw 'Marker audit requires native-only enablement before observation and all six local marker records.'
    }
    $previousTimestamp = -1.0
    for ($index = 0; $index -lt 6; $index++) {
        $call = $Audit.calls[$index]
        $observed = $Audit.observed[$index]
        $eventId = 1 + ($index % 2)
        $batchId = 1 + [int][Math]::Floor($index / 2)
        $timestamp = [double]$call.timestamp
        $observedTimestamp = [double]$observed.timestamp
        $payload = if ($eventId -eq 1) { @(2, 1, 3, 32, $batchId) } else { @(2, $batchId) }
        if ($call.eventId -ne $eventId -or $call.batchId -ne $batchId -or $call.providerEnabled -ne $true -or
            $observed.eventId -ne $eventId -or @($observed.payload).Count -ne $payload.Count -or
            ![double]::IsFinite($timestamp) -or $timestamp -le $previousTimestamp -or
            ![double]::IsFinite($observedTimestamp) -or $observedTimestamp -lt $timestamp -or
            ($index -lt 5 -and $observedTimestamp -gt [double]$Audit.calls[$index + 1].timestamp)) {
            throw 'Marker call and local observation identities, ordering or enablement do not match.'
        }
        for ($field = 0; $field -lt $payload.Count; $field++) {
            if ($observed.payload[$field] -ne $payload[$field]) { throw 'Marker observation payload differs from the versioned wire schema.' }
        }
        $previousTimestamp = $timestamp
    }
    for ($index = 0; $index -lt 3; $index++) {
        $seconds = ([double]$Audit.calls[$index * 2 + 1].timestamp - [double]$Audit.calls[$index * 2].timestamp) / $frequency
        $reported = [double]$Audit.batchSeconds[$index]
        if (! [double]::IsFinite($reported) -or $seconds -lt 10 -or [Math]::Abs($reported - $seconds) -gt 0.000001) {
            throw 'All marker audit windows must exceed ten seconds and match independent call timestamps.'
        }
    }
}

function Test-InProcessNativeWriteObserverAttachment {
    param([string[]]$Statuses, [int]$ProcessId, [int]$ObserverId)
    if ($ProcessId -le 0 -or $ObserverId -le 0 -or @($Statuses).Count -eq 0) { return $false }
    $threads = [Collections.Generic.HashSet[int]]::new()
    foreach ($status in $Statuses) {
        $fields = @{}
        foreach ($name in @('Tgid', 'Pid', 'TracerPid')) {
            $matches = [regex]::Matches($status, "(?m)^${name}:\s*([0-9]+)\s*$")
            if ($matches.Count -ne 1) { return $false }
            $number = 0
            if (![int]::TryParse($matches[0].Groups[1].Value, [ref]$number)) { return $false }
            $fields[$name] = $number
        }
        if ($fields.Tgid -ne $ProcessId -or $fields.TracerPid -ne $ObserverId -or
            $fields.Pid -le 0 -or !$threads.Add($fields.Pid)) { return $false }
    }
    return $threads.Contains($ProcessId)
}

Export-ModuleMember -Function Assert-InProcessMarkerAudit, Test-InProcessNativeWriteObserverAttachment

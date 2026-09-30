#requires -Version 7.0
Set-StrictMode -Version Latest

$MetricNames = @('completeEventsPerSecond', 'publishReturnP95Ms', 'publishReturnP99Ms',
    'allHandlersP95Ms', 'allHandlersP99Ms', 'allocatedBytesPerEvent', 'gen0', 'gen1', 'gen2',
    'cpuMicrosecondsPerEvent', 'contentionsPerEvent', 'workItemsPerEvent')

function Read-InProcessResult {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][hashtable]$Expected)
    $result = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    foreach ($key in $Expected.Keys) {
        if ($key -ne 'runs' -and $result.$key -cne $Expected[$key]) {
            throw "Unexpected configuration '$key' in $Path"
        }
    }
    if ($result.schemaVersion -ne 1 -or @($result.samples).Count -ne $Expected.runs -or
        $result.receiptSupported -isnot [bool] -or ($result.mode -eq 'receipt' -and !$result.receiptSupported)) {
        throw "Invalid benchmark result structure in $Path"
    }
    foreach ($sample in $result.samples) {
        foreach ($name in $MetricNames) {
            $value = $sample.$name
            if ($value -isnot [ValueType] -or $value -is [bool] -or
                ![double]::IsFinite([double]$value) -or $value -lt 0 -or
                ($name -eq 'completeEventsPerSecond' -and $value -eq 0) -or
                ($name -in @('gen0', 'gen1', 'gen2') -and [Math]::Floor($value) -ne $value)) {
                throw "Invalid metric '$name' in $Path"
            }
        }
        if ($sample.steadyRate -notmatch '^(\d+/\d+/\d+ events/s min/median/max|n/a \(<3s\))$') {
            throw "Invalid steady throughput in $Path"
        }
        if ($sample.publishReturnP95Ms -gt $sample.publishReturnP99Ms -or
            $sample.allHandlersP95Ms -gt $sample.allHandlersP99Ms) {
            throw "Unordered percentiles in $Path"
        }
    }
    return $result
}

function Get-InProcessMedian {
    param([double[]]$Values)
    if (!$Values -or $Values.Count -eq 0) { return $null }
    $sorted = @($Values | Sort-Object)
    return ($sorted[[int][Math]::Floor(($sorted.Count - 1) / 2)] +
        $sorted[[int][Math]::Ceiling(($sorted.Count - 1) / 2)]) / 2
}

function Get-InProcessStatistics {
    param([Parameter(Mandatory)][object[]]$Runs)
    foreach ($group in ($Runs | Group-Object -Property Mode, Workload, Variant)) {
        $stats = [ordered]@{
            Mode = $group.Group[0].Mode; Workload = $group.Group[0].Workload
            Variant = $group.Group[0].Variant; Processes = $group.Count
        }
        foreach ($name in $MetricNames) {
            $processMedians = @($group.Group | ForEach-Object { Get-InProcessMedian @($_.Result.samples.$name) })
            $stats[$name] = Get-InProcessMedian $processMedians
            if ($name -eq 'completeEventsPerSecond') {
                $stats['minimum'] = ($processMedians | Measure-Object -Minimum).Minimum
                $stats['maximum'] = ($processMedians | Measure-Object -Maximum).Maximum
            }
        }
        $steady = @($group.Group | ForEach-Object {
            $values = @($_.Result.samples | ForEach-Object {
                if ($_.steadyRate -match '^\d+/(\d+)/\d+ ') { [double]$Matches[1] }
            })
            if ($values.Count -eq @($_.Result.samples).Count) { Get-InProcessMedian $values }
        })
        $stats['steady'] = if ($steady.Count -eq $group.Count) { Get-InProcessMedian $steady } else { $null }
        [PSCustomObject]$stats
    }
}

function Format-InProcessNumber {
    param($Value, [int]$Digits = 2)
    if ($null -eq $Value) { return 'n/a' }
    return ([double]$Value).ToString("F$Digits", [Globalization.CultureInfo]::InvariantCulture)
}

function New-InProcessReport {
    param([Parameter(Mandatory)][object[]]$Runs,
        [Parameter(Mandatory)][string]$BaselineCommit, [Parameter(Mandatory)][string]$CandidateCommit)
    $statistics = @(Get-InProcessStatistics $Runs | Sort-Object Mode, Workload, Variant)
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add('# In-Process Performance Comparison')
    $lines.Add('')
    $lines.Add("Baseline: ``$BaselineCommit``. Candidate: ``$CandidateCommit``. Exact binaries and dirty-source flags are recorded in manifest.json.")
    $lines.Add('Same runner, SDK, harness and configuration; sequential alternating fresh processes. Metrics below are medians of per-process batch medians, not pooled percentiles. Process ranges show medians, not individual events.')
    $lines.Add('Performance differences are informational, not proof of equivalence or a hard regression gate. Hosted runners can still vary. Build, execution, missing-data and validation failures fail the job.')
    $lines.Add('')
    $lines.Add('| Mode | Workload | Variant | Processes | Complete events/s | Process median range | Difference | Steady events/s |')
    $lines.Add('| --- | --- | --- | ---: | ---: | --- | --- | ---: |')
    foreach ($row in $statistics) {
        $baseline = @($statistics | Where-Object { $_.Mode -eq $row.Mode -and $_.Workload -eq $row.Workload -and $_.Variant -eq 'baseline' })
        $difference = '-'
        if ($row.Variant -eq 'candidate') {
            if ($baseline.Count -eq 1) {
                $difference = (Format-InProcessNumber (($row.completeEventsPerSecond / $baseline[0].completeEventsPerSecond - 1) * 100)) + '%'
            }
            elseif ($row.Mode -eq 'receipt') { $difference = 'new API / no baseline' }
            else { throw "Missing ordinary-publish baseline for $($row.Workload)" }
        }
        $lines.Add("| $($row.Mode) | $($row.Workload) | $($row.Variant) | $($row.Processes) | $(Format-InProcessNumber $row.completeEventsPerSecond 0) | $(Format-InProcessNumber $row.minimum 0)-$(Format-InProcessNumber $row.maximum 0) | $difference | $(Format-InProcessNumber $row.steady 0) |")
    }
    $lines.Add('')
    $lines.Add('| Mode | Workload | Variant | Publish-return p95 / p99 ms | All-handler p95 / p99 ms |')
    $lines.Add('| --- | --- | --- | --- | --- |')
    foreach ($row in $statistics) {
        $lines.Add("| $($row.Mode) | $($row.Workload) | $($row.Variant) | $(Format-InProcessNumber $row.publishReturnP95Ms 3) / $(Format-InProcessNumber $row.publishReturnP99Ms 3) | $(Format-InProcessNumber $row.allHandlersP95Ms 3) / $(Format-InProcessNumber $row.allHandlersP99Ms 3) |")
    }
    $lines.Add('')
    $lines.Add('Publish-return measures admission attempts in publish mode, but final receipt availability in receipt mode. All-handler timestamps are recorded inside handlers; receipt mode additionally waits for final pipeline outcomes. Different modes are different guarantees, not interchangeable competitors.')
    $lines.Add('')
    $lines.Add('| Mode | Workload | Variant | B/event | CPU us/event | GC gen0 / gen1 / gen2 | Contentions/event | Work items/event |')
    $lines.Add('| --- | --- | --- | ---: | ---: | --- | ---: | ---: |')
    foreach ($row in $statistics) {
        $lines.Add("| $($row.Mode) | $($row.Workload) | $($row.Variant) | $(Format-InProcessNumber $row.allocatedBytesPerEvent) | $(Format-InProcessNumber $row.cpuMicrosecondsPerEvent) | $(Format-InProcessNumber $row.gen0 1) / $(Format-InProcessNumber $row.gen1 1) / $(Format-InProcessNumber $row.gen2 1) | $(Format-InProcessNumber $row.contentionsPerEvent 5) | $(Format-InProcessNumber $row.workItemsPerEvent 3) |")
    }
    $lines.Add('')
    $lines.Add('Allocation/CPU/GC include producer, handler, event, instrumentation and runtime work, not isolated library costs. Allocation includes sink setup and sorting; CPU/contention/work-item deltas exclude them. Receipt binding happens once; receipt success validation is included in its measurement. Short runs may have no full-second steady windows. No durable-service or broker performance is measured.')
    return $lines -join "`n"
}

Export-ModuleMember -Function Read-InProcessResult, Get-InProcessMedian, Get-InProcessStatistics, New-InProcessReport

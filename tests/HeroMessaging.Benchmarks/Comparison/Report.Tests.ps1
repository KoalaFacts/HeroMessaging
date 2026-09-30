#requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Report.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Calibration.psm1') -Force

function New-Fixture {
    param([double[]]$Rates)
    [PSCustomObject]@{
        schemaVersion = 1; count = 256; handlerCount = 1; producerCount = 32
        capacity = 64; parallelism = 24; workload = 'noop'; mode = 'publish'
        warmupCount = 32; receiptSupported = $false
        samples = @($Rates | ForEach-Object { [PSCustomObject]@{
            completeEventsPerSecond = $_; steadyRate = 'n/a (<3s)'
            publishReturnP95Ms = 1.0; publishReturnP99Ms = 2.0
            allHandlersP95Ms = 1.0; allHandlersP99Ms = 3.0
            allocatedBytesPerEvent = 1024.0; gen0 = 1; gen1 = 0; gen2 = 0
            cpuMicrosecondsPerEvent = 10.0; contentionsPerEvent = 0.0; workItemsPerEvent = 1.0
        } })
    }
}

function Assert-Equal {
    param($Expected, $Actual)
    if ($Expected -cne $Actual) { throw "Expected '$Expected', got '$Actual'." }
}

function Assert-Throws {
    param([scriptblock]$Action)
    $threw = $false
    try { & $Action | Out-Null } catch { $threw = $true }
    if (!$threw) { throw 'Expected validation to fail.' }
}

$expected = @{
    count = 256; handlerCount = 1; producerCount = 32; capacity = 64; parallelism = 24
    workload = 'noop'; mode = 'publish'; warmupCount = 32; runs = 3
}
$fixture = Join-Path ([IO.Path]::GetTempPath()) "HeroMessaging-Comparison-$([Guid]::NewGuid().ToString('N')).json"
try {
    $valid = New-Fixture @(10, 20, 1000)
    $valid | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fixture -Encoding utf8
    $parsed = Read-InProcessResult $fixture $expected
    Assert-Equal 3 @($parsed.samples).Count
    Assert-Equal 2 (Get-InProcessMedian @(1, 3))
    Assert-Equal 2 (Get-InProcessMedian @(1, 2, 3))
    Assert-Equal 3200000 (Get-InProcessCalibrationCount 256000 1 10 10000000)
    Assert-Equal 320000 (Get-InProcessCalibrationCount 256000 10 10 10000000)
    Assert-Equal 256000 (Get-InProcessCalibrationCount 256000 12.5 10 10000000)
    Assert-Equal 256000 (Get-InProcessCalibrationCount 256000 10 10 10000000 -Attempt 2)
    Assert-Equal 355556 (Get-InProcessCalibrationCount 256000 9 10 10000000 -Attempt 2)
    Assert-Equal 10000000 (Get-InProcessCalibrationCount 256000 0.01 10 10000000)
    Assert-Equal 10000000 (Get-InProcessCalibrationCount 10000000 10 10 10000000)
    foreach ($seconds in @(0, -1, [double]::NaN, [double]::PositiveInfinity)) {
        Assert-Throws { Get-InProcessCalibrationCount 256000 $seconds 10 10000000 }
    }
    Assert-Throws { Get-InProcessCalibrationCount 10000000 1 10 10000000 }
    Assert-Throws { Get-InProcessCalibrationCount 256000 1 10 128000 }
    Assert-Throws { Read-InProcessResult $fixture $expected -MinimumBatchSeconds 10 }
    $sustained = New-Fixture @(10, 20, 25)
    $sustained.samples | ForEach-Object { $_.steadyRate = '1/2/3 events/s min/median/max' }
    $sustained | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fixture -Encoding utf8
    Read-InProcessResult $fixture $expected -MinimumBatchSeconds 10 | Out-Null
    $boundary = New-Fixture @(25.6, 25.6, 25.6)
    $boundary.samples | ForEach-Object { $_.steadyRate = '20/25/30 events/s min/median/max' }
    $boundary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fixture -Encoding utf8
    Read-InProcessResult $fixture $expected -MinimumBatchSeconds 10 | Out-Null
    $paired = @(
        [PSCustomObject]@{ Variant = 'baseline'; Mode = 'publish'; Workload = 'noop'; Result = $boundary },
        [PSCustomObject]@{ Variant = 'candidate'; Mode = 'publish'; Workload = 'noop'; Result = $boundary }
    )
    $boundaryStats = @(Get-InProcessStatistics $paired)
    Assert-Equal 9 $boundaryStats[0].minimumSteadyWindows
    Assert-Equal 10 $boundaryStats[0].batchMinimumSeconds
    $sustainedReport = New-InProcessReport $paired ('a' * 40) ('b' * 40) -MinimumBatchSeconds 10
    if ($sustainedReport -notmatch 'minimum duration of 10 seconds' -or $sustainedReport -match 'Smoke mode') {
        throw 'Sustained report did not preserve duration evidence.'
    }
    $sustained.samples[0].steadyRate = 'n/a (<3s)'
    $sustained | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fixture -Encoding utf8
    Assert-Throws { Read-InProcessResult $fixture $expected -MinimumBatchSeconds 10 }
    $sustained.samples[0].completeEventsPerSecond = 1e-320
    $sustained | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fixture -Encoding utf8
    Assert-Throws { Read-InProcessResult $fixture $expected }
    Assert-Throws { New-InProcessReport @(
        [PSCustomObject]@{ Variant = 'baseline'; Mode = 'publish'; Workload = 'noop'; Result = $valid },
        [PSCustomObject]@{ Variant = 'candidate'; Mode = 'publish'; Workload = 'noop'; Result = $valid }
    ) ('a' * 40) ('b' * 40) -MinimumBatchSeconds 10 }

    $invalid = New-Fixture @(10, 20)
    $invalid | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fixture -Encoding utf8
    Assert-Throws { Read-InProcessResult $fixture $expected }
    $invalid = New-Fixture @(10, 20, 30)
    $invalid.capacity = 8
    $invalid | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fixture -Encoding utf8
    Assert-Throws { Read-InProcessResult $fixture $expected }
    foreach ($value in @(-1.0, [double]::NaN, [double]::PositiveInfinity, '100')) {
        $invalid = New-Fixture @(10, 20, 30)
        $invalid.samples[0].completeEventsPerSecond = $value
        $invalid | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fixture -Encoding utf8
        Assert-Throws { Read-InProcessResult $fixture $expected }
    }
    $invalid = New-Fixture @(10, 20, 30)
    $invalid.samples[0].PSObject.Properties.Remove('gen0')
    $invalid | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fixture -Encoding utf8
    Assert-Throws { Read-InProcessResult $fixture $expected }
    $invalid = New-Fixture @(10, 20, 30)
    $invalid.samples[0].allHandlersP95Ms = 5
    $invalid | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fixture -Encoding utf8
    Assert-Throws { Read-InProcessResult $fixture $expected }

    $runs = @(
        [PSCustomObject]@{ Variant = 'baseline'; Mode = 'publish'; Workload = 'noop'; Result = $valid },
        [PSCustomObject]@{ Variant = 'baseline'; Mode = 'publish'; Workload = 'noop'; Result = New-Fixture @(30, 40, 50) },
        [PSCustomObject]@{ Variant = 'candidate'; Mode = 'publish'; Workload = 'noop'; Result = New-Fixture @(30, 60, 90) },
        [PSCustomObject]@{ Variant = 'candidate'; Mode = 'publish'; Workload = 'noop'; Result = New-Fixture @(60, 100, 110) }
    )
    $stats = @(Get-InProcessStatistics $runs)
    $baseline = $stats | Where-Object Variant -eq 'baseline'
    Assert-Equal 30 $baseline.completeEventsPerSecond
    Assert-Equal 20 $baseline.minimum
    Assert-Equal 40 $baseline.maximum
    Assert-Equal $null $baseline.steady
    Assert-Equal 256 $baseline.count
    Assert-Equal 0 $baseline.minimumSteadyWindows
    $receipt = New-Fixture @(1, 2, 3)
    $receipt.mode = 'receipt'
    $receipt.receiptSupported = $true
    $runs += [PSCustomObject]@{ Variant = 'candidate'; Mode = 'receipt'; Workload = 'noop'; Result = $receipt }
    $culture = [Globalization.CultureInfo]::CurrentCulture
    try {
        [Globalization.CultureInfo]::CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('fr-FR')
        $report = New-InProcessReport $runs ('a' * 40) ('b' * 40)
        if ($report -notmatch '166\.67%' -or $report -notmatch 'new API / no baseline' -or
            $report -notmatch 'Minimum full steady windows/batch') { throw 'Incorrect report delta, availability or sampling evidence.' }
    }
    finally { [Globalization.CultureInfo]::CurrentCulture = $culture }
    Assert-Throws { New-InProcessReport @($runs | Where-Object Variant -eq 'candidate') ('a' * 40) ('b' * 40) }
    $runs[2].Result.count = 128
    Assert-Throws { New-InProcessReport $runs ('a' * 40) ('b' * 40) }
    $runs[3].Result.count = 128
    Assert-Throws { New-InProcessReport $runs ('a' * 40) ('b' * 40) }
    Write-Host 'Comparison reporting tests passed.'
}
finally {
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture }
}

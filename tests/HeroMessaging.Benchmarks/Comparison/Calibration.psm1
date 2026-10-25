#requires -Version 7.0
Set-StrictMode -Version Latest

function Get-InProcessBatchSeconds {
    param([Parameter(Mandatory)][object]$Result)
    foreach ($sample in $Result.samples) {
        $seconds = $Result.count / [double]$sample.completeEventsPerSecond
        if (![double]::IsFinite($seconds) -or $seconds -le 0) {
            throw 'Invalid benchmark batch duration.'
        }
        $seconds
    }
}

function Get-InProcessCalibrationCount {
    param([ValidateRange(32, 20000000)][int]$CurrentCount,
        [Parameter(Mandatory)][double]$FastestSeconds,
        [ValidateRange(1, 30)][int]$MinimumBatchSeconds,
        [ValidateRange(32, 20000000)][int]$MaximumMessages,
        [ValidateRange(1, 4)][int]$Attempt = 1)
    if (![double]::IsFinite($FastestSeconds) -or $FastestSeconds -le 0 -or $CurrentCount -gt $MaximumMessages) {
        throw 'Invalid calibration input.'
    }
    # The fastest pilot can run more slowly than a later measured batch.
    # Require 50% pilot headroom. On later attempts, add a small reserve
    # so fixed per-batch overhead cannot asymptotically approach the 15-second
    # acceptance threshold from below. Measured-batch validity is unchanged.
    if ($FastestSeconds -ge $MinimumBatchSeconds * 1.5) { return $CurrentCount }
    if ($CurrentCount -eq $MaximumMessages) {
        if ($FastestSeconds -ge $MinimumBatchSeconds) { return $CurrentCount }
        throw 'Calibration reached MaximumMessages before MinimumBatchSeconds. Increase the bound or use a shorter explicit sampling duration.'
    }
    # Use the faster revision, with headroom; measured batches still enforce the minimum.
    $targetHeadroom = if ($Attempt -eq 1) { 1.5 } else { 1.6 }
    $scaled = [Math]::Ceiling($CurrentCount * ($MinimumBatchSeconds / $FastestSeconds) * $targetHeadroom)
    return [int][Math]::Min($MaximumMessages, [Math]::Max($CurrentCount + 1, $scaled))
}

function Get-InProcessCalibrationObservation {
    param([Parameter(Mandatory)][object]$Result, [ValidateRange(1, 10)][int]$Runs)
    $durations = @(Get-InProcessBatchSeconds $Result)
    if ($durations.Count -ne $Runs) { throw 'Calibration must use the measured batch count.' }
    [PSCustomObject]@{
        batchSeconds = $durations
        fastestSeconds = ($durations | Measure-Object -Minimum).Minimum
    }
}

Export-ModuleMember -Function Get-InProcessBatchSeconds, Get-InProcessCalibrationCount, Get-InProcessCalibrationObservation

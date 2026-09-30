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
    param([ValidateRange(32, 10000000)][int]$CurrentCount,
        [Parameter(Mandatory)][double]$FastestSeconds,
        [ValidateRange(1, 30)][int]$MinimumBatchSeconds,
        [ValidateRange(32, 10000000)][int]$MaximumMessages,
        [ValidateRange(1, 4)][int]$Attempt = 1)
    if (![double]::IsFinite($FastestSeconds) -or $FastestSeconds -le 0 -or $CurrentCount -gt $MaximumMessages) {
        throw 'Invalid calibration input.'
    }
    if ($FastestSeconds -ge $MinimumBatchSeconds * 1.25) { return $CurrentCount }
    if ($Attempt -gt 1 -and $FastestSeconds -ge $MinimumBatchSeconds) { return $CurrentCount }
    if ($CurrentCount -eq $MaximumMessages) {
        if ($FastestSeconds -ge $MinimumBatchSeconds) { return $CurrentCount }
        throw 'Calibration reached MaximumMessages before MinimumBatchSeconds. Increase the bound or use a shorter explicit sampling duration.'
    }
    # Use the faster revision, with headroom; measured batches still enforce the minimum.
    $scaled = [Math]::Ceiling($CurrentCount * ($MinimumBatchSeconds / $FastestSeconds) * 1.25)
    return [int][Math]::Min($MaximumMessages, [Math]::Max($CurrentCount + 1, $scaled))
}

Export-ModuleMember -Function Get-InProcessBatchSeconds, Get-InProcessCalibrationCount

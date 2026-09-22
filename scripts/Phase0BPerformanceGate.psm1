Set-StrictMode -Version Latest

function Get-Phase0BMedian {
    param(
        [Parameter(Mandatory = $true)][double[]]$Values
    )

    if ($Values.Count -eq 0) {
        throw 'CONFIGGAP_PERFORMANCE_GATE_INVALID: cannot calculate a median from no samples.'
    }

    $ordered = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($ordered.Count / 2)
    if (($ordered.Count % 2) -eq 0) {
        return ([decimal]$ordered[$middle - 1] + [decimal]$ordered[$middle]) / 2
    }

    return [decimal]$ordered[$middle]
}

function Get-Phase0BPerformanceGate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Report,
        [Parameter(Mandatory = $true)][int]$ExpectedObservationCount,
        [switch]$Retrospective
    )

    $requiredGuardedRuns = if ($Retrospective) { 3 } else { 5 }
    $medianHeadroomFactor = [decimal]1.10
    $reasons = [System.Collections.Generic.List[string]]::new()

    if ($null -eq $Report.frozenV1Baseline) {
        throw 'CONFIGGAP_PERFORMANCE_GATE_INVALID: frozenV1Baseline is missing.'
    }

    $frozenWallClockBound = [long]$Report.frozenV1Baseline.wallClockBoundMilliseconds
    $frozenPeakWorkingSetBound = [long]$Report.frozenV1Baseline.peakWorkingSetBoundBytes
    $runs = @($Report.runs)
    $durations = @($runs | ForEach-Object { [decimal]$_.durationMilliseconds })
    $peakWorkingSets = @($runs | ForEach-Object { [long]$_.peakWorkingSetBytes })
    $medianDuration = if ($durations.Count -gt 0) { Get-Phase0BMedian -Values ([double[]]$durations) } else { [decimal]0 }
    $minimumDuration = if ($durations.Count -gt 0) { ($durations | Measure-Object -Minimum).Minimum } else { [decimal]0 }
    $maximumPeakWorkingSet = if ($peakWorkingSets.Count -gt 0) { ($peakWorkingSets | Measure-Object -Maximum).Maximum } else { [long]0 }
    $medianDurationLimit = [decimal]$frozenWallClockBound * $medianHeadroomFactor

    $observationCountPass = $true
    if ([int]$Report.expectedObservationCount -ne $ExpectedObservationCount) {
        $observationCountPass = $false
        [void]$reasons.Add("report expected observation count $($Report.expectedObservationCount) does not match the derived count $ExpectedObservationCount")
    }
    foreach ($run in $runs) {
        if ([int]$run.observationCount -ne $ExpectedObservationCount) {
            $observationCountPass = $false
            [void]$reasons.Add("run $($run.run) observed $($run.observationCount) observations; expected $ExpectedObservationCount")
        }
    }

    $runCountPass = $runs.Count -ge $requiredGuardedRuns
    if (-not $runCountPass) {
        [void]$reasons.Add("only $($runs.Count) guarded runs were reported; at least $requiredGuardedRuns are required")
    }

    $minimumDurationPass = $runs.Count -gt 0 -and $minimumDuration -le $frozenWallClockBound
    if (-not $minimumDurationPass) {
        [void]$reasons.Add("minimum duration $minimumDuration ms exceeds frozen wall-clock bound $frozenWallClockBound ms")
    }

    $medianDurationPass = $runs.Count -gt 0 -and $medianDuration -le $medianDurationLimit
    if (-not $medianDurationPass) {
        [void]$reasons.Add("median duration $medianDuration ms exceeds the 1.10x frozen wall-clock limit $medianDurationLimit ms")
    }

    $peakWorkingSetPass = $runs.Count -gt 0 -and $maximumPeakWorkingSet -le $frozenPeakWorkingSetBound
    if (-not $peakWorkingSetPass) {
        [void]$reasons.Add("maximum peak working set $maximumPeakWorkingSet bytes exceeds frozen peak working-set bound $frozenPeakWorkingSetBound bytes")
    }

    $verdict = if ($reasons.Count -eq 0) { 'PASS' } else { 'FAIL' }
    [pscustomobject]@{
        verdict = $verdict
        requiredGuardedRuns = $requiredGuardedRuns
        reportedRunCount = $runs.Count
        expectedObservationCount = $ExpectedObservationCount
        observationCountPass = $observationCountPass
        runCountPass = $runCountPass
        minimumDurationMilliseconds = [long]$minimumDuration
        minimumDurationPass = $minimumDurationPass
        medianDurationMilliseconds = $medianDuration
        medianHeadroomFactor = $medianHeadroomFactor
        medianDurationLimitMilliseconds = $medianDurationLimit
        medianDurationPass = $medianDurationPass
        maximumPeakWorkingSetBytes = $maximumPeakWorkingSet
        peakWorkingSetBoundBytes = $frozenPeakWorkingSetBound
        peakWorkingSetPass = $peakWorkingSetPass
        frozenWallClockBoundMilliseconds = $frozenWallClockBound
        failureReasons = @($reasons)
    }
}

function Assert-Phase0BPerformanceGate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Report,
        [Parameter(Mandatory = $true)][int]$ExpectedObservationCount,
        [switch]$Retrospective
    )

    $result = Get-Phase0BPerformanceGate -Report $Report -ExpectedObservationCount $ExpectedObservationCount -Retrospective:$Retrospective
    if ($result.verdict -ne 'PASS') {
        throw "CONFIGGAP_PERFORMANCE_GATE_FAILED: $($result.failureReasons -join '; ')"
    }

    return $result
}

Export-ModuleMember -Function Get-Phase0BPerformanceGate, Assert-Phase0BPerformanceGate

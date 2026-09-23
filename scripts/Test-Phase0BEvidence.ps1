[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Get-Location).Path,
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path -LiteralPath $RepositoryRoot).Path
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $repo 'research/phase0b/REPORT.md'
}

$report = (Resolve-Path -LiteralPath $ReportPath).Path
$reportContent = Get-Content -Raw -LiteralPath $report
$metricsPath = Join-Path $repo 'research/phase0b/metrics.json'
if (-not (Test-Path -LiteralPath $metricsPath -PathType Leaf)) {
    throw "CONFIGGAP_EVIDENCE_METRICS: metrics evidence is missing: $metricsPath"
}

$metrics = Get-Content -Raw -LiteralPath $metricsPath | ConvertFrom-Json
if ($metrics.version -ne 1) {
    throw "CONFIGGAP_EVIDENCE_METRICS: unsupported metrics version in '$metricsPath'."
}

function Get-ExactlyOneMatch {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$ErrorCode,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $matches = [regex]::Matches($Text, $Pattern)
    if ($matches.Count -ne 1) {
        throw "${ErrorCode}: '$report' must contain exactly one $Description."
    }

    return $matches[0]
}

function Assert-ReportMetric {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)]$ReportValue,
        [Parameter(Mandatory = $true)]$MetricValue
    )

    try {
        if ([decimal]$ReportValue -eq [decimal]$MetricValue) { return }
    }
    catch {
        # Non-numeric fields, such as the verdict, are compared as strings below.
    }

    if ([string]$ReportValue -ne [string]$MetricValue) {
        throw "CONFIGGAP_EVIDENCE_METRIC_MISMATCH: $Name report='$ReportValue' metrics='$MetricValue'."
    }
}

function Get-IntMatch {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][int]$Group,
        [Parameter(Mandatory = $true)][string]$Name
    )

    return [int](Get-ExactlyOneMatch $Text $Pattern 'CONFIGGAP_EVIDENCE_SUMMARY' "$Name summary line").Groups[$Group].Value
}

$precisionSummary = Get-ExactlyOneMatch $reportContent '(?im)^\s*Precision protocol:\s*(\d+)/(\d+) cases passed; control blocking findings:\s*(\d+); failed cases:\s*(\d+)\s*$' 'CONFIGGAP_EVIDENCE_SUMMARY' 'Precision protocol summary line'
Assert-ReportMetric 'precision protocol true positives' $precisionSummary.Groups[1].Value $metrics.precisionProtocolTruePositives
Assert-ReportMetric 'precision protocol cases' $precisionSummary.Groups[2].Value $metrics.precisionProtocolCases
Assert-ReportMetric 'precision protocol control blocking findings' $precisionSummary.Groups[3].Value $metrics.precisionProtocolControlBlockingFindings
Assert-ReportMetric 'precision protocol failed cases' $precisionSummary.Groups[4].Value $metrics.precisionProtocolFailedCases

$precisionCases = Get-IntMatch $reportContent '(?im)^\s*Precision protocol cases:\s*(\d+)\s*$' 1 'Precision protocol cases'
$precisionPredictions = Get-IntMatch $reportContent '(?im)^\s*Precision protocol predictions:\s*(\d+)\s*$' 1 'Precision protocol predictions'
$precisionTruePositives = Get-IntMatch $reportContent '(?im)^\s*Precision protocol true positives:\s*(\d+)\s*$' 1 'Precision protocol true positives'
$precisionControl = Get-IntMatch $reportContent '(?im)^\s*Precision protocol control blocking findings:\s*(\d+)\s*$' 1 'Precision protocol control blocking findings'
$precisionFailures = Get-IntMatch $reportContent '(?im)^\s*Precision protocol failed cases:\s*(\d+)\s*$' 1 'Precision protocol failed cases'
Assert-ReportMetric 'precision protocol cases detail' $precisionCases $metrics.precisionProtocolCases
Assert-ReportMetric 'precision protocol predictions' $precisionPredictions $metrics.precisionProtocolPredictions
Assert-ReportMetric 'precision protocol true positives detail' $precisionTruePositives $metrics.precisionProtocolTruePositives
Assert-ReportMetric 'precision protocol control detail' $precisionControl $metrics.precisionProtocolControlBlockingFindings
Assert-ReportMetric 'precision protocol failures detail' $precisionFailures $metrics.precisionProtocolFailedCases

$blockingPrecision = Get-ExactlyOneMatch $reportContent '(?im)^\s*Blocking precision:\s*(\d+)/(\d+)\s*=\s*([0-9]+(?:\.[0-9]+)?)%\s*\(([^)]+)\)\s*$' 'CONFIGGAP_EVIDENCE_SUMMARY' 'Blocking precision summary line'
Assert-ReportMetric 'blocking precision numerator' $blockingPrecision.Groups[1].Value $metrics.precisionProtocolTruePositives
Assert-ReportMetric 'blocking precision denominator' $blockingPrecision.Groups[2].Value $metrics.precisionProtocolPredictions
Assert-ReportMetric 'blocking precision percentage' ([decimal]::Parse($blockingPrecision.Groups[3].Value, [Globalization.CultureInfo]::InvariantCulture)) ([decimal]$metrics.blockingPrecision)
Assert-ReportMetric 'blocking precision status' $blockingPrecision.Groups[4].Value $metrics.blockingPrecisionStatus

$observedPrecision = Get-ExactlyOneMatch $reportContent '(?im)^\s*Observed corpus precision:\s*(\d+)/(\d+)\s*=\s*([0-9]+(?:\.[0-9]+)?)%\s*$' 'CONFIGGAP_EVIDENCE_SUMMARY' 'Observed corpus precision summary line'
Assert-ReportMetric 'observed corpus precision numerator' $observedPrecision.Groups[1].Value $metrics.staticKeyTruePositives
Assert-ReportMetric 'observed corpus precision denominator' $observedPrecision.Groups[2].Value $metrics.staticKeyPredictions
Assert-ReportMetric 'observed corpus precision percentage' ([decimal]::Parse($observedPrecision.Groups[3].Value, [Globalization.CultureInfo]::InvariantCulture)) ([decimal]$metrics.corpusBlockingPrecision)

$supportedRecall = Get-ExactlyOneMatch $reportContent '(?im)^\s*Supported-domain recall:\s*(\d+)/(\d+)\s*=\s*([0-9]+(?:\.[0-9]+)?)%\s*$' 'CONFIGGAP_EVIDENCE_SUMMARY' 'Supported-domain recall summary line'
Assert-ReportMetric 'supported recall numerator' $supportedRecall.Groups[1].Value $metrics.staticKeyRecallNumerator
Assert-ReportMetric 'supported recall denominator' $supportedRecall.Groups[2].Value $metrics.staticKeyRecallDenominator
Assert-ReportMetric 'supported recall percentage' ([decimal]::Parse($supportedRecall.Groups[3].Value, [Globalization.CultureInfo]::InvariantCulture)) ([decimal]$metrics.staticKeyRecall)

$allRecall = Get-ExactlyOneMatch $reportContent '(?im)^\s*All-labeled-static-key recall:\s*(\d+)/(\d+)\s*=\s*([0-9]+(?:\.[0-9]+)?)%\s*$' 'CONFIGGAP_EVIDENCE_SUMMARY' 'All-labeled-static-key recall summary line'
Assert-ReportMetric 'all-labeled recall numerator' $allRecall.Groups[1].Value $metrics.allLabeledStaticRecallNumerator
Assert-ReportMetric 'all-labeled recall denominator' $allRecall.Groups[2].Value $metrics.allLabeledStaticRecallDenominator
Assert-ReportMetric 'all-labeled recall percentage' ([decimal]::Parse($allRecall.Groups[3].Value, [Globalization.CultureInfo]::InvariantCulture)) ([decimal]$metrics.allLabeledStaticRecall)

Assert-ReportMetric 'dynamic blocking findings' (Get-IntMatch $reportContent '(?im)^\s*Dynamic blocking findings:\s*(\d+)\s*$' 1 'Dynamic blocking findings') $metrics.dynamicBlockingFindings
Assert-ReportMetric 'load failures' (Get-IntMatch $reportContent '(?im)^\s*Load failures:\s*(\d+)\s*$' 1 'Load failures') $metrics.loadFailureCount
$verdict = (Get-ExactlyOneMatch $reportContent '(?im)^\s*Verdict:\s*(PASS|FAIL)\s*$' 'CONFIGGAP_EVIDENCE_SUMMARY' 'Verdict summary line').Groups[1].Value
Assert-ReportMetric 'verdict' $verdict $metrics.verdict

$performancePath = Join-Path $repo 'research/phase0b/performance.json'
if (-not (Test-Path -LiteralPath $performancePath -PathType Leaf)) {
    throw "CONFIGGAP_EVIDENCE_PERFORMANCE: performance evidence is missing: $performancePath"
}
Import-Module (Join-Path $repo 'scripts/Phase0BPerformanceGate.psm1') -Force
$performance = Get-Content -Raw -LiteralPath $performancePath | ConvertFrom-Json
$performanceGate = Get-Phase0BPerformanceGate -Report $performance -ExpectedObservationCount 3302
if ($performanceGate.verdict -ne 'PASS') {
    throw "CONFIGGAP_EVIDENCE_PERFORMANCE_GATE: committed performance evidence failed: $($performanceGate.failureReasons -join '; ')"
}
$comparisonBaselineGate = Get-Phase0BPerformanceGate -Report ([pscustomobject]@{
        expectedObservationCount = 3302
        runs = @(
            [pscustomobject]@{ run = 1; observationCount = 3302; durationMilliseconds = 21274; peakWorkingSetBytes = 257155072 }
            [pscustomobject]@{ run = 2; observationCount = 3302; durationMilliseconds = 19633; peakWorkingSetBytes = 251711488 }
            [pscustomobject]@{ run = 3; observationCount = 3302; durationMilliseconds = 20129; peakWorkingSetBytes = 252014592 }
        )
        frozenV1Baseline = $performance.frozenV1Baseline
    }) -ExpectedObservationCount 3302 -Retrospective
if ($comparisonBaselineGate.verdict -ne 'PASS') {
    throw "CONFIGGAP_EVIDENCE_COMPARISON_BASELINE: the comparison baseline failed: $($comparisonBaselineGate.failureReasons -join '; ')"
}
if ([long]$performance.frozenV1Baseline.wallClockBoundMilliseconds -ne 21000 -or [long]$performance.frozenV1Baseline.peakWorkingSetBoundBytes -ne 274726912) {
    throw 'CONFIGGAP_EVIDENCE_PERFORMANCE_BASELINE: frozen V1 resource limits changed.'
}
Write-Output "Resource performance gate consistency: PASS ($($performanceGate.reportedRunCount) runs; minimum $($performanceGate.minimumDurationMilliseconds) ms; median $($performanceGate.medianDurationMilliseconds) ms; maximum peak $($performanceGate.maximumPeakWorkingSetBytes) bytes; frozen limits 21000 ms / 274726912 bytes)."
Write-Output "Comparison baseline statistic consistency: PASS (3 samples; minimum $($comparisonBaselineGate.minimumDurationMilliseconds) ms; median $($comparisonBaselineGate.medianDurationMilliseconds) ms; maximum peak $($comparisonBaselineGate.maximumPeakWorkingSetBytes) bytes)."

Write-Output 'Evaluation metrics summary consistency: PASS'

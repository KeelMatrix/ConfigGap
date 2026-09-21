[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Get-Location).Path,
    [string]$ReportPath
)

$repo = (Resolve-Path -LiteralPath $RepositoryRoot).Path
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $repo 'research/phase0b/REPORT.md'
}

$report = (Resolve-Path -LiteralPath $ReportPath).Path
$reportContent = Get-Content -Raw -LiteralPath $report
$head = (& git -C $repo rev-parse HEAD 2>&1).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
    throw "CONFIGGAP_EVIDENCE_HEAD: could not resolve a full HEAD SHA for '$repo'."
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

$codeCandidate = (Get-ExactlyOneMatch $reportContent '(?im)^\s*Code candidate ref:\s*([0-9a-f]{40})\s*$' 'CONFIGGAP_EVIDENCE_CANDIDATE' 'Code candidate ref line').Groups[1].Value.ToLowerInvariant()
$evidenceCheckpoint = (Get-ExactlyOneMatch $reportContent '(?im)^\s*Evidence checkpoint ref:\s*([0-9a-f]{40})\s*$' 'CONFIGGAP_EVIDENCE_CHECKPOINT' 'Evidence checkpoint ref line').Groups[1].Value.ToLowerInvariant()
$resolvedCodeCandidate = (& git -C $repo rev-parse --verify "$codeCandidate^{commit}" 2>&1).Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $resolvedCodeCandidate -ne $codeCandidate) {
    throw "CONFIGGAP_EVIDENCE_CANDIDATE: code candidate ref '$codeCandidate' does not resolve to the named repository commit."
}
$resolvedEvidenceCheckpoint = (& git -C $repo rev-parse --verify "$evidenceCheckpoint^{commit}" 2>&1).Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $resolvedEvidenceCheckpoint -ne $evidenceCheckpoint) {
    throw "CONFIGGAP_EVIDENCE_CHECKPOINT: evidence checkpoint ref '$evidenceCheckpoint' does not resolve to the named repository commit."
}
& git -C $repo merge-base --is-ancestor $codeCandidate $evidenceCheckpoint 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "CONFIGGAP_EVIDENCE_CHECKPOINT: code candidate '$codeCandidate' must be an ancestor of evidence checkpoint '$evidenceCheckpoint'."
}
& git -C $repo merge-base --is-ancestor $evidenceCheckpoint $head 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "CONFIGGAP_EVIDENCE_CHECKPOINT: evidence checkpoint '$evidenceCheckpoint' is not an ancestor of repository HEAD '$head'."
}
$evidenceCommitMessage = (& git -C $repo log -1 --format=%s $evidenceCheckpoint 2>&1).Trim()
$evidenceChangedFiles = @(& git -C $repo diff-tree --no-commit-id --name-only -r $evidenceCheckpoint 2>&1 | Where-Object { $_.Trim().Length -gt 0 })
$allowedEvidenceFiles = @(
    'research/phase0b/REPORT.md',
    'research/phase0b/metrics.json',
    'research/phase0b/performance.json',
    'research/phase0b/env-prevalence.json',
    'scripts/Test-Phase0BEvidence.ps1'
)

if ($evidenceCommitMessage -ne 'docs: refresh Phase 0B evidence' -or
    $evidenceChangedFiles.Count -eq 0 -or
    @($evidenceChangedFiles | Where-Object { $allowedEvidenceFiles -notcontains $_.Trim() }).Count -gt 0) {
    throw "CONFIGGAP_EVIDENCE_CHECKPOINT: evidence checkpoint '$evidenceCheckpoint' is not the expected evidence-only commit."
}

$metricsPath = Join-Path $repo 'research/phase0b/metrics.json'
if (-not (Test-Path -LiteralPath $metricsPath)) {
    throw "CONFIGGAP_EVIDENCE_METRICS: metrics evidence is missing: $metricsPath"
}

$metrics = Get-Content -Raw -LiteralPath $metricsPath | ConvertFrom-Json
if ($metrics.version -ne 1) {
    throw "CONFIGGAP_EVIDENCE_METRICS: unsupported metrics version in '$metricsPath'."
}

function Assert-ReportMetric {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)]$ReportValue,
        [Parameter(Mandatory = $true)]$MetricValue
    )

    try {
        if ([decimal]$ReportValue -eq [decimal]$MetricValue) {
            return
        }
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

$dynamicBlocking = Get-IntMatch $reportContent '(?im)^\s*Dynamic blocking findings:\s*(\d+)\s*$' 1 'Dynamic blocking findings'
$loadFailures = Get-IntMatch $reportContent '(?im)^\s*Load failures:\s*(\d+)\s*$' 1 'Load failures'
Assert-ReportMetric 'dynamic blocking findings' $dynamicBlocking $metrics.dynamicBlockingFindings
Assert-ReportMetric 'load failures' $loadFailures $metrics.loadFailureCount

$verdict = (Get-ExactlyOneMatch $reportContent '(?im)^\s*Verdict:\s*(PASS|FAIL)\s*$' 'CONFIGGAP_EVIDENCE_SUMMARY' 'Verdict summary line').Groups[1].Value
Assert-ReportMetric 'verdict' $verdict $metrics.verdict

Write-Output "Code candidate ref: $codeCandidate"
Write-Output "Evidence checkpoint ref: $evidenceCheckpoint"
Write-Output "Metrics summary consistency: PASS"
Write-Output "Evidence candidate consistency: PASS (code candidate -> evidence checkpoint -> report anchor; HEAD $head)"

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
$head = (& git -C $repo rev-parse HEAD 2>&1).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
    throw "CONFIGGAP_EVIDENCE_HEAD: could not resolve a full HEAD SHA for '$repo'."
}

$matches = [regex]::Matches((Get-Content -Raw -LiteralPath $report), '(?im)^\s*Candidate ref:\s*([0-9a-f]{40})\s*$')
if ($matches.Count -ne 1) {
    throw "CONFIGGAP_EVIDENCE_CANDIDATE: '$report' must contain exactly one 'Candidate ref:' line with a full commit SHA."
}

$candidate = $matches[0].Groups[1].Value.ToLowerInvariant()
$parent = (& git -C $repo rev-parse HEAD^ 2>&1).Trim()
$commitMessage = (& git -C $repo log -1 --format=%s 2>&1).Trim()
$changedFiles = @(& git -C $repo diff-tree --no-commit-id --name-only -r HEAD 2>&1 | Where-Object { $_.Trim().Length -gt 0 })
$allowedEvidenceFiles = @(
    'README.md',
    'research/phase0b/REPORT.md',
    'research/phase0b/metrics.json',
    'research/phase0b/env-prevalence.json',
    'scripts/Test-Phase0BEvidence.ps1'
)
$isEvidenceOnlyCheckpoint =
    $candidate -ceq $parent.ToLowerInvariant() -and
    $commitMessage -ceq 'docs: refresh Phase 0B evidence' -and
    $changedFiles.Count -gt 0 -and
    @($changedFiles | Where-Object { $allowedEvidenceFiles -notcontains $_.Trim() }).Count -eq 0

if ($candidate -cne $head.ToLowerInvariant() -and -not $isEvidenceOnlyCheckpoint) {
    throw "CONFIGGAP_EVIDENCE_STALE: report candidate ref '$candidate' differs from HEAD '$head'."
}

Write-Output "Evidence candidate ref: $candidate"
Write-Output "HEAD: $head"
if ($isEvidenceOnlyCheckpoint) {
    Write-Output "Evidence checkpoint parent: $parent"
    Write-Output 'Evidence candidate consistency: PASS (evidence-only checkpoint)'
}
else {
    Write-Output 'Evidence candidate consistency: PASS'
}

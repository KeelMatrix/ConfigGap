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
if ($candidate -cne $head.ToLowerInvariant()) {
    throw "CONFIGGAP_EVIDENCE_STALE: report candidate ref '$candidate' differs from HEAD '$head'."
}

Write-Output "Evidence candidate ref: $candidate"
Write-Output "HEAD: $head"
Write-Output 'Evidence candidate consistency: PASS'

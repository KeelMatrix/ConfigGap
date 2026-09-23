[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ReportPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'research/phase0b/REPORT.md')
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$report = (Resolve-Path -LiteralPath $ReportPath).Path
$text = Get-Content -Raw -LiteralPath $report
$match = [regex]::Match($text, '(?m)^Evaluation source ref:\s*`([^`]+)`\s*$')
if (-not $match.Success) {
    throw 'CONFIGGAP_REPORT_REF_MISSING: REPORT.md must record an evaluation source ref as an exact commit SHA.'
}

$sourceRef = $match.Groups[1].Value.Trim()
$head = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
    throw 'CONFIGGAP_REPORT_REF_GIT_FAILURE: could not resolve HEAD to a full commit SHA.'
}

$resolvedRef = if ($sourceRef -match '^[0-9a-f]{40}$') {
    $resolveOutput = (& git -C $repo rev-parse --verify "$($sourceRef.ToLowerInvariant())^{commit}" 2>&1 | Out-String).Trim().ToLowerInvariant()
    $resolveExit = $LASTEXITCODE
    if ($resolveExit -ne 0) {
        throw "CONFIGGAP_REPORT_REF_UNRESOLVED: evaluation source ref '$sourceRef' does not resolve to a commit in the repository."
    }

    $resolveOutput
}
else {
    throw "CONFIGGAP_REPORT_REF_INVALID: unsupported evaluation source ref '$sourceRef'. Use a full commit SHA."
}

if ($resolvedRef -ne $sourceRef.ToLowerInvariant()) {
    throw "CONFIGGAP_REPORT_REF_UNRESOLVED: evaluation source ref '$sourceRef' does not resolve to a commit in the repository."
}

& git -C $repo merge-base --is-ancestor $resolvedRef $head 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "CONFIGGAP_REPORT_REF_NOT_ANCESTOR: evaluation source ref '$sourceRef' is not an ancestor of HEAD '$head'."
}

Write-Output "Evaluation source ref: $sourceRef"
Write-Output "HEAD: $head"
Write-Output 'Evaluation source ref consistency: PASS (source ref resolves and is an ancestor of HEAD)'

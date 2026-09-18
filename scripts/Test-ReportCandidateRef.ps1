[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ReportPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'research/phase0b/REPORT.md')
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$report = (Resolve-Path -LiteralPath $ReportPath).Path
$text = Get-Content -Raw -LiteralPath $report
$match = [regex]::Match($text, '(?m)^Recorded candidate ref:\s*`([^`]+)`\s*$')
if (-not $match.Success) {
    throw 'CONFIGGAP_REPORT_REF_MISSING: REPORT.md must record a candidate ref as `HEAD` or an exact commit SHA.'
}

$recordedRef = $match.Groups[1].Value.Trim()
$head = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
    throw 'CONFIGGAP_REPORT_REF_GIT_FAILURE: could not resolve HEAD to a full commit SHA.'
}

$resolvedRef = if ($recordedRef -eq 'HEAD') {
    $head
}
elseif ($recordedRef -match '^[0-9a-f]{40}$') {
    $recordedRef.ToLowerInvariant()
}
else {
    throw "CONFIGGAP_REPORT_REF_INVALID: unsupported recorded candidate ref '$recordedRef'. Use `HEAD` or a full commit SHA."
}

if ($resolvedRef -ne $head) {
    throw "CONFIGGAP_REPORT_REF_MISMATCH: report records '$recordedRef', but HEAD is '$head'."
}

Write-Output "Report candidate ref: $recordedRef"
Write-Output "HEAD: $head"
Write-Output 'Report candidate ref consistency: PASS'

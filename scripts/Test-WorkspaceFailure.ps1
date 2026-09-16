[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$output = Join-Path $repo 'artifacts/workspace-failure-regression.json'
$raw = & dotnet run --project (Join-Path $repo 'tools/ConfigGap.Probe/ConfigGap.Probe.csproj') -c Release --no-build -- `
    --analyze-only `
    --repository-root $repo `
    --solution (Join-Path $repo 'fixtures/FixtureBroken/FixtureBroken.sln') `
    --output $output 2>&1
$exitCode = $LASTEXITCODE
$text = ($raw -join [Environment]::NewLine)
if ($exitCode -eq 0) {
    throw 'Workspace failure regression did not fail the deliberately broken project.'
}
if ($text -notmatch 'CONFIGGAP_COMPILATION_LOAD_FAILURE') {
    throw "Workspace failure regression failed without the named actionable error: $text"
}
Write-Output 'Workspace failure regression passed: broken compilation failed closed with CONFIGGAP_COMPILATION_LOAD_FAILURE.'

[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ScratchRoot = (Join-Path $env:TEMP "configgap-phase0b-$PID"),
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'research/phase0b/metrics.json'),
    [switch]$SkipRestore
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$indexPath = Join-Path $repo 'research/phase0b/corpus-index.json'
$labelsRoot = Join-Path $repo 'research/phase0b/labels'
$probeProject = Join-Path $repo 'tools/ConfigGap.Probe/ConfigGap.Probe.csproj'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
$clonesRoot = Join-Path $scratch 'corpus-clones'
$output = [IO.Path]::GetFullPath($OutputPath)

if (-not (Test-Path -LiteralPath $indexPath)) { throw "Corpus index not found: $indexPath" }
if (-not (Test-Path -LiteralPath $labelsRoot)) { throw "Corpus labels not found: $labelsRoot" }
if (-not (Test-Path -LiteralPath $probeProject)) { throw "Probe project not found: $probeProject" }

New-Item -ItemType Directory -Force -Path $clonesRoot | Out-Null
$index = Get-Content -Raw -LiteralPath $indexPath | ConvertFrom-Json
$clock = [Diagnostics.Stopwatch]::StartNew()
try {
    foreach ($repository in $index.repositories) {
        $target = Join-Path $clonesRoot $repository.id
        if (Test-Path -LiteralPath $target) {
            Remove-Item -LiteralPath $target -Recurse -Force
        }

        Write-Output "Cloning $($repository.id) at $($repository.commitSha)"
        & git clone --depth 1 --no-tags --no-checkout $repository.url $target 2>&1 | Write-Output
        if ($LASTEXITCODE -ne 0) { throw "Clone failed for $($repository.id) with exit code $LASTEXITCODE." }

        & git -C $target cat-file -e "$($repository.commitSha)^{commit}" 2>$null
        if ($LASTEXITCODE -ne 0) {
            & git -C $target fetch --depth 1 origin $repository.commitSha 2>&1 | Write-Output
            if ($LASTEXITCODE -ne 0) { throw "Commit fetch failed for $($repository.id) with exit code $LASTEXITCODE." }
        }

        & git -C $target checkout --detach $repository.commitSha 2>&1 | Write-Output
        if ($LASTEXITCODE -ne 0) { throw "Commit checkout failed for $($repository.id) with exit code $LASTEXITCODE." }

        $actualSha = (& git -C $target rev-parse HEAD).Trim()
        if ($actualSha -ne $repository.commitSha) {
            throw "Commit mismatch for $($repository.id): expected $($repository.commitSha), got $actualSha."
        }

        $status = ((& git -C $target status --porcelain) -join "`n").Trim()
        if ($status) { throw "Fresh clone is not clean for $($repository.id): $status" }

        $labelPath = Join-Path $labelsRoot ($repository.id + '.json')
        $label = Get-Content -Raw -LiteralPath $labelPath | ConvertFrom-Json
        $solutionPath = [IO.Path]::GetFullPath((Join-Path $target $label.solution))
        if (-not $SkipRestore) {
            Write-Output "Restoring $($repository.id) project assets"
            & dotnet restore $solutionPath --ignore-failed-sources --nologo --verbosity quiet 2>&1 | Write-Output
            if ($LASTEXITCODE -ne 0) { throw "Restore failed for $($repository.id) with exit code $LASTEXITCODE." }
        }
    }

    $probeClock = [Diagnostics.Stopwatch]::StartNew()
    $raw = & dotnet run --project $probeProject -c Release --no-build -- `
        --corpus-index $indexPath `
        --labels-root $labelsRoot `
        --clones-root $clonesRoot `
        --output $output 2>&1
    $probeExit = $LASTEXITCODE
    $probeClock.Stop()
    $raw | Write-Output
    Write-Output ("Restore skipped: {0}" -f [bool]$SkipRestore)
    Write-Output ("Metric command duration: {0} ms" -f $probeClock.ElapsedMilliseconds)
    if ($probeExit -ne 0) { exit $probeExit }
}
finally {
    $clock.Stop()
    if (Test-Path -LiteralPath $clonesRoot) {
        Remove-Item -LiteralPath $clonesRoot -Recurse -Force
    }
    Write-Output ("Total corpus command duration: {0} ms" -f $clock.ElapsedMilliseconds)
    Write-Output ("Scratch clones present after cleanup: {0}" -f (Test-Path -LiteralPath $clonesRoot))
}

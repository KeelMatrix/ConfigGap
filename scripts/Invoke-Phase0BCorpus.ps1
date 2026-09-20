[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ScratchRoot = (Join-Path $env:TEMP "configgap-phase0b-$PID"),
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'research/phase0b/metrics.json'),
    [string]$EnvEvidencePath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'research/phase0b/env-prevalence.json'),
    [switch]$SkipRestore,
    [int]$RestoreTimeoutSeconds = 600,
    [int]$CommandTimeoutSeconds = 300,
    [int]$OverallTimeoutSeconds = 3600
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$indexPath = Join-Path $repo 'research/phase0b/corpus-index.json'
$labelsRoot = Join-Path $repo 'research/phase0b/labels'
$probeProject = Join-Path $repo 'tools/ConfigGap.Probe/ConfigGap.Probe.csproj'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
$clonesRoot = Join-Path $scratch 'corpus-clones'
$preflightPath = Join-Path $scratch 'preflight-failures.json'
$output = [IO.Path]::GetFullPath($OutputPath)
$envEvidenceOutput = [IO.Path]::GetFullPath($EnvEvidencePath)
$cloneTimeoutSeconds = 600
$longPathsEnabled = $null
$configuredGitLongPathsValue = ''
$effectiveGitLongPathsValue = 'not-applicable'
$gitCommandPrefix = @('-c', 'http.version=HTTP/1.1')
$exercisedLongPathMode = 'platform-default'

if ($RestoreTimeoutSeconds -lt 1 -or $CommandTimeoutSeconds -lt 1 -or $OverallTimeoutSeconds -lt 1) {
    throw 'CONFIGGAP_POLICY_INVALID: timeout values must be positive.'
}

if (-not (Test-Path -LiteralPath $indexPath)) { throw "Corpus index not found: $indexPath" }
if (-not (Test-Path -LiteralPath $labelsRoot)) { throw "Corpus labels not found: $labelsRoot" }
if (-not (Test-Path -LiteralPath $probeProject)) { throw "Probe project not found: $probeProject" }

if ($IsWindows) {
    $longPathsEnabled = (Get-ItemPropertyValue 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem' -Name LongPathsEnabled -ErrorAction SilentlyContinue) -eq 1
    $gitLongPathsSetting = ((& git config --get core.longpaths 2>$null) -join "`n")
    if ($null -eq $gitLongPathsSetting) {
        $gitLongPathsSetting = ''
    }
    $gitLongPathsValues = @($gitLongPathsSetting -split '[\r\n]+' | Where-Object { $_.Trim().Length -gt 0 })
    $configuredGitLongPathsValue = if ($gitLongPathsValues.Count -eq 0) { '' } else { $gitLongPathsValues[-1].Trim() }
    if ($longPathsEnabled) {
        $gitCommandPrefix += @('-c', 'core.longpaths=true')
        $effectiveGitLongPathsValue = 'true'
        $exercisedLongPathMode = 'script-owned-core.longpaths'
    }
    else {
        $effectiveGitLongPathsValue = if ($configuredGitLongPathsValue.Length -eq 0) { '<unset>' } else { $configuredGitLongPathsValue }
        $exercisedLongPathMode = 'host-configured'
    }
    Write-Output ("Windows long-path preflight: OS={0}; LongPathsEnabled={1}; Git configured core.longpaths={2}; effective core.longpaths={3}; scratchRootLength={4}; mode={5}" -f `
        $true, $longPathsEnabled, $(if ($configuredGitLongPathsValue.Length -eq 0) { '<unset>' } else { $configuredGitLongPathsValue }), $effectiveGitLongPathsValue, $scratch.Length, $exercisedLongPathMode)
}
else {
    Write-Output ("Non-Windows path preflight: LongPathsEnabled={0}; effective core.longpaths={1}; scratchRootLength={2}; mode={3}" -f `
        '<not-applicable>', $effectiveGitLongPathsValue, $scratch.Length, $exercisedLongPathMode)
}

$deadline = [DateTime]::UtcNow.AddSeconds($OverallTimeoutSeconds)

function Assert-RunWithinDeadline {
    if ([DateTime]::UtcNow -ge $deadline) {
        throw "CONFIGGAP_CORPUS_TIMEOUT: overall corpus run exceeded the $OverallTimeoutSeconds-second limit. Use a fresh scratch root and review restore/load duration."
    }
}

function Invoke-BoundedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
        [string]$WorkingDirectory = $repo
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    Assert-RunWithinDeadline
    $remainingMilliseconds = [int][Math]::Floor(($deadline - [DateTime]::UtcNow).TotalMilliseconds)
    if ($remainingMilliseconds -lt 1) {
        throw "CONFIGGAP_CORPUS_TIMEOUT: overall corpus run exceeded the $OverallTimeoutSeconds-second limit."
    }
    $waitMilliseconds = [Math]::Min($TimeoutSeconds * 1000, $remainingMilliseconds)

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Could not start $FilePath."
        }

        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($waitMilliseconds)) {
            try { $process.Kill($true) } catch { }
            if ($waitMilliseconds -lt $TimeoutSeconds * 1000) {
                throw "CONFIGGAP_CORPUS_TIMEOUT: overall corpus run exceeded the $OverallTimeoutSeconds-second limit while running $FilePath."
            }
            throw "CONFIGGAP_COMMAND_TIMEOUT: $FilePath exceeded the $TimeoutSeconds-second limit."
        }

        [pscustomobject]@{
            ExitCode = $process.ExitCode
            StandardOutput = $standardOutput.GetAwaiter().GetResult()
            StandardError = $standardError.GetAwaiter().GetResult()
        }
    }
    finally {
        $process.Dispose()
    }
}

function Invoke-GitCommand {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
        [string]$WorkingDirectory = $repo
    )

    $gitArguments = @()
    $gitArguments += $gitCommandPrefix
    $gitArguments += $Arguments
    Invoke-BoundedCommand 'git' $gitArguments $TimeoutSeconds -WorkingDirectory $WorkingDirectory
}

function Write-CommandOutput {
    param([Parameter(Mandatory = $true)]$Result)
    if ($Result.StandardOutput) {
        $standardOutput = $Result.StandardOutput.TrimEnd()
        Write-Output $(if ($standardOutput.Length -gt 4000) { $standardOutput.Substring(0, 4000) + ' ...[output truncated]' } else { $standardOutput })
    }
    if ($Result.StandardError) {
        $standardError = $Result.StandardError.TrimEnd()
        Write-Output $(if ($standardError.Length -gt 4000) { $standardError.Substring(0, 4000) + ' ...[output truncated]' } else { $standardError })
    }
}

function Compact-Error {
    param([Parameter(Mandatory = $true)][string]$Text)
    $compact = ($Text -replace '[\r\n]+', ' ').Trim()
    $compact = [regex]::Replace($compact, '(?i)[a-z]:\\[^\s''"]+', '<path>')
    $compact = [regex]::Replace($compact, '(?i)(?<![a-z0-9])/(?:[^\s/]+/)+[^\s]+', '<path>')
    if ($compact.Length -gt 500) { return $compact.Substring(0, 500) }
    return $compact
}

function Add-PreflightMetrics {
    if (-not (Test-Path -LiteralPath $output)) {
        throw "CONFIGGAP_EVIDENCE_METRICS: corpus analysis did not produce metrics at '$output'."
    }

    $metrics = Get-Content -Raw -LiteralPath $output | ConvertFrom-Json
    $metrics | Add-Member -NotePropertyName corpusPreflight -NotePropertyValue ([pscustomobject][ordered]@{
        mode = $exercisedLongPathMode
        longPathsEnabled = $longPathsEnabled
        configuredCoreLongpaths = if ($configuredGitLongPathsValue.Length -eq 0) { $null } else { $configuredGitLongPathsValue }
        effectiveCoreLongpaths = $effectiveGitLongPathsValue
        scratchRootLength = $scratch.Length
    }) -Force
    $metrics | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $output -Encoding utf8
    Write-Output ("Metrics preflight metadata: mode={0}; effective core.longpaths={1}; LongPathsEnabled={2}; scratchRootLength={3}" -f `
        $exercisedLongPathMode, $effectiveGitLongPathsValue, $(if ($null -eq $longPathsEnabled) { '<not-applicable>' } else { $longPathsEnabled }), $scratch.Length)
}

if (Test-Path -LiteralPath $clonesRoot) {
    Remove-Item -LiteralPath $clonesRoot -Recurse -Force
    if (Test-Path -LiteralPath $clonesRoot) {
        throw "CONFIGGAP_SCRATCH_CLEANUP_FAILURE: could not remove the previous corpus clone root '$clonesRoot'. Use a new clean scratch root and retry."
    }
}
New-Item -ItemType Directory -Force -Path $clonesRoot | Out-Null
$index = Get-Content -Raw -LiteralPath $indexPath | ConvertFrom-Json
$preflightFailures = [ordered]@{}
$environmentTemplateEvidence = [System.Collections.Generic.List[object]]::new()
$clock = [Diagnostics.Stopwatch]::StartNew()
$probeExitCode = 0
try {
    foreach ($repository in $index.repositories) {
        Assert-RunWithinDeadline
        $target = Join-Path $clonesRoot $repository.id
        if (Test-Path -LiteralPath $target) {
            $resolvedTarget = [IO.Path]::GetFullPath($target)
            if (-not $resolvedTarget.StartsWith($clonesRoot, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to remove a path outside the corpus scratch root: $resolvedTarget"
            }
            Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
        }

        Write-Output "Preflight cloning $($repository.id) at $($repository.commitSha)"
        $clone = Invoke-GitCommand @('clone', '--depth', '1', '--no-tags', '--no-checkout', $repository.url, $target) $cloneTimeoutSeconds
        Write-CommandOutput $clone
        if ($clone.ExitCode -ne 0) {
            throw "CONFIGGAP_CORPUS_CLONE_PREFLIGHT_FAILURE: clone failed for $($repository.id) with exit code $($clone.ExitCode)."
        }

        $commitCheck = Invoke-GitCommand @('-C', $target, 'cat-file', '-e', "$($repository.commitSha)^{commit}") 30
        if ($commitCheck.ExitCode -ne 0) {
            $fetch = Invoke-GitCommand @('-C', $target, 'fetch', '--depth', '1', 'origin', $repository.commitSha) 120
            Write-CommandOutput $fetch
            if ($fetch.ExitCode -ne 0) { throw "CONFIGGAP_CORPUS_CLONE_PREFLIGHT_FAILURE: commit fetch failed for $($repository.id) with exit code $($fetch.ExitCode)." }
        }

        $checkout = Invoke-GitCommand @('-C', $target, 'checkout', '--detach', $repository.commitSha) 60
        Write-CommandOutput $checkout
        if ($checkout.ExitCode -ne 0) { throw "CONFIGGAP_CORPUS_CLONE_PREFLIGHT_FAILURE: commit checkout failed for $($repository.id) with exit code $($checkout.ExitCode)." }

        $actualSha = (Invoke-GitCommand @('-C', $target, 'rev-parse', 'HEAD') 30).StandardOutput.Trim()
        if ($actualSha -ne $repository.commitSha) {
            throw "CONFIGGAP_CORPUS_CLONE_PREFLIGHT_FAILURE: commit mismatch for $($repository.id): expected $($repository.commitSha), got $actualSha."
        }

        $status = (Invoke-GitCommand @('-C', $target, 'status', '--porcelain') 30).StandardOutput.Trim()
        if ($status) { throw "CONFIGGAP_CORPUS_CLONE_PREFLIGHT_FAILURE: fresh clone is not clean for $($repository.id): $status" }
        Write-Output "Clone preflight passed: $($repository.id) at $actualSha"
    }

    Write-Output "Pinned corpus clone preflight passed for $(@($index.repositories).Count) repositories; beginning restore and analysis preparation."
    foreach ($repository in $index.repositories) {
        Assert-RunWithinDeadline
        $target = Join-Path $clonesRoot $repository.id

        $labelPath = Join-Path $labelsRoot ($repository.id + '.json')
        $label = Get-Content -Raw -LiteralPath $labelPath | ConvertFrom-Json
        $solutionPath = [IO.Path]::GetFullPath((Join-Path $target $label.solution))
        $selectedProjectDirectory = Split-Path -Parent $label.solution
        $selectedProjectDirectory = $selectedProjectDirectory.Replace('\', '/')
        $templateFiles = Get-ChildItem -LiteralPath $target -Recurse -File -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^(\.env|env)(\.(example|template|sample|dist))?$' }
        foreach ($templateFile in $templateFiles) {
            $relativeTemplatePath = [IO.Path]::GetRelativePath($target, $templateFile.FullName).Replace('\', '/')
            $templateDirectory = (Split-Path -Parent $relativeTemplatePath).Replace('\', '/')
            $belongsToSelectedApplication =
                $templateDirectory.Equals($selectedProjectDirectory, [StringComparison]::OrdinalIgnoreCase) -or
                $templateDirectory.StartsWith($selectedProjectDirectory + '/', [StringComparison]::OrdinalIgnoreCase)
            $environmentTemplateEvidence.Add([ordered]@{
                repositoryId = $repository.id
                path = $relativeTemplatePath
                kind = if ($templateFile.Name -ieq '.env.example') { '.env.example' } elseif ($templateFile.Name -ieq '.env') { 'actual-env-excluded' } else { 'explicit-template-equivalent' }
                belongsToSelectedApplicationDeclarationPolicy = $belongsToSelectedApplication
            })
        }
        if (-not $SkipRestore) {
            Write-Output "Restoring $($repository.id) project assets (timeout ${RestoreTimeoutSeconds}s; overall run bound ${OverallTimeoutSeconds}s)"
            try {
                $restore = Invoke-BoundedCommand 'dotnet' @('restore', $solutionPath, '--ignore-failed-sources', '--disable-parallel', '-m:1', '--nologo', '--verbosity', 'quiet') $RestoreTimeoutSeconds -WorkingDirectory (Split-Path -Parent $solutionPath)
                Write-CommandOutput $restore
                if ($restore.ExitCode -ne 0) {
                    $preflightFailures[$repository.id] = "Restore exited $($restore.ExitCode): $(Compact-Error ($restore.StandardError + ' ' + $restore.StandardOutput))"
                }
            }
            catch {
                if ($_.Exception.Message.StartsWith('CONFIGGAP_CORPUS_TIMEOUT:', [StringComparison]::Ordinal)) {
                    throw
                }
                $preflightFailures[$repository.id] = (Compact-Error $_.Exception.Message)
            }
        }
    }

    if ($preflightFailures.Count -eq 0) {
        '{}' | Set-Content -LiteralPath $preflightPath -Encoding utf8
    }
    else {
        $preflightFailures | ConvertTo-Json | Set-Content -LiteralPath $preflightPath -Encoding utf8
    }
    $envExampleRepositories = @($environmentTemplateEvidence |
        Where-Object { $_.kind -eq '.env.example' } |
        ForEach-Object { $_.repositoryId } |
        Sort-Object -Unique)
    $envEvidence = [ordered]@{
        version = 1
        method = 'Enumerate repository-wide file names only at each pinned commit; do not read .env or template contents. Classify selected application membership by the directory of the labeled project.'
        repositoryCount = @($index.repositories).Count
        envExampleRepositoryCount = $envExampleRepositories.Count
        templateFileCount = $environmentTemplateEvidence.Count
        files = @($environmentTemplateEvidence | Sort-Object repositoryId, path)
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $envEvidenceOutput) | Out-Null
    $envEvidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $envEvidenceOutput -Encoding utf8
    Write-Output "Environment template files found: $($environmentTemplateEvidence.Count); repositories with .env.example: $($envEvidence.envExampleRepositoryCount)/$($envEvidence.repositoryCount)"
    $probeArguments = @(
        'run', '--project', $probeProject, '-c', 'Release', '--no-build', '--',
        '--corpus-index', $indexPath,
        '--labels-root', $labelsRoot,
        '--clones-root', $clonesRoot,
        '--preflight-failures', $preflightPath,
        '--output', $output
    )
    $probeClock = [Diagnostics.Stopwatch]::StartNew()
    $probe = Invoke-BoundedCommand 'dotnet' $probeArguments $CommandTimeoutSeconds
    $probeClock.Stop()
    Write-CommandOutput $probe
    Write-Output ("Restore skipped: {0}" -f [bool]$SkipRestore)
    Write-Output ("Metric command duration: {0} ms" -f $probeClock.ElapsedMilliseconds)
    if ($probe.ExitCode -ne 0) { $probeExitCode = $probe.ExitCode }
    else { Add-PreflightMetrics }
}
finally {
    $clock.Stop()
    if (Test-Path -LiteralPath $clonesRoot) {
        Remove-Item -LiteralPath $clonesRoot -Recurse -Force
    }
    Write-Output ("Total corpus command duration: {0} ms" -f $clock.ElapsedMilliseconds)
    Write-Output ("Scratch clones present after cleanup: {0}" -f (Test-Path -LiteralPath $clonesRoot))
}
if ($probeExitCode -ne 0) { exit $probeExitCode }

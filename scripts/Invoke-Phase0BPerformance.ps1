[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ScratchRoot = (Join-Path $env:TEMP "configgap-phase0b-benchmark-$PID"),
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'research/phase0b/performance.json'),
    [int]$ProjectCount = 50,
    [int]$RunCount = 5
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Phase0BPerformanceGate.psm1') -Force
if ($RunCount -lt 5) {
    throw 'CONFIGGAP_PERFORMANCE_POLICY_INVALID: the gated benchmark requires at least five guarded runs.'
}
if ($ProjectCount -lt 1) {
    throw 'CONFIGGAP_PERFORMANCE_POLICY_INVALID: project count must be positive.'
}

$repo = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
$synthetic = Join-Path $scratch 'synthetic-50'
$probeProject = Join-Path $repo 'tools/ConfigGap.Probe/ConfigGap.Probe.csproj'
$fixtureManifestPath = Join-Path $repo 'fixtures/expected.json'
$output = [IO.Path]::GetFullPath($OutputPath)
$gateBaseline = if (Test-Path -LiteralPath $output -PathType Leaf) {
    Get-Content -Raw -LiteralPath $output | ConvertFrom-Json
}
else {
    throw "CONFIGGAP_PERFORMANCE_BASELINE_MISSING: the committed frozen baseline is required at '$output'."
}
if ($null -eq $gateBaseline.frozenV1Baseline) {
    throw "CONFIGGAP_PERFORMANCE_BASELINE_INVALID: frozenV1Baseline is missing from '$output'."
}
if (-not (Test-Path -LiteralPath $probeProject)) {
    throw "Probe project not found: $probeProject"
}
if (-not (Test-Path -LiteralPath $fixtureManifestPath)) {
    throw "Fixture manifest not found: $fixtureManifestPath"
}

$fixtureManifest = Get-Content -Raw -LiteralPath $fixtureManifestPath | ConvertFrom-Json
if ($fixtureManifest.version -ne 1 -or @($fixtureManifest.patterns).Count -lt 1) {
    throw "CONFIGGAP_PERFORMANCE_FIXTURE_MANIFEST: unsupported or empty fixture manifest: $fixtureManifestPath"
}

$projectFixturePrefix = 'fixtures/FixtureConsumer/Patterns/'
$projectFixturePatterns = @($fixtureManifest.patterns | Where-Object { $_.source.StartsWith($projectFixturePrefix, [StringComparison]::OrdinalIgnoreCase) })
$sharedFixturePatterns = @($fixtureManifest.patterns | Where-Object { -not $_.source.StartsWith($projectFixturePrefix, [StringComparison]::OrdinalIgnoreCase) })
$projectFixtureObservationCount = [int](($projectFixturePatterns | ForEach-Object {
    if ($null -eq $_.expectedObservationCount) { 1 } else { [int]$_.expectedObservationCount }
} | Measure-Object -Sum).Sum)
$sharedFixtureObservationCount = [int](($sharedFixturePatterns | ForEach-Object {
    if ($null -eq $_.expectedObservationCount) { 1 } else { [int]$_.expectedObservationCount }
} | Measure-Object -Sum).Sum)
$expectedObservations = [int](($projectFixtureObservationCount * $ProjectCount) + $sharedFixtureObservationCount)
if ($expectedObservations -lt 1) {
    throw 'CONFIGGAP_PERFORMANCE_FIXTURE_MANIFEST: derived observation count must be positive.'
}
if (Test-Path -LiteralPath $scratch) {
    throw "Benchmark scratch root already exists; use a new clean root: $scratch"
}

function Invoke-Dotnet {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [string]$WorkingDirectory = $repo
    )

    $clock = [Diagnostics.Stopwatch]::StartNew()
    $raw = & dotnet @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $clock.Stop()
    $tail = (($raw | ForEach-Object { $_.ToString() } | Select-Object -Last 12) -join [Environment]::NewLine)
    $tail = $tail.Replace($repo, '<repo>', [StringComparison]::OrdinalIgnoreCase).Replace($scratch, '<scratch>', [StringComparison]::OrdinalIgnoreCase)
    if ($exitCode -ne 0) {
        throw "CONFIGGAP_PERFORMANCE_COMMAND_FAILURE: dotnet $($Arguments -join ' ') exited $exitCode.`n$tail"
    }

    [pscustomobject]@{
        DurationMilliseconds = [long][Math]::Round($clock.Elapsed.TotalMilliseconds, 0)
        OutputTail = $tail
    }
}

New-Item -ItemType Directory -Force -Path $scratch | Out-Null
try {
    Write-Output "Generating one clean $ProjectCount-project synthetic solution."
    Write-Output "Derived expected observation count: $expectedObservations ($projectFixtureObservationCount per generated consumer project; $sharedFixtureObservationCount shared fixture observations)."
    [void](Invoke-Dotnet @(
        'run', '--project', $probeProject, '-c', 'Release', '--no-build', '--',
        '--generate-synthetic', $synthetic,
        '--repository-root', $repo,
        '--project-count', $ProjectCount.ToString([Globalization.CultureInfo]::InvariantCulture)))

    Write-Output 'Restoring the generated solution once before guarded measurements.'
    [void](Invoke-Dotnet @('restore', (Join-Path $synthetic 'ConfigGap.Synthetic.sln'), '--nologo', '--verbosity', 'quiet', '--ignore-failed-sources', '-p:NuGetAudit=false'))

    $runs = [System.Collections.Generic.List[object]]::new()
    for ($run = 1; $run -le $RunCount; $run++) {
        $resultPath = Join-Path $scratch "performance-$run.json"
        $result = Invoke-Dotnet @(
            'run', '--project', $probeProject, '-c', 'Release', '--no-build', '--',
            '--analyze-only',
            '--repository-root', $synthetic,
            '--solution', (Join-Path $synthetic 'ConfigGap.Synthetic.sln'),
            '--expected-observations', $expectedObservations.ToString([Globalization.CultureInfo]::InvariantCulture),
            '--output', $resultPath)
        $measurement = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
        if ($measurement.observationCount -ne $expectedObservations) {
            throw "CONFIGGAP_UNEXPECTED_OBSERVATIONS: run $run expected $expectedObservations observations derived from $fixtureManifestPath, found $($measurement.observationCount)."
        }

        Write-Output "Guarded run $run raw output tail:"
        Write-Output $result.OutputTail
        $runs.Add([ordered]@{
            run = $run
            observationCount = [int]$measurement.observationCount
            declaredSurfaceCount = [int]$measurement.declaredSurfaceCount
            durationMilliseconds = [long]$measurement.durationMilliseconds
            peakWorkingSetBytes = [long]$measurement.peakWorkingSetBytes
        })
        Write-Output "Guarded run ${run}: observations $($measurement.observationCount); duration $($measurement.durationMilliseconds) ms; peak working set $($measurement.peakWorkingSetBytes) bytes"
    }

    $durations = @($runs | ForEach-Object { [double]$_.durationMilliseconds })
    $mean = ($durations | Measure-Object -Average).Average
    $sampleVariance = if ($durations.Count -gt 1) {
        (($durations | ForEach-Object { ($_ - $mean) * ($_ - $mean) } | Measure-Object -Sum).Sum) / ($durations.Count - 1)
    }
    else { 0 }
    $sampleStandardDeviation = [Math]::Sqrt($sampleVariance)
    $maximumDuration = ($durations | Measure-Object -Maximum).Maximum
    $wallClockEstimate = [Math]::Max($maximumDuration, $mean + (2 * $sampleStandardDeviation))
    $wallClockBound = [long]([Math]::Ceiling($wallClockEstimate / 100) * 100)
    $workingSets = @($runs | ForEach-Object { [double]$_.peakWorkingSetBytes })
    $maximumWorkingSet = ($workingSets | Measure-Object -Maximum).Maximum
    $workingSetBound = [long]([Math]::Ceiling(($maximumWorkingSet * 1.10) / 1048576) * 1048576)

    $machine = [ordered]@{
        os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        processorCount = [Environment]::ProcessorCount
        dotnetSdk = (& dotnet --version).Trim()
        powershell = $PSVersionTable.PSVersion.ToString()
    }
    $report = [ordered]@{
        version = 1
        method = 'Generate one clean deterministic solution from the committed fixture corpus, restore it once, derive the expected observation count from fixtures/expected.json, then run the unchanged analyzer at least five times with an exact observation-count guard on every run.'
        protocol = 'The default gated run reports every raw sample and enforces: at least five guarded runs; minimum duration at or below the frozen wall-clock bound; median duration at or below 1.10 times that frozen bound; every peak working set at or below the frozen peak bound; and the exact derived observation count on every run. The 1.10 median headroom tolerates isolated scheduler noise while still detecting sustained slowness. The frozen limits are committed inputs and are never rewritten by this harness. Descriptive run-derived bounds are reported for evidence only and are not acceptance limits. This is a reproducible regression gate for the documented environment class and generated input, not a portable SLA.'
        acceptancePolicy = [ordered]@{
            minimumGuardedRuns = 5
            durationCapabilityRule = 'minimum duration <= frozen wall-clock bound'
            durationSustainedRule = 'median duration <= frozen wall-clock bound * 1.10'
            medianHeadroomFactor = 1.10
            peakWorkingSetRule = 'every raw peak working set <= frozen peak working-set bound'
            observationCountRule = "every run observation count == $expectedObservations"
            comparisonBaselineMinimumSamples = 3
            comparisonBaselineNote = 'A comparison baseline with at least three raw observations may be evaluated against the duration and peak predicates; it is not a substitute for the default five-run gated protocol.'
        }
        comparisonBaselineEvaluation = [ordered]@{
            source = 'Fresh-clone resource comparison baseline'
            observationCounts = @(3302, 3302, 3302)
            durationMilliseconds = @(21274, 19633, 20129)
            peakWorkingSetBytes = @(257155072, 251711488, 252014592)
            minimumDurationMilliseconds = 19633
            medianDurationMilliseconds = 20129
            medianDurationLimitMilliseconds = 23100
            maximumPeakWorkingSetBytes = 257155072
            frozenWallClockBoundMilliseconds = 21000
            frozenPeakWorkingSetBoundBytes = 274726912
            statisticVerdict = 'PASS'
            outcome = 'The comparison baseline satisfies the duration and peak predicates; it is not a substitute for the default five-run gated protocol.'
        }
        projectCount = $ProjectCount
        fixtureManifest = 'fixtures/expected.json'
        projectFixturePatternCount = $projectFixturePatterns.Count
        sharedFixturePatternCount = $sharedFixturePatterns.Count
        expectedObservationCount = $expectedObservations
        runs = @($runs)
        variance = [ordered]@{
            wallClockMeanMilliseconds = [long][Math]::Round($mean, 0)
            wallClockSampleVarianceMillisecondsSquared = [long][Math]::Round($sampleVariance, 0)
            wallClockSampleStandardDeviationMilliseconds = [long][Math]::Round($sampleStandardDeviation, 0)
            maximumWallClockMilliseconds = [long]$maximumDuration
            wallClockMargin = 'two sample standard deviations, with maximum-observation floor'
            derivedWallClockBoundMilliseconds = $wallClockBound
            maximumPeakWorkingSetBytes = [long]$maximumWorkingSet
            peakWorkingSetMargin = '10% above observed maximum'
            derivedPeakWorkingSetBoundBytes = $workingSetBound
        }
        machine = $machine
        frozenV1Baseline = [ordered]@{
            scope = $gateBaseline.frozenV1Baseline.scope
            wallClockBoundMilliseconds = [long]$gateBaseline.frozenV1Baseline.wallClockBoundMilliseconds
            peakWorkingSetBoundBytes = [long]$gateBaseline.frozenV1Baseline.peakWorkingSetBoundBytes
            interpretation = 'Committed V1 acceptance limits. The default harness compares fresh raw samples with these values and never rewrites them.'
        }
    }
    $gate = Get-Phase0BPerformanceGate -Report ([pscustomobject]$report) -ExpectedObservationCount $expectedObservations
    Write-Output "Guarded run count: $($gate.reportedRunCount) (required: at least $($gate.requiredGuardedRuns))"
    Write-Output "Minimum duration: $($gate.minimumDurationMilliseconds) ms; frozen bound: $($gate.frozenWallClockBoundMilliseconds) ms; pass: $($gate.minimumDurationPass)"
    Write-Output "Median duration: $($gate.medianDurationMilliseconds) ms; limit: $($gate.medianDurationLimitMilliseconds) ms (headroom factor $($gate.medianHeadroomFactor)); pass: $($gate.medianDurationPass)"
    Write-Output "Maximum peak working set: $($gate.maximumPeakWorkingSetBytes) bytes; frozen bound: $($gate.peakWorkingSetBoundBytes) bytes; pass: $($gate.peakWorkingSetPass)"
    Write-Output "Observation-count guard: $($gate.observationCountPass) (expected: $($gate.expectedObservationCount))"
    $report.gate = [ordered]@{
        verdict = $gate.verdict
        requiredGuardedRuns = $gate.requiredGuardedRuns
        reportedRunCount = $gate.reportedRunCount
        minimumDurationMilliseconds = $gate.minimumDurationMilliseconds
        medianDurationMilliseconds = $gate.medianDurationMilliseconds
        medianDurationLimitMilliseconds = $gate.medianDurationLimitMilliseconds
        maximumPeakWorkingSetBytes = $gate.maximumPeakWorkingSetBytes
        frozenWallClockBoundMilliseconds = $gate.frozenWallClockBoundMilliseconds
        frozenPeakWorkingSetBoundBytes = $gate.peakWorkingSetBoundBytes
        expectedObservationCount = $gate.expectedObservationCount
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output) | Out-Null
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $output -Encoding utf8
    Write-Output "Derived wall-clock bound: $wallClockBound ms"
    Write-Output "Derived peak working-set bound: $workingSetBound bytes"
    Write-Output "Frozen wall-clock bound: $($gate.frozenWallClockBoundMilliseconds) ms"
    Write-Output "Frozen peak working-set bound: $($gate.peakWorkingSetBoundBytes) bytes"
    Write-Output "Resource gate verdict: $($gate.verdict)"
    Write-Output "Machine-readable report: $output"
    if ($gate.verdict -ne 'PASS') {
        throw "CONFIGGAP_PERFORMANCE_GATE_FAILED: the committed frozen resource gate rejected this run."
    }
}
finally {
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
    Write-Output "Benchmark scratch present after cleanup: $(Test-Path -LiteralPath $scratch)"
}

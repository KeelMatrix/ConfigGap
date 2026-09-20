[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ScratchRoot = (Join-Path $env:TEMP "configgap-phase0b-benchmark-$PID"),
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'research/phase0b/performance.json'),
    [int]$ProjectCount = 50,
    [int]$RunCount = 3
)

$ErrorActionPreference = 'Stop'
if ($RunCount -lt 3) {
    throw 'CONFIGGAP_PERFORMANCE_POLICY_INVALID: the benchmark requires at least three guarded runs.'
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
        method = 'Generate one clean deterministic solution from the committed fixture corpus, restore it once, derive the expected observation count from fixtures/expected.json, then run the unchanged analyzer at least three times with an exact observation-count guard on every run.'
        protocol = 'Wall-clock bound is the greater of the observed maximum and mean plus two sample standard deviations, rounded up to the next 100 ms. Working-set bound is the observed maximum plus a 10% measurement margin, rounded up to the next MiB. These are measured regression bounds for this generated input and machine, not portable SLAs.'
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
            scope = 'The exact generated 50-project solution, exact observation-count guard, and machine description above.'
            wallClockBoundMilliseconds = $wallClockBound
            peakWorkingSetBoundBytes = $workingSetBound
            interpretation = 'Measured regression bound only; rerun the same protocol after material analyzer changes.'
        }
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output) | Out-Null
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $output -Encoding utf8
    Write-Output "Derived wall-clock bound: $wallClockBound ms"
    Write-Output "Derived peak working-set bound: $workingSetBound bytes"
    Write-Output "Machine-readable report: $output"
}
finally {
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
    Write-Output "Benchmark scratch present after cleanup: $(Test-Path -LiteralPath $scratch)"
}

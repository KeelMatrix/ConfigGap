[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Phase0BPerformanceGate.psm1') -Force

function Assert-PerformanceContract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$fixture = Join-Path ([IO.Path]::GetTempPath()) "configgap-performance-negative-$([Guid]::NewGuid().ToString('N'))"
$baselinePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'research/phase0b/performance.json'

try {
    $baseline = Get-Content -Raw -LiteralPath $baselinePath | ConvertFrom-Json
    $runs = 1..5 | ForEach-Object {
        [pscustomobject]@{
            run = $_
            observationCount = 3302
            declaredSurfaceCount = 50
            durationMilliseconds = 21001
            peakWorkingSetBytes = 250000000
        }
    }
    $negative = [ordered]@{
        version = 1
        expectedObservationCount = 3302
        runs = @($runs)
        frozenV1Baseline = $baseline.frozenV1Baseline
    }
    New-Item -ItemType Directory -Path $fixture -Force | Out-Null
    $negativePath = Join-Path $fixture 'performance.json'
    $negative | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $negativePath -Encoding utf8

    $result = Get-Phase0BPerformanceGate -Report ([pscustomobject]$negative) -ExpectedObservationCount 3302
    Assert-PerformanceContract ($result.verdict -eq 'FAIL') 'The deliberately slow resource sample was accepted by the gate.'
    Assert-PerformanceContract (@($result.failureReasons | Where-Object { $_ -match 'minimum duration' }).Count -gt 0) 'The negative resource sample did not report the exceeded duration predicate.'
    Write-Output 'Negative resource gate check: PASS (a five-run sample at 21001 ms was rejected against the frozen 21000 ms capability bound).'
    Write-Output "Raw negative samples: duration 21001/21001/21001/21001/21001 ms; peak 250000000 bytes on every run; observations 3302 on every run."
    Write-Output 'Negative resource gate verdict: FAIL (expected).'
}
finally {
    if (Test-Path -LiteralPath $fixture) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
    Write-Output "Negative-check scratch present after cleanup: $(Test-Path -LiteralPath $fixture)"
}

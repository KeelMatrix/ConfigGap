# ConfigGap analysis-core benchmark

The checked-in performance protocol generates one clean 50-project solution from the fixture corpus, restores it once, and runs the unchanged analyzer three times with an exact observation-count guard. The result is a measured regression bound for this generated input and machine, not a portable service-level agreement.

## Current measurement

The fresh Windows measurement used 51 project fixture patterns, 2 shared fixture patterns, and 2,802 guarded observations on each run:

- durations: 27,472 ms, 23,565 ms, and 26,520 ms;
- peak working sets: 246,468,608 bytes, 240,398,336 bytes, and 243,007,488 bytes;
- derived bound: 30,000 ms wall clock and 271,581,184 bytes peak working set;
- environment: Windows 10.0.19045, x64, 16 processors, .NET SDK 8.0.425, PowerShell 7.6.6.

The machine-readable result is [`research/phase0b/performance.json`](../research/phase0b/performance.json). Linux and macOS measurements are not available for this candidate; the platform matrix and reproducible Docker check are documented in [`platform-support.md`](platform-support.md). The Docker check has not run in CI.

## Reproduce

```powershell
pwsh -NoProfile -File .\scripts\Invoke-Phase0BPerformance.ps1 `
  -RepositoryRoot (Get-Location).Path `
  -ScratchRoot (Join-Path $env:TEMP 'configgap-performance') `
  -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/performance.json')
```

The protocol disables NuGet audit during the measurement restore and ignores unavailable sources so the benchmark measures analyzer behavior rather than advisory-service availability. The separate vulnerability audit remains an independent release gate.

# ConfigGap analysis-core benchmark

The checked-in performance protocol generates one clean 50-project solution from the fixture corpus, restores it once, and runs the unchanged analyzer three times with an exact observation-count guard. The result is a measured regression bound for this generated input and machine, not a portable service-level agreement.

## Current measurement

The fresh Windows measurement used 51 project fixture patterns, 2 shared fixture patterns, and 2,802 guarded observations on each run:

- durations: 25,294 ms, 21,134 ms, and 21,804 ms;
- peak working sets: 241,889,280 bytes, 245,456,896 bytes, and 242,364,416 bytes;
- derived bound: 27,300 ms wall clock and 270,532,608 bytes peak working set;
- environment: Windows 10.0.19045, x64, 16 processors, .NET SDK 8.0.425, PowerShell 7.6.6.

The machine-readable result is [`research/phase0b/performance.json`](../research/phase0b/performance.json). The separate Linux platform matrix passed the verified implementation commit's build, full tests, package, and consumer smoke; it was not a performance-bound measurement. Hosted CI run [35608970814](https://github.com/KeelMatrix/ConfigGap/actions/runs/35608970814) verified commit `3189f051` on the Ubuntu, Windows, and macOS matrix legs; the current tip adds documentation-only changes on top of that verified commit. macOS remains unverified locally.

## Reproduce

```powershell
pwsh -NoProfile -File .\scripts\Invoke-Phase0BPerformance.ps1 `
  -RepositoryRoot (Get-Location).Path `
  -ScratchRoot (Join-Path $env:TEMP 'configgap-performance') `
  -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/performance.json')
```

The protocol disables NuGet audit during the measurement restore and ignores unavailable sources so the benchmark measures analyzer behavior rather than advisory-service availability. The separate vulnerability audit remains an independent release gate.

# ConfigGap analysis-core benchmark

The checked-in performance protocol generates one clean 50-project solution from the fixture corpus, restores it once, and runs the unchanged analyzer three times with an exact observation-count guard. The result is a measured regression bound for this generated input and machine, not a portable service-level agreement.

## Current measurement

The fresh Windows measurement used 57 project fixture patterns, 2 shared fixture patterns, and 3,302 guarded observations on each run:

- durations: 19,654 ms, 16,965 ms, and 17,269 ms;
- peak working sets: 247,726,080 bytes, 248,832,000 bytes, and 248,885,248 bytes;
- derived bound: 21,000 ms wall clock and 274,726,912 bytes peak working set;
- environment: Windows 10.0.19045, x64, 16 processors, .NET SDK 8.0.425, PowerShell 7.6.6.

The machine-readable result is [`research/phase0b/performance.json`](../research/phase0b/performance.json). The separate Linux platform matrix covers build, full tests, package creation, and consumer smoke; it is not a performance-bound measurement. macOS remains unverified locally.

## Final fix closure rerun

At the final analyzer code state before this documentation update, the same protocol guarded 3,302 observations on all three runs:

| Run | Duration | Peak working set |
| ---: | ---: | ---: |
| 1 | 19,161 ms | 252,293,120 bytes |
| 2 | 17,270 ms | 251,019,264 bytes |
| 3 | 19,174 ms | 249,253,888 bytes |

The raw maxima were 19,174 ms and 252,293,120 bytes. The rerun-derived bounds were 20,800 ms and 277,872,640 bytes. The committed frozen V1 acceptance bounds remain 21,000 ms and 274,726,912 bytes: each raw sample is below both frozen limits, so the frozen limits are retained and not weakened. The higher rerun-derived working-set value is the protocol's 10% measurement margin, not a raw observation; the acceptance comparison uses raw maxima against the frozen bounds. The exact-count guard passed on every run and the benchmark scratch directory was absent after cleanup.

## Reproduce

```powershell
pwsh -NoProfile -File .\scripts\Invoke-Phase0BPerformance.ps1 `
  -RepositoryRoot (Get-Location).Path `
  -ScratchRoot (Join-Path $env:TEMP 'configgap-performance') `
  -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/performance.json')
```

The protocol disables NuGet audit during the measurement restore and ignores unavailable sources so the benchmark measures analyzer behavior rather than advisory-service availability. The separate vulnerability audit remains an independent release gate.

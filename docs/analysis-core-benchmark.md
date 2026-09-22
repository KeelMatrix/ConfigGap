# ConfigGap analysis-core benchmark

The checked-in performance protocol generates one clean 50-project solution from the fixture corpus, restores it once, and runs the unchanged analyzer at least five times with an exact observation-count guard. The machine-readable contract is [`research/phase0b/performance.json`](../research/phase0b/performance.json); [`scripts/Phase0BPerformanceGate.psm1`](../scripts/Phase0BPerformanceGate.psm1) is the enforcement point used by the benchmark and evidence checks.

The default gate compares fresh raw samples with the committed frozen limits. It requires all of the following:

- at least five guarded runs, with every raw sample reported;
- minimum duration at or below the frozen 21,000 ms wall-clock limit;
- median duration at or below 1.10 times that limit (23,100 ms), providing bounded tolerance for isolated scheduler noise while detecting sustained slowness;
- every peak working set at or below the frozen 274,726,912-byte limit; and
- exactly 3,302 observations on every run.

The run-derived bounds remain descriptive evidence only. The harness never overwrites `frozenV1Baseline`, and there is no implicit re-baselining path.

## Final five-run measurement

The fresh Windows measurement used 57 project fixture patterns, 2 shared fixture patterns, and 3,302 guarded observations on each run:

| Run | Duration | Peak working set |
| ---: | ---: | ---: |
| 1 | 19,979 ms | 248,131,584 bytes |
| 2 | 20,048 ms | 247,496,704 bytes |
| 3 | 19,633 ms | 250,871,808 bytes |
| 4 | 19,075 ms | 249,589,760 bytes |
| 5 | 19,510 ms | 250,834,944 bytes |

- Descriptive derived bounds: 20,500 ms wall clock and 276,824,064 bytes peak working set;
- statistic: minimum 19,075 ms; median 19,633 ms; median limit 23,100 ms;
- maximum raw peak working set: 250,871,808 bytes;
- exact observation-count guard: 3,302 on all five runs;
- resource gate verdict: `PASS`;
- environment: Windows 10.0.19045, x64, 16 processors, .NET SDK 8.0.425, PowerShell 7.6.6;
- frozen acceptance limits: 21,000 ms and 274,726,912 bytes; exit code: `0`;
- cleanup: benchmark scratch directory absent after cleanup (`False` when tested for presence).

The gate is reproducible for the documented environment class and generated input, not a portable service-level agreement. The separate Linux platform matrix covers build, full tests, package creation, and consumer smoke; it is not a performance-bound measurement. macOS remains unverified locally.

## Independent three-sample evaluation

The independent fresh-clone observation set that motivated the gate reconciliation was evaluated against the same duration and peak predicates:

| Run | Duration | Peak working set |
| ---: | ---: | ---: |
| 1 | 21,274 ms | 257,155,072 bytes |
| 2 | 19,633 ms | 251,711,488 bytes |
| 3 | 20,129 ms | 252,014,592 bytes |

All three samples reported 3,302 observations. The minimum duration is 19,633 ms, the median is 20,129 ms against the 23,100 ms median limit, and the maximum peak is 257,155,072 bytes against the 274,726,912-byte frozen limit. The retrospective statistic verdict is `PASS`. This external set has three observations, so it is evidence for the statistic only; it is not a substitute for the default five-run gated protocol.

## Reproduce

```powershell
pwsh -NoProfile -File .\scripts\Invoke-Phase0BPerformance.ps1 `
  -RepositoryRoot (Get-Location).Path `
  -ScratchRoot (Join-Path $env:TEMP 'configgap-performance') `
  -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/performance.json')
```

The committed negative check proves the gate fails closed when the capability bound is genuinely exceeded:

```powershell
pwsh -NoProfile -File .\scripts\Test-Phase0BPerformance.ps1
```

It supplies five exact-count samples at 21,001 ms with peaks below the frozen memory limit and expects the resource gate verdict `FAIL`.

The protocol disables NuGet audit during the measurement restore and ignores unavailable sources so the benchmark measures analyzer behavior rather than advisory-service availability. The separate vulnerability audit remains an independent release gate.

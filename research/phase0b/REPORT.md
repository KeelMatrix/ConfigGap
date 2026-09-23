# ConfigGap evaluation evidence

## Verdict

Overall verdict: PASS

Evaluation source ref: `e66ba4fafcc38206095333dd5bc2814abd719e08`

This report records the reproducible corpus, performance, and platform evaluation for the analyzer. The labeled corpus, precision/recall thresholds, and resource limits are evaluation inputs; they are not adjusted to improve a result.

## Corpus execution

- Pinned repositories requested and analyzed: 10.
- Clone preflight: 10/10 pinned repositories transferred successfully.
- Corpus command: `pwsh -NoProfile -File .\scripts\Invoke-Phase0BCorpus.ps1 -RepositoryRoot (Get-Location).Path -ScratchRoot (Join-Path $env:TEMP 'configgap-phase0b-corpus-final')`.
- Precision protocol: 29/29 cases passed; control blocking findings: 0; failed cases: 0.
- Blocking precision: 29/29 = 100.00% (VERIFIED).
- Observed corpus precision: 7/7 = 100.00%.
- Supported-domain recall: 29/29 = 100.00%.
- All-labeled-static-key recall: 29/29 = 100.00%.
- Dynamic blocking findings: 0.
- Load failures: 0.
- Corpus metric duration: 50,324 ms; metric command duration: 52,123 ms; total command duration: 126,377 ms.
- Restore skipped: `False`.
- Preflight metadata: `mode=script-owned-core.longpaths; effective core.longpaths=true; LongPathsEnabled=True; scratchRootLength=141`.
- Environment prevalence inventory: 2 named files across 10 repositories; 1 `.env.example`; actual `.env` content was excluded.

Precision protocol cases: 29
Precision protocol predictions: 29
Precision protocol true positives: 29
Precision protocol control blocking findings: 0
Precision protocol failed cases: 0

Raw result tail:

```text
Blocking precision: 29/29 = 100.00% (VERIFIED)
Precision protocol: 29/29 cases passed; control blocking findings: 0; failed cases: 0
Observed corpus precision: 7/7 = 100.00%
Supported-domain recall: 29/29 = 100.00%
All-labeled-static-key recall: 29/29 = 100.00%
Dynamic blocking findings: 0
Load failures: 0
Verdict: PASS
Duration: 50324 ms
Restore skipped: False
Metric command duration: 52123 ms
Metrics preflight metadata: mode=script-owned-core.longpaths; effective core.longpaths=true; LongPathsEnabled=True; scratchRootLength=141
Total corpus command duration: 126377 ms
```

## Performance evidence

The machine-readable resource contract is [`research/phase0b/performance.json`](performance.json), enforced by [`scripts/Phase0BPerformanceGate.psm1`](../../scripts/Phase0BPerformanceGate.psm1) through the benchmark and evidence checks. The default protocol requires at least five guarded runs and reports every raw sample. Acceptance uses the minimum duration, a median duration with a 1.10 headroom factor, every raw peak working set, and the exact observation count; run-derived bounds are descriptive only and never replace the frozen limits.

The analyzer ran against a clean generated 50-project solution with 3,302 guarded observations:

| Run | Duration | Peak working set |
| ---: | ---: | ---: |
| 1 | 19,979 ms | 248,131,584 bytes |
| 2 | 20,048 ms | 247,496,704 bytes |
| 3 | 19,633 ms | 250,871,808 bytes |
| 4 | 19,075 ms | 249,589,760 bytes |
| 5 | 19,510 ms | 250,834,944 bytes |

- Descriptive derived wall-clock bound: 20,500 ms.
- Descriptive derived peak working-set bound: 276,824,064 bytes.
- Gate statistic: minimum 19,075 ms; median 19,633 ms; median limit 23,100 ms; maximum raw peak 250,871,808 bytes.
- Gate verdict: `PASS`; all five observation counts were exactly 3,302; exit code `0`.
- Environment: Windows 10.0.19045, x64, 16 processors, .NET SDK 8.0.425, PowerShell 7.6.6.
- Frozen acceptance limits, unchanged: 21,000 ms wall clock and 274,726,912 bytes peak working set.
- Cleanup: benchmark scratch directory was absent after cleanup.
- The gate is reproducible for the documented environment class and generated input, not a portable service-level agreement.

Command:

```powershell
$scratch = Join-Path $env:TEMP 'configgap-phase0b-performance-final'
pwsh -NoProfile -File .\scripts\Invoke-Phase0BPerformance.ps1 `
  -RepositoryRoot (Get-Location).Path `
  -ScratchRoot $scratch `
  -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/performance.json')
```

Raw output tail:

```text
Guarded run 1: observations 3302; duration 19979 ms; peak working set 248131584 bytes
Guarded run 2: observations 3302; duration 20048 ms; peak working set 247496704 bytes
Guarded run 3: observations 3302; duration 19633 ms; peak working set 250871808 bytes
Guarded run 4: observations 3302; duration 19075 ms; peak working set 249589760 bytes
Guarded run 5: observations 3302; duration 19510 ms; peak working set 250834944 bytes
Guarded run count: 5 (required: at least 5)
Minimum duration: 19075 ms; frozen bound: 21000 ms; pass: True
Median duration: 19633 ms; limit: 23100.0 ms (headroom factor 1.1); pass: True
Maximum peak working set: 250871808 bytes; frozen bound: 274726912 bytes; pass: True
Observation-count guard: True (expected: 3302)
Derived wall-clock bound: 20500 ms
Derived peak working-set bound: 276824064 bytes
Frozen wall-clock bound: 21000 ms
Frozen peak working-set bound: 274726912 bytes
Resource gate verdict: PASS
Benchmark scratch present after cleanup: False
```

## Comparison baseline evaluation

The fresh-clone comparison baseline is `21,274/19,633/20,129` ms with peaks `257,155,072/251,711,488/252,014,592` bytes and 3,302 observations on each run. Under the documented statistic, minimum duration is 19,633 ms <= 21,000 ms, median duration is 20,129 ms <= 23,100 ms, and maximum peak working set is 257,155,072 bytes <= 274,726,912 bytes. Comparison statistic verdict: `PASS`. Its three samples are explicitly not a substitute for the default five-run gated protocol.

## Cross-platform evidence

The reproducible platform harness is [`scripts/Test-PlatformMatrix.ps1`](../../scripts/Test-PlatformMatrix.ps1). It runs in the official image `mcr.microsoft.com/dotnet/sdk:8.0`, digest `sha256:78235e09001f52b6592c458ac010775ebac6725422e80cd0c1650590f67b2743`.

Command:

```powershell
pwsh -NoProfile -File .\scripts\Test-PlatformMatrix.ps1
```

Linux matrix results:

| Step | Exit | Duration |
| --- | ---: | ---: |
| restore | 0 | 16,274 ms |
| release-build | 0 | 13,262 ms |
| release-test | 0 | 148,626 ms |
| release-pack | 0 | 6,724 ms |
| consumer-smoke | 0 | 9,980 ms |

The raw tails included a zero-warning/zero-error Release build, passing core and CLI tests, successful `.nupkg`/`.snupkg` creation, and passing clean/missing/dynamic consumer cases. Windows evidence includes the five-run performance protocol and the targeted core-test rerun. macOS remains expected only when documented MSBuild/Roslyn workspace loading works and is not independently verified here.

## Reproducibility and limitations

`research/phase0b/metrics.json`, `research/phase0b/performance.json`, the label files, and the scripts linked above are the machine-readable and executable evaluation inputs. The label protocol records source locations, access kinds, normalized keys, framework ownership, and uncertainty dispositions while excluding values and secrets. The selected-project scope is a reproducible analyzability boundary, not a claim that every repository project is analyzed.

The measured resource bounds apply to the generated input and recorded Windows machine. The platform harness verifies the documented Linux path; local macOS workspace compatibility remains unverified. Workspace failures must remain actionable exit-code-2 failures rather than clean results.

## Reproducibility snapshot

The pinned corpus was run with restore enabled and the five-run resource protocol from fresh scratch areas. The corpus result was `29/29` precision cases, `29/29` supported-domain recall, `29/29` all-labeled-static-key recall, `0` dynamic blocking findings, `0` load failures, `Verdict: PASS`, and `Scratch clones present after cleanup: False`.

| Run | Duration | Peak working set |
| ---: | ---: | ---: |
| 1 | 19,979 ms | 248,131,584 bytes |
| 2 | 20,048 ms | 247,496,704 bytes |
| 3 | 19,633 ms | 250,871,808 bytes |
| 4 | 19,075 ms | 249,589,760 bytes |
| 5 | 19,510 ms | 250,834,944 bytes |

The resource raw maximum duration was `20,048 ms`, while the minimum was `19,075 ms`; the median was `19,633 ms`. The frozen V1 bounds remain exactly `21,000 ms` and `274,726,912 bytes`; the documented minimum/median statistic remains fail-closed and never changes the bound. The maximum raw peak was `250,871,808 bytes`, every observation count was exactly `3,302`, the gate verdict was `PASS`, and the benchmark scratch directory was absent after cleanup.

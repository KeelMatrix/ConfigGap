# ConfigGap evaluation evidence

## Verdict

Overall verdict: PASS

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
- Corpus metric duration: 39,532 ms; metric command duration: 40,036 ms; total command duration: 151,041 ms.
- Restore skipped: `False`.
- Preflight metadata: `mode=script-owned-core.longpaths; effective core.longpaths=true; LongPathsEnabled=True; scratchRootLength=86`.
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
Duration: 39532 ms
Restore skipped: False
Metric command duration: 40036 ms
```

## Performance evidence

The analyzer ran against a clean generated 50-project solution with 3,302 guarded observations:

| Run | Duration | Peak working set |
| ---: | ---: | ---: |
| 1 | 19,654 ms | 247,726,080 bytes |
| 2 | 16,965 ms | 248,832,000 bytes |
| 3 | 17,269 ms | 248,885,248 bytes |

- Derived wall-clock bound: 21,000 ms.
- Derived peak working-set bound: 274,726,912 bytes.
- Environment: Windows 10.0.19045, x64, 16 processors, .NET SDK 8.0.425, PowerShell 7.6.6.
- The bounds are regression bounds for this generated input and machine, not portable service-level agreements.

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
Guarded run 1: Observations: 3302; Duration: 19654 ms; Peak working set: 247726080 bytes
Guarded run 2: Observations: 3302; Duration: 16965 ms; Peak working set: 248832000 bytes
Guarded run 3: Observations: 3302; Duration: 17269 ms; Peak working set: 248885248 bytes
Derived wall-clock bound: 21000 ms
Derived peak working-set bound: 274726912 bytes
Benchmark scratch present after cleanup: False
```

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

The raw tails included a zero-warning/zero-error Release build, passing core and CLI tests, successful `.nupkg`/`.snupkg` creation, and passing clean/missing/dynamic consumer cases. Windows evidence includes the three-run performance protocol and the targeted core-test rerun. macOS remains expected only when documented MSBuild/Roslyn workspace loading works and is not independently verified here.

## Reproducibility and limitations

`research/phase0b/metrics.json`, `research/phase0b/performance.json`, the label files, and the scripts linked above are the machine-readable and executable evaluation inputs. The label protocol records source locations, access kinds, normalized keys, framework ownership, and uncertainty dispositions while excluding values and secrets. The selected-project scope is a reproducible analyzability boundary, not a claim that every repository project is analyzed.

The measured resource bounds apply to the generated input and recorded Windows machine. The platform harness verifies the documented Linux path; local macOS workspace compatibility remains unverified. Workspace failures must remain actionable exit-code-2 failures rather than clean results.

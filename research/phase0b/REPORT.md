# ConfigGap Phase 0B feasibility evidence

## Verdict

Overall verdict: PASS

Recorded candidate ref: `HEAD`

Code candidate ref: 581611aa3b930ba0599e9fe44c7dd5f8cf68daee
Evidence checkpoint ref: f89af6200dfa30f1b1df545ce13f825de4bd78bc

This report records fresh evidence against the final analyzer candidate. The corpus, synthetic performance protocol, and candidate-specific Linux matrix were run after the analyzer location-semantics fix. Labels and thresholds were not changed to improve a result.

## Corpus execution

- Pinned repositories requested and analyzed: 10.
- Clone preflight: 10/10 pinned repositories transferred successfully.
- Corpus command: `pwsh -NoProfile -File .\scripts\Invoke-Phase0BCorpus.ps1 -RepositoryRoot (Get-Location).Path -ScratchRoot $env:SCRATCH_DIR\configgap-phase0b-corpus-final-6`.
- Precision protocol: 29/29 cases passed; control blocking findings: 0; failed cases: 0.
Precision protocol cases: 29
Precision protocol predictions: 29
Precision protocol true positives: 29
Precision protocol control blocking findings: 0
Precision protocol failed cases: 0
- Blocking precision: 29/29 = 100.00% (VERIFIED).
- Observed corpus precision: 7/7 = 100.00%.
- Supported-domain recall: 29/29 = 100.00%.
- All-labeled-static-key recall: 29/29 = 100.00%.
- Dynamic blocking findings: 0.
- Load failures: 0.
- Corpus metric duration: 52,450 ms; metric command duration: 53,782 ms; total command duration: 134,411 ms.
- Restore skipped: `False`.
- Preflight metadata: `mode=script-owned-core.longpaths; effective core.longpaths=true; LongPathsEnabled=True; scratchRootLength=108`.
- Environment prevalence inventory: 2 named files across 10 repositories; 1 `.env.example`; actual `.env` content was excluded.

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
Duration: 52450 ms
Restore skipped: False
Metric command duration: 53782 ms
```

## Performance evidence

The unchanged analyzer ran against a clean generated 50-project solution with 2,802 guarded observations:

| Run | Duration | Peak working set |
| ---: | ---: | ---: |
| 1 | 25,294 ms | 241,889,280 bytes |
| 2 | 21,134 ms | 245,456,896 bytes |
| 3 | 21,804 ms | 242,364,416 bytes |

- Derived wall-clock bound: 27,300 ms.
- Derived peak working-set bound: 270,532,608 bytes.
- Environment: Windows 10.0.19045, x64, 16 processors, .NET SDK 8.0.425, PowerShell 7.6.6.
- The bounds are regression bounds for this generated input and machine, not portable service-level agreements.

Command:

```powershell
$scratch = Join-Path $env:SCRATCH_DIR 'configgap-phase0b-performance-final-5'
pwsh -NoProfile -File .\scripts\Invoke-Phase0BPerformance.ps1 `
  -RepositoryRoot (Get-Location).Path `
  -ScratchRoot $scratch `
  -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/performance.json')
```

Raw output tail:

```text
Guarded run 1: Observations: 2802; Duration: 25294 ms; Peak working set: 241889280 bytes
Guarded run 2: Observations: 2802; Duration: 21134 ms; Peak working set: 245456896 bytes
Guarded run 3: Observations: 2802; Duration: 21804 ms; Peak working set: 242364416 bytes
Derived wall-clock bound: 27300 ms
Derived peak working-set bound: 270532608 bytes
Benchmark scratch present after cleanup: False
```

## Cross-platform evidence

The reproducible platform harness is [`scripts/Test-PlatformMatrix.ps1`](../../scripts/Test-PlatformMatrix.ps1). It was run against the final analyzer candidate in the official image `mcr.microsoft.com/dotnet/sdk:8.0`, digest `sha256:78235e09001f52b6592c458ac010775ebac6725422e80cd0c1650590f67b2743`.

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

Raw tails included zero-warning/zero-error Release build, 10 passing core tests and 18 passing CLI tests, successful `.nupkg`/`.snupkg` creation, and passing clean/missing/dynamic consumer cases. The check has not run in GitHub Actions; the repository workflow remains tag/manual-release only.

Windows evidence for this candidate includes the three-run performance protocol and the targeted 10/10 core test rerun. macOS remains expected only when documented MSBuild/Roslyn workspace loading works and is not independently verified here.

## Evidence guard state

`Test-Phase0BEvidence.ps1` and `Test-ReportCandidateRef.ps1` are run against the report-anchor commit after this document is committed. Both must pass at the exact candidate ref before publication consideration.

No package publication, tag, release, deployment, visibility change, workflow run, or private CI run was performed.

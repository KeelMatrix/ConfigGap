# ConfigGap Phase 0B feasibility evidence

## Verdict

Overall verdict: BLOCKED

Recorded candidate ref: `HEAD`

Code candidate ref: 976f1a133bfd5015d9c4f8f640bb9f37e8c7b750
Evidence checkpoint ref: 0ebc2254d3626872052791f057abfdb793ec9f98

This report records fresh evidence for the final analyzer source candidate and does not reuse the earlier corpus metrics. The synthetic performance protocol completed. The labeled real-repository corpus did not reach analysis because the first pinned repository tree could not be transferred by Git in this environment. No precision, recall, dynamic-access, or corpus-load result is claimed.

## Corpus execution

- Pinned repositories requested: 10.
- First repository: `eShopOnWeb` at `4da8212117e87d808d4bbc7da6286fd2147ce606`.
- Result: `CONFIGGAP_CORPUS_CLONE_PREFLIGHT_FAILURE`, Git exit code 128.
- Raw failure tail: `fatal: unable to access 'https://github.com/dotnet-architecture/eShopOnWeb.git/': Failed to connect to github.com:443 after 21050 ms`.
- Analysis started: no.
- Precision, supported-domain recall, all-labeled recall, dynamic blocking, load failures: not measured.

The harness was retried only after bounded transport fixes: Git HTTP/1.1, blob filtering, and canonical `.git` endpoints. Metadata-only access and a direct partial-clone diagnostic succeeded, but the bounded harness process still failed during the required tree transfer. The machine-readable corpus evidence therefore records `BLOCKED` and intentionally contains no stale corpus percentages.

## Performance evidence

The unchanged analyzer ran against a clean generated 50-project solution with 2,802 guarded observations:

| Run | Duration | Peak working set |
| ---: | ---: | ---: |
| 1 | 27,472 ms | 246,468,608 bytes |
| 2 | 23,565 ms | 240,398,336 bytes |
| 3 | 26,520 ms | 243,007,488 bytes |

- Derived wall-clock bound: 30,000 ms.
- Derived peak working-set bound: 271,581,184 bytes.
- Environment: Windows 10.0.19045, x64, 16 processors, .NET SDK 8.0.425, PowerShell 7.6.6.
- The bounds are regression bounds for this generated input and machine, not portable service-level agreements.

Command:

```powershell
$scratch = Join-Path $env:SCRATCH_DIR 'configgap-phase0b-performance-final-3'
pwsh -NoProfile -File .\scripts\Invoke-Phase0BPerformance.ps1 `
  -RepositoryRoot (Get-Location).Path `
  -ScratchRoot $scratch `
  -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/performance.json')
```

Raw output tails:

```text
Guarded run 1: Observations: 2802; Duration: 27472 ms; Peak working set: 246468608 bytes
Guarded run 2: Observations: 2802; Duration: 23565 ms; Peak working set: 240398336 bytes
Guarded run 3: Observations: 2802; Duration: 26520 ms; Peak working set: 243007488 bytes
Derived wall-clock bound: 30000 ms
Derived peak working-set bound: 271581184 bytes
Benchmark scratch present after cleanup: False
```

## Cross-platform evidence

The reproducible platform harness is [`scripts/Test-PlatformMatrix.ps1`](../../scripts/Test-PlatformMatrix.ps1). It uses the official `mcr.microsoft.com/dotnet/sdk:8.0` image and records the image digest, restore/build/test/pack/consumer-smoke durations, and raw tails. It has not run in CI.

The local Docker check returned:

```text
failed to connect to the docker API at npipe:////./pipe/docker_engine; check if the path is correct: open //./pipe/docker_engine: The system cannot find the file specified.
```

Linux candidate evidence is therefore unverified. macOS remains expected only when documented MSBuild/Roslyn workspace loading works and is unverified locally.

## Evidence guard state

`Test-ReportCandidateRef.ps1` is expected to pass after this report-anchor commit resolves `Recorded candidate ref: HEAD` to the checked-out commit. `Test-Phase0BEvidence.ps1` remains expected to fail closed because the required fresh corpus summary was not produced; this is the intended blocker signal, not a threshold or label adjustment.

No package, tag, release, deployment, visibility change, workflow run, or private CI run was performed.

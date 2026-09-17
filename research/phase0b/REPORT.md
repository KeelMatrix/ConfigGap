# ConfigGap Phase 0B feasibility evidence

## Verdict

Overall verdict: PASS

Code candidate ref: 5adc6c7869d3355cb7a067c1c7330a7abfe5e011
Evidence checkpoint ref: eae79e8bfa71fa20fe76e58f841fdb8076da9dfb

This report records the Roslyn analysis-core evidence for the exact code
candidate above. It does not claim a product release, package artifact, or
production maximum-cardinality proof.

The candidate composes relative `IConfigurationSection` paths for direct and
bounded local-section aliases across indexers, `GetValue<T>`, and
`GetRequiredSection`; rejects missing required configuration fields and
unknown JSON properties; and explicitly freezes bounded helper propagation at
the same-compilation boundary. Reassigned local sections and cross-project
helper access remain unknown and non-blocking.

## Corpus results

- 10 pinned repositories analyzed; load failures: 0.
- Blocking precision: 29/29 = 100.00% (VERIFIED).
- Supported-domain recall: 29/29 = 100.00%.
- All-labeled-static-key recall: 29/29 = 100.00%.
- Observed corpus precision: 7/7 = 100.00%.
- Dynamic blocking findings: 0.

Precision protocol cases: 29
Precision protocol predictions: 29
Precision protocol true positives: 29
Precision protocol control blocking findings: 0
Precision protocol failed cases: 0

## Fixture, test, and build evidence

The repository was restored with the local central-package-management
override required by the surrounding validation workspace. The product
projects themselves remain unchanged by that validation override.

Build command:

    dotnet build KeelMatrix.ConfigGap.sln -c Release --no-restore -p:ManagePackageVersionsCentrally=false -p:NuGetAudit=false

Raw output tail:

    KeelMatrix.ConfigGap.Core -> ...\src\KeelMatrix.ConfigGap.Core\bin\Release\net8.0\KeelMatrix.ConfigGap.Core.dll
    ConfigGap.Probe -> ...\tools\ConfigGap.Probe\bin\Release\net8.0\KeelMatrix.ConfigGap.Probe.dll
    FixtureConsumer -> ...\fixtures\FixtureConsumer\bin\Release\net8.0\ConfigGap.FixtureConsumer.dll
    Build succeeded.
        0 Warning(s)
        0 Error(s)
    Time Elapsed: 00:00:01.43

Focused test command:

    dotnet test tests\KeelMatrix.ConfigGap.Core.Tests\KeelMatrix.ConfigGap.Core.Tests.csproj -c Release --no-restore -p:ManagePackageVersionsCentrally=false

Raw output tail:

    A total of 1 test files matched the specified pattern.
    Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10, Duration: 18 s

Fixture command:

    dotnet run --project tools\ConfigGap.Probe\ConfigGap.Probe.csproj -c Release --no-build -- --solution KeelMatrix.ConfigGap.sln --repository-root . --output artifacts\probe-results.json

Raw output tail:

    Patterns: 42/42 passed
    Normalization: 2/2 passed
    Options sections: 7/7 passed
    Dynamic/unresolvable: 12 unknown; never classified as missing: True
    Machine-readable report: artifacts\probe-results.json
    Duration: 6796 ms

The committed fixture set includes direct relative-section regressions,
bounded local-section alias regressions for all three relative consumers, a
reassigned-section safety fixture, and the referenced-project helper boundary
fixture. Configuration tests cover missing `version`, missing
`declarationSurfaces`, and an unexpected property.

## Corpus command and raw output tail

    $stamp=Get-Date -Format 'yyyyMMdd-HHmmss'; $scratch=Join-Path $env:TEMP "configgap-stage1a-$stamp-corpus"; pwsh -NoProfile -File .\scripts\Invoke-Phase0BCorpus.ps1 -RepositoryRoot (Get-Location).Path -ScratchRoot $scratch -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/metrics.json') -EnvEvidencePath (Join-Path (Get-Location).Path 'research/phase0b/env-prevalence.json')

    Windows long-path preflight: OS=True; LongPathsEnabled=True; Git configured core.longpaths=<unset>; effective core.longpaths=true; scratchRootLength=116; mode=script-owned-core.longpaths
    Environment template files found: 2; repositories with .env.example: 1/10
    Blocking precision: 29/29 = 100.00% (VERIFIED)
    Precision protocol: 29/29 cases passed; control blocking findings: 0; failed cases: 0
    Observed corpus precision: 7/7 = 100.00%
    Supported-domain recall: 29/29 = 100.00%
    All-labeled-static-key recall: 29/29 = 100.00%
    Dynamic blocking findings: 0
    Load failures: 0
    Verdict: PASS
    Restore skipped: False
    Metric command duration: 41222 ms
    Total corpus command duration: 101438 ms
    Scratch clones present after cleanup: False

The machine-readable corpus evidence is [`metrics.json`](metrics.json). It
contains 10 pinned repositories, 29 supported-domain keys, 7 observed
blocking-precision cases, zero dynamic blocking findings, and zero load
failures. The file-name-only template inventory is
[`env-prevalence.json`](env-prevalence.json); actual `.env` contents were not
read.

## Performance command and raw output tails

    $stamp=Get-Date -Format 'yyyyMMdd-HHmmss'; $scratch=Join-Path $env:TEMP "configgap-stage1a-$stamp-performance"; pwsh -NoProfile -File .\scripts\Invoke-Phase0BPerformance.ps1 -RepositoryRoot (Get-Location).Path -ScratchRoot $scratch -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/performance.json')

    Derived expected observation count: 2252 (45 per generated consumer project; 2 shared fixture observations).
    Guarded run 1: Observations: 2252; Declared surfaces: 50; Duration: 21198 ms; Peak working set: 237490176 bytes
    Guarded run 2: Observations: 2252; Declared surfaces: 50; Duration: 18817 ms; Peak working set: 234844160 bytes
    Guarded run 3: Observations: 2252; Declared surfaces: 50; Duration: 21318 ms; Peak working set: 237359104 bytes
    Derived wall-clock bound: 23300 ms
    Derived peak working-set bound: 262144000 bytes
    Benchmark scratch present after cleanup: False

The machine-readable performance evidence is
[`performance.json`](performance.json). It records the 42-pattern manifest,
2,252-observation guard, three guarded runs, and the measured Windows/.NET
machine description. The bounds are regression bounds for this generated
input and machine, not portable service-level agreements.

## Evidence consistency and SHA proof

The evidence guard requires the evidence checkpoint to be a direct evidence-
only child of the code candidate and requires the checked-out report anchor to
be the direct child of that evidence checkpoint. This prevents later code
changes from silently reusing an older corpus or performance result.

The final SHA proof is recorded after the report-anchor commit:

    git rev-parse HEAD
    git rev-parse origin/main
    git ls-remote origin refs/heads/main
    git status --porcelain

No package, tag, release, deployment, visibility change, workflow file, or
private CI run was performed. The analysis projects remain non-packable.

# ConfigGap Phase 0B feasibility evidence

## Verdict

Overall verdict: PASS

Code candidate ref: 49ff3d1b3dbe6b65c74a3b3f934a49c74cc1b398
Evidence checkpoint ref: 49ff3d1b3dbe6b65c74a3b3f934a49c74cc1b398

This report records the Roslyn analysis-core evidence for the exact code
candidate above. It does not claim a product release, package artifact, or
production maximum-cardinality proof.

The candidate closes relative `IConfigurationSection` path composition for
indexers, `GetValue<T>`, and `GetRequiredSection`; rejects missing required
configuration fields and unknown JSON properties; and explicitly freezes
bounded helper propagation at the same-compilation boundary. Cross-project
helper access remains unknown and non-blocking.

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

    Patterns: 38/38 passed
    Normalization: 2/2 passed
    Options sections: 7/7 passed
    Dynamic/unresolvable: 11 unknown; never classified as missing: True
    Machine-readable report: artifacts\probe-results.json
    Duration: 6731 ms

The committed fixture set includes the three relative-section regressions and
the referenced-project helper boundary fixture. Configuration tests cover
missing `version`, missing `declarationSurfaces`, and an unexpected property.

## Corpus command and raw output tail

    pwsh -NoProfile -File .\scripts\Invoke-Phase0BCorpus.ps1 -RepositoryRoot (Get-Location).Path -ScratchRoot (Join-Path $env:TEMP 'configgap-phase0b-candidate-49ff-corpus') -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/metrics.json') -EnvEvidencePath (Join-Path (Get-Location).Path 'research/phase0b/env-prevalence.json')

    Windows long-path preflight: OS=True; LongPathsEnabled=True; Git configured core.longpaths=<unset>; effective core.longpaths=true; scratchRootLength=115; mode=script-owned-core.longpaths
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
    Metric command duration: 47912 ms
    Total corpus command duration: 105797 ms
    Scratch clones present after cleanup: False

The machine-readable corpus evidence is [`metrics.json`](metrics.json). It
contains 10 pinned repositories, 29 supported-domain keys, 7 observed
blocking-precision cases, zero dynamic blocking findings, and zero load
failures. The file-name-only template inventory is
[`env-prevalence.json`](env-prevalence.json); actual `.env` contents were not
read.

## Performance command and raw output tails

    pwsh -NoProfile -File .\scripts\Invoke-Phase0BPerformance.ps1 -RepositoryRoot (Get-Location).Path -ScratchRoot (Join-Path $env:TEMP 'configgap-phase0b-candidate-49ff-performance') -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/performance.json')

    Derived expected observation count: 1802 (36 per generated consumer project; 2 shared fixture observations).
    Guarded run 1: Observations: 1802; Declared surfaces: 50; Duration: 21134 ms; Peak working set: 231284736 bytes
    Guarded run 2: Observations: 1802; Declared surfaces: 50; Duration: 19599 ms; Peak working set: 229351424 bytes
    Guarded run 3: Observations: 1802; Declared surfaces: 50; Duration: 18503 ms; Peak working set: 230297600 bytes
    Derived wall-clock bound: 22400 ms
    Derived peak working-set bound: 254803968 bytes
    Benchmark scratch present after cleanup: False

The machine-readable performance evidence is
[`performance.json`](performance.json). It records the 38-pattern manifest,
1,802-observation guard, three guarded runs, and the measured Windows/.NET
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

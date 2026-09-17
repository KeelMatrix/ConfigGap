# ConfigGap Phase 0B feasibility evidence

## Verdict

Overall verdict: PASS

Code candidate ref: c7b0d4b25b8dc3a711778b3518367b04416f0952
Evidence checkpoint ref: eae79e8bfa71fa20fe76e58f841fdb8076da9dfb

This report records the Roslyn analysis-core evidence for the exact code
candidate above. It does not claim a product release, package artifact, or
production maximum-cardinality proof.

The candidate resolves named and positional arguments for supported generic
`GetValue<T>` calls, preserves template ancestor sections, reports unresolved
root `GetChildren()` access as informational, and keeps required Options
binding evidence distinct from ordinary bindable evidence. It composes relative
`IConfigurationSection` paths for direct and bounded local-section aliases and
explicitly freezes bounded helper propagation at the same-compilation boundary.
Reassigned local sections, non-generic `GetValue` overloads, and cross-project
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
    Time Elapsed: 00:00:00.93

Focused test command:

    dotnet test tests\KeelMatrix.ConfigGap.Core.Tests\KeelMatrix.ConfigGap.Core.Tests.csproj -c Release --no-build --no-restore -p:ManagePackageVersionsCentrally=false -p:NuGetAudit=false

Raw output tail:

    A total of 1 test files matched the specified pattern.
    Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10, Duration: 19 s

Fixture command:

    dotnet run --project tools\ConfigGap.Probe\ConfigGap.Probe.csproj -c Release --no-build -- --solution KeelMatrix.ConfigGap.sln --repository-root . --output artifacts\probe-results.json

Raw output tail:

    Patterns: 48/48 passed
    Normalization: 2/2 passed
    Options sections: 9/9 passed
    Dynamic/unresolvable: 14 unknown; never classified as missing: True
    Machine-readable report: artifacts\probe-results.json
    Duration: 7573 ms

The committed fixture set includes named and positional `GetValue<T>` argument
regressions, template-only Options sections, root `GetChildren()` unknown
access, required-plus-bindable Options evidence, direct relative-section
regressions, bounded local-section aliases for all three relative consumers, a
reassigned-section safety fixture, and the referenced-project helper boundary
fixture. Configuration tests cover missing `version`, missing
`declarationSurfaces`, an unexpected property, and sanitized exit-code-2
recovery text.

Workspace-failure command:

    pwsh -NoProfile -File .\scripts\Test-WorkspaceFailure.ps1 -RepositoryRoot (Get-Location).Path

Raw output tail:

    Workspace failure regression passed: broken compilation failed closed with CONFIGGAP_COMPILATION_LOAD_FAILURE.

## Corpus command and raw output tail

    $stamp=Get-Date -Format 'yyyyMMdd-HHmmss'; $scratch=Join-Path $env:TEMP "configgap-stage1a-$stamp-corpus"; pwsh -NoProfile -File .\scripts\Invoke-Phase0BCorpus.ps1 -RepositoryRoot (Get-Location).Path -ScratchRoot $scratch -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/metrics.json') -EnvEvidencePath (Join-Path (Get-Location).Path 'research/phase0b/env-prevalence.json')

    Windows long-path preflight: OS=True; LongPathsEnabled=True; Git configured core.longpaths=<unset>; effective core.longpaths=true; scratchRootLength=94; mode=script-owned-core.longpaths
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
    Metric command duration: 43314 ms
    Total corpus command duration: 105296 ms
    Scratch clones present after cleanup: False

The machine-readable corpus evidence is [`metrics.json`](metrics.json). It
contains 10 pinned repositories, 29 supported-domain keys, 7 observed
blocking-precision cases, zero dynamic blocking findings, and zero load
failures. The file-name-only template inventory is
[`env-prevalence.json`](env-prevalence.json); actual `.env` contents were not
read.

## Performance command and raw output tails

    $stamp=Get-Date -Format 'yyyyMMdd-HHmmss'; $scratch=Join-Path $env:TEMP "configgap-stage1a-$stamp-performance"; pwsh -NoProfile -File .\scripts\Invoke-Phase0BPerformance.ps1 -RepositoryRoot (Get-Location).Path -ScratchRoot $scratch -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/performance.json')

    Derived expected observation count: 2552 (51 per generated consumer project; 2 shared fixture observations).
    Guarded run 1: Observations: 2552; Declared surfaces: 50; Duration: 21265 ms; Peak working set: 245657600 bytes
    Guarded run 2: Observations: 2552; Declared surfaces: 50; Duration: 20340 ms; Peak working set: 244576256 bytes
    Guarded run 3: Observations: 2552; Declared surfaces: 50; Duration: 21198 ms; Peak working set: 237768704 bytes
    Derived wall-clock bound: 22000 ms
    Derived peak working-set bound: 270532608 bytes
    Benchmark scratch present after cleanup: False

The machine-readable performance evidence is
[`performance.json`](performance.json). It records the 48-pattern manifest,
2,552-observation guard, three guarded runs, and the measured Windows/.NET
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

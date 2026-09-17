# ConfigGap Phase 0B feasibility evidence

## Verdict

Overall verdict: PASS

Code candidate ref: 95a591ee593bc95f1a913e5af91639ebbd05930d
Evidence checkpoint ref: HEAD (resolved by SHA proof below)

The final bounded Phase 0 round closes the helper-location, corpus-preflight,
performance-evidence, report-consistency, and adversarial-fixture findings.
The analyzer remains a non-packable Roslyn feasibility probe; this evidence
does not claim a product release or package artifact.

Final clean corpus results:

- 10 pinned repositories analyzed; load failures: 0.
- Supported-domain recall: 29/29 = 100.00%.
- All-labeled-static-key recall: 29/29 = 100.00%.
- Controlled blocking precision: 29/29 = 100.00%, with a genuine denominator of 29.
- Dynamic blocking findings: 0.

## Implemented bounded fixes

- Helper-mediated supported static keys now use the literal or const call site
  as the primary finding location. Regression fixtures assert this for both
  literal and const arguments. The shared helper access is not used as a
  per-key primary location.
- fullstackhero-webapi labels now map the pinned source call sites exactly:
  CachingOptions:Redis at Program.cs:38, JwtOptions:SigningKey at Program.cs:39,
  and DatabaseOptions:ConnectionString at Program.cs:40.
- Windows fallback preflight validates clone, pinned checkout, and clean status
  for every real corpus repository before restore or analysis. The final run
  used the documented short-root fallback with effective
  core.longpaths=<unset> and no process-local Git configuration injection.
- Performance expectations are derived from the current fixture manifest. The
  committed evidence uses one clean 50-project solution, one restore, and
  three guarded runs.
- The evidence guard compares report summary fields with metrics.json and
  verifies the evidence-only checkpoint relationship.
- Committed boundary fixtures cover key-controlled branch/reassignment and a
  same-name helper declared by an unrelated type. Both remain unknown.

## Fixture and build evidence

Commands and raw output tails:

    dotnet build .\KeelMatrix.ConfigGap.sln -c Release --no-restore -p:ManagePackageVersionsCentrally=false -p:RunAnalyzers=false
    Time Elapsed: 00:00:11.22
    Build succeeded.
        0 Warning(s)
        0 Error(s)

    dotnet run --project tools/ConfigGap.Probe/ConfigGap.Probe.csproj -c Release --no-build -- --solution KeelMatrix.ConfigGap.sln --repository-root . --output artifacts/probe-results.json
    Patterns: 33/33 passed
    Normalization: 2/2 passed
    Options sections: 6/6 passed
    Dynamic/unresolvable: 10 unknown; never classified as missing: True
    Duration: 9810 ms
    Machine-readable report: artifacts\probe-results.json

The focused fixture run includes 32 generated-project patterns and one shared
fixture pattern. The helper call-site assertions observed the declared lines
8 and 15. The two new boundary fixtures were classified unknown. No package
was built because the repository is intentionally non-packable.

## Final clean corpus run

Command:

    pwsh -NoProfile -File .\scripts\Invoke-Phase0BCorpus.ps1 -RepositoryRoot (Get-Location).Path -ScratchRoot 'C:\Users\rdime\AppData\Local\Temp\cg585c' -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/metrics.json') -EnvEvidencePath (Join-Path (Get-Location).Path 'research/phase0b/env-prevalence.json')

The command ran from 2026-09-17 15:17:34 through 15:25:16 (462.3 s,
measured from the command runner timestamps). The resolved scratch root was
40 characters. No unrecorded workaround was used.

Raw output tail:

    Windows long-path preflight: OS=True; Git effective core.longpaths=<unset>; scratchRootLength=40; fallbackLimit=80; mode=short-root-fallback
    Clone preflight passed: fullstackhero-webapi at 3f2959e683e9f83f13e55e1678c9119f63c7e8e5
    Pinned corpus clone preflight passed for 10 repositories; beginning restore and analysis preparation.
    Environment template files found: 2; repositories with .env.example: 1/10
    Analyzing fullstackhero-webapi (src/Host/FSH.Starter.Api/FSH.Starter.Api.csproj)
    Blocking precision: 29/29 = 100.00% (VERIFIED)
    Precision protocol: 29/29 cases passed; control blocking findings: 0; failed cases: 0
    Observed corpus precision: 5/5 = 100.00%
    Supported-domain recall: 29/29 = 100.00%
    All-labeled-static-key recall: 29/29 = 100.00%
    Dynamic blocking findings: 0
    Load failures: 0
    Verdict: PASS
    Restore skipped: False
    Scratch clones present after cleanup: False

Per-repository recall and blocking evidence:

| Repository | Status | Blocking TP/predictions | Supported recall | All-key recall | Dynamic blocking |
| --- | --- | ---: | ---: | ---: | ---: |
| aspnet-core-mvc | analyzed | 0/0 | 8/8 | 8/8 | 0 |
| bookcatalog-api | analyzed | 0/0 | 6/6 | 6/6 | 0 |
| clean-architecture-jason-taylor | analyzed | 1/1 | 1/1 | 1/1 | 0 |
| clean-architecture-manga | analyzed | 1/1 | 3/3 | 3/3 | 0 |
| clean-minimal-api | analyzed | 0/0 | 1/1 | 1/1 | 0 |
| dotnet-rpg | analyzed | 0/0 | 1/1 | 1/1 | 0 |
| eShopOnWeb | analyzed | 3/3 | 4/4 | 4/4 | 0 |
| fullstackhero-webapi | analyzed | 0/0 | 3/3 | 3/3 | 0 |
| oidcproxy-net | analyzed | 0/0 | 1/1 | 1/1 | 0 |
| razor-pages-ioptions-samples | analyzed | 0/0 | 1/1 | 1/1 | 0 |
| Total | 10 analyzed; 0 load failures | 5/5 | 29/29 | 29/29 | 0 |

The machine-readable result is research/phase0b/metrics.json. Its summary is:

    Precision protocol cases: 29
    Precision protocol predictions: 29
    Precision protocol true positives: 29
    Precision protocol control blocking findings: 0
    Precision protocol failed cases: 0
    Precision protocol result: 29/29 cases passed; control blocking findings: 0; failed cases: 0
    Observed corpus result: 5/5 = 100.00%
    Supported-domain recall result: 29/29 = 100.00%
    All-labeled-static-key recall result: 29/29 = 100.00%
    Dynamic blocking count: 0
    Load failure count: 0
    Result verdict: PASS

## Benchmark evidence

Command:

    pwsh -NoProfile -File .\scripts\Invoke-Phase0BPerformance.ps1 -RepositoryRoot (Get-Location).Path -ScratchRoot 'C:\Users\rdime\AppData\Local\Temp\cg585p' -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/performance.json')

Raw guarded-run output tails:

    Derived expected observation count: 1601 (32 per generated consumer project; 1 shared fixture observations).
    Guarded run 1: Observations: 1601; Declared surfaces: 150; Duration: 32851 ms; Peak working set: 227540992 bytes
    Guarded run 2: Observations: 1601; Declared surfaces: 150; Duration: 34192 ms; Peak working set: 227696640 bytes
    Guarded run 3: Observations: 1601; Declared surfaces: 150; Duration: 32716 ms; Peak working set: 230154240 bytes
    Derived wall-clock bound: 34900 ms
    Derived peak working-set bound: 253755392 bytes
    Benchmark scratch present after cleanup: False

The evidence is research/phase0b/performance.json. It records the current
fixture manifest, 50 generated projects, 32 project patterns, one shared
pattern, expected observation count 1,601, and the current Windows/.NET
machine description. The performance guard passes against these derived
expectations.

## Report consistency evidence

Command:

    pwsh -NoProfile -File .\scripts\Test-Phase0BEvidence.ps1 -RepositoryRoot (Get-Location).Path

The final evidence-checkpoint output is recorded below after the evidence-only
commit:

    Code candidate recorded above: 95a591ee593bc95f1a913e5af91639ebbd05930d
    Evidence checkpoint output: the final SHA proof below resolves HEAD after push
    Metrics summary consistency: PASS
    Evidence candidate consistency: PASS (evidence-only checkpoint; parent 95a591ee593bc95f1a913e5af91639ebbd05930d)

## Precision and boundary evidence

The report denominator is 29 throughout. The two helper-mediated
fullstackhero-webapi precision cases now resolve to their literal call-site
locations, and the corrected labels are taken from the pinned upstream source.
The new branch/reassignment and same-name-unrelated-helper fixtures remain
unknown, with no false positive or blocking finding. :/__ normalization and
Options sections are both complete at 100% in the focused fixture run.

## Declaration-template evidence

The corpus run enumerated repository file names only and did not read .env or
template contents. It found two candidate files and one repository with
.env.example; the evidence is research/phase0b/env-prevalence.json.

## SHA proof

The final proof was run after pushing the evidence checkpoint to main:

    git rev-parse HEAD
    git rev-parse origin/main
    git ls-remote origin refs/heads/main
    gh api repos/KeelMatrix/ConfigGap/commits/main --jq .sha
    git status --porcelain
    git ls-remote --tags origin

The exact post-push SHA values and the empty status/tag outputs are recorded in
the completion evidence for this change. All four SHA-producing commands must
resolve the evidence checkpoint ref; the status and tag commands must produce
no output. No tag, release, package publication, deployment, visibility
change, workflow file, or private Actions run was performed.

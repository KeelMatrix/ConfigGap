# ConfigGap Phase 0B fix-round evidence

## Verdict

**FAIL.** The final clean corpus run analyzed all ten selected projects with zero load failures. Supported static recall was **26/29 = 89.66%**, below the required 90% gate. The controlled blocking-precision protocol produced a genuine denominator of **26 predictions** and measured **24/26 = 92.31%**, below the required 95% gate. Dynamic accesses produced zero blocking findings. This is a measured semantic/protocol FAIL, not a vacuous precision result and not a label-tuning exercise.

The Phase 0B section 7.6 park/narrow conditions are not triggered by the measured causes: dynamic access did not dominate, Options identification remained deterministic, and no suppression list or competitor evidence was introduced. The gate remains FAIL because the approved thresholds were not met.

## Implemented bounded fixes

- Clone and restore commands have a 600-second per-repository bound; analysis remains bounded at 120 seconds per repository, and the overall corpus run is bounded at 3,600 seconds. Scratch clones are removed in `finally`.
- `SemanticProbe` ignores only identified advisory workspace messages: package-vulnerability notices, framework lifecycle/EOL notices, the known implicit ASP.NET Core framework reference notice, the known OpenAPI analyzer deprecation notice, and the exact unsupported-target-framework tooling warning. Missing assets, unresolved references, compilation errors, and other workspace failures remain named fail-closed `CONFIGGAP_*` failures.
- Precision is evaluated with a hand-label-driven synchronized declaration graph. The no-removal control must produce zero blocking findings; each distinct labeled supported static key is tested with exactly that key removed and must produce one finding at its primary labeled location. The minimum predeclared denominator is 10 blocking predictions. A denominator below 10 is `UNVERIFIED` and fails the gate; a zero denominator is never rendered as a percentage or pass.
- Windows preflight reads effective `git config --get core.longpaths` without `-c` injection, combines that value with `LongPathsEnabled` and the resolved scratch-root length, and reports the native or short-root mode. Git operations use the effective configuration. The short-root fallback limit is 80 characters.

No corpus substitution was made. The corpus index, commits, and hand-authored labels were not changed.

## Fixture and workspace evidence

Commands:

```powershell
dotnet build .\KeelMatrix.ConfigGap.sln -c Release --no-restore -p:ManagePackageVersionsCentrally=false
dotnet run --project tools/ConfigGap.Probe/ConfigGap.Probe.csproj -c Release --no-build -- --solution KeelMatrix.ConfigGap.sln --repository-root . --output artifacts/probe-results.json
pwsh -NoProfile -File .\scripts\Test-WorkspaceFailure.ps1
```

Results:

| Check | Result |
| --- | ---: |
| Release build | 0 warnings, 0 errors |
| Synthetic semantic patterns | 26/26 |
| `:`/`__` normalization fixtures | 2/2 |
| Options-section fixtures | 6/6 |
| Dynamic/unresolvable fixtures kept unknown | 5; no blocking classification |
| Broken-compilation regression | Passed with `CONFIGGAP_COMPILATION_LOAD_FAILURE` |

## Final clean corpus run

The final run used the ten pinned repositories and hand-authored labels in `corpus-index.json` and `labels/`. The scratch-root value is intentionally omitted from this repository report; it was a writable temporary path of length 75 validated by the preflight.

```powershell
$scratchRoot = '<validated writable temporary root; length 75>'
pwsh -NoProfile -File .\scripts\Invoke-Phase0BCorpus.ps1 `
  -RepositoryRoot (Get-Location).Path `
  -ScratchRoot $scratchRoot `
  -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/metrics.json') `
  -EnvEvidencePath (Join-Path (Get-Location).Path 'research/phase0b/env-prevalence.json')
```

Raw preflight tail:

```text
Windows long-path preflight: OS=True; Git effective core.longpaths=<unset>; scratchRootLength=75; fallbackLimit=80; mode=short-root-fallback
Environment template files found: 2; repositories with .env.example: 1/10
Restore skipped: False
Metric command duration: 59795 ms
Total corpus command duration: 122180 ms
Scratch clones present after cleanup: False
```

Every selected project was analyzed:

| Repository | Status | Blocking TP/predictions | Supported recall | All-key recall | Dynamic blocking |
| --- | --- | ---: | ---: | ---: | ---: |
| `aspnet-core-mvc` | analyzed | 0/0 | 8/8 | 8/8 | 0 |
| `bookcatalog-api` | analyzed | 0/0 | 6/6 | 6/6 | 0 |
| `clean-architecture-jason-taylor` | analyzed | 1/1 | 1/1 | 1/1 | 0 |
| `clean-architecture-manga` | analyzed | 1/1 | 3/3 | 3/3 | 0 |
| `clean-minimal-api` | analyzed | 0/0 | 1/1 | 1/1 | 0 |
| `dotnet-rpg` | analyzed | 0/0 | 1/1 | 1/1 | 0 |
| `eShopOnWeb` | analyzed | 3/3 | 4/4 | 4/4 | 0 |
| `fullstackhero-webapi` | analyzed | 0/0 | 0/3 | 0/3 | 0 |
| `oidcproxy-net` | analyzed | 0/0 | 1/1 | 1/1 | 0 |
| `razor-pages-ioptions-samples` | analyzed | 0/0 | 1/1 | 1/1 | 0 |
| **Total** | **10 analyzed; 0 load failures** | **5/5** | **26/29** | **26/29** | **0** |

The machine-readable result is [metrics.json](metrics.json). Its precision fields are:

```text
Precision protocol cases: 29
Precision protocol predictions: 26
Precision protocol true positives: 24
Precision protocol control blocking findings: 0
Precision protocol failed cases: 5
Blocking precision: 24/26 = 92.31% (FAIL)
Observed corpus precision: 5/5 = 100.00%
Supported-domain recall: 26/29 = 89.66%
All-labeled-static-key recall: 26/29 = 89.66%
Dynamic blocking findings: 0
Load failures: 0
Verdict: FAIL
```

The observed-corpus 5/5 value is retained as context; the Phase 0 precision claim uses the controlled protocol and its genuine 26-prediction denominator.

## Per-key missed-cause classification

The supported recall misses are not workspace failures: all ten projects loaded. They are three semantic analyzer misses in the selected `fullstackhero-webapi` project, where the hand labels treat literal arguments to a local required-value helper as supported `IConfiguration` indexer accesses. The analyzer does not perform that helper interprocedural propagation. No label was changed to improve the result.

Other labeled accesses outside the supported recall domain are recorded separately:

| Cause | Keys/accesses |
| --- | --- |
| Workspace/load failure | None; all ten selected projects analyzed |
| Genuinely unsupported pattern | `ConnectionStrings:DefaultConnection` in `aspnet-core-mvc`; `ConnectionStrings:SurveyConnectionString` in `bookcatalog-api`; `ConnectionStrings:DefaultConnection` in `dotnet-rpg`; `CatalogBaseUrl` in `eShopOnWeb` (`GetValue(Type, string)` overload) |
| Dynamic/unknown | Two nested outer indexer accesses in `eShopOnWeb` |
| Label error | Two precision-protocol primary locations were one line later in analyzer output: `OidcProxy` (`oidcproxy-net`, labeled line 10, observed line 9) and `AzureSettings` (`razor-pages-ioptions-samples`, labeled line 60, observed line 59) |
| Semantic analyzer miss | `DatabaseOptions:ConnectionString`, `CachingOptions:Redis`, and `JwtOptions:SigningKey` in `fullstackhero-webapi` at `Program.cs:31-33` |

The additional semantic-miss row is kept explicit because none of the four requested non-semantic causes would honestly describe these three loaded, statically labeled helper-indirection cases.

## Benchmark

The benchmark was regenerated once with one clean generated 50-project solution, one restore, and three guarded analyzer runs. Each run required exactly 1,251 observations. Command:

```powershell
pwsh -NoProfile -File .\scripts\Invoke-Phase0BPerformance.ps1 `
  -RepositoryRoot (Get-Location).Path `
  -ScratchRoot '<validated writable benchmark scratch root>' `
  -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/performance.json')
```

Raw guarded output tails:

```text
Run 1: Observations 1251; Declared surfaces 150; Duration 18021 ms; Peak working set 209518592 bytes
Run 2: Observations 1251; Declared surfaces 150; Duration 14498 ms; Peak working set 211910656 bytes
Run 3: Observations 1251; Declared surfaces 150; Duration 18141 ms; Peak working set 209588224 bytes
Benchmark scratch present after cleanup: False
```

The measured mean was 16,887 ms, sample variance 4,282,896 ms², and sample standard deviation 2,070 ms. The derived wall-clock bound is **21,100 ms**, using the greater of the observed maximum and mean plus two sample standard deviations, rounded up to 100 ms. The observed maximum working set was 211,910,656 bytes; the derived bound is **233,832,448 bytes**, using a 10% margin rounded up to the next MiB. These are regression bounds for the exact generated input and documented SDK/machine, not portable SLAs. Full records are in [performance.json](performance.json).

## Windows long-path preflight evidence

The effective Git setting was read with `git config --get core.longpaths`; it was unset, so the final corpus run exercised `short-root-fallback` with `LongPathsEnabled=True` and a 75-character resolved scratch root. Clone/fetch/checkout/status commands did not inject `core.longpaths=true`.

The negative preflight was also exercised with a resolved scratch root of 99 characters while the effective setting was unset. It failed before creating scratch state with:

```text
CONFIGGAP_LONG_PATH_PREREQUISITE: Windows long paths are unavailable and the effective scratch root is too long. Enable Windows LongPathsEnabled and Git core.longpaths, or provide a validated scratch root whose full path is at most 80 characters.
```

## Declaration-template evidence

The final run enumerated repository file names only and did not read `.env` or template contents. It found two candidate files and one repository with `.env.example`; none belonged to a selected application declaration policy. `.env.example` remains deferred as a first-class v1 surface. The evidence is [env-prevalence.json](env-prevalence.json).

## SHA proof

After committing the final source and evidence, the canonical `main` proof uses these four read-only commands on the same checkout and remote:

```powershell
git fetch origin
git rev-parse HEAD
git rev-parse origin/main
git ls-remote origin refs/heads/main
gh api repos/KeelMatrix/ConfigGap/commits/main --jq .sha
```

All four reported the same final commit SHA. No tag, GitHub Release, package publication, deployment, visibility change, workflow file, or private Actions run was performed.

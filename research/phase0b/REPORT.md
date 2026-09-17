# ConfigGap Phase 0B fix-round evidence

## Verdict

**FAIL.** The final clean corpus run analyzed all ten selected projects with zero load failures. Supported static recall was **29/29 = 100.00%** and all-labeled-static recall was **29/29 = 100.00%**. The controlled blocking-precision protocol produced a genuine denominator of **29 predictions** and measured **27/29 = 93.10%**, below the required 95% gate. Dynamic accesses produced zero blocking findings. This is a measured protocol FAIL, not a vacuous precision result and not a label-tuning exercise.

The Phase 0B section 7.6 park/narrow conditions are not triggered: dynamic access did not dominate, Options identification remained deterministic, and no suppression list or competitor evidence was introduced. R1 is closed; the remaining failure is two line-only precision cases in the unchanged fullstackhero labels. The binding stop rule applies after this final run; no further analyzer round is started.

## Implemented bounded fixes

- Clone and restore commands have a 600-second per-repository bound; analysis remains bounded at 120 seconds per repository, and the overall corpus run is bounded at 3,600 seconds. Scratch clones are removed in `finally`.
- `SemanticProbe` ignores only identified advisory workspace messages: package-vulnerability notices, framework lifecycle/EOL notices, the known implicit ASP.NET Core framework reference notice, the known OpenAPI analyzer deprecation notice, and the exact unsupported-target-framework tooling warning. Missing assets, unresolved references, compilation errors, and other workspace failures remain named fail-closed `CONFIGGAP_*` failures.
- Bounded same-compilation helper propagation follows a source-declared nonvirtual method or local function through a direct `string` parameter into a supported access, accepts literal/`const` call-site arguments, and stops at two hops. Reassignment, key-controlled branching, nonconstant calls, virtual/interface dispatch, cross-assembly resolution, and deeper chains remain `unknown`.
- Invocation locations use the called member name, so multiline `GetSection` and `BindConfiguration` accesses are reported on the source line containing the access method.
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
| Release compile build (`-p:RunAnalyzers=false`) | 0 warnings, 0 errors |
| Synthetic semantic patterns | 31/31 (original 26/26 retained) |
| `:`/`__` normalization fixtures | 2/2 |
| Options-section fixtures | 6/6 |
| Dynamic/unresolvable fixtures kept unknown | 8; no blocking classification |
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
Metric command duration: 50995 ms
Total corpus command duration: 245088 ms
Scratch clones present after cleanup: False
```

Raw final evaluator output:

```text
Analyzing aspnet-core-mvc (Webgentle.BookStore/Webgentle.BookStore/Webgentle.BookStore.csproj)
Analyzing bookcatalog-api (BookCatalog/BookCatalog.csproj)
Analyzing clean-architecture-jason-taylor (src/Web/Web.csproj)
Analyzing clean-architecture-manga (accounts-api/src/WebApi/WebApi.csproj)
Analyzing clean-minimal-api (Customers.Api/Customers.Api.csproj)
Analyzing dotnet-rpg (dotnet-rpg.csproj)
Analyzing eShopOnWeb (src/Web/Web.csproj)
Analyzing fullstackhero-webapi (src/Host/FSH.Starter.Api/FSH.Starter.Api.csproj)
Analyzing oidcproxy-net (integrationtests/Host.TestApps.OpenIdDict/Host.TestApps.OpenIdDict.csproj)
Analyzing razor-pages-ioptions-samples (DataAnnotatedValidationApplication/DataAnnotatedValidationApplication.csproj)
Blocking precision: 27/29 = 93.10% (FAIL)
Precision protocol: 27/29 cases passed; control blocking findings: 0; failed cases: 2
Observed corpus precision: 5/5 = 100.00%
Supported-domain recall: 29/29 = 100.00%
All-labeled-static-key recall: 29/29 = 100.00%
Dynamic blocking findings: 0
Load failures: 0
Verdict: FAIL
Restore skipped: False
Metric command duration: 50995 ms
Total corpus command duration: 245088 ms
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
| `fullstackhero-webapi` | analyzed | 0/0 | 3/3 | 3/3 | 0 |
| `oidcproxy-net` | analyzed | 0/0 | 1/1 | 1/1 | 0 |
| `razor-pages-ioptions-samples` | analyzed | 0/0 | 1/1 | 1/1 | 0 |
| **Total** | **10 analyzed; 0 load failures** | **5/5** | **29/29** | **29/29** | **0** |

The machine-readable result is [metrics.json](metrics.json). Its precision fields are:

```text
Precision protocol cases: 29
Precision protocol predictions: 29
Precision protocol true positives: 27
Precision protocol control blocking findings: 0
Precision protocol failed cases: 2
Blocking precision: 27/29 = 93.10% (FAIL)
Observed corpus precision: 5/5 = 100.00%
Supported-domain recall: 29/29 = 100.00%
All-labeled-static-key recall: 29/29 = 100.00%
Dynamic blocking findings: 0
Load failures: 0
Verdict: FAIL
```

The observed-corpus 5/5 value is retained as context; the Phase 0 precision claim uses the controlled protocol and its genuine 26-prediction denominator.

## Per-key missed-cause classification

The three prior supported recall misses in `fullstackhero-webapi` are closed by the bounded local-helper propagation. No label was changed.

Other labeled accesses outside the supported recall domain are recorded separately:

| Cause | Keys/accesses |
| --- | --- |
| Workspace/load failure | None; all ten selected projects analyzed |
| Genuinely unsupported pattern | `ConnectionStrings:DefaultConnection` in `aspnet-core-mvc`; `ConnectionStrings:SurveyConnectionString` in `bookcatalog-api`; `ConnectionStrings:DefaultConnection` in `dotnet-rpg`; `CatalogBaseUrl` in `eShopOnWeb` (`GetValue(Type, string)` overload) |
| Dynamic/unknown | Two nested outer indexer accesses in `eShopOnWeb` |
| Label/location evidence resolved | `OidcProxy` is labeled on `.GetSection` line 10 and `AzureSettings` on `.BindConfiguration` line 60; the analyzer now reports those member-name lines. |
| Remaining precision failures | `CachingOptions:Redis` and `JwtOptions:SigningKey` in `fullstackhero-webapi`: correct key/file, observed shared helper access line 31, labels retain lines 32/33 as required; no label was changed. |

The remaining precision row is kept explicit because the final run's only failures are source-line mismatches after the supported helper keys were resolved; this is not a dynamic or load failure and does not satisfy a section 7.6 park/narrow trigger.

## Pinned upstream line evidence

The two originally reported line mismatches were checked against their exact pinned upstream commits. The labels are correct; the analyzer location policy was corrected.

```text
https://raw.githubusercontent.com/oidcproxydotnet/OidcProxy.Net/c6a695c05e197fe294c1585100e1c25a899bd7b0/integrationtests/Host.TestApps.OpenIdDict/Program.cs
   9: var config = builder.Configuration
  10:     .GetSection("OidcProxy")
  11:     .Get<OidcProxyConfig>();

https://raw.githubusercontent.com/karenpayneoregon/razor-pages-IOptions-samples/48ad402ee7c476afbf0f8daf19c7e021d339984b/DataAnnotatedValidationApplication/Program.cs
  57:     private static void ValidateAzureSettings(WebApplicationBuilder builder)
  58:     {
  59:         builder.Services.AddOptions<AzureSettings>()
  60:             .BindConfiguration(nameof(AzureSettings))
  61:             .ValidateDataAnnotations()
```

The corresponding precision cases pass at lines 10 and 60 after reporting the called member name.

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

Candidate ref: abfe7dfa7949854a48acce7edc3239db67739423

The canonical `main` proof for that candidate uses these read-only commands on the same checkout and remote:

```powershell
git fetch origin
git rev-parse HEAD
git rev-parse origin/main
git ls-remote origin refs/heads/main
gh api repos/KeelMatrix/ConfigGap/commits/main --jq .sha
```

All SHA-producing commands reported the candidate ref, and `rev-list --left-right --count origin/main...HEAD` was `0 0`. No tag, GitHub Release, package publication, deployment, visibility change, workflow file, or private Actions run was performed.

Recorded output:

```text
HEAD:       abfe7dfa7949854a48acce7edc3239db67739423
origin/main: abfe7dfa7949854a48acce7edc3239db67739423
rev-list:   0 0
ls-remote:  abfe7dfa7949854a48acce7edc3239db67739423 refs/heads/main
GitHub API: abfe7dfa7949854a48acce7edc3239db67739423
```

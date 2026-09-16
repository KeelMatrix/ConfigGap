# ConfigGap Phase 0B fix-round evidence

## Verdict

**FAIL.** The corrected evaluator is fail-closed and the clean corpus run is reproducible, but the gate cannot pass while six pinned repositories fail restore preflight. The run reports 100.00% blocking precision over zero predictions, 4/29 (13.79%) recall in the supported static domain, 4/29 (13.79%) recall over all labeled static keys, zero dynamic blocking findings, and six named load failures. This is a trustworthy FAIL, not a tuned corpus result.

The synthetic fixture passes 26/26 pattern checks, normalization 2/2, Options behavior 6/6, and all five dynamic accesses remain `unknown` without a blocking finding. The failed corpus rows retain their label denominators and are never presented as clean zero-observation results.

## Corpus and independent labels

The checked-in corpus index is [corpus-index.json](corpus-index.json). It contains only public repository URLs, exact commit SHAs, and selection rationale. No cloned source is committed. The ten selected repositories and labels are unchanged from the prior review. Labels are independent manual truth data; they record source path, line, access kind, resolved key, ownership, and uncertainty disposition without values or secrets. The selected `.csproj` is the analysis unit; project references are loaded only to construct its compilation. Declaration truth is the repository-wide checked-in `appsettings*.json` graph.

## Diagnosis of the 13 originally missed static keys

All 13 originally missed keys are cause class **(a), workspace/compilation load failure**. In reviewed candidate `932bc8e804244871070f94a0052082b5670e2863`, four repositories returned zero observations. A direct `dotnet build --no-restore` on the pinned BookCatalog project reproduced `NETSDK1004` because `project.assets.json` was absent. The final restore-enabled run now reports the failure before analysis for BookCatalog and the other affected legacy projects; clean-minimal-api restores and analyzes its previously missed key at 1/1. There is therefore no evidence that these 13 misses are extractor gaps, unsupported-domain accesses, or label errors, and no labels were silently changed.

| Key | Repository; selected project | File and line | Exact access form | Cause | Evidence |
| --- | --- | ---: | --- | --- | --- |
| `JwtSecurityToken` | `bookcatalog-api`; `BookCatalog/BookCatalog.csproj` | `Program.cs:15` | `builder.Configuration.GetSection("JwtSecurityToken")` | (a) | Original run was zero-observation; direct no-restore build reproduced missing assets; final preflight is `CONFIGGAP_RESTORE_FAILURE`. |
| `AppSettings` | `bookcatalog-api`; `BookCatalog/BookCatalog.csproj` | `Helpers/ConfigHelper.cs:15` | `builder.Configuration.GetSection("AppSettings")` | (a) | Same BookCatalog assets failure; no analyzer result was treated as clean. |
| `JwtSecurityToken:Audience` | `bookcatalog-api`; `BookCatalog/BookCatalog.csproj` | `Helpers/AuthenticationHelper.cs:21` | `builder.Configuration["JwtSecurityToken:Audience"]` | (a) | Same BookCatalog assets failure. |
| `JwtSecurityToken:Issuer` | `bookcatalog-api`; `BookCatalog/BookCatalog.csproj` | `Helpers/AuthenticationHelper.cs:22` | `builder.Configuration["JwtSecurityToken:Issuer"]` | (a) | Same BookCatalog assets failure. |
| `JwtSecurityToken:Key` | `bookcatalog-api`; `BookCatalog/BookCatalog.csproj` | `Helpers/AuthenticationHelper.cs:23` | `builder.Configuration["JwtSecurityToken:Key"]` | (a) | Same BookCatalog assets failure. |
| `JwtSecurityToken:Subject` | `bookcatalog-api`; `BookCatalog/BookCatalog.csproj` | `Controllers/JWTokenController.cs:40` | `configuration["JwtSecurityToken:Subject"]` | (a) | Same BookCatalog assets failure. |
| `AuthenticationModule:AuthorityUrl` | `clean-architecture-manga`; `accounts-api/src/WebApi/WebApi.csproj` | `Modules/Common/AuthenticationExtensions.cs:43` | `configuration["AuthenticationModule:AuthorityUrl"]` | (a) | Original run was zero-observation; final restore preflight timed out and emitted a named failure. |
| `ASPNETCORE_BASEPATH` | `clean-architecture-manga`; `accounts-api/src/WebApi/WebApi.csproj` | `Modules/Common/ReverseProxyExtensions.cs:32` | `configuration["ASPNETCORE_BASEPATH"]` | (a) | Same selected-project restore timeout; the repeated Swagger access at line 100 has the same cause. |
| `PersistenceModule:DefaultConnection` | `clean-architecture-manga`; `accounts-api/src/WebApi/WebApi.csproj` | `Modules/SQLServerExtensions.cs:41` | `configuration.GetValue<string>("PersistenceModule:DefaultConnection")` | (a) | Same selected-project restore timeout. |
| `DatabaseOptions:ConnectionString` | `fullstackhero-webapi`; `src/Host/FSH.Starter.Api/FSH.Starter.Api.csproj` | `Program.cs:31` | `config[key]` through the local required-value helper | (a) | Original run was zero-observation; final restore preflight timed out and emitted a named failure. |
| `CachingOptions:Redis` | `fullstackhero-webapi`; `src/Host/FSH.Starter.Api/FSH.Starter.Api.csproj` | `Program.cs:32` | `config[key]` through the local required-value helper | (a) | Same selected-project restore timeout. |
| `JwtOptions:SigningKey` | `fullstackhero-webapi`; `src/Host/FSH.Starter.Api/FSH.Starter.Api.csproj` | `Program.cs:33` | `config[key]` through the local required-value helper | (a) | Same selected-project restore timeout. |
| `Database:ConnectionString` | `clean-minimal-api`; `Customers.Api/Customers.Api.csproj` | `Program.cs:16` | `config.GetValue<string>("Database:ConnectionString")` | (a), repaired | Original run had no assets and zero observations; the final run restored the project and detected 1/1. |

The repeated `JwtSecurityToken:Issuer`, `JwtSecurityToken:Audience`, and `JwtSecurityToken:Key` accesses in `JWTokenController.cs` are occurrences of already-counted normalized keys, not additional distinct missed keys. The four-cause classification was applied from the domain definition and build/workspace evidence, never inferred from analyzer silence.

## Fail-closed workspace behavior

`SemanticProbe` now throws named `CONFIGGAP_WORKSPACE_LOAD_FAILURE` or `CONFIGGAP_COMPILATION_LOAD_FAILURE` when MSBuildWorkspace reports an error, a selected project is missing, a compilation is null, or compiler errors prevent a valid compilation. The corpus evaluator records restore, analysis, and timeout failures as `load-failed`, preserves the corresponding label denominator, and exits non-zero. The regression command:

```powershell
pwsh -NoProfile -File .\scripts\Test-WorkspaceFailure.ps1
```

passes against a deliberately broken project and requires `CONFIGGAP_COMPILATION_LOAD_FAILURE`. The benchmark also fails on `CONFIGGAP_ZERO_OBSERVATIONS` or `CONFIGGAP_UNEXPECTED_OBSERVATIONS`.

## Clean Windows corpus run

The exact restore-enabled command was run from a fresh scratch root; `-SkipRestore` was not supplied:

```powershell
$scratchRoot = Join-Path $env:TEMP 'configgap-phase0b-run'
pwsh -NoProfile -File .\scripts\Invoke-Phase0BCorpus.ps1 `
  -RepositoryRoot (Get-Location).Path `
  -ScratchRoot $scratchRoot `
  -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/metrics.json') `
  -EnvEvidencePath (Join-Path (Get-Location).Path 'research/phase0b/env-prevalence.json')
```

The Windows preflight verified OS long-path support and Git `core.longpaths`; the script also accepts a validated scratch root with a full path of at most 80 characters when those prerequisites are unavailable. Each restore has a 30-second bound, each analysis has a 120-second repository bound, and all clones are removed in `finally`.

```text
Environment template files found: 2; repositories with .env.example: 1/10
ConfigGap Phase 0B corpus evaluation
Repository                       Status       TP/blocking  Supported recall  All-key recall  Dynamic blocking
------------------------------  -----------  -----------  -----------------  --------------  ----------------
aspnet-core-mvc                 load-failed      0/0             0/8              0/8                      0
bookcatalog-api                 load-failed      0/0             0/6              0/6                      0
clean-architecture-jason-taylor load-failed      0/0             0/1              0/1                      0
clean-architecture-manga        load-failed      0/0             0/3              0/3                      0
clean-minimal-api               analyzed         0/0             1/1              1/1                      0
dotnet-rpg                      analyzed         0/0             1/1              1/1                      0
eShopOnWeb                      load-failed      0/0             0/4              0/4                      0
fullstackhero-webapi            load-failed      0/0             0/3              0/3                      0
oidcproxy-net                   analyzed         0/0             1/1              1/1                      0
razor-pages-ioptions-samples    analyzed         0/0             1/1              1/1                      0

Blocking precision: 0/0 = 100.00%
Supported-domain recall: 4/29 = 13.79%
All-labeled-static-key recall: 4/29 = 13.79%
Dynamic blocking findings: 0
Load failures: 6
Verdict: FAIL
Restore skipped: False
Scratch clones present after cleanup: False
```

The machine-readable result is [metrics.json](metrics.json). The supported denominator is hand-labeled `supportedStatic=true`, `owner=application`, non-null key, and kind `indexer`, `get-value`, `section`, `required-section`, `options-bind`, or `options-bind-configuration`. It excludes dynamic/unresolvable accesses, unsupported/provider-specific forms, and framework-owned keys under nugget section 8. The all-labeled denominator is the same non-null static label set before that supported-kind/framework-owned exclusion. Both memberships are label-defined, never analyzer-defined.

## Options and framework-owned negative coverage

Options binding now requires the resolved symbol to be the Microsoft `OptionsBuilder<T>.BindConfiguration` extension in `Microsoft.Extensions.DependencyInjection`, with receiver type from `Microsoft.Extensions.Options`. The permanent impostor fixture defines a user `OptionsBuilder<T>.BindConfiguration`; it remains `unknown`. The real Microsoft Options fixture remains recognized. The fixture run reports Options behavior 6/6.

`FrameworkOwnedMissing.cs` accesses `Kestrel:Endpoints:Https:Url` without declaring it. The evaluator reports it as `framework-owned`, not a blocking missing key. A declared framework-owned key remains classified as framework-owned as well. The explicit inventory is implemented in `FrameworkOwnedKeys`; application sections are not suppressed by name resemblance alone.

## Benchmark protocol and bound

The clean benchmark sequence was:

```powershell
$scratchRoot = Join-Path $env:TEMP 'configgap-phase0b-benchmark'
$synthetic = Join-Path $scratchRoot 'synthetic-50'
New-Item -ItemType Directory -Force $scratchRoot | Out-Null
dotnet run --project .\tools\ConfigGap.Probe\ConfigGap.Probe.csproj -c Release --no-build -- `
  --generate-synthetic $synthetic --repository-root . --project-count 50
dotnet restore (Join-Path $synthetic 'ConfigGap.Synthetic.sln') --nologo --verbosity quiet
dotnet run --project .\tools\ConfigGap.Probe\ConfigGap.Probe.csproj -c Release --no-build -- `
  --analyze-only --repository-root $synthetic `
  --solution (Join-Path $synthetic 'ConfigGap.Synthetic.sln') `
  --expected-observations 1251 `
  --output (Join-Path $scratchRoot 'performance-1.json')
dotnet run --project .\tools\ConfigGap.Probe\ConfigGap.Probe.csproj -c Release --no-build -- `
  --analyze-only --repository-root $synthetic `
  --solution (Join-Path $synthetic 'ConfigGap.Synthetic.sln') `
  --expected-observations 1251 `
  --output (Join-Path $scratchRoot 'performance-2.json')
```

Restore exited 0; the generated solution contained 51 project entries and 51 unique project GUIDs, and both guarded runs observed exactly 1,251 accesses:

| Run | Observations | Declared surfaces | Wall clock | Peak working set |
| --- | ---: | ---: | ---: | ---: |
| Primary | 1,251 | 150 | 16,905 ms | 215,400,448 bytes |
| Repeat | 1,251 | 150 | 16,401 ms | 209,768,448 bytes |

The mean was 16,653 ms; the absolute difference was 504 ms (2.98% from primary). The regenerated bound is `<=16,905 ms` and `<=215,400,448 bytes` for this exact generated solution and machine, not a portable SLA. Complete machine-readable records are in [performance.json](performance.json).

## `.env.example` evidence and decision

The script enumerated file names repository-wide at each pinned commit without reading `.env` or any template contents. It found two candidate files: `clean-architecture-jason-taylor/src/Web/ClientApp-React/.env` (actual environment file, excluded) and `fullstackhero-webapi/deploy/docker/.env.example` (one `.env.example`, outside the selected application directory). Thus the measured prevalence is **1/10 repositories**, and **0/10 selected application declaration policies**. The evidence is [env-prevalence.json](env-prevalence.json).

`.env.example` remains deferred as a first-class v1 declaration surface. Actual `.env` files are out of scope and were not read.

## Gate mapping

| Specification gate | Evidence | Result |
| --- | --- | --- |
| Blocking precision >=95% | 0/0 = 100.00% | PASS, vacuous because load failures left no predictions |
| Supported-domain recall >=90% | 4/29 = 13.79% | FAIL |
| All-labeled-static recall | 4/29 = 13.79% | Reported separately; not used to hide exclusions |
| Dynamic access causes zero blocking findings | 0 | PASS |
| Fixture normalization and Options behavior | 2/2 normalization; 6/6 Options; 26/26 patterns | PASS |
| Workspace/load-failure safety | Broken fixture names compilation failure; 6 restore failures retained | PASS for fail-closed behavior; corpus gate FAIL |
| Practical ~50-project performance | 16,905 ms maximum; 215,400,448-byte maximum | PASS as a measured seconds-scale bound |
| `.env.example` research | 1/10 repository-wide; 0/10 selected app policies | Decision recorded: defer |
| Overall fix-round verdict | Six load failures and recall below threshold | FAIL |

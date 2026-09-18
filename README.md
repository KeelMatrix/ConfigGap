# ConfigGap

ConfigGap is a .NET configuration dependency analyzer. The shipping command uses a bounded Roslyn/MSBuild analysis core to compare statically used configuration keys with checked-in declaration surfaces.
The reusable analysis core is non-packable; the shipping command is in `src/KeelMatrix.ConfigGap` and the Phase 0 regression harness remains in `tools/ConfigGap.Probe`.

The core loads projects through `MSBuildWorkspace`, resolves supported `IConfiguration` and Options patterns with Roslyn symbols, normalizes `:` and environment-variable `__` hierarchy separators, and classifies drift conservatively:

- `ERROR` `CG001` — a statically known application key is absent from configured declaration surfaces.
- `WARNING` `CG002` — a declared example leaf key was not observed by analyzed code.
- `INFO` `CG900` — an access could not be resolved statically. Dynamic means unknown, never missing.

`ConfigGapReport` is schema version 1 and contains findings, counts, stable source locations, and key names only. Exit-code-2 reports also include a stable, sanitized recovery message. Reports never contain declaration values, source content, or paths. `ConfigGapExitCode` is `0` for trustworthy analysis without blocking findings, `1` for trustworthy analysis with blocking findings, and `2` when analysis could not run trustworthily.
## Check a repository

The frozen CLI contract is:

```text
configgap check [--solution <path>] [--project <path>] [--config <path>] [--format text|json]
```

From a repository containing one solution, the first useful command is:

```bash
configgap check
```

`--solution` selects the solution. `--project` selects one project from that solution, or analyzes the project directly when no solution is available. `--config` selects one supported declaration surface directly (`appsettings*.json` or `.env.example`) or a version 1 ConfigGap surface file. `--format text` is the default; `--format json` emits the versioned deterministic local report.

A surface file contains only relative supported-surface paths:

```json
{
  "version": 1,
  "declarationSurfaces": [
    "src/Payments/appsettings.json",
    "src/Payments/.env.example"
  ]
}
```

Without `--config`, ConfigGap reads the structure of repository `appsettings*.json` files and `.env.example` files, excluding `bin`, `obj`, and `artifacts`. It never requires or reads actual `.env` files. JSON values are ignored; environment-template values are ignored. Declaration paths must remain inside the repository.

## Findings and exit codes

`CG001 used-but-undeclared` is a blocking error for a statically resolved application key absent from the configured declaration surfaces. The text diagnostic includes the key, repository-relative source location, and explanation. `CG900 dynamic-key-unverified` is informational: dynamic or unsupported key expressions remain unknown and never create a blocking finding.

Exit codes are stable:

```text
0  Complete trustworthy analysis with no blocking CG001 finding.
1  Complete trustworthy analysis with one or more CG001 findings.
2  Workspace, configuration, parse, option, or unsupported execution failure.
```

Exit `0` is never returned for a workspace or declaration-load failure. JSON reports contain `version`, `trustworthyAnalysis`, `exitCode`, project/file counts, declaration-surface paths, sorted findings, and bounded diagnostics. They do not contain configuration values or source contents. Reports are written only to the local output stream; ConfigGap does not upload them.

Analysis is bounded to 120 seconds, at most 128 declaration surfaces, and 1 MiB per declaration file. Use `--project` or a version 1 `--config` surface file to narrow a large workspace.

Supported static key forms are literals, `const` values, static concatenations, and static interpolations in the supported `IConfiguration`/Options patterns. `:` and environment `__` are normalized case-insensitively. Required, bindable, and actually read keys are distinct; v1 reports supported statically used keys and does not infer requiredness from writable Options properties. Dynamic is unknown, not broken.

After a complete trustworthy analysis, ConfigGap requests anonymous activation and at-most-weekly heartbeat telemetry through `KeelMatrix.Telemetry`. Only coarse buckets and local/CI class are prepared by ConfigGap; keys, values, section names, filenames, paths, project names, source snippets, and report contents are never passed to telemetry. Telemetry is best-effort and cannot change the analysis result. Set `KEELMATRIX_NO_TELEMETRY=1` for local or CI validation.


## Declaration configuration

Without a `.configgap.json` file, the core reads `appsettings.json` files only. Environment-specific appsettings files and declaration templates are opt-in. A configuration file is versioned and resolves paths relative to its own directory:

```json
{
  "version": 1,
  "frameworkOwnedPolicy": "exclude",
  "declarationSurfaces": [
    { "kind": "json", "path": "appsettings.json" },
    { "kind": "json", "path": "appsettings.Production.json" },
    { "kind": "template", "path": ".env.example" }
  ]
}
```

Template parsing records names only. Actual `.env` files are never read by the default policy and are rejected as declaration templates. See [`configgap-config.schema.json`](docs/configgap-config.schema.json) and [`configgap-report.schema.json`](docs/configgap-report.schema.json).

## Run the regression harness

From the repository root:

```powershell
dotnet restore KeelMatrix.ConfigGap.sln
dotnet build KeelMatrix.ConfigGap.sln -c Release
dotnet test tests/KeelMatrix.ConfigGap.Core.Tests/KeelMatrix.ConfigGap.Core.Tests.csproj -c Release
dotnet run --project tools/ConfigGap.Probe/ConfigGap.Probe.csproj -c Release -- --solution KeelMatrix.ConfigGap.sln --repository-root . --output artifacts/probe-results.json
```

The probe remains a non-packable host for the hand-labeled Phase 0 corpus. Its semantic loader fails closed on missing compilations, compiler errors, workspace failures, missing selected projects, and zero-observation benchmarks. Supported static resolution is limited to literals, `const` values, static concatenations/interpolations, bounded local `IConfigurationSection` aliases without reassignment, and source-declared direct-string helper propagation through at most two same-compilation hops. Unsupported, dynamic, virtual/interface, reassigned, branched, and deeper helper patterns remain unknown.

The real-corpus and performance evidence scripts remain under `scripts/`. Their reports are evidence for the exercised input and machine, not a production maximum or portable SLA.
The semantic loader fails closed. A missing compilation, compiler error, MSBuildWorkspace failure, missing selected project, or zero-observation benchmark produces a named `CONFIGGAP_*` diagnostic and a non-zero exit instead of a clean result. The workspace regression can be run with:

```powershell
pwsh -NoProfile -File .\scripts\Test-WorkspaceFailure.ps1
```

The Options binding fixture requires the resolved method to be the Microsoft `OptionsBuilder<T>.BindConfiguration` extension. A user-defined type with the same name is retained as `unknown`.

Supported key resolution is deliberately bounded to string literals, `const` values, statically resolvable concatenations, and interpolations whose parts are statically resolvable. Variables, parameters, method calls, computed values, and configuration-supplied key expressions are reported as `unknown`.

The probe also follows a frozen local-helper boundary: inside the same compilation, a non-virtual source method or local function may forward one `string` parameter directly into one supported indexer, `GetValue<T>`, `GetSection`, `GetRequiredSection`, or framework `BindConfiguration` access. A call-site argument is eligible only when it is a string literal or `const`; the parameter must not be reassigned, used to choose a branch, or routed through field/property/collection state. At most two local-helper hops are followed. Interface/virtual dispatch, cross-assembly helpers, out-of-depth chains, and non-constant call sites remain `unknown` and never create a blocking finding. Resolved observations are anchored at the helper call site where the static key argument is supplied.

The workspace path is registered with `Microsoft.Build.Locator`, and fixture projects are opened with `Microsoft.CodeAnalysis.Workspaces.MSBuild` so project references and compilation references are resolved by MSBuild rather than by regular-expression scanning. The Options fixture uses the framework's `BindConfiguration` API rather than a local substitute.

The pinned real-repository evaluation is reproducible from a clean scratch directory. It clones and restores every selected project before evaluation, records per-repository clone/restore/load failures, bounds clone and restore at 600 seconds and analysis at 120 seconds per repository, and bounds the overall run at 3,600 seconds. All scratch clones are removed in `finally`, and the command exits non-zero for any load failure:

```powershell
$scratchRoot = Join-Path $env:TEMP 'configgap-phase0b-run'
pwsh -NoProfile -File .\scripts\Invoke-Phase0BCorpus.ps1 `
  -RepositoryRoot (Get-Location).Path `
  -ScratchRoot $scratchRoot `
  -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/metrics.json') `
  -EnvEvidencePath (Join-Path (Get-Location).Path 'research/phase0b/env-prevalence.json')
```

On Windows, the script reads the effective Git `core.longpaths` setting without injecting a value, combines it with the OS setting and the actual resolved scratch-root length, and uses a documented short-root fallback when either long-path prerequisite is unavailable. It fails fast with an actionable `CONFIGGAP_LONG_PATH_PREREQUISITE` diagnostic if neither path is valid. The report includes supported-domain recall, all-labeled-static-key recall, the controlled precision protocol, dynamic-blocking count, status for every repository, and load-failure count.

The blocking precision protocol is predeclared and non-vacuous. For each distinct hand-labeled supported static application key, the evaluator synchronizes a declaration graph containing the labeled keys, removes exactly that key for one variant, and requires one blocking finding at the key's primary labeled location. A no-removal control variant must produce zero blocking findings. The precision claim requires at least 10 observed blocking predictions; below that denominator it reports `UNVERIFIED` and fails the gate. Dynamic and unresolvable accesses remain unknown and are never made blocking.

The tracked evidence report uses a symbolic candidate reference (`HEAD`) so the report cannot become self-stale when its own commit changes. Verify it with:

```powershell
pwsh -NoProfile -File .\scripts\Test-ReportCandidateRef.ps1
```

The performance protocol uses a clean generated solution, restores it once, and performs at least three guarded measurements. The checked-in bound is derived from the observed maximum and the mean plus two sample standard deviations, with the named margin and rounding recorded in `research/phase0b/performance.json`:

```powershell
pwsh -NoProfile -File .\scripts\Invoke-Phase0BPerformance.ps1 `
  -RepositoryRoot (Get-Location).Path `
  -ScratchRoot (Join-Path $env:TEMP 'configgap-phase0b-benchmark') `
  -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/performance.json')
```

Generated solution project GUIDs are unique, including the support project. A performance report is a measured regression bound for the exact generated input and machine, not a portable SLA.

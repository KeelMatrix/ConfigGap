# ConfigGap

This repository contains a bounded Roslyn feasibility probe for ConfigGap, a proposed .NET configuration dependency analyzer.

The probe loads the checked-in fixture solution through `MSBuildWorkspace`, resolves supported configuration and Options access patterns with Roslyn symbols, normalizes `:` and environment-variable `__` hierarchy separators, and compares the results with synthetic declaration files. It emits a deterministic JSON result file and a human-readable table.

The harness is intentionally non-packable and non-shipping. It is not the ConfigGap product: it has no product CLI, drift engine, diagnostics catalog, report contract, or telemetry.

## Run the corpus

From the repository root:

```powershell
dotnet run --project tools/ConfigGap.Probe/ConfigGap.Probe.csproj -c Release --no-build -- --solution KeelMatrix.ConfigGap.sln --repository-root . --output artifacts/probe-results.json
```

The command exits non-zero if a hand-labeled fixture does not match the observed semantic result. The JSON result is written under `artifacts/`, which is ignored by Git.

The semantic loader fails closed. A missing compilation, compiler error, MSBuildWorkspace failure, missing selected project, or zero-observation benchmark produces a named `CONFIGGAP_*` diagnostic and a non-zero exit instead of a clean result. The workspace regression can be run with:

```powershell
pwsh -NoProfile -File .\scripts\Test-WorkspaceFailure.ps1
```

The Options binding fixture requires the resolved method to be the Microsoft `OptionsBuilder<T>.BindConfiguration` extension. A user-defined type with the same name is retained as `unknown`.

Supported key resolution is deliberately bounded to string literals, `const` values, statically resolvable concatenations, and interpolations whose parts are statically resolvable. Variables, parameters, method calls, computed values, and configuration-supplied key expressions are reported as `unknown`.

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

The performance protocol uses a clean generated solution, restores it once, and performs at least three guarded measurements. The checked-in bound is derived from the observed maximum and the mean plus two sample standard deviations, with the named margin and rounding recorded in `research/phase0b/performance.json`:

```powershell
pwsh -NoProfile -File .\scripts\Invoke-Phase0BPerformance.ps1 `
  -RepositoryRoot (Get-Location).Path `
  -ScratchRoot (Join-Path $env:TEMP 'configgap-phase0b-benchmark') `
  -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/performance.json')
```

Generated solution project GUIDs are unique, including the support project. A performance report is a measured regression bound for the exact generated input and machine, not a portable SLA.

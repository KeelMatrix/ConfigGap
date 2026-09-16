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

Supported key resolution is deliberately bounded to string literals, `const` values, statically resolvable concatenations, and interpolations whose parts are statically resolvable. Variables, parameters, method calls, computed values, and configuration-supplied key expressions are reported as `unknown`.

The workspace path is registered with `Microsoft.Build.Locator`, and fixture projects are opened with `Microsoft.CodeAnalysis.Workspaces.MSBuild` so project references and compilation references are resolved by MSBuild rather than by regular-expression scanning.

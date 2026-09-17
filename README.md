# ConfigGap

ConfigGap contains the reusable, non-packable analysis core for detecting high-confidence drift between statically used .NET configuration keys and checked-in declaration surfaces. The core is consumed by the future tool host; this repository does not yet ship a CLI or package.

The core loads projects through `MSBuildWorkspace`, resolves supported `IConfiguration` and Options patterns with Roslyn symbols, normalizes `:` and environment-variable `__` hierarchy separators, and classifies drift conservatively:

- `ERROR` `CG001` — a statically known application key is absent from configured declaration surfaces.
- `WARNING` `CG002` — a declared example leaf key was not observed by analyzed code.
- `INFO` `CG900` — an access could not be resolved statically. Dynamic means unknown, never missing.

`ConfigGapReport` is schema version 1 and contains findings, counts, stable source locations, and key names only. It never contains declaration values. `ConfigGapExitCode` is `0` for trustworthy analysis without blocking findings, `1` for trustworthy analysis with blocking findings, and `2` when analysis could not run trustworthily.

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

The probe remains a non-packable host for the hand-labeled Phase 0 corpus. Its semantic loader fails closed on missing compilations, compiler errors, workspace failures, missing selected projects, and zero-observation benchmarks. Supported static resolution is limited to literals, `const` values, static concatenations/interpolations, and source-declared direct-string helper propagation through at most two same-compilation hops. Unsupported, dynamic, virtual/interface, reassigned, branched, and deeper helper patterns remain unknown.

The real-corpus and performance evidence scripts remain under `scripts/`. Their reports are evidence for the exercised input and machine, not a production maximum or portable SLA.

# ConfigGap Probe Development Guide

## Navigation

- `tools/ConfigGap.Probe` contains the non-shipping MSBuildWorkspace harness and deterministic report writer.
- `fixtures/FixtureConsumer` contains the labeled C# access-pattern corpus and declaration files.
- `fixtures/FixtureSupport` contains the referenced-project extension and Options fixture support.
- `fixtures/expected.json` contains hand-written expected labels for every pattern.
- `artifacts/` is disposable probe output and is ignored by Git.

## Commands

Restore and build the solution:

```powershell
dotnet restore KeelMatrix.ConfigGap.sln
dotnet build KeelMatrix.ConfigGap.sln -c Release --no-restore
```

Run the complete fixture corpus:

```powershell
dotnet run --project tools/ConfigGap.Probe/ConfigGap.Probe.csproj -c Release --no-build -- --solution KeelMatrix.ConfigGap.sln --repository-root . --output artifacts/probe-results.json
```

## Invariants

- The harness is non-packable and does not implement the ConfigGap product.
- Fixture labels are hand-written and must not be generated from observed output.
- Supported key resolution is limited to literals, `const` values, static concatenations, and static interpolations.
- Dynamic or unresolvable keys remain `unknown`; they are never treated as missing.
- Reports contain keys and classifications only; fixture values and source contents are not emitted.
- No workflow, telemetry, release, or package behavior belongs in this probe repository.

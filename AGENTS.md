# ConfigGap development guide

## Navigation

- `src/KeelMatrix.ConfigGap` contains the shipping `configgap` .NET tool.
- `src/KeelMatrix.ConfigGap.Core` contains the non-packable Roslyn/MSBuild analysis core.
- `tests/KeelMatrix.ConfigGap.Tests` contains CLI, report, privacy, and consumer contracts.
- `tests/KeelMatrix.ConfigGap.Core.Tests` contains focused analysis tests.
- `tests/FixtureClean` is the ASP.NET Core consumer used by the package smoke.
- `tools/ConfigGap.Probe` and `fixtures/` contain the labeled analysis corpus.
- `docs/` contains the configuration and report schemas plus user troubleshooting.
- `scripts/` contains package inspection, deterministic pack, and isolated consumer checks.

## Commands

```powershell
dotnet restore KeelMatrix.ConfigGap.sln --configfile NuGet.config
dotnet build KeelMatrix.ConfigGap.sln -c Release --no-restore
dotnet test KeelMatrix.ConfigGap.sln -c Release --no-build --no-restore
dotnet pack src/KeelMatrix.ConfigGap/KeelMatrix.ConfigGap.csproj -c Release --no-build --no-restore --include-symbols -p:SymbolPackageFormat=snupkg -o artifacts/packages
pwsh -NoProfile -File .\scripts\Inspect-Package.ps1 -PackagePath .\artifacts\packages\KeelMatrix.ConfigGap.0.1.0.nupkg -SymbolsPath .\artifacts\packages\KeelMatrix.ConfigGap.0.1.0.snupkg
pwsh -NoProfile -File .\scripts\Invoke-PackageSmoke.ps1 -PackagePath .\artifacts\packages\KeelMatrix.ConfigGap.0.1.0.nupkg
```

Keep package contents limited to the shipping tool, its runtime dependencies, README, license, icon, nuspec metadata, and symbols. Keep all other projects explicitly non-packable.

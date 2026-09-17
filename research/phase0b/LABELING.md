# Phase 0B corpus labeling protocol

The evaluation unit is one named, checked-in application project per repository. The selected project is recorded in its label file and is the only project whose source documents are analyzed by the probe; MSBuild may load project references only to construct the selected project's compilation. Declaration surfaces are the repository's checked-in `appsettings*.json` files, as required by the Phase 0A declaration graph. No analyzer output was used to create these labels.

For each selected project, a reviewer manually read every C# source file containing `IConfiguration` access syntax (`IConfiguration` indexers, `GetValue`, `GetSection`, `GetRequiredSection`, `GetChildren`, `Bind`, `BindConfiguration`, and `GetConnectionString`) and every checked-in appsettings surface relevant to that project. Literal, `const`, `nameof`, concatenated, and statically interpolated keys are `supportedStatic: true`; unknown expressions, nested configuration-derived keys, and APIs outside the Phase 0A detector are recorded with `supportedStatic: false`. A repeated access is retained as a separate audit item, while the metric reduces keys to distinct normalized keys per repository. `__` is normalized to `:` only by the evaluator's documented key normalizer.

The labels contain source path, line, optional column for nested accesses, access kind, resolved key where manually known, certainty, and an uncertainty disposition. Values and secrets are intentionally excluded. `owner: framework` is used for framework/provider-owned roots and those items are excluded from application precision/recall. `GetConnectionString` is listed as outside the Phase 0A detector and is not used as evidence for the static-domain gate.

## Primary-location policy

For a supported static key reached through a helper, the literal or `const` call site is the primary location. The shared helper's `IConfiguration` access is implementation context rather than a distinct per-key location; it may be retained as secondary metadata, but it must not replace the call-site primary location. Labels for helper-mediated accesses therefore record the line containing the helper invocation.

The selected project scope is a reproducible analyzability boundary, not a corpus filter based on results. It avoids Docker project files, unsupported solution formats, and repository-wide sample collections while preserving a real application project and its checked-in declarations. Repositories considered but excluded before labeling are listed at the end of this file.

## Excluded candidates

- `https://github.com/dotnet-architecture/eShopModernizing` — checked-out commit had no checked-in `appsettings*.json` surface.
- `https://github.com/Boeschenstein/aspnetcore3-configuration` — checked-out commit had no checked-in `appsettings*.json` surface.
- `https://github.com/thangchung/clean-architecture-dotnet` — the selected commit pins `6.0.100-preview.5.21302.13`, which is not installed on the validation machine; MSBuildWorkspace could not produce trustworthy project analysis.
- `https://github.com/LincolnLink/ASP.NET-Core-Eduardo-Pires-WebApi` — the selected repository project is under a nested `global.json` pin to SDK `5.0.408`, which is not installed on the validation machine; MSBuildWorkspace could not produce trustworthy project analysis.
- `https://github.com/dotnet-podcasts/dotnet-podcasts` — the shallow checkout at the candidate head was incomplete and did not provide a verifiable appsettings-backed application corpus.

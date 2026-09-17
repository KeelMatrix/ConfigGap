# Analysis core

The reusable analysis core is the non-packable `KeelMatrix.ConfigGap.Core` project. `ConfigGap.Probe` is a thin host that keeps the hand-labeled Phase 0 fixture, corpus, and performance harness runnable.

## Supported evidence

The extractor uses Roslyn symbols and `MSBuildWorkspace`; it does not scan source with regular expressions. It resolves literals, `const` values, static concatenations/interpolations, bounded local `IConfigurationSection` aliases initialized from statically known section paths, and source-declared non-virtual direct `string` helper propagation through at most two same-compilation hops. Supported consumers are configuration indexers, `GetValue<T>`, `GetSection`, `GetRequiredSection`, Options `Configure`/`Bind`, `BindConfiguration`, and statically known configuration-section binding. Unknown or unsupported access remains `CG900` informational.

Helper propagation is intentionally limited to methods and local functions declared in the compilation being analyzed. A helper declared in a referenced project is not propagated through its consuming call site: its parameterized body remains unknown and no key is inferred from the cross-project call. This is a conservative v1 boundary; use a direct access or a same-compilation helper when the key must be statically verified.

The report keeps these concepts separate:

- `knownKeys` — all statically known keys or sections;
- `bindableKeys` — statically identified Options ownership/binding sections;
- `requiredKeys` — `GetRequiredSection` evidence, including sections used directly for Options binding;
- `actuallyReadKeys` — indexer and `GetValue<T>` evidence.

A writable Options property does not make a key required.

## Configuration surfaces

The default declaration graph discovers `appsettings.json` only. `.configgap.json` explicitly opts in environment-specific JSON and generic declaration templates. JSON values are parsed only to walk property names; values are never stored in the graph or report. Template parsing stores names before `=` only and records normalized ancestor sections so a template-only section binding can be matched. Actual `.env` files are not read by default and cannot be configured as templates.

## Version and API revalidation

The implementation was built and tested with:

- .NET SDK `8.0.425` (`Microsoft.NET.Sdk`, MSBuild `17.11.48`), RID `win-x64`;
- `Microsoft.Build.Locator` `1.7.8`;
- `Microsoft.CodeAnalysis.CSharp.Workspaces` `4.14.0`;
- `Microsoft.CodeAnalysis.Workspaces.MSBuild` `4.14.0`.

The loader registers the SDK selected by `global.json` when available, opens solutions/projects with `MSBuildWorkspace`, checks compilation diagnostics before extraction, and fails closed on workspace/load errors. Only known advisory workspace diagnostics are ignored; unresolved references and compilation errors remain analysis failures. Exit-code-2 reports include a stable, sanitized recovery message alongside the failure code; paths, source content, and configuration values are not retained.

The JSON configuration and report contracts are version 1. Their schemas are committed in [`configgap-config.schema.json`](configgap-config.schema.json) and [`configgap-report.schema.json`](configgap-report.schema.json). Report ordering is stable by source, line, column, finding code, and key.

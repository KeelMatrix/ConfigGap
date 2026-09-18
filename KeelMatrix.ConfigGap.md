# KeelMatrix.ConfigGap product contract

ConfigGap is a local .NET tool that compares statically used configuration keys with explicitly declared repository configuration surfaces. It is a source-to-declaration check, not runtime validation and not a secret scanner.

## Supported v1 surface

- .NET 8 projects loaded through `MSBuildWorkspace`.
- `IConfiguration` indexer, `GetValue<T>`, `GetSection`, and `GetRequiredSection` when keys are literals or supported static expressions.
- Statically known Options section ownership, including framework `BindConfiguration("Section")`.
- `appsettings.json`, explicitly listed environment-specific JSON files, and explicitly listed `.env.example`-style name templates.
- Deterministic text and JSON reports with exit codes `0`, `1`, and `2`.

## Classification

- A known used key missing from configured declarations is a blocking `CG001` error.
- A dynamic or unresolvable access is `CG900` informational and remains unknown.
- Framework-owned configuration is excluded from application drift errors.
- Configuration values are never required for the analysis.

## Out of scope

Runtime startup validation, secret-value scanning, arbitrary providers, remote configuration, deployment manifests, hosted inventory, automatic file changes, and cloud-specific configuration services are outside the v1 contract.

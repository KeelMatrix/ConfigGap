# Capabilities and limits

ConfigGap compares statically resolved .NET configuration usage with the repository surfaces selected by its configuration file.

## Supported behavior

- Literal and supported constant `IConfiguration` indexer, `GetValue<T>`, `GetSection`, and `GetRequiredSection` access when receiver root provenance is established from root configuration types, known framework root properties, proven local/field/property aliases, or proven-root helper call-site arguments.
- Supported Options section ownership and binding forms, including `BindConfiguration`.
- `appsettings.json`, explicitly selected environment-specific JSON, and explicitly listed environment-name templates.
- Deterministic `:` and `__` hierarchy normalization.
- Text and versioned JSON reports with stable exit codes.

## Limits

Dynamic or unresolvable access is reported as informational (`CG900`), not as a missing-key error. A blocking `CG001` result requires a statically known application key that is absent from the configured declaration surfaces. Declared but unobserved example keys are non-blocking `CG002` warnings.

An `IConfiguration` parameter, property, or field that may hold a section or whose provenance cannot be established is reported as `CG900`; ConfigGap does not invent a root key for it. Local aliases initialized directly from a known `GetSection` call retain that section scope.

ConfigGap does not validate runtime startup state, inspect secret values, read actual `.env` files by default, or analyze Kubernetes, Helm, Terraform, Docker Compose, cloud parameter stores, hosted inventories, arbitrary custom providers, remote configuration, deployment manifests, or automatic configuration changes.

Analysis stays local. Key names may reveal architecture, so review reports before sharing them. Optional telemetry uses the shared activation/heartbeat contract and never receives keys, values, paths, or report contents. See [`PRIVACY.md`](../PRIVACY.md) for the data boundary and controls.

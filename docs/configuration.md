# Configuration reference

ConfigGap reads declaration structure, not values. Pass a version 1 configuration file with `--config` when the default `appsettings.json` discovery is not enough.

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

Paths are relative to the configuration file and must stay inside the repository. JSON values are ignored. Template parsing records names only. Actual `.env` files are rejected as declaration templates and are not needed by the default policy.

## Hierarchy

ConfigGap normalizes configuration hierarchy case-insensitively. The environment name `Payments__Provider` maps to the logical key `Payments:Provider`. JSON nesting produces the same logical path.

## Access and Options

The supported static forms are:

```csharp
configuration["Section:Key"];
configuration.GetValue<string>("Section:Key");
configuration.GetSection("Section");
configuration.GetRequiredSection("Section");
```

Literal reads are supported only when the receiver's root provenance is statically established. Supported provenance includes a root configuration type (`IConfigurationRoot`, `IConfigurationManager`, or their known concrete implementations), a known framework root `Configuration` property, a local/field/property initialized from one of those expressions, and a same-compilation helper parameter whose visible call-site arguments are proven roots. A direct `IConfiguration` parameter remains supported when no visible same-compilation call site establishes a different section scope; a section or otherwise unproven parameter/property/field produces `CG900` instead of an invented root key.

For example, `Read(root.GetSection("Payments"))` is not treated as a root receiver when `Read` accepts `IConfiguration`; its access is reported as `CG900` unless the receiver's provenance can be proven. A local alias initialized directly from `GetSection("Payments")` retains its known section scope.

Supported Options section ownership uses statically known sections and the framework `BindConfiguration("Section")` extension. A property being writable does not make its key required. A key can be known or bindable without being read by code; those concepts are kept separate.

Dynamic and unsupported expressions are reported as unknown information. They never become a missing-key error solely because the expression is dynamic.

## Framework-owned keys

Framework-owned configuration is excluded from application drift errors. ConfigGap does not claim that every framework key is an application declaration requirement.

## Reports and exit codes

- `CG001` is an `ERROR` and returns `1`.
- `CG900` is an `INFO` and does not block.
- Workspace, parsing, selection, and configuration failures return `2`.
- `0` means a trustworthy analysis completed without a blocking finding.

The CLI report schema is [`configgap-cli-report.schema.json`](configgap-cli-report.schema.json). Reports include key names and locations needed to explain a gap, but never configuration values. The reusable core contract is documented separately in [`analysis-core.md`](analysis-core.md).

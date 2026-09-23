# KeelMatrix.ConfigGap

Runtime validation checks startup state. ConfigGap checks earlier whether statically used .NET configuration keys are represented in the repository's declared configuration structure.

Install the .NET 8 tool:

```bash
dotnet tool install --global KeelMatrix.ConfigGap
```

Run it from an ASP.NET Core repository:

```bash
configgap check
```

Use `--solution`, `--project`, `--config`, and `--format text|json` to select the workspace and output. An explicit `--project` is analyzed directly unless `--solution` is also supplied; solution inference is used only when neither selector is supplied. `CG001` is a blocking used-but-undeclared key and returns exit code `1`. `CG002` is a non-blocking warning for a declared example key not observed by analyzed code. `CG900` is informational for dynamic or unresolvable access and never blocks. Exit code `2` means the workspace or configuration could not be analyzed trustworthily.

The JSON report preserves `knownKeys`, `bindableKeys`, `requiredKeys`, and `actuallyReadKeys` as separate states. The CLI schema is [`configgap-cli-report.schema.json`](https://github.com/KeelMatrix/ConfigGap/blob/main/docs/configgap-cli-report.schema.json).

The tool supports literal and statically resolvable `IConfiguration` access when receiver root provenance is established from a root configuration type, a known framework root `Configuration` property, a local/field/property initialized from a proven root, or a same-compilation helper parameter whose visible call-site arguments are proven roots. A section or otherwise unproven parameter/property/field produces `CG900` instead of an invented root key; local aliases initialized directly from `GetSection` retain their section scope. The tool also supports Options section binding, `:`/`__` hierarchy normalization, `appsettings*.json`, and explicitly listed `.env.example`-style name templates. It never needs configuration values or actual `.env` files. Key names may be sensitive; reports stay local. Telemetry uses the shared activation/heartbeat contract and does not receive analysis summaries. Set `KEELMATRIX_NO_TELEMETRY=1` for local validation.

See the [repository documentation](https://github.com/KeelMatrix/ConfigGap#readme) for supported patterns, schemas, limitations, privacy, and troubleshooting.

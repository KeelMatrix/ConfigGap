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

Use `--solution`, `--project`, `--config`, and `--format text|json` to select the workspace and output. `CG001` is a blocking used-but-undeclared key and returns exit code `1`. `CG900` is informational for dynamic or unresolvable access and never blocks. Exit code `2` means the workspace or configuration could not be analyzed trustworthily.

The tool supports literal and statically resolvable `IConfiguration` access, supported Options section binding, `:`/`__` hierarchy normalization, `appsettings*.json`, and explicitly listed `.env.example`-style name templates. It never needs configuration values or actual `.env` files. Key names may be sensitive; reports stay local and telemetry contains only coarse usage buckets. Set `KEELMATRIX_NO_TELEMETRY=1` for local validation.

See the [repository documentation](https://github.com/KeelMatrix/ConfigGap#readme) for supported patterns, schemas, limitations, privacy, and troubleshooting.

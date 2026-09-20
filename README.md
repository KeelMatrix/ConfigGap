# KeelMatrix.ConfigGap

Runtime validation can tell you whether configuration is valid when the app starts. ConfigGap checks a different problem earlier: which configuration keys your .NET code statically depends on, and whether your repository actually declares those keys coherently.

ConfigGap is a local .NET tool for ASP.NET Core and other .NET repositories that use `IConfiguration` or supported Options binding. It never needs configuration values and does not validate production startup state.

## Install

```bash
dotnet tool install --global KeelMatrix.ConfigGap
```

The tool targets .NET 8 and exposes the `configgap` command.

## Platform support

Windows has local Release-build and performance evidence for this candidate. Linux is expected but its Docker-based candidate check is unverified in the current environment, and macOS remains unverified locally. See [platform support](docs/platform-support.md) for the matrix, reproducible command, and residual uncertainty.

## First success

From a repository containing one solution:

```bash
configgap check
```

Select a workspace or report format explicitly when needed:

```bash
configgap check --solution src/App.sln --format json
configgap check --project src/App/App.csproj --config .configgap.json
```

The JSON report is deterministic and stays on the local output stream.

## What is checked

ConfigGap resolves these access forms when the key is a literal or a supported static expression:

```csharp
configuration["Payments:Provider"]
configuration.GetValue<string>("Payments:Provider")
configuration.GetSection("Payments")
configuration.GetRequiredSection("Payments")
```

Supported Options analysis includes statically known section ownership and the framework `BindConfiguration("Section")` pattern. A writable Options property is not treated as required merely because it exists. ConfigGap keeps known/bindable keys, required keys, and keys actually read by code conceptually separate; v1 reports the statically observed dependency surface.

Declaration surfaces are:

- `appsettings.json` discovered by the default policy;
- environment-specific `appsettings.<Environment>.json` files when listed in a version 1 configuration file;
- `.env.example`-style name templates when listed in a version 1 configuration file.

The version 1 configuration schema is in [`docs/configgap-config.schema.json`](docs/configgap-config.schema.json). Paths are relative to the configuration file and must remain inside the repository. JSON values and template values are ignored. Actual `.env` files are not required or read by default.

Environment names use the same hierarchy mapping as .NET configuration: `Payments__Provider` and `Payments:Provider` refer to the same logical key.

Dynamic or unresolvable access is unknown, not broken. It produces `CG900` informational output and never creates a blocking missing-key result. Framework-owned keys are excluded from application drift errors.

## Findings and exit codes

- `CG001` / `ERROR`: a statically used application key is absent from the configured declaration surfaces; exit code `1`.
- `CG002` / `WARNING`: a declared example leaf key was not observed in analyzed code; it never changes a clean exit to `1`.
- `CG900` / `INFO`: an access could not be resolved statically; it does not change a successful exit to `1`.
- `CONFIGGAP_*` diagnostics: the workspace or configuration could not be analyzed trustworthily; exit code `2`.

Exit code `0` means a trustworthy analysis completed without blocking findings. `1` means a trustworthy analysis found at least one blocking gap. `2` means no trustworthy result was produced. There are no value-based warnings in the v1 report; declaration values are never needed.

## Privacy and telemetry

Configuration key names may reveal architecture and can be sensitive. Keep reports local and review them before sharing. ConfigGap does not upload source, values, keys, section names, filenames, paths, project names, or report contents. Optional telemetry uses the shared activation/heartbeat wire contract and sends only its documented event, tool/version, runtime/process-context, week/timestamp, and pseudonymous hash fields. Telemetry is best-effort and cannot affect analysis.

CLI JSON reports also preserve the `knownKeys`, `bindableKeys`, `requiredKeys`, and `actuallyReadKeys` state arrays. The envelope and all CLI finding codes are defined in [`docs/configgap-cli-report.schema.json`](docs/configgap-cli-report.schema.json).

Disable telemetry for local or CI validation:

```powershell
$env:KEELMATRIX_NO_TELEMETRY = "1"
```

See [`PRIVACY.md`](PRIVACY.md) for the data boundary and the [KeelMatrix.Telemetry privacy policy](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md).

## Limitations

ConfigGap does not inspect secret values, runtime startup state, Kubernetes, Helm, Terraform, Docker Compose, cloud parameter stores, hosted inventories, arbitrary custom providers, remote configuration, deployment manifests, or automatic configuration files. Unsupported and dynamic access remains informational.

Analysis is bounded to 120 seconds, 128 declaration surfaces, and 1 MiB per declaration file. Use `--project` or an explicit configuration file to narrow a large workspace.

## Troubleshooting

ConfigGap loads projects through `MSBuildWorkspace`. Restore the selected solution or project with the same .NET SDK used by the repository, then run the command again. If the workspace cannot load, ConfigGap returns exit code `2` and prints an actionable `CONFIGGAP_*` diagnostic rather than reporting a clean result. See [`docs/troubleshooting.md`](docs/troubleshooting.md).

## Repository documentation

- [Configuration and schema](docs/configuration.md)
- [Troubleshooting and resource limits](docs/troubleshooting.md)
- [CLI report schema](docs/configgap-cli-report.schema.json)
- [Capabilities and limits](docs/capabilities-and-limits.md)
- [Security policy](SECURITY.md)
- [Changelog](CHANGELOG.md)

## License

KeelMatrix.ConfigGap is available under the MIT License. See [`LICENSE`](LICENSE).

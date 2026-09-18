# Troubleshooting

ConfigGap loads projects with `MSBuildWorkspace`, so the selected project must restore and load with the installed .NET SDK.

## Workspace failures

1. Run `dotnet --info` and confirm the repository's `global.json` SDK is installed.
2. Restore the selected solution or project with its repository `NuGet.config`.
3. Run `configgap check --project path/to/App.csproj` to narrow the load.
4. If the repository has multiple solutions, pass `--solution path/to/App.sln`.
5. Review the `CONFIGGAP_*` diagnostic. Exit code `2` means ConfigGap did not produce a trustworthy clean result.

ConfigGap does not fall back to regular-expression scanning when MSBuild or Roslyn cannot load a project.

## Configuration selection

Use `--config` for environment-specific JSON or an env-name template. Keep every declared path inside the repository and use forward slashes in portable configuration files. Do not point ConfigGap at an actual `.env` file.

## Resource limits

Analysis stops after 120 seconds, accepts at most 128 declaration surfaces, and reads at most 1 MiB from any one declaration file. Narrow the command with `--project` and an explicit configuration file when a repository exceeds those bounds.

## Privacy

Key names can reveal architecture. Review local text or JSON output before sharing it. Values, source content, project identity, and report contents are not sent to telemetry. Use `KEELMATRIX_NO_TELEMETRY=1` for validation runs.

# Privacy

ConfigGap analyzes source and declaration structure locally. It does not upload source, configuration values, configuration keys, section names, filenames, paths, project or repository names, code snippets, or report contents.

## Optional telemetry

After a complete trustworthy analysis, ConfigGap requests the shared activation event and at-most-weekly heartbeat through `KeelMatrix.Telemetry`. ConfigGap does not add a custom event or summary payload. The shared wire payload contains only the documented activation/heartbeat fields: event type, tool and tool/telemetry/schema versions, runtime, operating system, CI flag, UTC timestamp or ISO week, and `project_hash`/`installation_hash`.

`project_hash` is a one-way hash of a stable consuming-codebase fingerprint derived locally by the shared package from normalized repository identity or bounded project/solution structure. `installation_hash` is a one-way hash derived locally from a random installation-scoped salt. The raw repository identity, paths, project names, file contents, and salt are not sent; the hashes are pseudonymous correlation identifiers and should not be treated as proof of anonymity.

The shared published API does not accept project/file/finding/duration buckets, so ConfigGap does not emit them.

Telemetry is best-effort; failure cannot change analysis or its exit code.

Set `KEELMATRIX_NO_TELEMETRY=1` for local or CI validation. KeelMatrix development and validation runs are not production usage measurements.

## Local output and sensitive names

Reports remain on the local output stream. A key name can reveal architecture even when its value is absent, so review text and JSON reports before sharing them. ConfigGap does not need actual `.env` files and ignores declaration values.

See the [KeelMatrix.Telemetry privacy policy](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md) for the shared delivery and opt-out contract.

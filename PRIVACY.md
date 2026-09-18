# Privacy

ConfigGap analyzes source and declaration structure locally. It does not upload source, configuration values, configuration keys, section names, filenames, paths, project or repository names, code snippets, or report contents.

## Optional telemetry

After a complete trustworthy analysis, ConfigGap requests the shared anonymous activation and at-most-weekly heartbeat contract through `KeelMatrix.Telemetry`. The payload contains only product/runtime version, project-count, analyzed-file-count, finding-count, duration, and local/CI buckets. Telemetry is best-effort; failure cannot change analysis or its exit code.

Set `KEELMATRIX_NO_TELEMETRY=1` for local or CI validation. KeelMatrix development and validation runs are not production usage measurements.

## Local output and sensitive names

Reports remain on the local output stream. A key name can reveal architecture even when its value is absent, so review text and JSON reports before sharing them. ConfigGap does not need actual `.env` files and ignores declaration values.

See the [KeelMatrix.Telemetry privacy policy](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md) for the shared delivery and opt-out contract.

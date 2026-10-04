# Privacy

ConfigGap analyzes source and declaration structure locally. It does not upload source, configuration values, configuration keys, section names, filenames, paths, project or repository names, code snippets, or report contents.

## Optional telemetry

After a complete trustworthy analysis, ConfigGap asks the shared client to track activation and heartbeat. ConfigGap does not pass analysis results or configuration data into those calls. The shared package owns telemetry fields, project correlation, opt-out, state, cadence, delivery, and failure handling; its privacy policy is the source of truth for those details.

Set `KEELMATRIX_NO_TELEMETRY=1` for local or CI validation. KeelMatrix development and validation runs are not production usage measurements.

## Local output and sensitive names

Reports remain on the local output stream. A key name can reveal architecture even when its value is absent, so review text and JSON reports before sharing them. ConfigGap does not need actual `.env` files and ignores declaration values.

See the [KeelMatrix.Telemetry privacy policy](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md) for the shared delivery and opt-out contract.

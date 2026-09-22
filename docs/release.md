# Release path

The repository release workflow accepts tag pushes matching `vMAJOR.MINOR.PATCH` and manual dispatch. It validates the version and changelog contract, restores from `NuGet.config`, audits direct and transitive vulnerabilities, builds, tests, packs, validates the exact `.nupkg`/`.snupkg` set, runs package-contract mutation tests, and then uses NuGet Trusted Publishing through OIDC as `dmitriyzen`.

The shared pre-tag/workflow contract check is [`scripts/Verify-ReleaseContract.ps1`](../scripts/Verify-ReleaseContract.ps1). It requires a valid calendar date, a matching package/source version, and—when `-FirstRelease` is used—a non-empty `Added`-only release entry. It rejects an unreleased entry, disallowed first-release categories, remediation-history wording, and version disagreement. [`scripts/Test-ReleaseContract.ps1`](../scripts/Test-ReleaseContract.ps1) exercises finalized-pass, version-mismatch, impossible-date, empty-entry, disallowed-category, and unreleased cases.

The package payload is defined by [`scripts/ExpectedPackagePayload.txt`](../scripts/ExpectedPackagePayload.txt). The shipping project opts into the shared pack-time guard in `Directory.Build.props`, which rejects local configuration, environment, credential, and private-key inputs from the publish directory. [`scripts/Inspect-Package.ps1`](../scripts/Inspect-Package.ps1) compares the produced archive to that explicit payload and validates the exact symbol entries and symbol metadata.

Publication uses [`scripts/Publish-PackageArtifacts.ps1`](../scripts/Publish-PackageArtifacts.ps1). It performs two checked pushes in order: the tool package, then its symbol package. Duplicate skipping is intentionally not enabled; a duplicate response is a failed publication result that requires inspection rather than proof that the current bytes were accepted. [`scripts/Test-ReleasePublication.ps1`](../scripts/Test-ReleasePublication.ps1) verifies that a failed first push prevents the symbol push and fails the operation.

Release validation disables product and .NET CLI telemetry with `KEELMATRIX_NO_TELEMETRY=1` and `DOTNET_CLI_TELEMETRY_OPTOUT=1`.

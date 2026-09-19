# Release path

The repository release workflow is intentionally limited to tag pushes matching `vMAJOR.MINOR.PATCH` and manual dispatch. It validates the changelog/version contract, restores from `NuGet.config`, audits direct and transitive vulnerabilities, builds, tests, packs, validates the exact `.nupkg`/`.snupkg` set, and then uses NuGet Trusted Publishing through OIDC as `dmitriyzen`.

The shared pre-tag/workflow contract check is [`scripts/Verify-ReleaseContract.ps1`](../scripts/Verify-ReleaseContract.ps1). It rejects an unreleased or remediation-history first-release entry and version disagreement. The workflow sets `KEELMATRIX_NO_TELEMETRY=1` and `DOTNET_CLI_TELEMETRY_OPTOUT=1` for release validation.

This workflow is statically validated in the engineering evidence for this candidate and has not been executed or dispatched.

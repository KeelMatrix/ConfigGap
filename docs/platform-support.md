# ConfigGap platform support

This page records the platform evidence for the current ConfigGap v1 candidate. The repository supports the documented .NET 8 workspace-loading model only when the selected SDK can load the project types being analyzed.

## Platform matrix

| Platform | Status in this candidate | Evidence and boundary |
| --- | --- | --- |
| Windows | Locally exercised | Release build completed with zero warnings and zero errors. The Phase 0B performance protocol was exercised on Windows; the report records its exact measured environment and bound. |
| Linux | Expected, not verified in this environment | The reproducible Docker check uses the official `mcr.microsoft.com/dotnet/sdk:8.0` image and exercises restore, Release build, the full test suite, package installation, and clean/missing/dynamic fixture cases. This checkout could not run it because the Docker engine was unavailable; the candidate-specific corpus clone also failed before analysis because the public GitHub source was unreachable. |
| macOS | Expected when workspace loading works, not verified locally | No macOS SDK/MSBuild/Roslyn run was available for this candidate. macOS support is not claimed as independently verified. |

## Reproducible platform check

Run from the repository root with Docker available:

```powershell
pwsh -NoProfile -File ./scripts/Test-PlatformMatrix.ps1
```

The script prints the image tag and digest, each command duration, and the raw output tail for every step. It has not been run in GitHub Actions. The repository has no branch, pull-request, or scheduled workflow trigger for this check; the existing workflow is tag/manual-release only.

## Residual uncertainty

Linux and macOS workspace compatibility remains unverified for this exact candidate. In particular, MSBuild/Roslyn project loading, case-sensitive paths, newline handling, culture-sensitive logic, and the `:`/`__` normalization path still need platform-specific evidence. A workspace failure must remain an actionable exit-code-2 failure, never a clean result.

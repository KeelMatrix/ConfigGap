# ConfigGap platform support

This page records the platform evidence for the current ConfigGap v1 candidate. The repository supports the documented .NET 8 workspace-loading model only when the selected SDK can load the project types being analyzed.

## Platform matrix

| Platform | Status in this candidate | Evidence and boundary |
| --- | --- | --- |
| Windows | Locally exercised | The Phase 0B performance protocol and targeted core-test rerun passed on Windows. The report records the exact environment and measured bound. |
| Linux | Candidate-verified | The official `mcr.microsoft.com/dotnet/sdk:8.0` image passed restore, Release build, the full test suite, package creation, and clean/missing/dynamic consumer smoke. The exact image digest and step timings are recorded in the Phase 0B report. |
| macOS | Expected when workspace loading works, not verified locally | No macOS SDK/MSBuild/Roslyn run was available for this candidate. macOS support is not claimed as independently verified. |

## Reproducible platform check

Run from the repository root with Docker available:

```powershell
pwsh -NoProfile -File ./scripts/Test-PlatformMatrix.ps1
```

The script pulls the official .NET 8 SDK tag, prints its resolved digest, and records each command duration and raw output tail. It has not been run in GitHub Actions. The repository has no branch, pull-request, or scheduled workflow trigger for this check; the existing workflow is tag/manual-release only.

## Residual uncertainty

macOS workspace compatibility remains unverified for this exact candidate, including MSBuild/Roslyn project loading, case-sensitive paths, newline handling, culture-sensitive logic, and the `:`/`__` normalization path on that platform. A workspace failure must remain an actionable exit-code-2 failure, never a clean result.

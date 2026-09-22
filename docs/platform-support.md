# ConfigGap platform support

This page records the platform evidence for the documented ConfigGap v1 workspace-loading model. The repository supports that model only when the selected SDK can load the project types being analyzed.

## Platform matrix

| Platform | Status | Evidence and boundary |
| --- | --- | --- |
| Windows | Locally exercised | The evaluation performance protocol and targeted core-test rerun passed on Windows. The report records the exact environment and measured bound. |
| Linux | Verified | The official `mcr.microsoft.com/dotnet/sdk:8.0` image passed restore, Release build, the full test suite, package creation, and clean/missing/dynamic consumer smoke. The exact image digest and step timings are recorded in the evaluation report. |
| macOS | Expected when workspace loading works, not verified locally | No local macOS SDK/MSBuild/Roslyn run was available. macOS support is not claimed as independently verified. |

## Reproducible platform check

Run from the repository root with Docker available:

```powershell
pwsh -NoProfile -File ./scripts/Test-PlatformMatrix.ps1
```

The script pulls the official .NET 8 SDK tag, prints its resolved digest, and records each command duration and raw output tail. The repository workflow runs the same build, test, package, and consumer checks on its configured operating-system matrix. The Docker platform harness is executed from the Linux environment.

## Residual uncertainty

macOS workspace compatibility remains unverified locally, including MSBuild/Roslyn project loading, case-sensitive paths, newline handling, culture-sensitive logic, and the `:`/`__` normalization path on that platform. A workspace failure must remain an actionable exit-code-2 failure, never a clean result.

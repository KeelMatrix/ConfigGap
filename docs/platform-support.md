# ConfigGap platform support

This page records the platform evidence for the current ConfigGap v1 documentation tip. The hosted evidence below verified the preceding implementation commit; this tip adds documentation-only changes. The repository supports the documented .NET 8 workspace-loading model only when the selected SDK can load the project types being analyzed.

## Platform matrix

| Platform | Status at the verified implementation commit | Evidence and boundary |
| --- | --- | --- |
| Windows | Locally exercised | The Phase 0B performance protocol and targeted core-test rerun passed on Windows. The report records the exact environment and measured bound. |
| Linux | Verified | The official `mcr.microsoft.com/dotnet/sdk:8.0` image passed restore, Release build, the full test suite, package creation, and clean/missing/dynamic consumer smoke. The exact image digest and step timings are recorded in the Phase 0B report. |
| macOS | Expected when workspace loading works, not verified locally | No macOS SDK/MSBuild/Roslyn run was available for the verified implementation commit. macOS support is not claimed as independently verified. |

## Reproducible platform check

Run from the repository root with Docker available:

```powershell
pwsh -NoProfile -File ./scripts/Test-PlatformMatrix.ps1
```

The script pulls the official .NET 8 SDK tag, prints its resolved digest, and records each command duration and raw output tail. Hosted CI runs the repository workflow on `push` and pull requests; run [35608970814](https://github.com/KeelMatrix/ConfigGap/actions/runs/35608970814), triggered by `push`, verified commit `3189f051` and passed the Ubuntu, Windows, and macOS matrix legs. The current tip adds documentation-only changes on top of that verified commit. The hosted macOS leg is evidence for the documented build/test/package path, while macOS was not verified locally and the Docker platform harness is only executed from the Ubuntu leg.

## Residual uncertainty

macOS workspace compatibility remains unverified locally, including MSBuild/Roslyn project loading, case-sensitive paths, newline handling, culture-sensitive logic, and the `:`/`__` normalization path on that platform. The hosted macOS evidence cited above is from commit `3189f051`. A workspace failure must remain an actionable exit-code-2 failure, never a clean result.

# Analysis core benchmark

The checked-in performance protocol generates one clean 50-project solution from the fixture corpus, restores it once, and runs the analyzer three times with an exact observation-count guard. The result is a measured regression bound for this generated input and machine, not a portable SLA.

Final measured input and result:

- 40 project fixture patterns plus 2 shared fixture patterns;
- 2,252 observations on each guarded run;
- durations: 21,198 ms, 18,817 ms, and 21,318 ms;
- peak working sets: 237,490,176 bytes, 234,844,160 bytes, and 237,359,104 bytes;
- derived bound: 23,300 ms wall clock and 262,144,000 bytes peak working set;
- SDK: 8.0.425; Windows 10.0.19045, x64, 16 processors.

Command:

```powershell
pwsh -NoProfile -File .\scripts\Invoke-Phase0BPerformance.ps1 `
  -RepositoryRoot (Get-Location).Path `
  -ScratchRoot (Join-Path $env:TEMP 'configgap-performance') `
  -OutputPath (Join-Path (Get-Location).Path 'artifacts/analysis-core-performance.json')
```

# Analysis core benchmark

The checked-in performance protocol generates one clean 50-project solution from the fixture corpus, restores it once, and runs the analyzer three times with an exact observation-count guard. The result is a measured regression bound for this generated input and machine, not a portable SLA.

Final measured input and result:

- 33 project fixture patterns plus 1 shared fixture pattern;
- 1,651 observations on each guarded run;
- durations: 20,933 ms, 19,384 ms, and 18,045 ms;
- peak working sets: 229,347,328 bytes, 229,376,000 bytes, and 228,003,840 bytes;
- derived bound: 22,400 ms wall clock and 252,706,816 bytes peak working set;
- SDK: 8.0.425; Windows 10.0.19045, x64, 16 processors.

Command:

```powershell
pwsh -NoProfile -File .\scripts\Invoke-Phase0BPerformance.ps1 `
  -RepositoryRoot (Get-Location).Path `
  -ScratchRoot (Join-Path $env:TEMP 'configgap-performance') `
  -OutputPath (Join-Path (Get-Location).Path 'artifacts/analysis-core-performance.json')
```

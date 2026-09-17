# Analysis core benchmark

The checked-in performance protocol generates one clean 50-project solution from the fixture corpus, restores it once, and runs the analyzer three times with an exact observation-count guard. The result is a measured regression bound for this generated input and machine, not a portable SLA.

Final measured input and result:

- 46 project fixture patterns plus 2 shared fixture patterns;
- 2,552 observations on each guarded run;
- durations: 21,265 ms, 20,340 ms, and 21,198 ms;
- peak working sets: 245,657,600 bytes, 244,576,256 bytes, and 237,768,704 bytes;
- derived bound: 22,000 ms wall clock and 270,532,608 bytes peak working set;
- SDK: 8.0.425; Windows 10.0.19045, x64, 16 processors.

Command:

```powershell
pwsh -NoProfile -File .\scripts\Invoke-Phase0BPerformance.ps1 `
  -RepositoryRoot (Get-Location).Path `
  -ScratchRoot (Join-Path $env:TEMP 'configgap-performance') `
  -OutputPath (Join-Path (Get-Location).Path 'artifacts/analysis-core-performance.json')
```

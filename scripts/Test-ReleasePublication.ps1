[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Contract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$root = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path ([IO.Path]::GetTempPath()) "configgap-release-publication-$([Guid]::NewGuid().ToString('N'))"
$publisher = Join-Path $PSScriptRoot 'Publish-PackageArtifacts.ps1'

try {
    New-Item -ItemType Directory -Path $fixture -Force | Out-Null
    $packagePath = Join-Path $fixture 'package.nupkg'
    $symbolsPath = Join-Path $fixture 'package.snupkg'
    Set-Content -LiteralPath $packagePath -Value 'package' -Encoding utf8NoBOM
    Set-Content -LiteralPath $symbolsPath -Value 'symbols' -Encoding utf8NoBOM

    $logPath = Join-Path $fixture 'push.log'
    $runningOnWindows = [OperatingSystem]::IsWindows()
    $stubPath = Join-Path $fixture $(if ($runningOnWindows) { 'dotnet.cmd' } else { 'dotnet' })
    $env:CONFIGGAP_STUB_LOG = $logPath
    if ($runningOnWindows) {
        @"
@echo off
set /a count=0
if exist "%CONFIGGAP_STUB_LOG%" set /p count=<"%CONFIGGAP_STUB_LOG%"
set /a count+=1
>"%CONFIGGAP_STUB_LOG%" echo %count%
if %count% EQU 1 exit /b 17
exit /b 0
"@ | Set-Content -LiteralPath $stubPath -Encoding ascii
    }
    else {
        @'
#!/bin/sh
if [ -f "$CONFIGGAP_STUB_LOG" ]; then
    count=$(cat "$CONFIGGAP_STUB_LOG")
else
    count=0
fi
count=$((count + 1))
printf '%s\n' "$count" > "$CONFIGGAP_STUB_LOG"
if [ "$count" -eq 1 ]; then
    exit 17
fi
exit 0
'@ | Set-Content -LiteralPath $stubPath -Encoding utf8NoBOM
        & chmod +x $stubPath
    }

    $env:NUGET_API_KEY = 'synthetic-test-key'
    $output = (& pwsh -NoProfile -File $publisher -PackagePath $packagePath -SymbolsPath $symbolsPath -DotnetCommand $stubPath 2>&1 | Out-String)
    $exitCode = $LASTEXITCODE
    Assert-Contract ($exitCode -ne 0) 'The publication helper accepted a failed first push.'
    Assert-Contract ((Get-Content -Raw -LiteralPath $logPath).Trim() -ceq '1') 'The publication helper attempted the symbol push after the first push failed.'
    Assert-Contract ($output -match 'publication failed') 'The publication helper did not report the failed first push.'
    Write-Output 'Release publication test passed: failed first push stopped before symbol publication and left the operation failed.'
}
finally {
    Remove-Item Env:NUGET_API_KEY -ErrorAction SilentlyContinue
    Remove-Item Env:CONFIGGAP_STUB_LOG -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}

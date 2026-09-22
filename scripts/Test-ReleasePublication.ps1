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
    $stubPath = Join-Path $fixture 'dotnet.cmd'
    @"
@echo off
set /a count=0
if exist "$($logPath.Replace('\', '/'))" for /f %%A in ($($logPath.Replace('\', '/'))) do set /a count=%%A
set /a count+=1
>"$($logPath.Replace('\', '/'))" echo %count%
if %count% EQU 1 exit /b 17
exit /b 0
"@ | Set-Content -LiteralPath $stubPath -Encoding ascii

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
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}

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

function Invoke-PublicationScenario {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][int]$FailOnCall,
        [Parameter(Mandatory = $true)][int]$ExpectedExitCode,
        [Parameter(Mandatory = $true)][int]$ExpectedCallCount,
        [Parameter(Mandatory = $true)][string]$PackagePath,
        [Parameter(Mandatory = $true)][string]$SymbolsPath,
        [Parameter(Mandatory = $true)][string]$StubPath,
        [Parameter(Mandatory = $true)][string]$LogPath,
        [Parameter(Mandatory = $true)][string]$StatePath
    )

    Remove-Item -LiteralPath $LogPath, $StatePath -Force -ErrorAction SilentlyContinue
    $env:CONFIGGAP_STUB_FAIL_ON_CALL = [string]$FailOnCall
    $output = (& pwsh -NoProfile -File $publisher -PackagePath $PackagePath -SymbolsPath $SymbolsPath -DotnetCommand $StubPath 2>&1 | Out-String)
    $exitCode = $LASTEXITCODE
    Assert-Contract ($exitCode -eq $ExpectedExitCode) "$Name returned exit code $exitCode instead of $ExpectedExitCode.`n$output"

    $log = @(Get-Content -LiteralPath $LogPath)
    $callCount = @($log | Where-Object { $_ -match '^CALL=' }).Count
    Assert-Contract ($callCount -eq $ExpectedCallCount) "$Name made $callCount push calls instead of $ExpectedCallCount.`n$($log -join "`n")"
    return [pscustomobject]@{ Name = $Name; ExitCode = $exitCode; Output = $output; Log = $log }
}

function Get-CallArguments {
    param(
        [Parameter(Mandatory = $true)][string[]]$Log,
        [Parameter(Mandatory = $true)][int]$Call
    )

    $insideCall = $false
    $arguments = [System.Collections.Generic.List[string]]::new()
    foreach ($line in $Log) {
        if ($line -ceq "CALL=$Call") {
            $insideCall = $true
            continue
        }
        if ($line -match '^CALL=') {
            if ($insideCall) { break }
            continue
        }
        if ($insideCall -and $line.StartsWith('ARG=', [StringComparison]::Ordinal)) {
            $arguments.Add($line.Substring(4))
        }
    }

    return @($arguments)
}

function Format-SafeArguments {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$PackagePath,
        [Parameter(Mandatory = $true)][string]$SymbolsPath
    )

    $safe = [System.Collections.Generic.List[string]]::new()
    $redactNext = $false
    foreach ($argument in $Arguments) {
        if ($redactNext) {
            $safe.Add('<redacted>')
            $redactNext = $false
            continue
        }
        if ($argument -ceq $PackagePath) { $safe.Add('<package.nupkg>') }
        elseif ($argument -ceq $SymbolsPath) { $safe.Add('<package.snupkg>') }
        else { $safe.Add($argument) }
        if ($argument -ceq '--api-key') { $redactNext = $true }
    }
    return $safe -join ' '
}

try {
    New-Item -ItemType Directory -Path $fixture -Force | Out-Null
    $packagePath = Join-Path $fixture 'package.nupkg'
    $symbolsPath = Join-Path $fixture 'package.snupkg'
    Set-Content -LiteralPath $packagePath -Value 'package' -Encoding utf8NoBOM
    Set-Content -LiteralPath $symbolsPath -Value 'symbols' -Encoding utf8NoBOM

    $logPath = Join-Path $fixture 'push.log'
    $statePath = Join-Path $fixture 'push.state'
    $runningOnWindows = [OperatingSystem]::IsWindows()
    $stubPath = Join-Path $fixture $(if ($runningOnWindows) { 'dotnet.cmd' } else { 'dotnet' })
    $env:CONFIGGAP_STUB_LOG = $logPath
    $env:CONFIGGAP_STUB_STATE = $statePath
    if ($runningOnWindows) {
        @"
@echo off
setlocal EnableDelayedExpansion
set /a count=0
if exist "%CONFIGGAP_STUB_STATE%" set /p count=<"%CONFIGGAP_STUB_STATE%"
set /a count+=1
>"%CONFIGGAP_STUB_STATE%" echo !count!
>>"%CONFIGGAP_STUB_LOG%" echo CALL=!count!
:argument_loop
if "%~1"=="" goto argument_end
>>"%CONFIGGAP_STUB_LOG%" echo ARG=%~1
shift
goto argument_loop
:argument_end
if !count! EQU %CONFIGGAP_STUB_FAIL_ON_CALL% exit /b 17
exit /b 0
"@ | Set-Content -LiteralPath $stubPath -Encoding ascii
    }
    else {
        @'
#!/bin/sh
if [ -f "$CONFIGGAP_STUB_STATE" ]; then
    count=$(cat "$CONFIGGAP_STUB_STATE")
else
    count=0
fi
count=$((count + 1))
printf '%s\n' "$count" > "$CONFIGGAP_STUB_STATE"
printf 'CALL=%s\n' "$count" >> "$CONFIGGAP_STUB_LOG"
for argument in "$@"; do
    printf 'ARG=%s\n' "$argument" >> "$CONFIGGAP_STUB_LOG"
done
if [ "$count" -eq "$CONFIGGAP_STUB_FAIL_ON_CALL" ]; then
    exit 17
fi
exit 0
'@ | Set-Content -LiteralPath $stubPath -Encoding utf8NoBOM
        & chmod +x $stubPath
    }

    $env:NUGET_API_KEY = 'synthetic-test-key'
    $success = Invoke-PublicationScenario -Name 'successful publication' -FailOnCall 0 -ExpectedExitCode 0 -ExpectedCallCount 2 -PackagePath $packagePath -SymbolsPath $symbolsPath -StubPath $stubPath -LogPath $logPath -StatePath $statePath
    $primaryArguments = @(Get-CallArguments -Log $success.Log -Call 1)
    $symbolArguments = @(Get-CallArguments -Log $success.Log -Call 2)
    Assert-Contract ($primaryArguments.Count -gt 0 -and $primaryArguments[0] -ceq 'nuget' -and $primaryArguments[1] -ceq 'push') 'The first command was not a NuGet push.'
    Assert-Contract ($primaryArguments -contains $packagePath) 'The first command did not push the primary package.'
    Assert-Contract ($primaryArguments -contains '--no-symbols') 'The primary package push did not suppress implicit symbol submission.'
    Assert-Contract ($primaryArguments -notcontains $symbolsPath) 'The first command explicitly submitted the symbol package.'
    Assert-Contract ($symbolArguments.Count -gt 0 -and $symbolArguments[0] -ceq 'nuget' -and $symbolArguments[1] -ceq 'push') 'The second command was not a NuGet push.'
    Assert-Contract ($symbolArguments -contains $symbolsPath) 'The second command did not push the symbol package.'
    Assert-Contract ($symbolArguments -notcontains '--no-symbols') 'The explicit symbol package push unexpectedly suppressed symbols.'
    Assert-Contract (@($success.Log | Where-Object { $_ -ceq "ARG=$symbolsPath" }).Count -eq 1) 'The symbol package was submitted more than once.'
    Write-Output "Captured primary push arguments: $(Format-SafeArguments -Arguments $primaryArguments -PackagePath $packagePath -SymbolsPath $symbolsPath)"
    Write-Output "Captured symbol push arguments: $(Format-SafeArguments -Arguments $symbolArguments -PackagePath $packagePath -SymbolsPath $symbolsPath)"
    Write-Output 'Release publication argument test passed: primary push includes --no-symbols; the second push is the only symbol-package submission.'

    $firstFailure = Invoke-PublicationScenario -Name 'first-push failure' -FailOnCall 1 -ExpectedExitCode 1 -ExpectedCallCount 1 -PackagePath $packagePath -SymbolsPath $symbolsPath -StubPath $stubPath -LogPath $logPath -StatePath $statePath
    Assert-Contract ($firstFailure.Output -match 'publication failed') 'The publication helper did not report the failed first push.'
    Assert-Contract (@($firstFailure.Log | Where-Object { $_ -ceq "ARG=$symbolsPath" }).Count -eq 0) 'The publication helper attempted the symbol push after the first push failed.'
    Write-Output 'Release publication failure test passed: failed primary push stopped before symbol submission and failed the operation.'

    $secondFailure = Invoke-PublicationScenario -Name 'second-push failure' -FailOnCall 2 -ExpectedExitCode 1 -ExpectedCallCount 2 -PackagePath $packagePath -SymbolsPath $symbolsPath -StubPath $stubPath -LogPath $logPath -StatePath $statePath
    Assert-Contract ($secondFailure.Output -match 'publication failed') 'The publication helper did not report the failed symbol push.'
    Assert-Contract ($secondFailure.Output -match [regex]::Escape($symbolsPath)) 'The failed symbol push did not identify the symbol artifact.'
    Write-Output 'Release publication failure test passed: failed symbol push failed the operation after one primary push.'
}
finally {
    Remove-Item Env:NUGET_API_KEY -ErrorAction SilentlyContinue
    Remove-Item Env:CONFIGGAP_STUB_LOG -ErrorAction SilentlyContinue
    Remove-Item Env:CONFIGGAP_STUB_STATE -ErrorAction SilentlyContinue
    Remove-Item Env:CONFIGGAP_STUB_FAIL_ON_CALL -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}

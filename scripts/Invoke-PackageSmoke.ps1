[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [string]$ExpectedVersion = '0.1.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Contract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Invoke-Checked {
    param([string]$File, [string[]]$Arguments)
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Command '$File' failed with exit code $LASTEXITCODE." }
}

function Invoke-Tool {
    param([string]$WorkingDirectory, [string[]]$Arguments)
    Push-Location -LiteralPath $WorkingDirectory
    try {
        $output = (& $toolPath @Arguments 2>&1 | Out-String).TrimEnd()
        return [PSCustomObject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    }
    finally { Pop-Location }
}

function Copy-Sample {
    param([string]$Destination)
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($name in @('FixtureClean.csproj', 'Program.cs', 'ConfigurationUse.cs', 'appsettings.json')) {
        Copy-Item -LiteralPath (Join-Path $repo "tests/FixtureClean/$name") -Destination (Join-Path $Destination $name)
    }
    @'
{
  "version": 1,
  "declarationSurfaces": [
    { "kind": "json", "path": "appsettings.json" }
  ]
}
'@ | Set-Content -LiteralPath (Join-Path $Destination 'configgap.json') -Encoding utf8NoBOM
}

function Restore-Sample {
    param([string]$SampleRoot)
    [void](Invoke-Checked -File 'dotnet' -Arguments @('restore', (Join-Path $SampleRoot 'FixtureClean.csproj'), '--configfile', $nugetConfig, '--packages', $nugetPackages, '--ignore-failed-sources', '--nologo'))
}

function Assert-JsonCase {
    param(
        [string]$Name,
        [string]$Root,
        [int]$ExpectedExitCode,
        [bool]$ExpectBlocking,
        [bool]$ExpectDynamic
    )
    Restore-Sample -SampleRoot $Root
    $result = Invoke-Tool -WorkingDirectory $Root -Arguments @('check', '--project', 'FixtureClean.csproj', '--config', 'configgap.json', '--format', 'json')
    Write-Host "=== $Name (exit $($result.ExitCode)) ==="
    Write-Host $result.Output
    Assert-Contract ($result.ExitCode -eq $ExpectedExitCode) "$Name returned $($result.ExitCode), expected $ExpectedExitCode."
    $report = $result.Output | ConvertFrom-Json
    Assert-Contract ([bool]$report.trustworthyAnalysis) "$Name did not produce a trustworthy analysis."
    $findings = @($report.findings)
    $blocking = @($findings | Where-Object { $_.code -eq 'CG001' })
    $dynamic = @($findings | Where-Object { $_.code -eq 'CG900' })
    Assert-Contract (($blocking.Count -gt 0) -eq $ExpectBlocking) "$Name blocking finding assertion failed."
    Assert-Contract (($dynamic.Count -gt 0) -eq $ExpectDynamic) "$Name dynamic finding assertion failed."
    if ($ExpectBlocking) {
        Assert-Contract ($result.Output -match 'CG001') "$Name did not include CG001."
    }
    else {
        Assert-Contract ($result.Output -notmatch 'CG001') "$Name unexpectedly included CG001."
    }
    if ($ExpectDynamic) {
        Assert-Contract ($result.Output -match 'CG900') "$Name did not include CG900."
    }
    return $result
}

$repo = Split-Path -Parent $PSScriptRoot
Assert-Contract (Test-Path -LiteralPath $PackagePath -PathType Leaf) "Package was not found: $PackagePath"

$smokeRoot = Join-Path ([IO.Path]::GetTempPath()) "configgap-package-smoke-$([Guid]::NewGuid().ToString('N'))"
$feed = Join-Path $smokeRoot 'feed'
$install = Join-Path $smokeRoot 'install'
$nugetPackages = Join-Path $smokeRoot 'packages'
$httpCache = Join-Path $smokeRoot 'http-cache'
$pluginsCache = Join-Path $smokeRoot 'plugins-cache'
$dotnetHome = Join-Path $smokeRoot 'dotnet-home'
$nugetConfig = Join-Path $smokeRoot 'NuGet.config'
$clean = Join-Path $smokeRoot 'clean'
$missing = Join-Path $smokeRoot 'missing'
$dynamic = Join-Path $smokeRoot 'dynamic'
$counterexample = Join-Path $smokeRoot 'counterexample'
$toolExecutable = if ([OperatingSystem]::IsWindows()) { 'configgap.exe' } else { 'configgap' }
$toolPath = Join-Path $install $toolExecutable
$isolatedTelemetryCache = Join-Path $nugetPackages 'keelmatrix.telemetry\0.1.0'
$saved = @{}

try {
    New-Item -ItemType Directory -Path $feed, $install, $nugetPackages, $httpCache, $pluginsCache, $dotnetHome -Force | Out-Null
    Copy-Item -LiteralPath (Resolve-Path -LiteralPath $PackagePath).Path -Destination $feed
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$($feed.Replace('&', '&amp;'))" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
"@ | Set-Content -LiteralPath $nugetConfig -Encoding utf8NoBOM

    foreach ($name in @('NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'NUGET_PLUGINS_CACHE_PATH', 'DOTNET_CLI_HOME', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'KEELMATRIX_NO_TELEMETRY')) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }
    $env:NUGET_PACKAGES = $nugetPackages
    $env:NUGET_HTTP_CACHE_PATH = $httpCache
    $env:NUGET_PLUGINS_CACHE_PATH = $pluginsCache
    $env:DOTNET_CLI_HOME = $dotnetHome
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:KEELMATRIX_NO_TELEMETRY = '1'

    Assert-Contract (-not (Test-Path -LiteralPath $isolatedTelemetryCache)) 'The isolated cache unexpectedly contains KeelMatrix.Telemetry before installation.'
    Invoke-Checked -File 'dotnet' -Arguments @('tool', 'install', '--tool-path', $install, '--configfile', $nugetConfig, '--version', $ExpectedVersion, 'KeelMatrix.ConfigGap', '--add-source', $feed, '--ignore-failed-sources', '--no-cache')
    Assert-Contract (Test-Path -LiteralPath $toolPath -PathType Leaf) 'The isolated tool executable was not installed.'
    $telemetryAssembly = Get-ChildItem -LiteralPath $install -Recurse -Force -Filter 'KeelMatrix.Telemetry.dll' -File | Select-Object -First 1
    Assert-Contract ($null -ne $telemetryAssembly) 'The installed tool does not contain its declared KeelMatrix.Telemetry runtime dependency.'
    Write-Output 'Global KeelMatrix.Telemetry cache is not used; install succeeded from the empty isolated cache and controlled sources.'

    Copy-Sample -Destination $clean
    Copy-Sample -Destination $missing
    Copy-Sample -Destination $dynamic
    Copy-Sample -Destination $counterexample
    $missingSource = Get-Content -Raw -LiteralPath (Join-Path $missing 'ConfigurationUse.cs')
    $missingSource = $missingSource.Replace('configuration["Clean:Key"]', 'configuration["Missing:Key"]')
    Set-Content -LiteralPath (Join-Path $missing 'ConfigurationUse.cs') -Value $missingSource -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $counterexample 'appsettings.json') -Value '{"Clean":{"Key":null},"Primary":null}' -Encoding utf8NoBOM
    @'
using Microsoft.Extensions.Configuration;

namespace FixtureClean;

public static class ConfigurationCounterexamples
{
    public static string? Read(IConfiguration configuration, string sectionName)
    {
        configuration.GetSection(sectionName).Bind(new Settings());
        return configuration.GetValue<string>("Primary", configuration["Fallback"]!);
    }

    private sealed class Settings
    {
        public string? Value { get; set; }
    }
}
'@ | Set-Content -LiteralPath (Join-Path $counterexample 'ConfigurationCounterexamples.cs') -Encoding utf8NoBOM

    [void](Assert-JsonCase -Name 'clean case' -Root $clean -ExpectedExitCode 0 -ExpectBlocking $false -ExpectDynamic $true)
    $missingResult = Assert-JsonCase -Name 'missing declaration case' -Root $missing -ExpectedExitCode 1 -ExpectBlocking $true -ExpectDynamic $true
    Assert-Contract ($missingResult.Output -match 'Missing:Key') 'The missing declaration case did not identify the planted key.'
    [void](Assert-JsonCase -Name 'dynamic key case' -Root $dynamic -ExpectedExitCode 0 -ExpectBlocking $false -ExpectDynamic $true)
    $counterexampleResult = Assert-JsonCase -Name 'preserved read and unknown bind case' -Root $counterexample -ExpectedExitCode 1 -ExpectBlocking $true -ExpectDynamic $true
    $counterexampleReport = $counterexampleResult.Output | ConvertFrom-Json
    Assert-Contract (@($counterexampleReport.actuallyReadKeys) -contains 'Fallback') 'The installed tool dropped the independently evaluated Fallback read.'
    Assert-Contract (@($counterexampleReport.findings | Where-Object { $_.code -eq 'CG900' -and $_.source -eq 'ConfigurationCounterexamples.cs' }).Count -gt 0) 'The installed tool dropped the unknown Bind observation.'
    Assert-Contract ($counterexampleResult.Output -match 'Fallback') 'The installed tool did not report the preserved Fallback read.'
    Write-Output 'Isolated package-consumer smoke passed: clean, blocking missing declaration, dynamic key, preserved fallback read, and unknown Bind.'
}
finally {
    foreach ($name in $saved.Keys) {
        [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process')
    }
    if (Test-Path -LiteralPath $smokeRoot) { Remove-Item -LiteralPath $smokeRoot -Recurse -Force }
}

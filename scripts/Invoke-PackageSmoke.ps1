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

function Invoke-SourceTool {
    param([string]$WorkingDirectory, [string[]]$Arguments)
    Push-Location -LiteralPath $WorkingDirectory
    try {
        $output = (& dotnet $sourceToolPath @Arguments 2>&1 | Out-String).TrimEnd()
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

function Assert-ReceiverProvenanceCase {
    param(
        [string]$Name,
        [string]$Root,
        [bool]$ExpectUnusedRootDeclaration
    )

    $installedResult = Assert-JsonCase -Name $Name -Root $Root -ExpectedExitCode 0 -ExpectBlocking $false -ExpectDynamic $true
    $sourceResult = Invoke-SourceTool -WorkingDirectory $Root -Arguments @('check', '--project', 'FixtureClean.csproj', '--config', 'configgap.json', '--format', 'json')
    Assert-Contract ($sourceResult.ExitCode -eq $installedResult.ExitCode) "$Name source-built and installed tools returned different exit codes."
    Assert-Contract ($sourceResult.Output -ceq $installedResult.Output) "$Name source-built and installed tool reports differ."

    $report = $installedResult.Output | ConvertFrom-Json
    $findings = @($report.findings)
    $receiverFindings = @($findings | Where-Object { $_.source -eq 'ReceiverProvenance.cs' })
    Assert-Contract ($receiverFindings.Where({ $_.code -eq 'CG900' }).Count -eq 9) "$Name did not report all nine unproven parameter/property/field receivers as CG900."
    Assert-Contract ($receiverFindings.Where({ $_.code -eq 'CG001' }).Count -eq 0) "$Name invented a blocking root key for an unproven receiver."
    foreach ($key in @('ParameterRootOnly', 'ParameterSectionOnly', 'PropertyRootOnly', 'PropertySectionOnly', 'FieldRootOnly', 'FieldSectionOnly', 'UnseenCallerLeaf', 'UnseenWrappedLeaf')) {
        Assert-Contract (@($report.actuallyReadKeys) -notcontains $key) "$Name incorrectly reported '$key' as a root read."
    }
    Assert-Contract (@($report.actuallyReadKeys) -contains 'ProvenRootRead') "$Name dropped a helper parameter passed a proven-root alias."
    Assert-Contract (@($report.actuallyReadKeys) -contains 'ConstructorFieldRoot') "$Name dropped a constructor-assigned root field."
    Assert-Contract (@($report.actuallyReadKeys) -contains 'ConstructorPropertyRoot') "$Name dropped a constructor-assigned root property."
    Assert-Contract (@($report.actuallyReadKeys) -notcontains 'ConstructorAssignedSectionOnly') "$Name reported a constructor-assigned section as a root read."
    if ($ExpectUnusedRootDeclaration) {
        Assert-Contract (@($findings | Where-Object { $_.code -eq 'CG002' -and $_.key -eq 'UnseenCallerLeaf' }).Count -eq 1) "$Name did not report the unused root-shaped declaration."
        Assert-Contract (@($findings | Where-Object { $_.code -eq 'CG002' -and $_.key -eq 'UnseenWrappedLeaf' }).Count -eq 1) "$Name did not report the unused wrapped-receiver declaration."
    }
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
$receiverProvenanceRoot = Join-Path $smokeRoot 'receiver-provenance-root'
$receiverProvenanceSection = Join-Path $smokeRoot 'receiver-provenance-section'
$toolExecutable = if ([OperatingSystem]::IsWindows()) { 'configgap.exe' } else { 'configgap' }
$toolPath = Join-Path $install $toolExecutable
$sourceToolPath = Join-Path $repo 'src/KeelMatrix.ConfigGap/bin/Release/net8.0/KeelMatrix.ConfigGap.dll'
$isolatedTelemetryCache = Join-Path $nugetPackages 'keelmatrix.telemetry\0.1.1'
$saved = @{}

try {
    Assert-Contract (Test-Path -LiteralPath $sourceToolPath -PathType Leaf) "Source-built tool was not found: $sourceToolPath"
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
    Copy-Sample -Destination $receiverProvenanceRoot
    Copy-Sample -Destination $receiverProvenanceSection
    $missingSource = Get-Content -Raw -LiteralPath (Join-Path $missing 'ConfigurationUse.cs')
    $missingSource = $missingSource.Replace('configuration["Clean:Key"]', 'configuration["Missing:Key"]')
    Set-Content -LiteralPath (Join-Path $missing 'ConfigurationUse.cs') -Value $missingSource -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $counterexample 'appsettings.json') -Value '{"Clean":{"Key":null},"Primary":null}' -Encoding utf8NoBOM
    @'
using Microsoft.Extensions.Configuration;

namespace FixtureClean;

public static class ConfigurationCounterexamples
{
    private static IConfigurationRoot Root { get; } = null!;

    public static string? Read(IConfiguration configuration, string sectionName)
    {
        configuration.GetSection(sectionName).Bind(new Settings());
        return configuration.GetValue<string>("Primary", configuration["Fallback"]!);
    }

    public static string? ExerciseProvenRootCallSite() => Read(Root, GetDynamicSection());

    private static string GetDynamicSection() => string.Empty;

    private sealed class Settings
    {
        public string? Value { get; set; }
    }
}
'@ | Set-Content -LiteralPath (Join-Path $counterexample 'ConfigurationCounterexamples.cs') -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $receiverProvenanceRoot 'appsettings.json') -Value '{"Clean":{"Key":null},"ParameterRootOnly":null,"PropertyRootOnly":null,"FieldRootOnly":null,"ConstructorFieldRoot":null,"ConstructorPropertyRoot":null,"ProvenRootRead":null,"UnseenCallerLeaf":null,"UnseenWrappedLeaf":null,"Payments":{"ParameterSectionOnly":null,"PropertySectionOnly":null,"FieldSectionOnly":null,"ConstructorAssignedSectionOnly":null}}' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $receiverProvenanceSection 'appsettings.json') -Value '{"Clean":{"Key":null},"ParameterRootOnly":null,"PropertyRootOnly":null,"FieldRootOnly":null,"ConstructorFieldRoot":null,"ConstructorPropertyRoot":null,"ProvenRootRead":null,"Payments":{"ParameterSectionOnly":null,"PropertySectionOnly":null,"FieldSectionOnly":null,"ConstructorAssignedSectionOnly":null,"UnseenCallerLeaf":null,"UnseenWrappedLeaf":null}}' -Encoding utf8NoBOM
    $receiverSource = @'
using Microsoft.Extensions.Configuration;

namespace FixtureClean;

public static class ReceiverProvenance
{
    private static IConfigurationRoot Root { get; } = null!;
    private static IConfiguration SectionProperty => Root.GetSection("Payments");
    private static IConfiguration SectionField = Root.GetSection("Payments");

    public static string? ExternalEntryPoint(IConfiguration configuration) => configuration["UnseenCallerLeaf"];

    public static string? ExternalEntryPointParenthesized(IConfiguration configuration) => (configuration)["UnseenWrappedLeaf"];

    public static string? ParameterRootOnly() => ReadParameterRootOnly(Root.GetSection("Payments"));
    public static string? ParameterSectionOnly() => ReadParameterSectionOnly(Root.GetSection("Payments"));
    public static string? PropertyRootOnly() => SectionProperty["PropertyRootOnly"];
    public static string? PropertySectionOnly() => SectionProperty["PropertySectionOnly"];
    public static string? FieldRootOnly() => SectionField["FieldRootOnly"];
    public static string? FieldSectionOnly() => SectionField["FieldSectionOnly"];

    public static string? ProvenRootAlias()
    {
        IConfiguration alias = Root;
        return ReadProvenRoot(alias);
    }

    public static string? ConstructorAssignedRoot()
    {
        var receiver = new ConstructorAssignedReceiver(Root);
        return receiver.ReadField() ?? receiver.ReadProperty();
    }

    public static string? ConstructorAssignedSectionOnly() =>
        new ConstructorAssignedSectionReceiver(Root.GetSection("Payments")).Read();

    private static string? ReadParameterRootOnly(IConfiguration configuration) => configuration["ParameterRootOnly"];
    private static string? ReadParameterSectionOnly(IConfiguration configuration) => configuration["ParameterSectionOnly"];
    private static string? ReadProvenRoot(IConfiguration configuration) => configuration["ProvenRootRead"];
}

public sealed class ConstructorAssignedReceiver
{
    private readonly IConfiguration _field;
    private IConfiguration Property { get; }

    public ConstructorAssignedReceiver(IConfiguration configuration)
    {
        _field = configuration;
        Property = configuration;
    }

    public string? ReadField() => _field["ConstructorFieldRoot"];
    public string? ReadProperty() => Property["ConstructorPropertyRoot"];
}

public sealed class ConstructorAssignedSectionReceiver
{
    private readonly IConfiguration _field;

    public ConstructorAssignedSectionReceiver(IConfiguration configuration)
    {
        _field = configuration;
    }

    public string? Read() => _field["ConstructorAssignedSectionOnly"];
}
'@
    foreach ($root in @($receiverProvenanceRoot, $receiverProvenanceSection)) {
        Set-Content -LiteralPath (Join-Path $root 'ReceiverProvenance.cs') -Value $receiverSource -Encoding utf8NoBOM
    }

    [void](Assert-JsonCase -Name 'clean case' -Root $clean -ExpectedExitCode 0 -ExpectBlocking $false -ExpectDynamic $true)
    $missingResult = Assert-JsonCase -Name 'missing declaration case' -Root $missing -ExpectedExitCode 1 -ExpectBlocking $true -ExpectDynamic $true
    Assert-Contract ($missingResult.Output -match 'Missing:Key') 'The missing declaration case did not identify the planted key.'
    [void](Assert-JsonCase -Name 'dynamic key case' -Root $dynamic -ExpectedExitCode 0 -ExpectBlocking $false -ExpectDynamic $true)
    $counterexampleResult = Assert-JsonCase -Name 'preserved read and unknown bind case' -Root $counterexample -ExpectedExitCode 1 -ExpectBlocking $true -ExpectDynamic $true
    $counterexampleReport = $counterexampleResult.Output | ConvertFrom-Json
    Assert-Contract (@($counterexampleReport.actuallyReadKeys) -contains 'Fallback') 'The installed tool dropped the independently evaluated Fallback read.'
    Assert-Contract (@($counterexampleReport.findings | Where-Object { $_.code -eq 'CG900' -and $_.source -eq 'ConfigurationCounterexamples.cs' }).Count -gt 0) 'The installed tool dropped the unknown Bind observation.'
    Assert-Contract ($counterexampleResult.Output -match 'Fallback') 'The installed tool did not report the preserved Fallback read.'
    Assert-ReceiverProvenanceCase -Name 'root-shaped receiver provenance case' -Root $receiverProvenanceRoot -ExpectUnusedRootDeclaration $true
    Assert-ReceiverProvenanceCase -Name 'section-shaped receiver provenance case' -Root $receiverProvenanceSection -ExpectUnusedRootDeclaration $false
    Write-Output 'Isolated package-consumer smoke passed: clean, blocking missing declaration, dynamic key, preserved fallback read, unknown Bind, and source/installed-equivalent receiver provenance.'
}
finally {
    foreach ($name in $saved.Keys) {
        [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process')
    }
    if (Test-Path -LiteralPath $smokeRoot) { Remove-Item -LiteralPath $smokeRoot -Recurse -Force }
}

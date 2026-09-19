[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Contract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$root = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path ([IO.Path]::GetTempPath()) "configgap-release-contract-$([Guid]::NewGuid().ToString('N'))"
$checker = Join-Path $PSScriptRoot 'Verify-ReleaseContract.ps1'

try {
    New-Item -ItemType Directory -Path (Join-Path $fixture 'src/KeelMatrix.ConfigGap') -Force | Out-Null
    @'
<Project>
  <PropertyGroup>
    <Version>0.1.0</Version>
    <PackageVersion>$(Version)</PackageVersion>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $fixture 'Directory.Build.props') -Encoding utf8NoBOM
    @'
<Project>
  <PropertyGroup>
    <Version>0.1.0</Version>
    <PackageVersion>0.1.0</PackageVersion>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $fixture 'src/KeelMatrix.ConfigGap/KeelMatrix.ConfigGap.csproj') -Encoding utf8NoBOM

    @'
# Changelog

## [Unreleased]

### Added

- Planned work.
'@ | Set-Content -LiteralPath (Join-Path $fixture 'CHANGELOG.md') -Encoding utf8NoBOM
    $plannedOutput = (& pwsh -NoProfile -File $checker -Version 0.1.0 -FirstRelease -RepositoryRoot $fixture 2>&1 | Out-String)
    Assert-Contract ($LASTEXITCODE -ne 0 -and $plannedOutput -match 'no dated') 'The release contract accepted a changelog with only Unreleased.'

    @'
# Changelog

## [Unreleased]

## [0.1.0] - 2026-09-19

### Added

- Static analysis for declared .NET configuration usage.
'@ | Set-Content -LiteralPath (Join-Path $fixture 'CHANGELOG.md') -Encoding utf8NoBOM
    & pwsh -NoProfile -File $checker -Version 0.1.0 -TagName v0.1.0 -FirstRelease -RepositoryRoot $fixture
    Assert-Contract ($LASTEXITCODE -eq 0) 'The release contract rejected a finalized matching changelog.'

    @'
<Project>
  <PropertyGroup>
    <Version>0.2.0</Version>
    <PackageVersion>$(Version)</PackageVersion>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $fixture 'Directory.Build.props') -Encoding utf8NoBOM
    $mismatchOutput = (& pwsh -NoProfile -File $checker -Version 0.1.0 -TagName v0.1.0 -FirstRelease -RepositoryRoot $fixture 2>&1 | Out-String)
    Assert-Contract ($LASTEXITCODE -ne 0 -and $mismatchOutput -match 'expected') 'The release contract accepted a source version mismatch.'
    Write-Output 'Release contract tests passed: Unreleased rejection, finalized match, and version mismatch rejection.'
}
finally {
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}

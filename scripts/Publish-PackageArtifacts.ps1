[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [Parameter(Mandatory = $true)]
    [string]$SymbolsPath,

    [string]$Source = 'https://api.nuget.org/v3/index.json',

    [string]$DotnetCommand = 'dotnet'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Contract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Invoke-CheckedPush {
    param(
        [string]$Path,
        [switch]$NoSymbols
    )

    Assert-Contract (Test-Path -LiteralPath $Path -PathType Leaf) "Package artifact was not found: $Path"
    $arguments = @('nuget', 'push', $Path, '--api-key', $env:NUGET_API_KEY, '--source', $Source)
    if ($NoSymbols) { $arguments += '--no-symbols' }
    & $DotnetCommand @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet publication failed for '$Path' with exit code $LASTEXITCODE."
    }
}

Assert-Contract (-not [string]::IsNullOrWhiteSpace($env:NUGET_API_KEY)) 'NUGET_API_KEY must be provided by the Trusted Publishing login step.'
Invoke-CheckedPush -Path $PackagePath -NoSymbols
Invoke-CheckedPush -Path $SymbolsPath
Write-Output 'NuGet package and symbol publication commands completed successfully.'

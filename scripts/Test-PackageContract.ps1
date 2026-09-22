[CmdletBinding()]
param(
    [string]$PackagePath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/packages/KeelMatrix.ConfigGap.0.1.0.nupkg'),

    [string]$SymbolsPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/packages/KeelMatrix.ConfigGap.0.1.0.snupkg'),

    [string]$ExpectedVersion = '0.1.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Contract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Open-Archive {
    param([string]$Path, [IO.Compression.ZipArchiveMode]$Mode)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    return [IO.Compression.ZipFile]::Open((Resolve-Path -LiteralPath $Path).Path, $Mode)
}

function Copy-Entry {
    param([IO.Compression.ZipArchiveEntry]$Source, [IO.Compression.ZipArchiveEntry]$Destination)
    $input = $Source.Open()
    $output = $Destination.Open()
    try { $input.CopyTo($output) }
    finally {
        $output.Dispose()
        $input.Dispose()
    }
}

function Rewrite-Archive {
    param(
        [string]$SourcePath,
        [string]$DestinationPath,
        [string]$RemoveEntry,
        [string]$AddEntry
    )

    $source = Open-Archive -Path $SourcePath -Mode Read
    $destination = [IO.Compression.ZipFile]::Open($DestinationPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $source.Entries) {
            if ($entry.FullName -ceq $RemoveEntry) { continue }
            $copy = $destination.CreateEntry($entry.FullName)
            Copy-Entry -Source $entry -Destination $copy
        }

        if (-not [string]::IsNullOrWhiteSpace($AddEntry)) {
            $added = $destination.CreateEntry($AddEntry)
            $writer = [IO.StreamWriter]::new($added.Open())
            try { $writer.Write('synthetic unexpected package content') }
            finally { $writer.Dispose() }
        }
    }
    finally {
        $destination.Dispose()
        $source.Dispose()
    }
}

function Invoke-InspectorExpectFailure {
    param(
        [string]$MutatedPackagePath,
        [string]$MutatedSymbolsPath,
        [string]$ExpectedMessage,
        [string]$CaseName
    )

    $inspector = Join-Path $PSScriptRoot 'Inspect-Package.ps1'
    $output = (& pwsh -NoProfile -File $inspector -PackagePath $MutatedPackagePath -SymbolsPath $MutatedSymbolsPath -ExpectedVersion $ExpectedVersion 2>&1 | Out-String)
    $exitCode = $LASTEXITCODE
    Assert-Contract ($exitCode -ne 0) "The package inspector accepted the $CaseName mutation."
    Assert-Contract ($output -match $ExpectedMessage) "The package inspector rejected the $CaseName mutation for an unexpected reason: $output"
    Write-Output "Negative package gate passed: $CaseName (exit $exitCode)."
}

$root = Split-Path -Parent $PSScriptRoot
$publishRoot = Join-Path $root 'src/KeelMatrix.ConfigGap/bin/Release/net8.0/publish'
$guardedConfig = Join-Path $publishRoot 'appsettings.Development.json'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "configgap-package-negative-$([Guid]::NewGuid().ToString('N'))"
$mutatedPackage = Join-Path $temporaryRoot "KeelMatrix.ConfigGap.$ExpectedVersion.nupkg"
$mutatedSymbols = Join-Path $temporaryRoot "KeelMatrix.ConfigGap.$ExpectedVersion.snupkg"

try {
    Assert-Contract (Test-Path -LiteralPath $PackagePath -PathType Leaf) "Package was not found: $PackagePath"
    Assert-Contract (Test-Path -LiteralPath $SymbolsPath -PathType Leaf) "Symbols were not found: $SymbolsPath"
    Assert-Contract (Test-Path -LiteralPath $publishRoot -PathType Container) "Publish directory was not found: $publishRoot"
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null

    Set-Content -LiteralPath $guardedConfig -Value '{"ConnectionStrings":{"Default":"synthetic"}}' -Encoding utf8NoBOM
    try {
        $packArgs = @(
            'pack',
            (Join-Path $root 'src/KeelMatrix.ConfigGap/KeelMatrix.ConfigGap.csproj'),
            '-c', 'Release',
            '--no-build',
            '--no-restore',
            '-o', (Join-Path $temporaryRoot 'guarded-pack'),
            '--nologo'
        )
        $packOutput = (& dotnet @packArgs 2>&1 | Out-String)
        $packExitCode = $LASTEXITCODE
        Assert-Contract ($packExitCode -ne 0) 'The pack-time guard accepted appsettings.Development.json in the publish directory.'
        Assert-Contract ($packOutput -match 'prohibited publish input') "The pack-time guard rejected the local configuration mutation for an unexpected reason: $packOutput"
        Write-Output "Negative package gate passed: prohibited local configuration file (exit $packExitCode)."
    }
    finally {
        if (Test-Path -LiteralPath $guardedConfig) { Remove-Item -LiteralPath $guardedConfig -Force }
    }

    Rewrite-Archive -SourcePath $PackagePath -DestinationPath $mutatedPackage -RemoveEntry '' -AddEntry 'unexpected-entry.txt'
    Invoke-InspectorExpectFailure -MutatedPackagePath $mutatedPackage -MutatedSymbolsPath $SymbolsPath -ExpectedMessage 'explicit\s+expected\s+public\s+artifact\s+set' -CaseName 'unexpected archive entry'

    Rewrite-Archive -SourcePath $SymbolsPath -DestinationPath $mutatedSymbols -RemoveEntry 'tools/net8.0/any/KeelMatrix.ConfigGap.pdb' -AddEntry ''
    Invoke-InspectorExpectFailure -MutatedPackagePath $PackagePath -MutatedSymbolsPath $mutatedSymbols -ExpectedMessage 'explicit\s+expected\s+symbol\s+set|required\s+symbol\s+entry' -CaseName 'missing required symbol entry'

    Write-Output 'Package contract negative tests passed.'
}
finally {
    if (Test-Path -LiteralPath $guardedConfig) { Remove-Item -LiteralPath $guardedConfig -Force }
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}

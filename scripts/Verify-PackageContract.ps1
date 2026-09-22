[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [Parameter(Mandatory = $true)]
    [string]$SymbolsPath,

    [string]$ExpectedVersion = '0.1.0',

    [string]$ExpectedRepositoryCommit
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

function Get-CanonicalArchiveHash {
    param([string]$Path)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Path).Path)
    try {
        $parts = [Collections.Generic.List[string]]::new()
        foreach ($entry in @($archive.Entries | Sort-Object FullName)) {
            $name = $entry.FullName.Replace('\', '/')
            $memory = [IO.MemoryStream]::new()
            $stream = $entry.Open()
            try { $stream.CopyTo($memory); $bytes = $memory.ToArray() }
            finally { $stream.Dispose(); $memory.Dispose() }
            if ($name -eq '_rels/.rels') {
                $text = [Text.Encoding]::UTF8.GetString($bytes)
                $text = [regex]::Replace($text, '(?i)(?<=Target="/package/services/metadata/core-properties/)[0-9a-f]{32}(?=\.psmdcp")', '{core-properties-id}')
                $text = [regex]::Replace($text, '(?i)(?<=Id=")R[0-9A-F]+(?=")', '{relationship-id}')
                $bytes = [Text.Encoding]::UTF8.GetBytes($text)
            }
            $canonicalName = [regex]::Replace($name, '(?i)(?<=core-properties/)[0-9a-f]{32}(?=\.psmdcp$)', '{core-properties-id}')
            $hash = ([Security.Cryptography.SHA256]::Create().ComputeHash($bytes) | ForEach-Object ToString x2) -join ''
            $parts.Add("$canonicalName=$hash")
        }
        $payload = [Text.Encoding]::UTF8.GetBytes(($parts -join "`n"))
        return ([Security.Cryptography.SHA256]::Create().ComputeHash($payload) | ForEach-Object ToString x2) -join ''
    }
    finally { $archive.Dispose() }
}

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src/KeelMatrix.ConfigGap/KeelMatrix.ConfigGap.csproj'
$commit = if ([string]::IsNullOrWhiteSpace($ExpectedRepositoryCommit)) { (& git -C $repo rev-parse HEAD).Trim() } else { $ExpectedRepositoryCommit.Trim() }
$verificationRoot = Join-Path ([IO.Path]::GetTempPath()) "configgap-package-contract-$([Guid]::NewGuid().ToString('N'))"
$first = Join-Path $verificationRoot 'first'
$second = Join-Path $verificationRoot 'second'
$inspector = Join-Path $PSScriptRoot 'Inspect-Package.ps1'

try {
    Assert-Contract ($commit -match '^[0-9a-fA-F]{40}$') 'A full repository commit is required for deterministic package evidence.'
    Assert-Contract (Test-Path -LiteralPath $PackagePath -PathType Leaf) "Package artifact was not found: $PackagePath"
    Assert-Contract (Test-Path -LiteralPath $SymbolsPath -PathType Leaf) "Symbol artifact was not found: $SymbolsPath"
    New-Item -ItemType Directory -Path $first, $second -Force | Out-Null
    $pack = @('pack', $project, '-c', 'Release', '--no-build', '--no-restore', '--include-symbols', '-p:SymbolPackageFormat=snupkg', "-p:PackageVersion=$ExpectedVersion", "-p:SourceRevisionId=$commit", "-p:RepositoryCommit=$commit")
    Invoke-Checked -File 'dotnet' -Arguments ($pack + @('-o', $first))
    Invoke-Checked -File 'dotnet' -Arguments ($pack + @('-o', $second))
    $expected = @("KeelMatrix.ConfigGap.$ExpectedVersion.nupkg", "KeelMatrix.ConfigGap.$ExpectedVersion.snupkg") | Sort-Object
    foreach ($directory in @($first, $second)) {
        $actual = @(Get-ChildItem -LiteralPath $directory -File | Select-Object -ExpandProperty Name | Sort-Object)
        Assert-Contract (@(Compare-Object $expected $actual).Count -eq 0) "Unexpected package output in ${directory}: $($actual -join ', ')."
    }
    $firstPackage = Join-Path $first $expected[0]
    $secondPackage = Join-Path $second $expected[0]
    $firstSymbols = Join-Path $first $expected[1]
    $secondSymbols = Join-Path $second $expected[1]
    Assert-Contract ((Get-CanonicalArchiveHash $firstPackage) -ceq (Get-CanonicalArchiveHash $secondPackage)) 'Repeat tool packs are not deterministic.'
    Assert-Contract ((Get-CanonicalArchiveHash $firstSymbols) -ceq (Get-CanonicalArchiveHash $secondSymbols)) 'Repeat symbol packs are not deterministic.'
    Invoke-Checked -File 'pwsh' -Arguments @('-NoProfile', '-File', $inspector, '-PackagePath', $PackagePath, '-SymbolsPath', $SymbolsPath, '-ExpectedVersion', $ExpectedVersion, '-ExpectedRepositoryCommit', $commit)
    Write-Output "Repeat-pack determinism passed for $ExpectedVersion."
    Write-Output "Tool package SHA256: $((Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash)"
    Write-Output "Symbol package SHA256: $((Get-FileHash -LiteralPath $SymbolsPath -Algorithm SHA256).Hash)"
}
finally {
    if (Test-Path -LiteralPath $verificationRoot) { Remove-Item -LiteralPath $verificationRoot -Recurse -Force }
}

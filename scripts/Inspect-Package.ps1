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
    if (-not $Condition) {
        throw $Message
    }
}

function Open-Archive {
    param([Parameter(Mandatory = $true)][string]$Path)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    return [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Path).Path)
}

function Get-NormalizedNames {
    param([Parameter(Mandatory = $true)][IO.Compression.ZipArchive]$Archive)
    return @($Archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') } | Sort-Object)
}

function Get-EntryBytes {
    param(
        [Parameter(Mandatory = $true)][IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)][string]$Name
    )
    $entry = $Archive.GetEntry($Name)
    Assert-Contract ($null -ne $entry) "Archive entry '$Name' is missing."
    $memory = [IO.MemoryStream]::new()
    $stream = $entry.Open()
    try {
        $stream.CopyTo($memory)
        return $memory.ToArray()
    }
    finally {
        $stream.Dispose()
        $memory.Dispose()
    }
}

function Get-EntryText {
    param(
        [Parameter(Mandatory = $true)][IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)][string]$Name
    )
    return [Text.Encoding]::UTF8.GetString((Get-EntryBytes -Archive $Archive -Name $Name)).TrimStart([char]0xFEFF)
}

function Read-BigEndianInt32 {
    param([byte[]]$Bytes, [int]$Offset)
    return (([int]$Bytes[$Offset] -shl 24) -bor ([int]$Bytes[$Offset + 1] -shl 16) -bor ([int]$Bytes[$Offset + 2] -shl 8) -bor [int]$Bytes[$Offset + 3])
}

function Assert-Icon {
    param([byte[]]$Bytes)
    $signature = [byte[]](0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a)
    Assert-Contract ($Bytes.Length -ge 24) 'The package icon is too small to be a PNG.'
    Assert-Contract (@(Compare-Object $signature $Bytes[0..7]).Count -eq 0) 'The package icon is not a PNG.'
    Assert-Contract ([Text.Encoding]::ASCII.GetString($Bytes[12..15]) -ceq 'IHDR') 'The package icon has no PNG IHDR chunk.'
    Assert-Contract ((Read-BigEndianInt32 -Bytes $Bytes -Offset 16) -eq 512) 'The package icon width is not exactly 512 pixels.'
    Assert-Contract ((Read-BigEndianInt32 -Bytes $Bytes -Offset 20) -eq 512) 'The package icon height is not exactly 512 pixels.'
    Assert-Contract ($Bytes.Length -le 200KB) "The package icon is larger than 200 KB ($($Bytes.Length) bytes)."
}

function Assert-ProjectPackability {
    param([string]$RepositoryRoot)
    $projects = @(Get-ChildItem -LiteralPath $RepositoryRoot -Recurse -Filter '*.csproj' -File |
        Where-Object { $_.FullName -notmatch '[\\/]bin[\\/]|[\\/]obj[\\/]' })
    $shipping = @($projects | Where-Object {
        $relativePath = [IO.Path]::GetRelativePath($RepositoryRoot, $_.FullName).Replace('\', '/')
        $relativePath -ceq 'src/KeelMatrix.ConfigGap/KeelMatrix.ConfigGap.csproj'
    })
    Assert-Contract ($shipping.Count -eq 1) 'The shipping project is not uniquely identified.'
    $shippingText = Get-Content -Raw -LiteralPath $shipping[0].FullName
    Assert-Contract ($shippingText -match '<IsPackable>true</IsPackable>') 'The shipping project must be packable.'
    foreach ($project in $projects | Where-Object { $_.FullName -ne $shipping[0].FullName }) {
        $text = Get-Content -Raw -LiteralPath $project.FullName
        Assert-Contract ($text -match '<IsPackable>false</IsPackable>') "Non-shipping project is missing an explicit IsPackable=false: $($project.Name)."
    }
}

function Assert-ForbiddenEntries {
    param([string[]]$Names)
    $forbidden = $Names | Where-Object {
        $_ -match '(?i)(^|/)(\.git|\.env[^/]*|research|fixtures|tests|scripts|docs|artifacts|bin|obj)(/|$)' -or
        $_ -match '(?i)(^|/)(appsettings(?:\.[^/]*)?\.json|.*(?:credentials|secrets|local\.telemetry).*|.*\.(pfx|p12|pem|key|snk))$' -or
        $_ -match '(?i)(prompt-engineering|orchestration|review-process)'
    }
    Assert-Contract (@($forbidden).Count -eq 0) "Forbidden package entries: $($forbidden -join ', ')."
    $sourceEntries = $Names | Where-Object { $_ -match '(?i)\.(cs|csproj|props|targets|sln|user)$' }
    Assert-Contract (@($sourceEntries).Count -eq 0) "Source or project files were packaged: $($sourceEntries -join ', ')."
}

function Inspect-ToolPackage {
    param(
        [string]$Path,
        [string]$RepositoryRoot
    )
    Assert-Contract (Test-Path -LiteralPath $Path -PathType Leaf) "Package was not found: $Path"
    Assert-Contract ((Split-Path -Leaf $Path) -ceq "KeelMatrix.ConfigGap.$ExpectedVersion.nupkg") 'The tool package filename does not match its version.'
    $archive = Open-Archive -Path $Path
    try {
        $names = Get-NormalizedNames -Archive $archive
        Assert-ForbiddenEntries -Names $names
        $nuspecName = $names | Where-Object { $_ -ceq 'KeelMatrix.ConfigGap.nuspec' }
        Assert-Contract (@($nuspecName).Count -eq 1) 'The tool package must contain exactly one KeelMatrix.ConfigGap.nuspec.'
        foreach ($required in @('README.md', 'LICENSE', 'icon.png', 'DotnetToolSettings.xml')) {
            Assert-Contract ($names -contains $required -or $names -contains "tools/net8.0/any/$required") "Required package entry '$required' is missing."
        }
        $corePropertyEntries = @($names | Where-Object { $_ -match '^package/services/metadata/core-properties/[0-9a-f]{32}\.psmdcp$' })
        Assert-Contract ($corePropertyEntries.Count -eq 1) 'The tool package must contain exactly one NuGet core-properties entry.'
        $expectedRoot = @('_rels/.rels', '[Content_Types].xml', 'KeelMatrix.ConfigGap.nuspec', 'README.md', 'LICENSE', 'icon.png') + $corePropertyEntries
        $actualRoot = @($names | Where-Object { $_ -notlike 'tools/net8.0/any/*' }) | Sort-Object
        Assert-Contract (@(Compare-Object ($expectedRoot | Sort-Object) $actualRoot).Count -eq 0) 'The tool package root differs from the explicit expected public artifact set.'
        $payload = @($names | Where-Object { $_ -like 'tools/net8.0/any/*' -and -not $_.EndsWith('/') })
        Assert-Contract ($payload.Count -gt 0) 'The tool package has no net8.0 tool payload.'
        $publishRoot = Join-Path $RepositoryRoot 'src/KeelMatrix.ConfigGap/bin/Release/net8.0/publish'
        Assert-Contract (Test-Path -LiteralPath $publishRoot -PathType Container) "The Release publish directory is missing: $publishRoot"
        $manifestPath = Join-Path $RepositoryRoot 'scripts/ExpectedPackagePayload.txt'
        Assert-Contract (Test-Path -LiteralPath $manifestPath -PathType Leaf) "The explicit package payload manifest is missing: $manifestPath"
        $expectedPayload = @(Get-Content -LiteralPath $manifestPath |
            ForEach-Object { $_.Trim() } |
            Where-Object { $_ -and -not $_.StartsWith('#', [StringComparison]::Ordinal) } |
            ForEach-Object { "tools/net8.0/any/$($_.Replace('\', '/'))" } |
            Sort-Object -Unique)
        Assert-Contract ($expectedPayload.Count -gt 0) 'The explicit package payload manifest is empty.'
        Assert-Contract ($expectedPayload -contains 'tools/net8.0/any/DotnetToolSettings.xml') 'The explicit package payload manifest must include DotnetToolSettings.xml.'
        Assert-Contract (@(Compare-Object $expectedPayload ($payload | Sort-Object)).Count -eq 0) 'The tool payload differs from the explicit expected public artifact set.'
        Assert-Contract ($payload -contains 'tools/net8.0/any/KeelMatrix.ConfigGap.dll') 'The shipping tool assembly is missing.'
        Assert-Contract ($payload -contains 'tools/net8.0/any/KeelMatrix.ConfigGap.runtimeconfig.json') 'The tool runtime configuration is missing.'
        Assert-Contract ($payload -contains 'tools/net8.0/any/KeelMatrix.Telemetry.dll') 'The required KeelMatrix.Telemetry runtime assembly is missing.'

        $nuspec = [xml](Get-EntryText -Archive $archive -Name 'KeelMatrix.ConfigGap.nuspec')
        $metadata = $nuspec.package.metadata
        Assert-Contract ($metadata.id -ceq 'KeelMatrix.ConfigGap') 'Package metadata has the wrong id.'
        Assert-Contract ($metadata.version -ceq $ExpectedVersion) 'Package metadata has the wrong version.'
        Assert-Contract ($metadata.authors -ceq 'KeelMatrix') 'Package metadata has the wrong authors.'
        Assert-Contract ($metadata.description -ceq 'Find gaps between statically used .NET configuration keys and the configuration structure your repository declares.') 'Package description is inconsistent.'
        Assert-Contract ($metadata.tags -match 'configuration' -and $metadata.tags -match 'roslyn' -and $metadata.tags -match 'dotnet-tool') 'Package tags are incomplete.'
        Assert-Contract ($metadata.readme -ceq 'README.md') 'Package README metadata is missing.'
        Assert-Contract ($metadata.license.InnerText -ceq 'LICENSE') 'Package license metadata is missing.'
        Assert-Contract ($metadata.icon -ceq 'icon.png') 'Package icon metadata is missing.'
        Assert-Contract ($metadata.repository.url -ceq 'https://github.com/KeelMatrix/ConfigGap') 'Repository metadata is inconsistent.'
        Assert-Contract ($metadata.releaseNotes -ceq 'Static analysis for gaps between declared and statically used .NET configuration.') 'Release notes are inconsistent.'
        Assert-Icon -Bytes (Get-EntryBytes -Archive $archive -Name 'icon.png')

        if (-not [string]::IsNullOrWhiteSpace($ExpectedRepositoryCommit)) {
            Assert-Contract ($metadata.repository.commit -ceq $ExpectedRepositoryCommit) 'Repository commit metadata is inconsistent.'
        }
        Write-Output "Tool package entries ($($names.Count)):`n$($names -join "`n")"
    }
    finally {
        $archive.Dispose()
    }
}

function Inspect-SymbolPackage {
    param(
        [string]$Path,
        [string]$ExpectedRepositoryCommit
    )
    Assert-Contract (Test-Path -LiteralPath $Path -PathType Leaf) "Symbol package was not found: $Path"
    Assert-Contract ((Split-Path -Leaf $Path) -ceq "KeelMatrix.ConfigGap.$ExpectedVersion.snupkg") 'The symbol package filename does not match its version.'
    $archive = Open-Archive -Path $Path
    try {
        $names = Get-NormalizedNames -Archive $archive
        Assert-ForbiddenEntries -Names $names
        $expectedEntries = @(
            '_rels/.rels',
            '[Content_Types].xml',
            'KeelMatrix.ConfigGap.nuspec',
            'core-properties-entry',
            'tools/net8.0/any/KeelMatrix.ConfigGap.Core.pdb',
            'tools/net8.0/any/KeelMatrix.ConfigGap.pdb'
        )
        $corePropertyEntries = @($names | Where-Object { $_ -match '^package/services/metadata/core-properties/[0-9a-f]{32}\.psmdcp$' })
        Assert-Contract ($corePropertyEntries.Count -eq 1) 'The symbol package must contain exactly one NuGet core-properties entry.'
        $expectedEntries[3] = $corePropertyEntries[0]
        Assert-Contract (@(Compare-Object ($expectedEntries | Sort-Object) ($names | Sort-Object)).Count -eq 0) 'The symbol package entries differ from the explicit expected symbol set.'
        foreach ($required in @('tools/net8.0/any/KeelMatrix.ConfigGap.Core.pdb', 'tools/net8.0/any/KeelMatrix.ConfigGap.pdb')) {
            Assert-Contract ($names -contains $required) "Required symbol entry '$required' is missing."
        }
        Assert-Contract (@($names | Where-Object { $_ -match '(?i)\.(cs|csproj|props|targets)$' }).Count -eq 0) 'The symbol package contains source or project files.'
        $nuspec = [xml](Get-EntryText -Archive $archive -Name 'KeelMatrix.ConfigGap.nuspec')
        $metadata = $nuspec.package.metadata
        Assert-Contract ($metadata.id -ceq 'KeelMatrix.ConfigGap') 'Symbol package metadata has the wrong id.'
        Assert-Contract ($metadata.version -ceq $ExpectedVersion) 'Symbol package metadata has the wrong version.'
        Assert-Contract ($metadata.packageTypes.packageType.name -ceq 'SymbolsPackage') 'Symbol package metadata is missing its SymbolsPackage type.'
        Assert-Contract ($metadata.repository.url -ceq 'https://github.com/KeelMatrix/ConfigGap') 'Symbol package repository metadata is inconsistent.'
        if (-not [string]::IsNullOrWhiteSpace($ExpectedRepositoryCommit)) {
            Assert-Contract ($metadata.repository.commit -ceq $ExpectedRepositoryCommit) 'Symbol package repository commit metadata is inconsistent.'
        }
        Write-Output "Symbol package entries ($($names.Count)):`n$($names -join "`n")"
    }
    finally {
        $archive.Dispose()
    }
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Assert-ProjectPackability -RepositoryRoot $repositoryRoot
Inspect-ToolPackage -Path $PackagePath -RepositoryRoot $repositoryRoot
Inspect-SymbolPackage -Path $SymbolsPath -ExpectedRepositoryCommit $ExpectedRepositoryCommit
Write-Output 'Package inspection passed.'

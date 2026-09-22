[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$TagName,

    [string]$RepositoryRoot,

    [switch]$FirstRelease
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Contract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

Assert-Contract ($Version -match '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') "Release version must be semantic MAJOR.MINOR.PATCH without a prerelease suffix: '$Version'."
if (-not [string]::IsNullOrWhiteSpace($TagName)) {
    Assert-Contract ($TagName -ceq "v$Version") "Release tag '$TagName' does not match v$Version."
}

$repo = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { Split-Path -Parent $PSScriptRoot } else { (Resolve-Path -LiteralPath $RepositoryRoot).Path }
$changelogPath = Join-Path $repo 'CHANGELOG.md'
$projectPath = Join-Path $repo 'src/KeelMatrix.ConfigGap/KeelMatrix.ConfigGap.csproj'
$propsPath = Join-Path $repo 'Directory.Build.props'
Assert-Contract (Test-Path -LiteralPath $changelogPath -PathType Leaf) 'CHANGELOG.md is required.'
Assert-Contract (Test-Path -LiteralPath $projectPath -PathType Leaf) 'The shipping project is required.'
Assert-Contract (Test-Path -LiteralPath $propsPath -PathType Leaf) 'Directory.Build.props is required.'

$changelog = Get-Content -Raw -LiteralPath $changelogPath
Assert-Contract ($changelog -match '(?m)^## \[Unreleased\]\s*$') 'CHANGELOG.md must retain a separate Unreleased section.'
$escapedVersion = [regex]::Escape($Version)
$match = [regex]::Match($changelog, "(?ms)^## \[$escapedVersion\]\s*-\s*(?<date>\d{4}-\d{2}-\d{2})\s*$")
Assert-Contract $match.Success "CHANGELOG.md has no dated [$Version] release entry."
$dateText = $match.Groups['date'].Value
$parsedDate = [datetime]::MinValue
Assert-Contract ([datetime]::TryParseExact(
        $dateText,
        'yyyy-MM-dd',
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::None,
        [ref]$parsedDate) -and $parsedDate.ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture) -ceq $dateText) "Release date '$dateText' is not a valid calendar date."
$sectionStart = $match.Index + $match.Length
$nextSection = [regex]::Match($changelog.Substring($sectionStart), '(?m)^## \[')
$section = if ($nextSection.Success) { $changelog.Substring($sectionStart, $nextSection.Index) } else { $changelog.Substring($sectionStart) }
Assert-Contract ($section -notmatch '(?i)\b(Unreleased|Planned|TBD|not yet published)\b') "CHANGELOG.md still describes [$Version] as not final."

if ($FirstRelease) {
    $categories = @([regex]::Matches($section, '(?m)^###\s+(?<name>\S(?:.*\S)?)\s*$') | ForEach-Object { $_.Groups['name'].Value.Trim() })
    Assert-Contract ($categories.Count -eq 1 -and $categories[0] -ceq 'Added') 'A first-release changelog entry must contain exactly one Added section and no other categories.'
    $addedMatch = [regex]::Match($section, '(?ms)^###\s+Added\s*$(?<body>.*)$')
    Assert-Contract $addedMatch.Success 'A first-release changelog entry must contain an Added section.'
    Assert-Contract ($addedMatch.Groups['body'].Value -match '(?m)^\s*[-*+]\s+\S') 'A first-release Added section must contain at least one non-empty bullet.'
    $markers = @('now', 'no longer', 'previously', 'formerly', 'used to', 'fixed', 'fixes', 'corrected', 'resolved', 'addressed', 'this removes', 'this fixes', 'changed from')
    foreach ($marker in $markers) {
        Assert-Contract ($section -notmatch "(?i)\b$([regex]::Escape($marker))\b") "First-release changelog entry contains remediation-history wording: '$marker'."
    }
}

function Read-Xml([string]$Path) { return [xml](Get-Content -Raw -LiteralPath $Path) }
foreach ($path in @($propsPath, $projectPath)) {
    $xml = Read-Xml $path
    foreach ($node in @($xml.SelectNodes("//*[local-name()='Version' or local-name()='PackageVersion']"))) {
        $value = $node.InnerText.Trim()
        if ($node.LocalName -eq 'PackageVersion' -and $value -eq '$(Version)') { continue }
        Assert-Contract ($value -ceq $Version) "$path contains $($node.LocalName) '$value', expected '$Version'."
    }
}

Assert-Contract ($changelog -match "(?m)^## \[$escapedVersion\]") "Release version [$Version] is not represented in the changelog."
Write-Output "Release contract passed for $Version$(if ($TagName) { " ($TagName)" })."

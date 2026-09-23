[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Get-Location).Path,
    [string] $Commit = 'HEAD'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Git {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $output = & git -C $RepositoryRoot @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed: $($output -join [Environment]::NewLine)"
    }

    return @($output)
}

function New-Text {
    param(
        [Parameter(Mandatory)]
        [int[]] $CharacterCodes
    )

    return -join ($CharacterCodes | ForEach-Object { [char] $_ })
}

function Test-PublicTextPath {
    param([Parameter(Mandatory)][string] $Path)

    $fileName = [IO.Path]::GetFileName($Path)
    if ($fileName -in @('LICENSE', '.editorconfig', '.gitattributes', '.gitignore')) { return $true }
    return [IO.Path]::GetExtension($Path).ToLowerInvariant() -in @(
        '.config', '.cs', '.csproj', '.json', '.md', '.props', '.ps1', '.psm1',
        '.sln', '.targets', '.txt', '.xml', '.yaml', '.yml'
    )
}

$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$reachableCommits = @(Invoke-Git -Arguments @('rev-list', $Commit))
if ($reachableCommits.Count -eq 0) {
    throw "No reachable commits were found from $Commit."
}

# Keep the detector itself free of the internal names it prevents from entering history.
$coAuthorLabel = New-Text @(67, 111, 45, 65, 117, 116, 104, 111, 114, 101, 100, 45, 66, 121)
$forbiddenTerms = @(
    (New-Text @(112, 97, 112, 101, 114, 99, 108, 105, 112)),
    (New-Text @(99, 111, 100, 101, 120)),
    (New-Text @(99, 104, 97, 116, 103, 112, 116)),
    (New-Text @(99, 108, 97, 117, 100, 101)),
    (New-Text @(100, 101, 101, 112, 115, 101, 101, 107)),
    (New-Text @(111, 112, 101, 110, 97, 105)),
    (New-Text @(112, 114, 111, 109, 112, 116, 45, 101, 110, 103, 105, 110, 101, 101, 114, 105, 110, 103)),
    (New-Text @(111, 114, 99, 104, 101, 115, 116, 114, 97, 116)),
    (New-Text @(114, 101, 118, 105, 101, 119, 45, 112, 114, 111, 99, 101, 115, 115)),
    (New-Text @(98, 111, 97, 114, 100, 32, 100, 101, 108, 105, 98, 101, 114, 97, 116, 105, 111, 110)),
    (New-Text @(105, 110, 116, 101, 114, 110, 97, 108, 32, 116, 111, 111, 108)),
    (New-Text @(97, 103, 101, 110, 116, 47, 109, 111, 100, 101, 108)),
    (New-Text @(109, 111, 100, 101, 108, 32, 97, 116, 116, 114, 105, 98, 117, 116, 105, 111, 110)),
    (New-Text @(112, 114, 111, 109, 112, 116, 32, 105, 110, 106, 101, 99, 116, 105, 111, 110))
)
$forbiddenFileTerms = @($forbiddenTerms) + @(
    (New-Text @(105, 110, 100, 101, 112, 101, 110, 100, 101, 110, 116, 32, 114, 101, 118, 105, 101, 119, 101, 114)),
    (New-Text @(114, 101, 118, 105, 101, 119, 101, 114, 32, 115, 97, 109, 112, 108, 101)),
    (New-Text @(114, 101, 118, 105, 101, 119, 101, 114, 115, 97, 109, 112, 108, 101)),
    (New-Text @(102, 105, 110, 97, 108, 32, 102, 105, 120, 32, 99, 108, 111, 115, 117, 114, 101)),
    (New-Text @(102, 105, 120, 32, 99, 108, 111, 115, 117, 114, 101, 32, 114, 101, 114, 117, 110)),
    (New-Text @(114, 101, 118, 105, 101, 119, 32, 114, 111, 117, 110, 100)),
    (New-Text @(114, 101, 109, 101, 100, 105, 97, 116, 105, 111, 110, 32, 114, 111, 117, 110, 100)),
    (New-Text @(114, 101, 118, 105, 101, 119, 32, 99, 104, 101, 99, 107, 112, 111, 105, 110, 116)),
    (New-Text @(99, 97, 110, 100, 105, 100, 97, 116, 101, 32, 99, 104, 101, 99, 107, 112, 111, 105, 110, 116)),
    (New-Text @(114, 101, 99, 111, 114, 100, 101, 100, 32, 99, 97, 110, 100, 105, 100, 97, 116, 101, 32, 114, 101, 102)),
    (New-Text @(102, 114, 111, 110, 116, 105, 101, 114, 32, 102, 105, 110, 100, 105, 110, 103))
)

$messageViolations = [System.Collections.Generic.List[string]]::new()
$fileViolations = [System.Collections.Generic.List[string]]::new()
$coAuthorPattern = '(?i)^\s*' + [regex]::Escape($coAuthorLabel) + '\s*:'

foreach ($reachableCommit in $reachableCommits) {
    $messageLines = @(Invoke-Git -Arguments @('show', '-s', '--format=%B', $reachableCommit))
    foreach ($line in $messageLines) {
        if ($line -match $coAuthorPattern) {
            $messageViolations.Add("$reachableCommit`tco-author trailer`t$line")
        }

        foreach ($forbiddenTerm in $forbiddenTerms) {
            if ($line.IndexOf($forbiddenTerm, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $messageViolations.Add("$reachableCommit`tforbidden history term`t$line")
                break
            }
        }
    }
}

$trackedFiles = @(Invoke-Git -Arguments @('ls-tree', '-r', '--name-only', $Commit) | Where-Object { Test-PublicTextPath -Path $_ })
foreach ($trackedFile in $trackedFiles) {
    $lineNumber = 0
    foreach ($line in @(Invoke-Git -Arguments @('show', "${Commit}:$trackedFile"))) {
        $lineNumber++
        if ($line -match $coAuthorPattern) {
            $fileViolations.Add("${trackedFile}:$lineNumber`tco-author trailer`t$line")
        }

        foreach ($forbiddenTerm in $forbiddenFileTerms) {
            if ($line.IndexOf($forbiddenTerm, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $fileViolations.Add("${trackedFile}:$lineNumber`tforbidden public-file term`t$line")
                break
            }
        }
    }
}

if ($messageViolations.Count -gt 0 -or $fileViolations.Count -gt 0) {
    Write-Output 'Public history hygiene check failed.'
    if ($messageViolations.Count -gt 0) {
        Write-Output "Commit message hygiene: FAIL ($($messageViolations.Count) violation(s))."
        $messageViolations | Sort-Object -Unique | ForEach-Object { Write-Output $_ }
    }
    else {
        Write-Output "Commit message hygiene: PASS ($($reachableCommits.Count) reachable commits scanned)."
    }
    if ($fileViolations.Count -gt 0) {
        Write-Output "Tracked public file hygiene: FAIL ($($fileViolations.Count) violation(s))."
        $fileViolations | Sort-Object -Unique | ForEach-Object { Write-Output $_ }
    }
    else {
        Write-Output "Tracked public file hygiene: PASS ($($trackedFiles.Count) files scanned)."
    }
    exit 1
}

Write-Output "Commit message hygiene: PASS ($($reachableCommits.Count) reachable commits scanned from $Commit)."
Write-Output "Tracked public file hygiene: PASS ($($trackedFiles.Count) files scanned from $Commit)."
Write-Output 'Public history hygiene check passed.'

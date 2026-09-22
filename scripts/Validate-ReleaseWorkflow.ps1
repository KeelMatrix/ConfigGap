[CmdletBinding()]
param(
    [string]$WorkflowPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Contract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$repo = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($WorkflowPath)) { $WorkflowPath = Join-Path $repo '.github/workflows/release.yml' }
Assert-Contract (Test-Path -LiteralPath $WorkflowPath -PathType Leaf) "Release workflow was not found: $WorkflowPath"
$workflow = Get-Content -Raw -LiteralPath $WorkflowPath

Assert-Contract ($workflow -match '(?m)^on:\s*$') 'Workflow must declare an on block.'
Assert-Contract ($workflow -match '(?m)^\s+push:\s*$' -and $workflow -match '(?m)^\s+tags:\s*$') 'Workflow must use tag-only push triggering.'
Assert-Contract ($workflow -match '(?m)^\s+workflow_dispatch:\s*$') 'Workflow must support manual dispatch.'
foreach ($forbiddenTrigger in @('pull_request', 'pull_request_target', 'schedule', 'workflow_run', 'repository_dispatch')) {
    Assert-Contract ($workflow -notmatch "(?m)^\s+${forbiddenTrigger}:\s*$") "Forbidden workflow trigger found: $forbiddenTrigger."
}
Assert-Contract ($workflow -notmatch '(?m)^\s+branches\s*:') 'Branch push triggering is forbidden for this release workflow.'
Assert-Contract ($workflow -match 'NuGet/login@v1') 'NuGet Trusted Publishing login is missing.'
Assert-Contract ($workflow -match 'user:\s+dmitriyzen') 'NuGet Trusted Publishing username must be dmitriyzen.'
Assert-Contract ($workflow -notmatch 'secrets\.NUGET_API_KEY|secrets\.NUGET_TOKEN') 'Long-lived NuGet publish secrets are forbidden.'
Assert-Contract ($workflow -match 'id-token:\s+write') 'OIDC id-token write permission is missing.'
Assert-Contract ($workflow -notmatch 'permissions:\s*write-all|contents:\s+write') 'Workflow permissions are broader than required.'
Assert-Contract ($workflow -match 'timeout-minutes:\s+30') 'A job timeout is required.'
Assert-Contract ($workflow -match 'KEELMATRIX_NO_TELEMETRY:\s*''1''') 'Release telemetry must be disabled.'
Assert-Contract ($workflow -match 'Invoke-VulnerabilityAudit\.ps1') 'Release workflow must run the vulnerability gate.'
Assert-Contract ($workflow -match 'Verify-ReleaseContract\.ps1') 'Release workflow must run the shared release contract check.'
Assert-Contract ($workflow -match 'Verify-PackageContract\.ps1') 'Release workflow must validate package metadata.'
Assert-Contract ($workflow -match 'Publish-PackageArtifacts\.ps1') 'Release workflow must use the checked publication helper.'
Assert-Contract ($workflow -notmatch '--skip-duplicate') 'Release workflow must not treat duplicate skipping as publication proof.'

$actions = [regex]::Matches($workflow, '(?m)^\s+uses:\s+(?<action>[^\s]+)\s*$') | ForEach-Object { $_.Groups['action'].Value }
Assert-Contract ($actions.Count -gt 0) 'Workflow has no actions.'
foreach ($action in $actions) {
    Assert-Contract ($action -match '@v\d+(?:\.\d+(?:\.\d+)?)?$') "Action reference is not version-pinned: $action"
}

Write-Output "Release workflow static contract passed: $WorkflowPath"

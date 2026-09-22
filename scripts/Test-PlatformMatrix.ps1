[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Image = 'mcr.microsoft.com/dotnet/sdk:8.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-Tail {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][AllowNull()][AllowEmptyString()][string[]]$Lines,
        [int]$Count = 20
    )

    $contentLines = @($Lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($contentLines.Count -eq 0) {
        return '<no output>'
    }

    return (($contentLines | Select-Object -Last $Count) -join [Environment]::NewLine)
}

function Invoke-CheckedDocker {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $output = @(& docker @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0) {
        throw "CONFIGGAP_PLATFORM_DOCKER_FAILURE: $Description failed with exit code $LASTEXITCODE.`n$(Get-Tail -Lines $output)"
    }

    return $output
}

$repo = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$dockerCommand = Get-Command docker -ErrorAction SilentlyContinue
if ($null -eq $dockerCommand) {
    throw 'CONFIGGAP_PLATFORM_DOCKER_UNAVAILABLE: docker was not found on PATH.'
}

$dockerInfo = @(& docker info 2>&1 | ForEach-Object { $_.ToString() })
if ($LASTEXITCODE -ne 0) {
    throw "CONFIGGAP_PLATFORM_DOCKER_UNAVAILABLE: docker info failed with exit code $LASTEXITCODE.`n$(Get-Tail -Lines $dockerInfo)"
}

Write-Output "Platform image tag: $Image"
Invoke-CheckedDocker -Arguments @('pull', $Image) -Description "pulling image '$Image'" | ForEach-Object { Write-Output $_ }
$digest = (Invoke-CheckedDocker -Arguments @('image', 'inspect', '--format', '{{index .RepoDigests 0}}', $Image) -Description "inspecting image '$Image'") -join ''
if ([string]::IsNullOrWhiteSpace($digest)) {
    throw "CONFIGGAP_PLATFORM_IMAGE_DIGEST: image '$Image' did not expose a repository digest."
}
Write-Output "Platform image digest: $digest"

$containerScript = @'
set -euo pipefail

run_step() {
  step_name="$1"
  shift
  log="/tmp/configgap-platform-${step_name}.log"
  start_ms=$(date +%s%3N)
  set +e
  if "$@" >"$log" 2>&1; then
    status=0
  else
    status=$?
  fi
  set -e
  end_ms=$(date +%s%3N)
  echo "STEP name=${step_name} exit=${status} duration_ms=$((end_ms - start_ms))"
  echo "RAW_TAIL_BEGIN name=${step_name}"
  tail -n 20 "$log"
  echo "RAW_TAIL_END name=${step_name}"
  return "$status"
}

consumer_smoke() {
  smoke_root=/tmp/configgap-platform-smoke
  package_root=/tmp/configgap-platform-pack
  tool_root="$smoke_root/tool"
  feed_config="$smoke_root/NuGet.config"
  rm -rf "$smoke_root"
  mkdir -p "$smoke_root"
  test -f "$package_root/KeelMatrix.ConfigGap.0.1.0.nupkg"

  cat >"$feed_config" <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="/tmp/configgap-platform-pack" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
EOF

  dotnet tool install --tool-path "$tool_root" --configfile "$feed_config" --version 0.1.0 KeelMatrix.ConfigGap --add-source "$package_root" --ignore-failed-sources --no-cache
  test -x "$tool_root/configgap"
  find "$tool_root" -name 'KeelMatrix.Telemetry.dll' -print -quit | grep -q .

  copy_fixture() {
    destination="$1"
    mkdir -p "$destination"
    cp tests/FixtureClean/FixtureClean.csproj "$destination/"
    cp tests/FixtureClean/Program.cs "$destination/"
    cp tests/FixtureClean/ConfigurationUse.cs "$destination/"
    cp tests/FixtureClean/appsettings.json "$destination/"
    cat >"$destination/configgap.json" <<'EOF'
{
  "version": 1,
  "declarationSurfaces": [
    { "kind": "json", "path": "appsettings.json" }
  ]
}
EOF
    dotnet restore "$destination/FixtureClean.csproj" --configfile "$feed_config" --ignore-failed-sources --nologo
  }

  run_case() {
    case_name="$1"
    root="$2"
    expected_exit="$3"
    expected_code="$4"
    set +e
    "$tool_root/configgap" check --project "$root/FixtureClean.csproj" --config "$root/configgap.json" --format json >"$smoke_root/${case_name}.json" 2>&1
    actual_exit=$?
    set -e
    test "$actual_exit" -eq "$expected_exit"
    grep -q "$expected_code" "$smoke_root/${case_name}.json"
  }

  clean_root="$smoke_root/clean"
  missing_root="$smoke_root/missing"
  dynamic_root="$smoke_root/dynamic"
  copy_fixture "$clean_root"
  copy_fixture "$missing_root"
  copy_fixture "$dynamic_root"
  sed -i 's/Clean:Key/Missing:Key/' "$missing_root/ConfigurationUse.cs"

  run_case clean "$clean_root" 0 CG900
  run_case missing "$missing_root" 1 CG001
  run_case dynamic "$dynamic_root" 0 CG900
  echo 'Consumer smoke cases passed: clean, blocking missing declaration, and informational dynamic key.'
}

run_step restore dotnet restore KeelMatrix.ConfigGap.sln --configfile NuGet.config --nologo --verbosity quiet -p:NuGetAudit=false
run_step release-build dotnet build KeelMatrix.ConfigGap.sln -c Release --no-restore --nologo
run_step release-test dotnet test KeelMatrix.ConfigGap.sln -c Release --no-build --no-restore --nologo
run_step release-pack dotnet pack src/KeelMatrix.ConfigGap/KeelMatrix.ConfigGap.csproj -c Release --no-build --no-restore --include-symbols -p:SymbolPackageFormat=snupkg -o /tmp/configgap-platform-pack --nologo
run_step consumer-smoke consumer_smoke
'@

$mount = "type=bind,source=$repo,target=/workspace"
$dockerArguments = @(
    'run', '--rm', '--pull=never',
    '--mount', $mount,
    '--workdir', '/workspace',
    $Image,
    'bash', '-lc', $containerScript
)

Write-Output 'Platform container command:'
Write-Output ("docker " + ($dockerArguments -join ' '))
$containerOutput = @(& docker @dockerArguments 2>&1 | ForEach-Object { $_.ToString() })
$containerExitCode = $LASTEXITCODE
$containerOutput | ForEach-Object { Write-Output $_ }
if ($containerExitCode -ne 0) {
    throw "CONFIGGAP_PLATFORM_CONTAINER_FAILURE: Linux platform matrix failed with exit code $containerExitCode."
}

Write-Output "Linux platform matrix passed with image '$Image' ($digest)."

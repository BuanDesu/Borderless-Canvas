param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
# Keep CLI state local and avoid an SDK workload probe unrelated to this project.
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.local/dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:MSBuildEnableWorkloadResolver = 'false'
Push-Location $taskRoot
try {
    dotnet build BorderlessFlatWindow.slnx --configuration $Configuration --nologo --disable-build-servers -m:1
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
} finally { Pop-Location }

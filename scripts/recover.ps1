param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$JournalPath,
    [switch]$Inspect
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $taskRoot "src/BorderlessFlatWindow.Recovery/bin/$Configuration/net10.0-windows/BorderlessFlatWindow.Recovery.exe"
# Build only the independent recovery project when needed; the GUI does not need to compile or start.
if (-not (Test-Path -LiteralPath $executable)) {
    $env:DOTNET_CLI_HOME = Join-Path $taskRoot '.local/dotnet'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:MSBuildEnableWorkloadResolver = 'false'
    dotnet build (Join-Path $taskRoot 'src/BorderlessFlatWindow.Recovery/BorderlessFlatWindow.Recovery.csproj') --configuration $Configuration --nologo --disable-build-servers -m:1
    if ($LASTEXITCODE -ne 0) { throw "Recovery build failed: $LASTEXITCODE" }
}
$recoveryArguments = @()
if ($JournalPath) { $recoveryArguments += @('--journal', $JournalPath) }
if ($Inspect) { $recoveryArguments += '--inspect' }
& $executable @recoveryArguments
exit $LASTEXITCODE

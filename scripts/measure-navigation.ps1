param(
    [ValidatePattern('^[a-zA-Z0-9-]+$')][string]$Label = 'navigation',
    [ValidateRange(1, 10)][int]$Repeat = 3,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.local/dotnet'
$env:MSBuildEnableWorkloadResolver = 'false'
if (!$NoBuild) { & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration }
$directory = Join-Path $taskRoot '.local/performance'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
Push-Location $taskRoot
try {
    for ($iteration = 1; $iteration -le $Repeat; $iteration++) {
        $output = Join-Path $directory "$Label-$iteration.json"
        if (Test-Path -LiteralPath $output) { throw "Measurement label already exists; select a new label: $Label" }
        dotnet run --project tests/BorderlessFlatWindow.App.Checks -c $Configuration --no-build -- --navigation-performance $output
        if ($LASTEXITCODE -ne 0) { throw "Synthetic navigation measurement failed: $LASTEXITCODE" }
    }
} finally { Pop-Location }

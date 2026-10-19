param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug', [switch]$SmokeUi, [switch]$NativeChecks)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
Push-Location $taskRoot
try {
    dotnet run --project tests/BorderlessFlatWindow.Checks --configuration $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw "Model checks failed: $LASTEXITCODE" }
    if ($SmokeUi) {
        dotnet run --project src/BorderlessFlatWindow.App --configuration $Configuration --no-build -- --smoke-check
        if ($LASTEXITCODE -ne 0) { throw "UI smoke check failed: $LASTEXITCODE" }
    }
    if ($NativeChecks) {
        dotnet run --project tests/BorderlessFlatWindow.Windows.Checks --configuration $Configuration --no-build
        if ($LASTEXITCODE -ne 0) { throw "Native checks failed: $LASTEXITCODE" }
    } else {
        dotnet run --project tests/BorderlessFlatWindow.Windows.Checks --configuration $Configuration --no-build -- --storage-only
        if ($LASTEXITCODE -ne 0) { throw "Recovery storage checks failed: $LASTEXITCODE" }
    }
} finally { Pop-Location }

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1')
Push-Location $taskRoot
try {
    dotnet run --project src/BorderlessFlatWindow.App --no-build
    if ($LASTEXITCODE -ne 0) { throw "Application exited: $LASTEXITCODE" }
} finally { Pop-Location }

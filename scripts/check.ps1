param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug', [switch]$SmokeUi, [switch]$NativeChecks, [switch]$LiveSurfaceChecks, [switch]$NativeOverviewChecks, [switch]$NativeBackdropChecks)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
Push-Location $taskRoot
try {
    dotnet run --project tests/BorderlessFlatWindow.Checks --configuration $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw "Model checks failed: $LASTEXITCODE" }
    & (Join-Path $PSScriptRoot 'check-trial-launchers.ps1') -Configuration $Configuration
    & (Join-Path $PSScriptRoot 'check-trial-verifier.ps1') -Configuration $Configuration
    if ($SmokeUi) {
        dotnet run --project src/BorderlessFlatWindow.App --configuration $Configuration --no-build -- --smoke-check
        if ($LASTEXITCODE -ne 0) { throw "UI smoke check failed: $LASTEXITCODE" }
        dotnet run --project src/BorderlessFlatWindow.App --configuration $Configuration --no-build -- --scene-check (Join-Path $taskRoot 'artifacts/overview-scene-check.json')
        if ($LASTEXITCODE -ne 0) { throw "Overview scene check failed: $LASTEXITCODE" }
    }
    if ($NativeChecks) {
        dotnet run --project tests/BorderlessFlatWindow.Windows.Checks --configuration $Configuration --no-build
        if ($LASTEXITCODE -ne 0) { throw "Native checks failed: $LASTEXITCODE" }
    } else {
        dotnet run --project tests/BorderlessFlatWindow.Windows.Checks --configuration $Configuration --no-build -- --storage-only
        if ($LASTEXITCODE -ne 0) { throw "Recovery storage checks failed: $LASTEXITCODE" }
    }
    if ($NativeOverviewChecks) {
        $nativeDirectory = Join-Path $taskRoot "artifacts/native-overview/$([Guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $nativeDirectory | Out-Null
        dotnet run --project src/BorderlessFlatWindow.App --configuration $Configuration --no-build -- --native-overview-check (Join-Path $nativeDirectory 'presenter.json')
        if ($LASTEXITCODE -ne 0) { throw "Native overview checks failed: $LASTEXITCODE" }
    }
    if ($NativeBackdropChecks) {
        $backdropDirectory = Join-Path $taskRoot "artifacts/native-backdrop/$([Guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $backdropDirectory | Out-Null
        dotnet run --project src/BorderlessFlatWindow.App --configuration $Configuration --no-build -- --native-backdrop-check (Join-Path $backdropDirectory 'backdrop.json')
        if ($LASTEXITCODE -ne 0) { throw "Native backdrop checks failed: $LASTEXITCODE" }
    }
    if ($LiveSurfaceChecks) {
        # Opt-in desktop integration lab, separate from the production presenter and headless CI.
        $labDirectory = Join-Path $taskRoot "artifacts/live-surface/$([Guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $labDirectory | Out-Null
        dotnet run --project src/BorderlessFlatWindow.App --configuration $Configuration --no-build -- --live-overview-lab (Join-Path $labDirectory 'native.json')
        if ($LASTEXITCODE -ne 0) { throw "Native live-surface checks failed: $LASTEXITCODE" }
        dotnet run --project src/BorderlessFlatWindow.App --configuration $Configuration --no-build -- --live-overview-lab (Join-Path $labDirectory 'solid.json') --lab-solid
        if ($LASTEXITCODE -ne 0) { throw "Fallback live-surface checks failed: $LASTEXITCODE" }
    }
} finally { Pop-Location }

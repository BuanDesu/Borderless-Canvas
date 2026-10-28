param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [ValidateSet('Core', 'Ui', 'Desktop')][string]$Layer = 'Core',
    [switch]$SmokeUi, [switch]$NativeChecks, [switch]$LiveSurfaceChecks, [switch]$NativeOverviewChecks, [switch]$NativeBackdropChecks
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if ($Layer -eq 'Desktop') { $NativeChecks = $true; $NativeOverviewChecks = $true }
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
Push-Location $taskRoot
try {
    & (Join-Path $PSScriptRoot 'check-project-structure.ps1') -Configuration $Configuration
    if ($Layer -ne 'Ui') {
        dotnet run --project tests/BorderlessFlatWindow.Checks --configuration $Configuration --no-build
        if ($LASTEXITCODE -ne 0) { throw "Model checks failed: $LASTEXITCODE" }
        & (Join-Path $PSScriptRoot 'check-trial-launchers.ps1') -Configuration $Configuration
        & (Join-Path $PSScriptRoot 'check-trial-verifier.ps1') -Configuration $Configuration
        $windowsArguments = @('run', '--project', 'tests/BorderlessFlatWindow.Windows.Checks', '--configuration', $Configuration, '--no-build')
        if (!$NativeChecks) { $windowsArguments += @('--', '--storage-only') }
        dotnet @windowsArguments
        if ($LASTEXITCODE -ne 0) { throw "Windows checks failed: $LASTEXITCODE" }
        if ($NativeChecks) {
            $workspaceOutput = Join-Path $taskRoot ('.local/workspace-native/' + [Guid]::NewGuid().ToString('N'))
            dotnet run --project tests/BorderlessFlatWindow.Windows.Checks --configuration $Configuration --no-build -- --workspace-only $workspaceOutput
            if ($LASTEXITCODE -ne 0) { throw 'Owned workspace native checks failed.' }
        }
        Write-Output 'PASS layer=core: models, storage, isolated input, launchers and package verifier. Physical desktop acceptance is separate.'
    }
    if ($Layer -eq 'Ui' -or $SmokeUi) {
        dotnet run --project tests/BorderlessFlatWindow.App.Checks --configuration $Configuration --no-build -- --smoke-check
        if ($LASTEXITCODE -ne 0) { throw "Hidden UI checks failed: $LASTEXITCODE" }
        dotnet run --project tests/BorderlessFlatWindow.App.Checks --configuration $Configuration --no-build -- --scene-check (Join-Path $taskRoot 'artifacts/overview-scene-check.json')
        if ($LASTEXITCODE -ne 0) { throw "Scene checks failed: $LASTEXITCODE" }
        $workspaceOutput = Join-Path $taskRoot ('.local/workspace-ui/' + [Guid]::NewGuid().ToString('N'))
        dotnet run --project tests/BorderlessFlatWindow.App.Checks --configuration $Configuration --no-build -- --workspace-check $workspaceOutput
        if ($LASTEXITCODE -ne 0) { throw 'Workspace UI integration checks failed.' }
        Write-Output 'PASS layer=ui: source-linked hidden UI and synthetic scene checks; no global input hooks or real window management.'
    }
    if ($NativeOverviewChecks) {
        $directory = Join-Path $taskRoot "artifacts/native-overview/$([Guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $directory | Out-Null
        dotnet run --project tests/BorderlessFlatWindow.App.Checks --configuration $Configuration --no-build -- --native-overview-check (Join-Path $directory 'presenter.json')
        if ($LASTEXITCODE -ne 0) { throw "Native overview checks failed: $LASTEXITCODE" }
    }
    if ($NativeBackdropChecks) {
        $directory = Join-Path $taskRoot "artifacts/native-backdrop/$([Guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $directory | Out-Null
        dotnet run --project tests/BorderlessFlatWindow.App.Checks --configuration $Configuration --no-build -- --native-backdrop-check (Join-Path $directory 'backdrop.json')
        if ($LASTEXITCODE -ne 0) { throw "Native backdrop checks failed: $LASTEXITCODE" }
    }
    if ($LiveSurfaceChecks) {
        $directory = Join-Path $taskRoot "artifacts/live-surface/$([Guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $directory | Out-Null
        dotnet run --project tools/BorderlessFlatWindow.Lab --configuration $Configuration --no-build -- overview (Join-Path $directory 'native.json')
        if ($LASTEXITCODE -ne 0) { throw 'Native surface lab failed.' }
        dotnet run --project tools/BorderlessFlatWindow.Lab --configuration $Configuration --no-build -- overview (Join-Path $directory 'solid.json') --solid
        if ($LASTEXITCODE -ne 0) { throw 'Solid surface lab failed.' }
    }
    if ($Layer -eq 'Desktop') { Write-Output 'PASS layer=desktop: native fixtures passed; physical device and long-running acceptance remain separate.' }
} finally { Pop-Location }

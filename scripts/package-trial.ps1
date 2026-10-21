param(
    [string]$OutputDirectory,
    [switch]$NativeChecks,
    [switch]$NativeOverviewChecks
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Push-Location $taskRoot
try {
    # Check the same sources before publishing. Native checks require an interactive Windows desktop.
    & (Join-Path $PSScriptRoot 'check.ps1') -Configuration Release -SmokeUi -NativeChecks:$NativeChecks -NativeOverviewChecks:$NativeOverviewChecks
    if ($LASTEXITCODE -ne 0) { throw "Trial checks failed: $LASTEXITCODE" }
    $revision = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source revision.' }
    $dirty = [bool](git status --porcelain --untracked-files=no)
    if (-not $OutputDirectory) {
        $OutputDirectory = Join-Path $taskRoot "artifacts/trial/BorderlessFlatWindow-$($revision.Substring(0, 8))-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    }
    $destination = [IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path -LiteralPath $destination) { throw "Output already exists; choose a new directory: $destination" }
    if (Test-Path -LiteralPath "$destination.zip") { throw "Archive already exists: $destination.zip" }
    $staging = Join-Path $taskRoot ".local/trial-publish/$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $destination,$staging -Force | Out-Null
    $projects = @(
        'src/BorderlessFlatWindow.App',
        'src/BorderlessFlatWindow.Recovery',
        'tests/BorderlessFlatWindow.Probe'
    )
    foreach ($project in $projects) {
        $name = Split-Path $project -Leaf
        $published = Join-Path $staging $name
        dotnet publish "$project/$name.csproj" --configuration Release --runtime win-x64 --self-contained false `
            --output $published --nologo --disable-build-servers -m:1 -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishReadyToRun=false
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $name" }
        foreach ($file in Get-ChildItem -LiteralPath $published -File -Recurse) {
            $relative = [IO.Path]::GetRelativePath($published, $file.FullName)
            $target = Join-Path $destination $relative
            if (Test-Path -LiteralPath $target) {
                if ((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) {
                    throw "Conflicting shared dependency: $($file.Name)"
                }
            } else {
                New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
                Copy-Item -LiteralPath $file.FullName -Destination $target
            }
        }
    }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/trial-guide.zh-CN.md') -Destination (Join-Path $destination '试用说明.md')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/interaction-refresh.md') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/integration-history.md') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/candy-glass-design.md') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/console-and-wallpaper.md') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/overview-compositor.md') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/fluent-motion.md') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/responsive-transitions.md') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/retained-overview.md') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/overview-load-baseline.md') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/capture-readback.md') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/native-overview-presenter.md') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'scripts/trial/Start-NativeOverview.cmd') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'scripts/trial/Start.cmd') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'scripts/trial/Recover.cmd') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'scripts/trial/Inspect-Recovery.cmd') -Destination $destination
    $files = @(Get-ChildItem -LiteralPath $destination -File -Recurse | Sort-Object FullName | ForEach-Object {
        @{ name = [IO.Path]::GetRelativePath($destination, $_.FullName); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    $manifest = @{
        schemaVersion = 1; sourceRevision = $revision; trackedSourcesModified = $dirty
        builtAtUtc = [DateTime]::UtcNow.ToString('O'); platform = 'win-x64'; frameworkDependent = $true
        requiredRuntime = 'Microsoft.WindowsDesktop.App 10.0 x64'; sdk = (dotnet --version)
        nativeChecksRun = [bool]$NativeChecks; nativeOverviewChecksRun = [bool]$NativeOverviewChecks; files = $files
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'manifest.json') -Encoding utf8
    Compress-Archive -LiteralPath $destination -DestinationPath "$destination.zip"
    Write-Output "Trial directory: $destination"
    Write-Output "Trial archive: $destination.zip"
    Write-Output "Runtime: .NET 10 Windows Desktop Runtime x64; SDK not required."
    if ($dirty) { Write-Warning 'Manifest records modified tracked sources; commit and rebuild for an exact revision.' }
} finally { Pop-Location }

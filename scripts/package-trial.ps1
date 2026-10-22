param(
    [string]$OutputDirectory,
    [switch]$SelfContained,
    [ValidatePattern('^10\.0\.\d+$')][string]$RuntimeVersion,
    [switch]$NativeChecks,
    [switch]$NativeOverviewChecks,
    [switch]$NativeBackdropChecks
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if ($RuntimeVersion -and !$SelfContained) { throw 'RuntimeVersion is available only with -SelfContained.' }
Push-Location $taskRoot
try {
    # Check the same sources before publishing. Native checks require an interactive Windows desktop.
    & (Join-Path $PSScriptRoot 'check.ps1') -Configuration Release -SmokeUi -NativeChecks:$NativeChecks -NativeOverviewChecks:$NativeOverviewChecks -NativeBackdropChecks:$NativeBackdropChecks
    if ($LASTEXITCODE -ne 0) { throw "Trial checks failed: $LASTEXITCODE" }
    $revision = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source revision.' }
    $dirty = [bool](git status --porcelain --untracked-files=no)
    $untrackedInputs = @(git ls-files --others --exclude-standard -- src tests scripts docs licenses .github .gitattributes README.md TODO.md THIRD-PARTY-NOTICES.md '*.props' '*.targets' '*.sln*' '*.csproj' global.json NuGet.Config)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect source inputs.' }
    if ($untrackedInputs.Count) { $dirty = $true }
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
        $publishArguments = @('publish', "$project/$name.csproj", '--configuration', 'Release', '--runtime', 'win-x64',
            '--self-contained', $SelfContained.IsPresent.ToString().ToLowerInvariant(), '--output', $published,
            '--nologo', '--disable-build-servers', '-m:1', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-p:PublishReadyToRun=false',
            '-p:DebugType=None', '-p:DebugSymbols=false')
        if ($RuntimeVersion) { $publishArguments += "-p:RuntimeFrameworkVersion=$RuntimeVersion" }
        dotnet @publishArguments
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $name" }
        $applicationDirectory = $destination
        if ($SelfContained -and $name -ne 'BorderlessFlatWindow.App') {
            $tool = if ($name -eq 'BorderlessFlatWindow.Recovery') { 'recovery' } else { 'probe' }
            $applicationDirectory = Join-Path $destination "tools/$tool"
        }
        foreach ($file in Get-ChildItem -LiteralPath $published -File -Recurse) {
            $relative = [IO.Path]::GetRelativePath($published, $file.FullName)
            $target = Join-Path $applicationDirectory $relative
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
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/native-backdrop-validation.md') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/region-arrangement.md') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/local-preferences.md') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/tray-control.md') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/deployment.md') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'THIRD-PARTY-NOTICES.md') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'scripts/trial/Launch.ps1') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'scripts/trial/Probe.cmd') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'scripts/trial/Start-NativeOverview.cmd') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'scripts/trial/Start-FormOverview.cmd') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'scripts/trial/Start.cmd') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'scripts/trial/Recover.cmd') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $taskRoot 'scripts/trial/Inspect-Recovery.cmd') -Destination $destination
    # Preserve the SDK distribution notices in both modes (the native apphost is also redistributed).
    $sdkVersion = (dotnet --version).Trim()
    $sdkEntry = @(dotnet --list-sdks | Where-Object { $_.StartsWith("$sdkVersion [") })
    if ($sdkEntry.Count -ne 1 -or $sdkEntry[0] -notmatch '\[(.+)\]$') { throw 'Cannot resolve SDK distribution notices.' }
    $dotnetDirectory = Split-Path -Parent $Matches[1]
    $noticeDirectory = Join-Path $destination 'licenses/dotnet-distribution'
    New-Item -ItemType Directory -Path $noticeDirectory -Force | Out-Null
    foreach ($notice in @('LICENSE.txt', 'ThirdPartyNotices.txt')) { Copy-Item -LiteralPath (Join-Path $dotnetDirectory $notice) -Destination $noticeDirectory }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'licenses/dotnet/MIT-LICENSE.TXT') -Destination $noticeDirectory
    $runtimeFrameworks = @()
    if ($SelfContained) {
        $config = Get-Content -Raw -LiteralPath (Join-Path $destination 'BorderlessFlatWindow.App.runtimeconfig.json') | ConvertFrom-Json
        $runtimeFrameworks = @($config.runtimeOptions.includedFrameworks)
        if ($runtimeFrameworks.Count -ne 2 -or (@($runtimeFrameworks.name | Sort-Object) -join ',') -ne 'Microsoft.NETCore.App,Microsoft.WindowsDesktop.App') { throw 'Unexpected self-contained runtime configuration.' }
        $assets = Get-Content -Raw -LiteralPath (Join-Path $taskRoot 'src/BorderlessFlatWindow.App/obj/project.assets.json') | ConvertFrom-Json
        foreach ($framework in $runtimeFrameworks) {
            if ($RuntimeVersion -and $framework.version -ne $RuntimeVersion) { throw 'Runtime patch differs from requested version.' }
            $package = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
            $relative = "$package/$($framework.version)"
            $packagePaths = @($assets.packageFolders.PSObject.Properties.Name | ForEach-Object { Join-Path $_ $relative } | Where-Object { Test-Path -LiteralPath $_ -PathType Container })
            if ($packagePaths.Count -ne 1) { throw "Cannot resolve runtime notices for $package." }
            $target = Join-Path $destination "licenses/$relative"
            New-Item -ItemType Directory -Path $target -Force | Out-Null
            $notices = @(Get-ChildItem -LiteralPath $packagePaths[0] -File | Where-Object { $_.Name -match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$' })
            if (!@($notices | Where-Object { $_.Name -like 'LICENSE*' }).Count) { throw "Missing license in $package." }
            foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $target }
        }
        foreach ($required in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll', 'PresentationFramework.dll')) {
            if (!(Test-Path -LiteralPath (Join-Path $destination $required) -PathType Leaf)) { throw "Portable runtime file missing: $required" }
        }
    }
    $portableChecksRun = $false
    if ($SelfContained -and $NativeOverviewChecks) {
        & (Join-Path $PSScriptRoot 'check-portable.ps1') -Directory $destination
        $portableChecksRun = $true
    }
    $files = @(Get-ChildItem -LiteralPath $destination -File -Recurse | Sort-Object FullName | ForEach-Object {
        @{ name = [IO.Path]::GetRelativePath($destination, $_.FullName); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    $manifest = @{
        schemaVersion = 1; sourceRevision = $revision; trackedSourcesModified = $dirty
        builtAtUtc = [DateTime]::UtcNow.ToString('O'); platform = 'win-x64'; frameworkDependent = !$SelfContained.IsPresent
        deployment = $(if ($SelfContained) { 'self-contained' } else { 'framework-dependent' })
        requiredRuntime = $(if ($SelfContained) { $null } else { 'Microsoft.WindowsDesktop.App 10.0 x64' })
        bundledFrameworks = $runtimeFrameworks; sdk = $sdkVersion
        portableChecksRun = $portableChecksRun
        nativeChecksRun = [bool]$NativeChecks; nativeOverviewChecksRun = [bool]$NativeOverviewChecks; nativeBackdropChecksRun = [bool]$NativeBackdropChecks; files = $files
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'manifest.json') -Encoding utf8
    Compress-Archive -LiteralPath $destination -DestinationPath "$destination.zip"
    & (Join-Path $PSScriptRoot 'verify-trial.ps1') -Directory $destination -Archive "$destination.zip"
    Write-Output "Trial directory: $destination"
    Write-Output "Trial archive: $destination.zip"
    if ($SelfContained) { Write-Output 'Runtime: included; Windows x64 portable folder, SDK and .NET installation not required.' }
    else { Write-Output 'Runtime: .NET 10 Windows Desktop Runtime x64; SDK not required.' }
    if ($dirty) { Write-Warning 'Manifest records modified tracked sources; commit and rebuild for an exact revision.' }
} finally { Pop-Location }

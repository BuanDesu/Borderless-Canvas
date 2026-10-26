param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$checkRoot = Join-Path $taskRoot ".local/verifier-checks/$([Guid]::NewGuid().ToString('N'))"
$baseline = Join-Path $checkRoot 'baseline'
New-Item -ItemType Directory -Path $baseline -Force | Out-Null
foreach ($project in @('src/BorderlessFlatWindow.App', 'src/BorderlessFlatWindow.Recovery', 'tests/BorderlessFlatWindow.Probe')) {
    $build = Join-Path $taskRoot "$project/bin/$Configuration/net10.0-windows"
    foreach ($file in (Get-ChildItem -LiteralPath $build -File | Where-Object { $_.Extension -in @('.dll', '.exe') -or $_.Name -match '\.(deps|runtimeconfig)\.json$' })) { Copy-Item -LiteralPath $file.FullName -Destination $baseline -Force }
}
foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'trial') -File)) { Copy-Item -LiteralPath $file.FullName -Destination $baseline }
foreach ($name in @('deployment.md', 'THIRD-PARTY-NOTICES.md', 'Assets/Licenses/FusionPixel/OFL.txt', 'licenses/dotnet-distribution/LICENSE.txt', 'licenses/dotnet-distribution/ThirdPartyNotices.txt', 'licenses/dotnet-distribution/MIT-LICENSE.TXT')) {
    $path = Join-Path $baseline $name; New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    [IO.File]::WriteAllText($path, 'Synthetic notice for verifier fixture, not a distributable package.')
}
function Save-CheckManifest([string]$Directory, [bool]$Portable = $false) {
    $manifest = [ordered]@{
        schemaVersion = 1; sourceRevision = '1111111111111111111111111111111111111111'; trackedSourcesModified = $false
        platform = 'win-x64'; frameworkDependent = !$Portable; deployment = $(if ($Portable) {'self-contained'} else {'framework-dependent'})
        requiredRuntime = $(if ($Portable) {$null} else {'Microsoft.WindowsDesktop.App 10.0 x64'})
        bundledFrameworks = $(if ($Portable) {@(@{name='Microsoft.NETCore.App';version='10.0.13'},@{name='Microsoft.WindowsDesktop.App';version='10.0.13'})} else {@()})
        files = @(Get-ChildItem -LiteralPath $Directory -File -Recurse | Where-Object {$_.Name -ne 'manifest.json'} | ForEach-Object {
            @{name=[IO.Path]::GetRelativePath($Directory,$_.FullName);bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
        })
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Directory 'manifest.json') -Encoding utf8
}
function Verify([string]$Directory, [string]$Archive) { & (Join-Path $PSScriptRoot 'verify-trial.ps1') -Directory $Directory -Archive $Archive -RequireClean }
function Reject([scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (!$rejected) { throw 'Invalid package passed verification.' }
}
Save-CheckManifest $baseline
$zipPath = "$baseline.zip"; Compress-Archive -LiteralPath $baseline -DestinationPath $zipPath
Verify $baseline $zipPath | Out-Null
$caseNumber = 0
foreach ($kind in @('hash', 'extra', 'missing', 'pdb', 'userdata', 'missing-notice', 'duplicate', 'escape', 'dirty', 'runtime-mismatch')) {
    $caseNumber++; $directory = Join-Path $checkRoot "case-$caseNumber"
    Copy-Item -LiteralPath $baseline -Destination $directory -Recurse
    $manifestPath = Join-Path $directory 'manifest.json'
    switch ($kind) {
        'hash' { [IO.File]::AppendAllText((Join-Path $directory 'Launch.ps1'), 'changed') }
        'extra' { [IO.File]::WriteAllText((Join-Path $directory 'extra.txt'), 'unlisted') }
        'missing' { Rename-Item -LiteralPath (Join-Path $directory 'BorderlessFlatWindow.App.dll') -NewName 'missing.bin' }
        'pdb' { [IO.File]::WriteAllText((Join-Path $directory 'private.pdb'), 'symbols'); Save-CheckManifest $directory }
        'userdata' { [IO.File]::WriteAllText((Join-Path $directory 'settings.json'), '{}'); Save-CheckManifest $directory }
        'missing-notice' { Rename-Item -LiteralPath (Join-Path $directory 'licenses/dotnet-distribution/LICENSE.txt') -NewName 'other.txt'; Save-CheckManifest $directory }
        'duplicate' {
            $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
            $manifest.files += $manifest.files[0]; $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath
        }
        'escape' {
            $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
            $manifest.files[0].name = '../outside.txt'; $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath
        }
        'dirty' {
            $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
            $manifest.trackedSourcesModified = $true; $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath
        }
        'runtime-mismatch' {
            [IO.File]::WriteAllText((Join-Path $directory 'BorderlessFlatWindow.App.runtimeconfig.json'), '{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.13"}]}}')
            Save-CheckManifest $directory
        }
    }
    Reject { Verify $directory }
}
# Archive corruption is checked independently of an otherwise valid directory/manifest.
foreach ($kind in @('duplicate', 'escape')) {
    $archivePath = Join-Path $checkRoot "$kind.zip"; Copy-Item -LiteralPath $zipPath -Destination $archivePath
    $archive = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = if ($kind -eq 'duplicate') { $archive.CreateEntry('baseline/Start.cmd') } else { $archive.CreateEntry('baseline/../outside.txt') }
        $stream = $entry.Open(); try { $stream.WriteByte(0) } finally { $stream.Dispose() }
    } finally { $archive.Dispose() }
    Reject { Verify $baseline $archivePath }
}
$portable = Join-Path $checkRoot 'portable'; Copy-Item -LiteralPath $baseline -Destination $portable -Recurse
foreach ($app in @('Recovery', 'Probe')) {
    $tool = Join-Path $portable "tools/$($app.ToLowerInvariant())"; New-Item -ItemType Directory -Path $tool -Force | Out-Null
    foreach ($suffix in @('.exe', '.dll', '.deps.json', '.runtimeconfig.json')) {
        $source = Join-Path $portable "BorderlessFlatWindow.$app$suffix"
        $target = Join-Path $tool "BorderlessFlatWindow.$app$suffix"
        if (![IO.Path]::GetFullPath($source).StartsWith($checkRoot + [IO.Path]::DirectorySeparatorChar) -or
            ![IO.Path]::GetFullPath($target).StartsWith($checkRoot + [IO.Path]::DirectorySeparatorChar)) { throw 'Fixture move escaped its workspace.' }
        Move-Item -LiteralPath $source -Destination $target
    }
}
foreach ($relative in @('', 'tools/recovery', 'tools/probe')) {
    $runtime = Join-Path $portable $relative
    foreach ($name in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll', 'PresentationFramework.dll')) {
        [IO.File]::WriteAllText((Join-Path $runtime $name), 'Synthetic runtime placeholder; verifier does not execute this fixture.')
    }
    $app = if (!$relative) {'App'} elseif ($relative.EndsWith('recovery')) {'Recovery'} else {'Probe'}
    [IO.File]::WriteAllText((Join-Path $runtime "BorderlessFlatWindow.$app.runtimeconfig.json"), '{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.13"},{"name":"Microsoft.WindowsDesktop.App","version":"10.0.13"}]}}')
}
foreach ($framework in @('microsoft.netcore.app', 'microsoft.windowsdesktop.app')) {
    $notice = Join-Path $portable "licenses/$framework.runtime.win-x64/10.0.13"; New-Item -ItemType Directory -Path $notice -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $notice 'LICENSE'), 'Synthetic runtime license placeholder.')
}
Save-CheckManifest $portable $true; Verify $portable | Out-Null
$config = Join-Path $portable 'tools/recovery/BorderlessFlatWindow.Recovery.runtimeconfig.json'
[IO.File]::WriteAllText($config, '{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.12"}]}}')
Save-CheckManifest $portable $true; Reject { Verify $portable }

# These non-executable placeholders isolate path detection from debug paths
# already present in the ordinary compiled test baseline.
$privateBaseline = Join-Path $checkRoot 'private-path-baseline'
Copy-Item -LiteralPath $baseline -Destination $privateBaseline -Recurse
foreach ($binary in (Get-ChildItem -LiteralPath $privateBaseline -File -Recurse | Where-Object { $_.Extension -in @('.dll', '.exe') })) {
    [IO.File]::WriteAllText($binary.FullName, 'Synthetic verifier-only binary placeholder, never executed. Vendor path D:\vendor\build\component.pdb is not a local private root.')
}
Save-CheckManifest $privateBaseline
& (Join-Path $PSScriptRoot 'verify-trial.ps1') -Directory $privateBaseline -RequireClean -RejectLocalBuildPaths | Out-Null
foreach ($encoding in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode)) {
    foreach ($privateRoot in @($taskRoot, $taskRoot.Replace('\', '/'), $taskRoot.Replace('\', '\\'), $env:USERPROFILE)) {
        if (!$privateRoot) { continue }
        $offsets = if ($encoding.CodePage -eq 1200) { @(0, 1) } else { @(0) }
        foreach ($offset in $offsets) {
            $caseNumber++; $directory = Join-Path $checkRoot "case-$caseNumber"
            Copy-Item -LiteralPath $privateBaseline -Destination $directory -Recurse
            $payload = $encoding.GetBytes('Synthetic CodeView/string payload: ' + $privateRoot + '/private-source.cs')
            if ($offset) { $payload = [byte[]]@(127) + $payload }
            [IO.File]::WriteAllBytes((Join-Path $directory 'BorderlessFlatWindow.App.dll'), $payload)
            Save-CheckManifest $directory
            $rejection = $null
            try { & (Join-Path $PSScriptRoot 'verify-trial.ps1') -Directory $directory -RequireClean -RejectLocalBuildPaths | Out-Null }
            catch { $rejection = $_.Exception.Message }
            if (!$rejection -or !$rejection.Contains('local build/profile path') -or $rejection.Contains($privateRoot)) {
                throw 'Private-path fixture was not rejected safely; matching value withheld.'
            }
        }
    }
}
Write-Output 'PASS Trial verifier validates compiled framework-dependent and synthetic isolated-runtime layouts; rejects changed/missing/unlisted payload, symbols/user data, notices, metadata, cross-tool patch mismatch, duplicate and escaping ZIP entries.'
Write-Output 'PASS Private-build verification rejects local workspace/profile paths in UTF-8/UTF-16 (both byte alignments) and slash/JSON forms without printing values, while accepting unrelated vendor paths.'

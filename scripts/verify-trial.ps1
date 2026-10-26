param([Parameter(Mandatory)][string]$Directory, [string]$Archive, [switch]$RequireClean, [switch]$RejectLocalBuildPaths)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($Directory).TrimEnd([IO.Path]::DirectorySeparatorChar)
$prefix = $root + [IO.Path]::DirectorySeparatorChar
$manifestPath = Join-Path $root 'manifest.json'
foreach ($path in @($root, $manifestPath)) {
    if ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse points are not package payload.' }
}
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.sourceRevision -notmatch '^[0-9a-f]{40}$' -or
    $manifest.trackedSourcesModified -isnot [bool] -or $manifest.frameworkDependent -isnot [bool] -or
    $manifest.platform -ne 'win-x64' -or !@($manifest.files).Count -or $RequireClean -and $manifest.trackedSourcesModified) { throw 'Manifest source/deployment metadata is invalid or not clean.' }
function Resolve-PayloadPath([string]$Name) {
    if (!$Name -or [IO.Path]::IsPathRooted($Name) -or $Name.Contains(':') -or
        @($Name -split '[/\\]' | Where-Object { $_ -in @('', '.', '..') -or $_ -match '[. ]$' }).Count) { throw 'Invalid relative payload path.' }
    $path = [IO.Path]::GetFullPath((Join-Path $root $Name))
    if (!$path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Payload path escapes package.' }
    return $path
}
# Walk without following junctions/symlinks, so verification reads only this package.
$paths = [Collections.Generic.List[string]]::new()
$directories = [Collections.Generic.Queue[string]]::new(); $directories.Enqueue($root)
while ($directories.Count) {
    foreach ($item in (Get-ChildItem -LiteralPath $directories.Dequeue() -Force)) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse points are not package payload.' }
        if ($item.PSIsContainer) { $directories.Enqueue($item.FullName) } else { $paths.Add($item.FullName) }
    }
}
$names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$privateBuildRoots = @()
if ($RejectLocalBuildPaths) {
    $privateBuildRoots = @(@((Split-Path -Parent $PSScriptRoot), $env:USERPROFILE) |
        Where-Object { $_ -and $_.Length -gt 3 } | ForEach-Object {
            $value = $_.TrimEnd('\', '/')
            $value; $value.Replace('\', '/'); $value.Replace('\', '\\')
        } | Select-Object -Unique)
}
foreach ($entry in $manifest.files) {
    $path = Resolve-PayloadPath $entry.name
    if (!$names.Add($entry.name.Replace('\', '/')) -or $entry.name -eq 'manifest.json' -or $entry.sha256 -notmatch '^[0-9a-fA-F]{64}$' -or
        $entry.bytes -isnot [long] -and $entry.bytes -isnot [int] -or $entry.bytes -lt 0) { throw 'Invalid/duplicate payload metadata.' }
    if ([IO.Path]::GetFileName($path) -match '^(settings|recovery|bookmarks)\.json$' -or [IO.Path]::GetExtension($path) -eq '.pdb') { throw 'Runtime user data or debug symbols must not be distributed.' }
    if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $entry.bytes -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) { throw 'Payload length/hash mismatch.' }
    if ($RejectLocalBuildPaths) {
        # CodeView paths use UTF-8; managed string heaps can use UTF-16. Inspect
        # binary payload too, without printing the matching directory or value.
        $bytes = [IO.File]::ReadAllBytes($path)
        foreach ($encoding in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode)) {
            $offsets = if ($encoding.CodePage -eq 1200 -and $bytes.Length -gt 1) { @(0, 1) } else { @(0) }
            foreach ($offset in $offsets) {
                $content = $encoding.GetString($bytes, $offset, $bytes.Length - $offset)
                foreach ($privateRoot in $privateBuildRoots) {
                    if ($content.IndexOf($privateRoot, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                        throw "Package contains a local build/profile path in payload: $($entry.name); value withheld."
                    }
                }
            }
        }
    }
}
if ($paths.Count -ne $names.Count + 1) { throw 'Package contains unlisted/missing files.' }
foreach ($required in @('BorderlessFlatWindow.App.exe', 'Probe.cmd',
    'Launch.ps1', 'Start.cmd', 'Recover.cmd', 'Inspect-Recovery.cmd', 'Start-NativeOverview.cmd', 'Start-FormOverview.cmd',
    'deployment.md', 'THIRD-PARTY-NOTICES.md', 'Assets/Licenses/FusionPixel/OFL.txt', 'licenses/dotnet-distribution/LICENSE.txt',
    'licenses/dotnet-distribution/ThirdPartyNotices.txt', 'licenses/dotnet-distribution/MIT-LICENSE.TXT')) {
    if (!$names.Contains($required)) { throw "Missing entry point/notice: $required" }
}
if (!$manifest.frameworkDependent) {
    if ($manifest.deployment -ne 'self-contained' -or $null -ne $manifest.requiredRuntime -or @($manifest.bundledFrameworks).Count -ne 2 -or
        (@($manifest.bundledFrameworks.name | Sort-Object) -join ',') -ne 'Microsoft.NETCore.App,Microsoft.WindowsDesktop.App' -or
        @($manifest.bundledFrameworks.version | Select-Object -Unique).Count -ne 1) { throw 'Portable manifest is inconsistent.' }
    foreach ($file in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll', 'PresentationFramework.dll')) { if (!$names.Contains($file)) { throw 'Portable runtime is incomplete.' } }
    foreach ($framework in $manifest.bundledFrameworks) {
        if ($framework.version -notmatch '^10\.0\.\d+$') { throw 'Unexpected bundled framework version.' }
        $licensePrefix = 'licenses/' + $framework.name.ToLowerInvariant() + '.runtime.win-x64/' + $framework.version + '/LICENSE'
        if (!$names.Contains($licensePrefix) -and !$names.Contains($licensePrefix + '.TXT')) { throw 'Bundled framework license missing.' }
    }
}
foreach ($app in @('App', 'Recovery', 'Probe')) {
    $toolPrefix = if (!$manifest.frameworkDependent -and $app -ne 'App') { "tools/$($app.ToLowerInvariant())/" } else { '' }
    if ($toolPrefix -and $names.Contains("BorderlessFlatWindow.$app.exe")) { throw 'Portable tools must retain their isolated runtime layout.' }
    foreach ($suffix in @('.exe', '.dll', '.deps.json')) { if (!$names.Contains("$($toolPrefix)BorderlessFlatWindow.$app$suffix")) { throw 'Application payload missing.' } }
    $configName = "$($toolPrefix)BorderlessFlatWindow.$app.runtimeconfig.json"
    if (!$names.Contains($configName)) { throw 'Application runtime configuration missing.' }
    $config = Get-Content -Raw -LiteralPath (Join-Path $root $configName) | ConvertFrom-Json
    if ($manifest.frameworkDependent) {
        if ($null -ne $config.runtimeOptions.includedFrameworks -or !$manifest.requiredRuntime -or
            $null -eq $config.runtimeOptions.framework -and $null -eq $config.runtimeOptions.frameworks) { throw 'Framework-dependent configuration is inconsistent.' }
    } else {
        foreach ($file in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll')) {
            if (!$names.Contains($toolPrefix + $file)) { throw 'Isolated runtime is incomplete.' }
        }
        $frameworks = @($config.runtimeOptions.includedFrameworks)
        if (!$frameworks.Count -or $null -ne $config.runtimeOptions.frameworks -or $null -ne $config.runtimeOptions.framework) { throw 'Portable configuration depends on an installed runtime.' }
        foreach ($framework in $frameworks) {
            if (!@($manifest.bundledFrameworks | Where-Object { $_.name -eq $framework.name -and $_.version -eq $framework.version }).Count) { throw 'Runtime patch differs between applications.' }
        }
        if ($app -eq 'App' -and (($frameworks | ForEach-Object { "$($_.name)/$($_.version)" } | Sort-Object) -join ',') -ne
            (($manifest.bundledFrameworks | ForEach-Object { "$($_.name)/$($_.version)" } | Sort-Object) -join ',')) { throw 'Desktop runtime configuration is incomplete.' }
    }
}
if ($Archive) {
    $zip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Archive))
    try {
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $zipPrefix = (Split-Path $root -Leaf) + '/'
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            if (!$name.StartsWith($zipPrefix, [StringComparison]::Ordinal)) { throw 'ZIP root mismatch.' }
            if ($name -eq $zipPrefix) { continue }
            $relative = $name.Substring($zipPrefix.Length).TrimEnd('/')
            $path = Resolve-PayloadPath $relative
            if ($name.EndsWith('/')) { if (!(Test-Path -LiteralPath $path -PathType Container)) { throw 'ZIP directory mismatch.' }; continue }
            if (!$seen.Add($relative) -or !(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $entry.Length) { throw 'ZIP duplicate or mismatched payload.' }
            $stream = $entry.Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
            if ($hash -ne (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash) { throw 'ZIP payload hash differs from directory.' }
        }
        if ($seen.Count -ne $paths.Count) { throw 'ZIP file count mismatch.' }
    } finally { $zip.Dispose() }
}
[pscustomobject]@{ Source = $manifest.sourceRevision; Deployment = $manifest.deployment; Files = $paths.Count; ArchiveVerified = [bool]$Archive; Clean = !$manifest.trackedSourcesModified }

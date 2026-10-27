param([Parameter(Mandatory)][string]$Directory, [switch]$Publish)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$package = [IO.Path]::GetFullPath($Directory)
$archive = "$package.zip"
$repository = 'BuanDesu/Borderless-Flat-Window'
$env:GODEBUG = 'http2client=0'
& (Join-Path $PSScriptRoot 'verify-trial.ps1') -Directory $package -Archive $archive -RequireClean -RejectLocalBuildPaths
$manifest = Get-Content -Raw -LiteralPath (Join-Path $package 'manifest.json') | ConvertFrom-Json
$version = $manifest.applicationVersion
if ($manifest.frameworkDependent -or !$manifest.portableChecksRun -or !$manifest.nativeChecksRun -or !$manifest.nativeOverviewChecksRun) { throw 'Publish only a fully checked portable preview.' }
if ($version -notmatch '^\d+\.\d+\.\d+-[0-9A-Za-z.-]+$') { throw 'This entry point publishes prereleases only.' }
$tag = "v$version"
$receipt = Join-Path $taskRoot ".local/release-publish/$tag"
New-Item -ItemType Directory -Path $receipt -Force | Out-Null
function Read-GitHub([string]$Endpoint) {
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        $json = gh api "repos/$repository/$Endpoint" 2> (Join-Path $receipt 'read-error.log')
        if ($LASTEXITCODE -eq 0) { return ($json -join "`n" | ConvertFrom-Json) }
        Start-Sleep -Milliseconds 300
    }
    throw 'GitHub read failed; mutations are not automatically retried.'
}
function Read-Release {
    $candidates = @(Read-GitHub 'releases?per_page=100' | Where-Object tag_name -EQ $tag)
    if ($candidates.Count -gt 1) { throw 'Multiple releases share this tag.' }
    if ($candidates.Count) { return $candidates[0] }
    return $null
}
function Git-Command([string[]]$Arguments) {
    $result = git -c http.version=HTTP/1.1 -c http.sslVersion=tlsv1.2 -c credential.helper= -c 'credential.helper=!gh auth git-credential' @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Git operation failed; inspect remote state before retrying.' }
    return ($result -join "`n").Trim()
}
function Assert-Source {
    $repo = gh repo view $repository --json isPrivate,defaultBranchRef | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or !$repo.isPrivate -or $repo.defaultBranchRef.name -ne 'main') { throw 'Repository must remain private on main.' }
    $head = Git-Command @('rev-parse', 'HEAD')
    if ($head -ne $manifest.sourceRevision -or (Git-Command @('status', '--porcelain', '--untracked-files=no'))) { throw 'Package must match clean current source.' }
    if ((Git-Command @('ls-remote', 'origin', 'refs/heads/main')).Split()[0] -ne $head) { throw 'Remote main differs from package source.' }
    $runs = @(Read-GitHub "actions/runs?head_sha=$head&per_page=20" | Select-Object -ExpandProperty workflow_runs)
    if (!$runs.Count -or @($runs | Where-Object { $_.head_sha -ne $head -or $_.status -ne 'completed' -or $_.conclusion -ne 'success' }).Count) { throw 'Exact source CI must have completed successfully.' }
    & (Join-Path $PSScriptRoot 'check-repository-privacy.ps1') -SourceRevision $head
}
Push-Location $taskRoot
try {
    Assert-Source
    $expectedVersion = ([xml](Get-Content -Raw -LiteralPath Version.props)).Project.PropertyGroup.Version
    if ($expectedVersion -ne $version) { throw 'Version.props differs from package.' }
    $email = Git-Command @('config', '--local', 'user.email')
    if ($email -notmatch '^[^\s<>@]+@users\.noreply\.github\.com$') { throw 'Tag identity must use GitHub noreply; value withheld.' }
    $remoteTag = Git-Command @('ls-remote', 'origin', "refs/tags/$tag^{}")
    if (!$remoteTag) {
        git show-ref --verify --quiet "refs/tags/$tag"
        if ($LASTEXITCODE -ne 1) { throw 'Local tag exists; inspect before publishing it.' }
        $priorName = $env:GIT_COMMITTER_NAME; $priorEmail = $env:GIT_COMMITTER_EMAIL
        try {
            $env:GIT_COMMITTER_NAME = 'BuanDesu'; $env:GIT_COMMITTER_EMAIL = $email
            Git-Command @('-c', 'tag.gpgSign=false', 'tag', '-a', $tag, $manifest.sourceRevision, '-m', "Portable preview $version") | Out-Null
        } finally { $env:GIT_COMMITTER_NAME = $priorName; $env:GIT_COMMITTER_EMAIL = $priorEmail }
        Git-Command @('push', 'origin', "refs/tags/$tag") | Out-Null
        $remoteTag = Git-Command @('ls-remote', 'origin', "refs/tags/$tag^{}")
    }
    if ($remoteTag.Split()[0] -ne $manifest.sourceRevision) { throw 'Release tag differs from checked package.' }
    git show-ref --verify --quiet "refs/tags/$tag"
    if ($LASTEXITCODE -ne 0) { Git-Command @('fetch', 'origin', "refs/tags/${tag}:refs/tags/$tag") | Out-Null }
    $tagger = (Git-Command @('for-each-ref', '--format=%(taggeremail)', "refs/tags/$tag")).Trim([char[]]'<>')
    if ($tagger -notmatch '^[^\s<>@]+@users\.noreply\.github\.com$') { throw 'Tagger metadata is not GitHub noreply; value withheld.' }
    $digest = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    $uploadArchive = Join-Path $receipt 'BorderlessFlatWindow-win-x64-portable.zip'
    if (Test-Path -LiteralPath $uploadArchive) {
        if ((Get-FileHash -LiteralPath $uploadArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $digest) { throw 'Existing staged upload differs; do not overwrite.' }
    } else { Copy-Item -LiteralPath $archive -Destination $uploadArchive }
    $archive = $uploadArchive
    $checksum = "$archive.sha256"
    [IO.File]::WriteAllText($checksum, "$digest  $([IO.Path]::GetFileName($archive))`n", [Text.Encoding]::ASCII)
    $notes = @"
Windows x64 便携试用版 **$version**。下载 ZIP 后完整解压，运行 Start.cmd；内含运行环境和 Recover.cmd。

本版优化导航空闲调度与隐藏诊断刷新：按输入、手势结束和相机动画唤醒，心跳、HUD 到期及位置确认独立运行；保留通知重试、1800ms 看门狗及生命周期处理。合成性能测量只覆盖隔离夹具中的工作次数、托管分配与进程 CPU，不代表实际屏幕呈现延迟或第三方应用性能。实验和 UI 检查不随包发布。物理触控板、远控、多屏/混合 DPI、锁屏/睡眠、干净系统与长期资源验收仍未完成，继续按 #11 / #12 / #13 跟踪。

来源提交：$($manifest.sourceRevision)
SHA-256：$digest
已通过精确来源 CI、模型/隐藏 UI/原生回归、包内运行时与隔离恢复验证、目录/ZIP 和本机构建路径检查。仓库保持私有，旧引用清理由 #72 跟踪。
"@
    $notesPath = Join-Path $receipt 'notes.md'; [IO.File]::WriteAllText($notesPath, $notes)
    $release = Read-Release
    if (!$release) {
        gh release create $tag --repo $repository --verify-tag --draft --prerelease --title "便携试用版 $version · Windows x64" --notes-file $notesPath *> (Join-Path $receipt 'create.log')
        $release = Read-Release
        if (!$release) { throw 'Draft creation failed; inspect state before retrying.' }
    }
    if (!$release.prerelease -or !$release.body.Contains($manifest.sourceRevision)) { throw 'Existing release does not match this preview source.' }
    $paths = @($archive, $checksum)
    $expectedNames = @($paths | ForEach-Object { [IO.Path]::GetFileName($_) })
    if (@($release.assets | Where-Object { $_.name -notin $expectedNames }).Count) { throw 'Unexpected release assets; do not overwrite.' }
    $missing = @($paths | Where-Object { [IO.Path]::GetFileName($_) -notin @($release.assets.name) })
    if ($missing.Count) {
        if (!$release.draft) { throw 'Do not add files to a published release.' }
        gh release upload $tag --repo $repository @missing *> (Join-Path $receipt 'upload.log')
    }
    $release = Read-Release
    foreach ($path in $paths) {
        $asset = @($release.assets | Where-Object name -EQ ([IO.Path]::GetFileName($path)))
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($asset.Count -ne 1 -or $asset[0].state -ne 'uploaded' -or $asset[0].size -ne (Get-Item -LiteralPath $path).Length -or $asset[0].digest -ne "sha256:$hash") { throw 'Remote asset size/hash verification failed; no automatic overwrite or deletion.' }
    }
    if ($Publish -and $release.draft) {
        Assert-Source
        gh release edit $tag --repo $repository --draft=false --latest=false *> (Join-Path $receipt 'publish.log')
        $release = Read-Release
        if ($release.draft) { throw 'Release remains a draft; inspect state before retrying.' }
    }
    [pscustomobject]@{ Tag=$tag; Version=$version; Source=$manifest.sourceRevision; Draft=$release.draft; Prerelease=$release.prerelease; Url=$release.html_url; Sha256=$digest } |
        ConvertTo-Json | Tee-Object -FilePath (Join-Path $receipt 'receipt.json')
} finally { Pop-Location }

param([Parameter(Mandatory)][string]$Directory, [switch]$Publish)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$repository = 'BuanDesu/Borderless-Canvas'
$repositoryId = 1435936136
$package = [IO.Path]::GetFullPath($Directory)
$archive = "$package.zip"
& (Join-Path $PSScriptRoot 'verify-trial.ps1') -Directory $package -Archive $archive -RequireClean -RejectLocalBuildPaths
$manifest = Get-Content -Raw -LiteralPath (Join-Path $package 'manifest.json') | ConvertFrom-Json
$version = $manifest.applicationVersion
if ($version -notmatch '^\d+\.\d+\.\d+-[0-9A-Za-z.-]+$' -or $manifest.frameworkDependent -or !$manifest.portableChecksRun -or !$manifest.nativeChecksRun -or !$manifest.nativeOverviewChecksRun) { throw 'Publish only a fully checked self-contained prerelease.' }
$tag = "v$version"
$receipt = Join-Path $taskRoot ".local/release-publish/$tag"
New-Item -ItemType Directory -Path $receipt -Force | Out-Null
function Read-GitHub([string]$Endpoint) {
    for ($attempt = 0; $attempt -lt 4; $attempt++) {
        $json = gh api "repos/$repository/$Endpoint" 2> (Join-Path $receipt 'read-error.log')
        if ($LASTEXITCODE -eq 0) { return ($json -join "`n" | ConvertFrom-Json) }
        Start-Sleep -Milliseconds 500
    }
    throw 'GitHub read failed; mutations are not automatically retried.'
}
function Read-Repository {
    $json = gh api "repos/$repository" 2> (Join-Path $receipt 'repository-error.log')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot verify the publishing repository.' }
    $repo = $json -join "`n" | ConvertFrom-Json
    if ($repo.id -ne $repositoryId -or $repo.full_name -ne $repository -or $repo.private -or $repo.default_branch -ne 'main') { throw 'Publishing repository identity, visibility or default branch differs.' }
    return $repo
}
function Git-Command([string[]]$Arguments) {
    $result = git @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Git operation failed; inspect state before retrying.' }
    return ($result -join "`n").Trim()
}
function Read-Release {
    $items = @(Read-GitHub 'releases?per_page=100' | Where-Object tag_name -EQ $tag)
    if ($items.Count -gt 1) { throw 'Multiple releases share this tag.' }
    if ($items.Count) { return $items[0] }
    return $null
}
Push-Location $taskRoot
try {
    Read-Repository | Out-Null
    $head = Git-Command @('rev-parse','HEAD')
    if ($head -ne $manifest.sourceRevision -or (Git-Command @('status','--porcelain','--untracked-files=no'))) { throw 'The package must match clean current source.' }
    if (([xml](Get-Content -Raw -LiteralPath Version.props)).Project.PropertyGroup.Version -ne $version) { throw 'Version.props differs from the package.' }
    if ((Read-GitHub 'branches/main').commit.sha -ne $head) { throw 'Remote main differs from the package source.' }
    $runs = @((Read-GitHub "actions/runs?head_sha=$head&per_page=100").workflow_runs)
    if (!$runs.Count -or @($runs | Where-Object { $_.head_sha -ne $head -or $_.status -ne 'completed' -or $_.conclusion -ne 'success' }).Count) { throw 'Exact source CI must have completed successfully.' }
    $jobs = @((Read-GitHub "actions/runs/$($runs[0].id)/jobs?per_page=100").jobs)
    foreach ($name in @('check','Hidden UI and scene checks')) {
        if (!@($jobs | Where-Object { $_.name -eq $name -and $_.conclusion -eq 'success' }).Count) { throw 'A required exact-source CI job is missing.' }
    }
    & (Join-Path $PSScriptRoot 'check-repository-privacy.ps1') -SourceRevision $head
    $email = Git-Command @('config','--local','user.email')
    if ($email -notmatch '^[^\s<>@]+@users\.noreply\.github\.com$') { throw 'Tag identity must use GitHub noreply; value withheld.' }
    $remoteTag = Git-Command @('ls-remote','origin',"refs/tags/$tag^{}")
    if (!$remoteTag) {
        git show-ref --verify --quiet "refs/tags/$tag"
        if ($LASTEXITCODE -ne 1) { throw 'Local tag already exists; inspect before publishing.' }
        $priorName = $env:GIT_COMMITTER_NAME; $priorEmail = $env:GIT_COMMITTER_EMAIL
        try {
            $env:GIT_COMMITTER_NAME = 'BuanDesu'; $env:GIT_COMMITTER_EMAIL = $email
            Git-Command @('-c','tag.gpgSign=false','tag','-a',$tag,$head,'-m',"Windows x64 portable preview $version") | Out-Null
        } finally { $env:GIT_COMMITTER_NAME = $priorName; $env:GIT_COMMITTER_EMAIL = $priorEmail }
        Git-Command @('push','origin',"refs/tags/$tag") | Out-Null
    }
    if ((Git-Command @('ls-remote','origin',"refs/tags/$tag^{}")).Split()[0] -ne $head) { throw 'The release tag differs from the checked source.' }
    $tagger = (Git-Command @('for-each-ref','--format=%(taggeremail)',"refs/tags/$tag")).Trim([char[]]'<>')
    if ($tagger -notmatch '^[^\s<>@]+@users\.noreply\.github\.com$') { throw 'Tag metadata must use noreply; value withheld.' }
    $digest = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    $upload = Join-Path $receipt "BorderlessCanvas-$version-win-x64-portable.zip"
    if (Test-Path -LiteralPath $upload) {
        if ((Get-FileHash -LiteralPath $upload -Algorithm SHA256).Hash.ToLowerInvariant() -ne $digest) { throw 'Existing staged upload differs; do not overwrite.' }
    } else { Copy-Item -LiteralPath $archive -Destination $upload }
    $checksum = "$upload.sha256"
    [IO.File]::WriteAllText($checksum, "$digest  $([IO.Path]::GetFileName($upload))`n", [Text.Encoding]::ASCII)
    $notes = @"
Windows x64 自包含便携预发行版 **$version**。下载 ZIP 并完整解压，运行 Start.cmd；内含 .NET 运行环境和独立 Recover.cmd。Source code 归档不是便携程序。

新增工作区窗口布局：在总览点击“+”确认保存成员和布局；点击卡片“打开并复原”，优先复用已有窗口，仅按需启动缺失的受支持本地 EXE。右击卡片可更新快照或编辑成员/启动项。明确选择只在当前管理器会话有效，所选窗口消失后要求重新选择，不自动改绑或启动替代实例。

这不是任意软件会话备份，不能恢复未保存内容或所有浏览器标签。30 秒只是调度时检查就绪/核验的期限，不会强制中断同步 File.Exists、Process.Start 或原生调用。代表性第三方程序、多文档/单实例、真实设备和长期运行仍有未测范围。

本包通过模型、存储、启动器、包校验、原生窗口与总览检查，并检查实际 App 加载包内 CoreCLR/host/WPF 及独立恢复入口。物理设备、多屏/混合 DPI、特殊窗口、干净系统与长期资源的完整验收仍未完成。更新前请暂停并恢复窗口、退出，再解压到新目录。

来源提交：$head
SHA-256：$digest
"@
    $notesPath = Join-Path $receipt 'notes.md'
    [IO.File]::WriteAllText($notesPath, $notes, [Text.Encoding]::UTF8)
    $release = Read-Release
    if (!$release) {
        Read-Repository | Out-Null
        gh release create $tag --repo $repository --verify-tag --draft --prerelease --title "Borderless Canvas $version · Windows x64 portable" --notes-file $notesPath *> (Join-Path $receipt 'create.log')
        if ($LASTEXITCODE -ne 0) { throw 'Draft creation failed; inspect state before retrying.' }
        $release = Read-Release
    }
    if (!$release.prerelease -or !$release.body.Contains($head) -or !$release.body.Contains($digest)) { throw 'Existing release does not match this checked package.' }
    $paths = @($upload,$checksum)
    $expected = @($paths | ForEach-Object { [IO.Path]::GetFileName($_) })
    if (@($release.assets | Where-Object { $_.name -notin $expected }).Count) { throw 'Unexpected release assets; do not overwrite.' }
    $missing = @($paths | Where-Object { [IO.Path]::GetFileName($_) -notin @($release.assets.name) })
    if ($missing.Count) {
        gh release upload $tag @missing --repo $repository *> (Join-Path $receipt 'upload.log')
        if ($LASTEXITCODE -ne 0) { throw 'Asset upload failed; inspect current assets before retrying.' }
    }
    $release = Read-Release
    if ($release.assets.Count -ne 2) { throw 'The portable archive and checksum must both be present.' }
    foreach ($path in $paths) {
        $asset = @($release.assets | Where-Object name -EQ ([IO.Path]::GetFileName($path)))
        if ($asset.Count -ne 1 -or $asset[0].size -ne (Get-Item -LiteralPath $path).Length) { throw 'Remote asset name/length differs.' }
        $localDigest = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($asset[0].digest -and $asset[0].digest -ne "sha256:$localDigest") { throw 'Remote asset digest differs.' }
    }
    if ($Publish -and $release.draft) {
        Read-Repository | Out-Null
        gh release edit $tag --repo $repository --draft=false --prerelease *> (Join-Path $receipt 'publish.log')
        if ($LASTEXITCODE -ne 0) { throw 'Publishing failed; inspect state before retrying.' }
    }
    $release = Read-Release
    if ($Publish -and ($release.draft -or !$release.prerelease)) { throw 'Public prerelease state was not verified.' }
    [ordered]@{ RepositoryId=$repositoryId; Version=$version; SourceRevision=$head; ReleaseId=$release.id; Tag=$tag; Draft=$release.draft; Prerelease=$release.prerelease; ArchiveSha256=$digest; AssetNames=@($release.assets.name) } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $receipt 'receipt.json')
    Write-Output "PASS Checked portable prerelease $tag; archive/checksum metadata verified."
} finally { Pop-Location }

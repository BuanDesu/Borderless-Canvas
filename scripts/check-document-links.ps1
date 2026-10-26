$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$documents = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'docs') -File -Filter '*.md' -Recurse)
foreach ($name in @('README.md', 'TODO.md', 'CHANGELOG.md', 'THIRD-PARTY-NOTICES.md', 'windows-canvas-feasibility.zh-CN.md', 'windows-spatial-canvas-requirements.zh-CN.md')) {
    $documents += Get-Item -LiteralPath (Join-Path $taskRoot $name)
}
foreach ($document in $documents) {
    $text = Get-Content -Raw -LiteralPath $document.FullName
    foreach ($match in [regex]::Matches($text, '\[[^\]]*\]\(([^\s)]+)\)')) {
        $link = $match.Groups[1].Value.Trim('<', '>').Split('#')[0]
        if (!$link -or $link -match '^([a-z]+:|/)') { continue }
        $target = [IO.Path]::GetFullPath((Join-Path $document.DirectoryName $link))
        if (!$target.StartsWith($taskRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $target)) {
            throw "Broken repository document link: $([IO.Path]::GetRelativePath($taskRoot, $document.FullName)) -> $link"
        }
    }
}
Write-Output "PASS Local document links: $($documents.Count) current/history/root documents (web links and heading anchors excluded)."

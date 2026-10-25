param(
    [string]$RepositoryDirectory = (Split-Path -Parent $PSScriptRoot),
    [ValidatePattern('^(HEAD|[0-9a-fA-F]{40})$')][string]$SourceRevision = 'HEAD',
    [AllowEmptyString()][ValidatePattern('^([0-9a-fA-F]{40})?$')][string]$BaseRevision = ''
)
$ErrorActionPreference = 'Stop'
Push-Location $RepositoryDirectory
try {
    $range = if ($BaseRevision -and $BaseRevision -notmatch '^0+$') { "$BaseRevision..$SourceRevision" } else { $SourceRevision }
    $arguments = @('log', '--format=%ae%x09%ce')
    $metadata = @(git @arguments $range)
    if ($LASTEXITCODE -ne 0 -or !$metadata.Count) { throw 'Cannot verify commit identity; fetch the complete audit range.' }
    foreach ($line in $metadata) {
        foreach ($email in ($line -split "`t")) {
            if ($email -notmatch '^(?:[^\s<>@]+@users\.noreply\.github\.com|noreply@github\.com)$') {
                throw 'Reachable commit metadata contains a non-noreply identity; the value was not printed.'
            }
        }
    }
    $files = @(git -c core.quotepath=false ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect tracked paths.' }
    $forbidden = '(?i)(^|/)(\.local|artifacts|bin|obj)/|\.(pdb|dmp|log|zip|pem|pfx|p12)$|(^|/)\.env(?:\.|$)|(^|/)(recovery|bookmarks|preferences)\.json$'
    $sensitive = @(
        '\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})\b',
        '\b(?:AKIA[0-9A-Z]{16}|AIza[0-9A-Za-z_-]{30,}|sk-(?:proj-)?[A-Za-z0-9_-]{30,})\b',
        '-----BEGIN (?:RSA |EC |OPENSSH |DSA |ENCRYPTED )?PRIVATE KEY-----',
        '(?i)\b[A-Z0-9._%+-]+@(?:qq|gmail|outlook|hotmail|163|126|live)\.[A-Z]{2,}\b',
        '(?i)\b[A-Z]:[\\/]+Users[\\/]+[^\\/\s"<>]+'
    )
    foreach ($file in $files) {
        if ($file -match $forbidden) { throw "Tracked runtime/private file rejected: $file" }
        $bytes = [IO.File]::ReadAllBytes((Join-Path (Get-Location).Path $file))
        if ($bytes -contains 0) { continue }
        $text = [Text.Encoding]::UTF8.GetString($bytes)
        foreach ($pattern in $sensitive) {
            if ($text -match $pattern) { throw "Potential private content rejected in $file; the value was not printed." }
        }
    }
    Write-Output "PASS Repository privacy: $($files.Count) tracked files and $($metadata.Count) reachable/range commit identities; platform copies require a separate audit."
} finally { Pop-Location }

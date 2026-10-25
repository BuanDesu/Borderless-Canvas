$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $taskRoot ('.local/privacy-fixtures/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
git -C $fixture init --quiet
git -C $fixture config user.name 'Privacy Fixture'
git -C $fixture config user.email 'fixture@users.noreply.github.com'
[IO.File]::WriteAllText((Join-Path $fixture 'sample.txt'), 'synthetic fixture')
git -C $fixture add -- sample.txt
git -C $fixture commit --quiet -m fixture
if ($LASTEXITCODE -ne 0) { throw 'Cannot prepare isolated privacy fixture.' }
$guard = Join-Path $PSScriptRoot 'check-repository-privacy.ps1'
& $guard -RepositoryDirectory $fixture
function Assert-Rejected([string]$Scenario, [scriptblock]$Prepare) {
    & $Prepare
    $rejected = $false
    try { & $guard -RepositoryDirectory $fixture | Out-Null } catch { $rejected = $true }
    if (!$rejected) { throw "Privacy fixture was not rejected: $Scenario" }
    Write-Output "PASS Privacy rejects $Scenario without printing the content."
}
Assert-Rejected 'credential' { [IO.File]::WriteAllText((Join-Path $fixture 'sample.txt'), ('gh' + 'p_' + ('A' * 40))) }
Assert-Rejected 'local profile path' { [IO.File]::WriteAllText((Join-Path $fixture 'sample.txt'), ('C:' + '\Users\Synthetic\private.txt')) }
[IO.File]::WriteAllText((Join-Path $fixture 'sample.txt'), 'synthetic fixture')
Assert-Rejected 'runtime data' { [IO.File]::WriteAllText((Join-Path $fixture '.env'), 'synthetic fixture'); git -C $fixture add --force -- .env }
git -C $fixture rm --quiet --cached -- .env
git -C $fixture -c user.email=noreply@github.com commit --quiet --allow-empty -m 'synthetic GitHub committer'
& $guard -RepositoryDirectory $fixture
$base = (git -C $fixture rev-parse HEAD).Trim()
Assert-Rejected 'personal author metadata' { git -C $fixture -c user.email=fixture@example.invalid commit --quiet --allow-empty -m 'synthetic identity rejection' }
git -C $fixture commit --quiet --allow-empty -m 'synthetic private identity restored'
Assert-Rejected 'non-noreply ancestor behind a clean tip' { }
$rangeRejected = $false
try { & $guard -RepositoryDirectory $fixture -BaseRevision $base | Out-Null } catch { $rangeRejected = $true }
if (!$rangeRejected) { throw 'A clean tip hid non-noreply metadata in the new commit range.' }
Write-Output 'PASS Privacy checks the full new commit range even after a clean tip.'
Write-Output 'PASS Repository privacy fixtures completed; isolated fixture remains only in ignored local data.'

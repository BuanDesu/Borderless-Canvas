param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) { $OutputPath = Join-Path $taskRoot "artifacts/overview-load/$(Get-Date -Format 'yyyyMMdd-HHmmss').json" }
$taskOutput = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $taskOutput) { throw 'Choose a new report path; existing measurements are preserved.' }
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release
Push-Location $taskRoot
try {
    $taskRevision = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify measurement source.' }
    $taskModified = [bool](git status --porcelain --untracked-files=no)
    dotnet run --project tools/BorderlessFlatWindow.Lab --configuration Release --no-build -- benchmark $taskOutput
    if ($LASTEXITCODE -ne 0) { throw "Overview benchmark failed: $LASTEXITCODE" }
    $taskReport = Get-Content -LiteralPath $taskOutput -Raw | ConvertFrom-Json
    $taskReport | Add-Member -NotePropertyName SourceRevision -NotePropertyValue $taskRevision
    $taskReport | Add-Member -NotePropertyName TrackedSourcesModified -NotePropertyValue $taskModified
    $taskReport | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $taskOutput -Encoding utf8
} finally { Pop-Location }

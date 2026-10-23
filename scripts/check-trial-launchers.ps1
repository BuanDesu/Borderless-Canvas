param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path $taskRoot ".local/launcher-checks/$([Guid]::NewGuid().ToString('N'))"
$package = Join-Path $fixtureRoot ("package & space' " + [char]0x6D4B)
New-Item -ItemType Directory -Path $package -Force | Out-Null
$recoveryBuild = Join-Path $taskRoot "src/BorderlessFlatWindow.Recovery/bin/$Configuration/net10.0-windows"
foreach ($file in (Get-ChildItem -LiteralPath $recoveryBuild -File)) { Copy-Item -LiteralPath $file.FullName -Destination $package }
foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'trial') -File)) { Copy-Item -LiteralPath $file.FullName -Destination $package }

function Quote-CheckArgument([string]$Value) { return '"' + $Value.Replace('"', '\"') + '"' }
function Invoke-LauncherCheck([string]$Mode, [string]$Journal, [string]$Directory = $package, [string]$Wrapper) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true; $start.RedirectStandardInput = $true
    if ($Wrapper) {
        $start.FileName = $env:ComSpec
        $start.Arguments = '/d /s /c ""' + (Join-Path $Directory $Wrapper) + '""'
    } else {
        $start.FileName = 'powershell.exe'
        $argsList = @('-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $Directory 'Launch.ps1'), '-Mode', $Mode)
        if ($Journal) { $argsList += '-JournalPath'; $argsList += $Journal }
        $start.Arguments = ($argsList | ForEach-Object { Quote-CheckArgument $_ }) -join ' '
    }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $process.StandardInput.Close()
        $output = $process.StandardOutput.ReadToEndAsync(); $errors = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(30000)) { throw "Launcher check is still running; PID $($process.Id). Do not restart it." }
        return [pscustomobject]@{ Code = $process.ExitCode; Output = $output.GetAwaiter().GetResult(); Error = $errors.GetAwaiter().GetResult() }
    } finally { $process.Dispose() }
}

$journalPath = Join-Path $fixtureRoot ("state & literal' " + [char]0x6D4B + '/recovery.json')
$result = Invoke-LauncherCheck -Mode Inspect -Journal $journalPath
if ($result.Code -ne 0 -or ($result.Output | ConvertFrom-Json).pending -ne 0) { throw 'Inspect launcher failed on quoted/Unicode path.' }
$result = Invoke-LauncherCheck -Mode Recover -Journal $journalPath
if ($result.Code -ne 0 -or ($result.Output | ConvertFrom-Json).Remaining -ne 0) { throw 'Empty recovery launcher failed.' }
$invalid = '{synthetic corrupt recovery journal'
[IO.File]::WriteAllText($journalPath, $invalid)
foreach ($mode in @('Inspect', 'Recover')) {
    $result = Invoke-LauncherCheck -Mode $mode -Journal $journalPath
    if ($result.Code -ne 1 -or !$result.Error.Contains('Recovery stopped') -or $result.Error.Contains('install the Desktop Runtime') -or
        [IO.File]::ReadAllText($journalPath) -ne $invalid) { throw 'Recovery error exit/diagnosis or preserved file changed.' }
}

$missing = Join-Path $fixtureRoot 'incomplete'
New-Item -ItemType Directory -Path $missing | Out-Null
foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'trial') -File)) { Copy-Item -LiteralPath $file.FullName -Destination $missing }
foreach ($wrapper in @('Start.cmd', 'Start-NativeOverview.cmd', 'Start-FormOverview.cmd', 'Probe.cmd', 'Recover.cmd', 'Inspect-Recovery.cmd')) {
    $result = Invoke-LauncherCheck -Directory $missing -Wrapper $wrapper
    if ($result.Code -ne 1 -or !$result.Error.Contains('package is incomplete')) { throw "Wrapper did not preserve failure: $wrapper" }
}

$appBuild = Join-Path $taskRoot "src/BorderlessFlatWindow.App/bin/$Configuration/net10.0-windows"
foreach ($name in @('BorderlessFlatWindow.App.exe', 'BorderlessFlatWindow.App.dll')) { Copy-Item -LiteralPath (Join-Path $appBuild $name) -Destination $package }
$configPath = Join-Path $package 'BorderlessFlatWindow.App.runtimeconfig.json'
[IO.File]::WriteAllText($configPath, '{"runtimeOptions":{"tfm":"net99.0","framework":{"name":"Microsoft.NETCore.App","version":"99.0.0"},"rollForward":"Disable"}}')
foreach ($mode in @('App', 'NativeOverview', 'FormOverview')) {
    $result = Invoke-LauncherCheck -Mode $mode
    if ($result.Code -eq 0 -or !$result.Error.Contains('Windows Desktop Runtime x64')) { throw 'Missing-runtime launch did not fail with a useful message.' }
}
[IO.File]::WriteAllText($configPath, '{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"99.0.0"}]}}')
$result = Invoke-LauncherCheck -Mode App
if ($result.Code -eq 0 -or !$result.Error.Contains('portable ZIP again') -or $result.Error.Contains('install the Desktop Runtime')) { throw 'Damaged portable launch was mistaken for a framework dependency.' }
Write-Output 'PASS Trial launchers preserve recovery results and literal paths; all six wrappers retain failures; missing/damaged runtime diagnoses remain distinct (no desktop targets).'

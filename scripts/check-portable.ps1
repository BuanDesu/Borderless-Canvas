param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
$package = [IO.Path]::GetFullPath($Directory)
$taskRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $taskRoot ".local/portable-checks/$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $output -Force | Out-Null
$config = Get-Content -Raw -LiteralPath (Join-Path $package 'BorderlessFlatWindow.App.runtimeconfig.json') | ConvertFrom-Json
if (@($config.runtimeOptions.includedFrameworks).Count -ne 2) { throw 'Portable checks require a self-contained desktop package.' }

function Run-PortableChild([string]$Executable, [string[]]$Arguments, [switch]$ObserveRuntime) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.WorkingDirectory = Split-Path -Parent $Executable
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    # Only these child processes see the override; installed .NET is never removed or changed.
    $start.Environment['DOTNET_ROOT_X64'] = Join-Path $output 'unavailable-runtime'
    $start.Environment['DOTNET_ROOT'] = $start.Environment['DOTNET_ROOT_X64']
    $start.Environment['DOTNET_DISABLE_GUI_ERRORS'] = '1'
    $process = [Diagnostics.Process]::Start($start)
    $modules = @{}; $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        do {
            if ($ObserveRuntime -and !$process.HasExited) {
                try {
                    $process.Refresh()
                    foreach ($module in $process.Modules) {
                        if ($module.ModuleName -in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'PresentationNative_cor3.dll', 'wpfgfx_cor3.dll')) { $modules[$module.ModuleName] = $module.FileName }
                    }
                } catch [ComponentModel.Win32Exception] { } catch [InvalidOperationException] { if (!$process.HasExited) { throw } }
            }
            if ($process.WaitForExit(100)) { break }
            if ($watch.ElapsedMilliseconds -gt 30000) { throw "Portable check still running; PID $($process.Id). Do not restart it." }
        } while ($true)
        return [pscustomobject]@{ Code=$process.ExitCode; Output=$stdout.GetAwaiter().GetResult(); Error=$stderr.GetAwaiter().GetResult(); Modules=$modules }
    } finally { $process.Dispose() }
}
$app = Join-Path $package 'BorderlessFlatWindow.App.exe'
$result = Run-PortableChild $app @('--native-overview-check', (Join-Path $output 'overview.json')) -ObserveRuntime
if ($result.Code -ne 0) { throw "Published native overview failed: $($result.Error)" }
foreach ($required in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'PresentationNative_cor3.dll')) {
    if (!$result.Modules.ContainsKey($required) -or !$result.Modules[$required].StartsWith($package + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Published runtime was not observed inside the package: $required" }
}
$recoveryOutput = Join-Path $output 'recovery-fixture'
$result = Run-PortableChild $app @('--trial-recovery-check', $recoveryOutput)
if ($result.Code -ne 0 -or !$result.Output.Contains('PASS Actual independent recovery process')) { throw "Published GUI recovery bridge failed: $($result.Error)" }
$before = Get-FileHash -LiteralPath (Join-Path $recoveryOutput 'recovery.json')
$result = Run-PortableChild $app @('--trial-recovery-check', $recoveryOutput)
if ($result.Code -ne 1 -or (Get-FileHash -LiteralPath $before.Path).Hash -ne $before.Hash) { throw 'Recovery fixture allowed reusing a nonempty output directory.' }
$journal = Join-Path $output "state & quoted'/recovery.json"
$shell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
foreach ($mode in @('Inspect', 'Recover')) {
    $result = Run-PortableChild $shell @('-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $package 'Launch.ps1'),'-Mode',$mode,'-JournalPath',$journal)
    if ($result.Code -ne 0) { throw "Published isolated recovery launcher failed: $($result.Error)" }
}
[pscustomobject]@{ RuntimeVersion=$config.runtimeOptions.includedFrameworks[0].version; PrivateRuntimeObserved=$true; NativeOverview=$true; IndependentRecovery=$true; ExistingFixturePreserved=$true; Launchers=$true } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'summary.json') -Encoding utf8
Write-Output 'PASS Published portable app loads CoreCLR/host/WPF from its own folder, passes native overview and actual isolated recovery bridge, preserves existing fixture output and runs both recovery launcher modes.'

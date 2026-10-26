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
$result = Run-PortableChild $app @('--runtime-check') -ObserveRuntime
if ($result.Code -ne 0 -or !$result.Output.Contains('PASS Production runtime')) { throw "Published runtime initialization failed: $($result.Error)" }
foreach ($required in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'PresentationNative_cor3.dll')) {
    if (!$result.Modules.ContainsKey($required) -or !$result.Modules[$required].StartsWith($package + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Published runtime was not observed inside the package: $required" }
}
$recoveryOutput = Join-Path $output 'recovery-fixture'
# UI fixtures live in a disposable harness. Its production dependencies and
# independent recovery executable are replaced by the exact published payload.
# The actual App executable above is checked separately; do not call this a
# byte-identical production UI binary test.
$harness = Join-Path $output 'harness'
dotnet publish (Join-Path $taskRoot 'tests/BorderlessFlatWindow.App.Checks/BorderlessFlatWindow.App.Checks.csproj') -c Release -r win-x64 --self-contained true --nologo --disable-build-servers -m:1 --output $harness "-p:RuntimeFrameworkVersion=$($config.runtimeOptions.includedFrameworks[0].version)" -p:DebugType=None -p:DebugSymbols=false "-p:PathMap=$taskRoot=/_/bfw"
if ($LASTEXITCODE -ne 0) { throw 'Cannot publish the isolated portable verification harness.' }
foreach ($name in @('Core', 'Application', 'Windows', 'Presentation')) {
    Copy-Item -LiteralPath (Join-Path $package "BorderlessFlatWindow.$name.dll") -Destination $harness -Force
}
foreach ($suffix in @('.exe')) {
    $target = [IO.Path]::GetFullPath((Join-Path $harness "BorderlessFlatWindow.Recovery$suffix"))
    if (!$target.StartsWith([IO.Path]::GetFullPath($output) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Harness cleanup escaped its isolated directory.' }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target }
}
New-Item -ItemType Directory -Path (Join-Path $harness 'tools') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $package 'tools/recovery') -Destination (Join-Path $harness 'tools') -Recurse
$checker = Join-Path $harness 'BorderlessFlatWindow.App.Checks.exe'
$result = Run-PortableChild $checker @('--native-overview-check', (Join-Path $output 'overview.json'))
if ($result.Code -ne 0) { throw "Published-dependency overview fixture failed: $($result.Error)" }
$result = Run-PortableChild $checker @('--trial-recovery-check', $recoveryOutput)
if ($result.Code -ne 0 -or !$result.Output.Contains('PASS Actual independent recovery process')) { throw "Published GUI recovery bridge failed: $($result.Error)" }
$before = Get-FileHash -LiteralPath (Join-Path $recoveryOutput 'recovery.json')
$result = Run-PortableChild $checker @('--trial-recovery-check', $recoveryOutput)
if ($result.Code -ne 1 -or (Get-FileHash -LiteralPath $before.Path).Hash -ne $before.Hash) { throw 'Recovery fixture allowed reusing a nonempty output directory.' }
$journal = Join-Path $output "state & quoted'/recovery.json"
$shell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
foreach ($mode in @('Inspect', 'Recover')) {
    $result = Run-PortableChild $shell @('-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $package 'Launch.ps1'),'-Mode',$mode,'-JournalPath',$journal)
    if ($result.Code -ne 0) { throw "Published isolated recovery launcher failed: $($result.Error)" }
}
[pscustomobject]@{ RuntimeVersion=$config.runtimeOptions.includedFrameworks[0].version; PrivateRuntimeObserved=$true; SourceLinkedUiHarness=$true; PublishedDependencies=$true; NativeOverview=$true; IndependentRecovery=$true; ExistingFixturePreserved=$true; Launchers=$true } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'summary.json') -Encoding utf8
Write-Output 'PASS Actual portable App loads its own CoreCLR/host/WPF; a disposable source-linked UI harness verifies published dependencies and the real recovery executable; fixture preservation and launchers pass.'

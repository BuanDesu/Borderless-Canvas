param(
    [ValidateSet('App', 'NativeOverview', 'FormOverview', 'Probe', 'Recover', 'Inspect')][string]$Mode = 'App',
    [string]$JournalPath
)
$ErrorActionPreference = 'Stop'

function Quote-NativeArgument([string]$Value) {
    # Windows CommandLineToArgvW/CRT quoting; no command shell interprets these arguments.
    return '"' + [regex]::Replace([regex]::Replace($Value, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') + '"'
}

$consoleMode = $Mode -in @('Recover', 'Inspect')
$executable = if ($consoleMode) { 'BorderlessFlatWindow.Recovery.exe' } elseif ($Mode -eq 'Probe') { 'BorderlessFlatWindow.Probe.exe' } else { 'BorderlessFlatWindow.App.exe' }
$process = $null
try {
    $path = Join-Path $PSScriptRoot $executable
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        if ($consoleMode) { $path = Join-Path $PSScriptRoot "tools/recovery/$executable" }
        elseif ($Mode -eq 'Probe') { $path = Join-Path $PSScriptRoot "tools/probe/$executable" }
    }
    $configPath = [IO.Path]::ChangeExtension($path, '.runtimeconfig.json')
    if (!(Test-Path -LiteralPath $path -PathType Leaf) -or !(Test-Path -LiteralPath $configPath -PathType Leaf)) {
        throw 'The package is incomplete. Extract the entire ZIP into a new folder, then try again.'
    }
    $config = Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json
    $selfContained = $null -ne $config.runtimeOptions.includedFrameworks -and @($config.runtimeOptions.includedFrameworks).Count -gt 0
    $arguments = @()
    if ($Mode -eq 'NativeOverview') { $arguments += '--native-overview' }
    if ($Mode -eq 'FormOverview') { $arguments += '--form-overview' }
    if ($Mode -eq 'Inspect') { $arguments += '--inspect' }
    if ($JournalPath) { $arguments += '--journal'; $arguments += [IO.Path]::GetFullPath($JournalPath) }
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $path; $start.WorkingDirectory = Split-Path -Parent $path
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.Arguments = ($arguments | ForEach-Object { Quote-NativeArgument $_ }) -join ' '
    # Suppress only the child apphost's missing-runtime GUI, not application error handling.
    $start.EnvironmentVariables['DOTNET_DISABLE_GUI_ERRORS'] = '1'
    if ($consoleMode) {
        $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
        $start.StandardOutputEncoding = [Text.Encoding]::UTF8; $start.StandardErrorEncoding = [Text.Encoding]::UTF8
    }
    $process = [Diagnostics.Process]::Start($start)
    if ($consoleMode) {
        $output = $process.StandardOutput.ReadToEndAsync(); $errors = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $text = $output.GetAwaiter().GetResult(); $errorText = $errors.GetAwaiter().GetResult()
        if ($text) { [Console]::Out.Write($text) }
        if ($errorText) { [Console]::Error.Write($errorText) }
    } elseif (!$process.WaitForExit(1800)) {
        # The application owns its lifetime. Never stop/restart it when this brief observation ends.
        exit 0
    }
    $result = $process.ExitCode
    if ($result -ne 0 -and !($consoleMode -and $result -eq 2)) {
        [Console]::Error.WriteLine("Borderless could not start or complete this operation (exit $result).")
        if ($result -eq 1 -and $consoleMode) { [Console]::Error.WriteLine('Read the recovery error above and deployment.md. Keep pending recovery records for retry.') }
        elseif ($selfContained) { [Console]::Error.WriteLine('Extract the complete portable ZIP again. See deployment.md and the recovery instructions.') }
        else { [Console]::Error.WriteLine('This package needs .NET 10 Windows Desktop Runtime x64. Use the portable package or install the Desktop Runtime: https://dotnet.microsoft.com/download/dotnet/10.0') }
    }
    exit $result
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine('See deployment.md. Keep any pending recovery journal until windows have been restored.')
    exit 1
} finally { if ($null -ne $process) { $process.Dispose() } }

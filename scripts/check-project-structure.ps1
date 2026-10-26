param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$projects = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.csproj' -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
$graph = @{}
foreach ($project in $projects) {
    $xml = [xml](Get-Content -Raw -LiteralPath $project.FullName)
    $references = @($xml.SelectNodes('//ProjectReference') | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_.Include) })
    if (@($references | Where-Object { $_ -match '\.(Checks|Lab|Probe)$' }).Count) { throw "Production dependency reaches a development tool: $($project.BaseName)" }
    $graph[$project.BaseName] = $references
}
if ($graph['BorderlessFlatWindow.Core'].Count -ne 0 -or ($graph['BorderlessFlatWindow.Application'] -join ',') -ne 'BorderlessFlatWindow.Core') { throw 'Portable layer dependency direction changed.' }
function Visit([string]$Name, [string[]]$Ancestors) {
    if ($Name -in $Ancestors) { throw 'Project reference cycle detected.' }
    foreach ($next in $graph[$Name]) { Visit $next ($Ancestors + $Name) }
}
foreach ($name in $graph.Keys) { Visit $name @() }
foreach ($name in @('BorderlessFlatWindow.Core', 'BorderlessFlatWindow.Application')) {
    foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $taskRoot "src/$name") -File -Filter '*.cs' -Recurse | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })) {
        if ((Get-Content -Raw -LiteralPath $file.FullName) -match '\b(DllImport|LibraryImport)\b|using\s+(System\.Windows|Microsoft\.Win32)') { throw "Platform import in portable layer: $name/$($file.Name)" }
    }
}
# Inspect evaluated compile items, not folder names alone: linked test sources must never enter a distributed assembly.
foreach ($name in @('BorderlessFlatWindow.App', 'BorderlessFlatWindow.Presentation')) {
    $items = dotnet msbuild (Join-Path $taskRoot "src/$name/$name.csproj") -nologo "-p:Configuration=$Configuration" -getItem:Compile
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect production compilation inputs.' }
    $compiled = ($items -join "`n" | ConvertFrom-Json).Items.Compile
    foreach ($item in $compiled) {
        if ($item.Identity -match '(?i)(^|[\\/])(tests|tools)[\\/]|(Checks?|Lab|LabSurface|Benchmark)(\.|$)') { throw "Diagnostic source compiled into production: $name/$($item.Identity)" }
    }
}
Write-Output 'PASS Structure: acyclic production references, portable layers, and evaluated App/Presentation compile lists exclude diagnostics and labs.'
function Read-UiHostProfile([string]$Project) {
    $result = dotnet msbuild $Project -nologo "-p:Configuration=$Configuration" '-getProperty:TargetFramework,UseWindowsForms,ApplicationHighDpiMode,RootNamespace'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect UI host configuration.' }
    return (($result -join "`n" | ConvertFrom-Json).Properties)
}
$runtimeProfile = Read-UiHostProfile (Join-Path $taskRoot 'src/BorderlessFlatWindow.App/BorderlessFlatWindow.App.csproj')
$fixtureProfile = Read-UiHostProfile (Join-Path $taskRoot 'tests/BorderlessFlatWindow.App.Checks/BorderlessFlatWindow.App.Checks.csproj')
foreach ($property in @('TargetFramework', 'UseWindowsForms', 'ApplicationHighDpiMode', 'RootNamespace')) {
    if ($runtimeProfile.$property -cne $fixtureProfile.$property) { throw "UI harness configuration differs from production: $property" }
}
Write-Output 'PASS UI host profile: framework, WinForms, DPI mode and resource namespace match production.'
& (Join-Path $PSScriptRoot 'check-document-links.ps1')

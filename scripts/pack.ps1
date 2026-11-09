param([string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'builds\packages'
New-Item -ItemType Directory -Force $out | Out-Null
$ids = 'Core', 'Actions.Content', 'Actions.Documents', 'Actions.Windows', 'Triggers'
if (-not $Version) {
    $props = [xml](Get-Content (Join-Path $root 'Directory.Packaging.props'))
    $Version = ($props.Project.PropertyGroup.EngineVersion).'#text'
}
foreach ($id in $ids) { Get-ChildItem $out -Filter "AutomationPortal.$id.$Version.*nupkg" -ErrorAction SilentlyContinue | Remove-Item -Force }
foreach ($id in $ids) {
    $proj = Join-Path $root "src\AutomationPortal.$id\AutomationPortal.$id.csproj"
    dotnet pack $proj -c Release -v q -clp:ErrorsOnly -p:EngineVersion=$Version -o $out | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "pack failed for AutomationPortal.$id" }
}
Get-ChildItem $out -Filter "*.$Version.nupkg" | Sort-Object Name | ForEach-Object Name

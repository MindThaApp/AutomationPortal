<#
.SYNOPSIS
  Publishes the unpackaged Automation Portal build: builds\AutomationPortal\ (AutomationPortal.exe + ap.exe + gallery, self-contained win-x64)
  and builds\AutomationPortal-<version>-win-x64.zip.
.PARAMETER NoZip       Skip the zip.
.PARAMETER NoReadyToRun  Skip ReadyToRun precompilation (smaller, but slower to start).
#>
param(
    [switch]$NoZip,
    [switch]$NoReadyToRun
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$builds = Join-Path $root 'builds'
$out = Join-Path $builds 'AutomationPortal'
$rid = 'win-x64'

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = $props.Project.PropertyGroup.Version
if (-not $version) { $version = '1.0.0' }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

$common = @('-c', 'Release', '-r', $rid, '--self-contained', 'true', '-o', $out,
            '-p:DebugType=none', '-p:DebugSymbols=false', '-p:PublishSingleFile=false', '-p:SatelliteResourceLanguages=en')
if (-not $NoReadyToRun) { $common += '-p:PublishReadyToRun=true' }

foreach ($project in 'src\AutomationPortal.App\AutomationPortal.App.csproj', 'src\AutomationPortal.Cli\AutomationPortal.Cli.csproj') {
    Write-Host "Publishing $project ..."
    & dotnet publish (Join-Path $root $project) @common
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project (exit $LASTEXITCODE)" }
}

foreach ($exe in 'AutomationPortal.exe', 'ap.exe') {
    if (-not (Test-Path (Join-Path $out $exe))) { throw "$exe is missing from $out" }
}
if (-not (Test-Path (Join-Path $out 'gallery'))) { throw "The gallery folder is missing from $out" }

$size = (Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Published to {0} ({1:N0} MB)" -f $out, $size)

if (-not $NoZip) {
    $zip = Join-Path $builds "AutomationPortal-$version-$rid.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Host ("Zip: {0} ({1:N0} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
}

<#
.SYNOPSIS
  Combines the x64 and arm64 Store packages into one upload: builds\AutomationPortal_<version>.msixbundle
.DESCRIPTION
  Builds any missing package first (scripts\package-msix.ps1 -Arch x64 / arm64), then runs makeappx bundle.
  The Store installs the right package for each PC from the bundle.
.PARAMETER Rebuild   Rebuild both packages even if they exist.
#>
param([switch]$Rebuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$builds = Join-Path $root 'builds'
$id = Get-Content (Join-Path $root 'packaging\identity.json') -Raw | ConvertFrom-Json

$staging = Join-Path $builds 'bundle-staging'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force $staging | Out-Null

foreach ($arch in 'x64', 'arm64') {
    $msix = Join-Path $builds ("AutomationPortal_{0}_{1}.msix" -f $id.Version, $arch)
    if ($Rebuild -or -not (Test-Path $msix)) {
        & (Join-Path $PSScriptRoot 'package-msix.ps1') -Arch $arch -Publish
        if (-not (Test-Path $msix)) { throw "package-msix.ps1 did not produce $msix" }
    }
    Copy-Item $msix $staging
}

$kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$makeappx = Get-ChildItem $kits -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^10\.' } | Sort-Object { [version]$_.Name } -Descending |
    ForEach-Object { Join-Path $_.FullName 'x64\makeappx.exe' } | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $makeappx) { throw 'makeappx.exe was not found. Install the Windows SDK.' }

$bundle = Join-Path $builds ("AutomationPortal_{0}.msixbundle" -f $id.Version)
if (Test-Path $bundle) { Remove-Item $bundle -Force }
& $makeappx bundle /d $staging /p $bundle /bv $id.Version /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makeappx bundle failed (exit $LASTEXITCODE)" }
Remove-Item $staging -Recurse -Force

Write-Host ("Bundle: {0} ({1:N0} MB, unsigned, x64 + arm64)" -f $bundle, ((Get-Item $bundle).Length / 1MB))

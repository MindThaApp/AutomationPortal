<#
.SYNOPSIS
  Builds the unsigned Microsoft Store package: builds\FlowMate_<version>_x64.msix
.DESCRIPTION
  1. Publishes the app (scripts\publish.ps1) unless builds\FlowMate already exists (use -Publish to force).
  2. Lays out builds\msix-layout\ = published app + generated AppxManifest.xml + packaging\Assets + resources.pri.
  3. Packs it with the Windows SDK's makeappx (which also validates the manifest). The Store signs on submission, so
     the package is left unsigned.
  Needs the Windows 10/11 SDK ("Windows SDK Signing Tools for Desktop Apps" + "MSIX packaging tools" are enough).
  Identity (name, publisher, version) comes from packaging\identity.json.
.PARAMETER Publish   Always run publish.ps1 first.
#>
param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$builds = Join-Path $root 'builds'
$app = Join-Path $builds 'FlowMate'
$layout = Join-Path $builds 'msix-layout'
$pkgDir = Join-Path $root 'packaging'

function Find-SdkTool([string]$name) {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $hit = Get-ChildItem $kits -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^10\.' } | Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$name" } | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($hit) { return $hit }
    $onPath = Get-Command $name -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    throw "$name was not found. Install the Windows SDK: winget install Microsoft.WindowsSDK.10.0.26100  (or add the 'Windows SDK Signing Tools for Desktop Apps' component in the Visual Studio Installer), then run this script again."
}

$makeappx = Find-SdkTool 'makeappx.exe'
$makepri = Find-SdkTool 'makepri.exe'
Write-Host "makeappx: $makeappx"

$id = Get-Content (Join-Path $pkgDir 'identity.json') -Raw | ConvertFrom-Json
if ($id.Version -notmatch '^\d+\.\d+\.\d+\.0$') { throw "Version in identity.json must look like 1.0.0.0 (the Store needs the last number to be 0)." }
$placeholder = $id.PackageName -eq 'FlowMate.Placeholder' -or $id.Publisher -like 'CN=00000000-*'
if ($placeholder) {
    Write-Warning "identity.json still has the placeholder identity. The package is fine for local testing but the Store will reject it. Paste the values from Partner Center > Product identity."
}

if ($Publish -or -not (Test-Path (Join-Path $app 'Flowmate.exe'))) {
    & (Join-Path $PSScriptRoot 'publish.ps1') -NoZip
    if ($LASTEXITCODE -ne 0) { throw 'publish.ps1 failed' }
}
if (-not (Test-Path (Join-Path $pkgDir 'Assets\StoreLogo.scale-100.png'))) { & (Join-Path $PSScriptRoot 'make-logos.ps1') }

# ---- layout ---------------------------------------------------------------------------------
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force $layout | Out-Null
Copy-Item (Join-Path $app '*') $layout -Recurse -Force
Copy-Item (Join-Path $pkgDir 'Assets') (Join-Path $layout 'Assets') -Recurse -Force

$xml = Get-Content (Join-Path $pkgDir 'AppxManifest.template.xml') -Raw -Encoding UTF8
foreach ($prop in $id.PSObject.Properties) {
    if ($prop.Name.StartsWith('_')) { continue }
    $xml = $xml.Replace('{{' + $prop.Name + '}}', [System.Security.SecurityElement]::Escape([string]$prop.Value))
}
if ($xml -match '\{\{\w+\}\}') { throw "The manifest still has unfilled tokens: $($Matches[0]). Add them to identity.json." }
$manifest = Join-Path $layout 'AppxManifest.xml'
[System.IO.File]::WriteAllText($manifest, $xml, (New-Object System.Text.UTF8Encoding($false)))

# resources.pri lets Windows pick the right scale / targetsize of each logo.
$cfg = Join-Path $builds 'priconfig.xml'
& $makepri createconfig /cf $cfg /dq en-US /pv 10.0.0 /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'makepri createconfig failed' }
& $makepri new /pr $layout /cf $cfg /mn $manifest /of (Join-Path $layout 'resources.pri') /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'makepri new failed' }

# ---- pack (validates the manifest) -------------------------------------------------------------
$msix = Join-Path $builds ("FlowMate_{0}_x64.msix" -f $id.Version)
if (Test-Path $msix) { Remove-Item $msix -Force }
& $makeappx pack /d $layout /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed (exit $LASTEXITCODE). Read the manifest error above." }

Write-Host ("Package: {0} ({1:N0} MB, unsigned)" -f $msix, ((Get-Item $msix).Length / 1MB))
Write-Host "Loose layout for local testing: $layout"

<#
.SYNOPSIS
  Builds the unsigned Microsoft Store package: builds\AutomationPortal_<version>.msixbundle (x64 + arm64, the Store upload) by default; -Arch x64 or -Arch arm64 builds one builds\AutomationPortal_<version>_<arch>.msix
.DESCRIPTION
  1. Publishes the app (scripts\publish.ps1) unless builds\AutomationPortal already exists (use -Publish to force).
  2. Lays out builds\msix-layout\ = published app + generated AppxManifest.xml + packaging\Assets + resources.pri.
  3. Packs it with the Windows SDK's makeappx (which also validates the manifest). The Store signs on submission, so
     the package is left unsigned.
  Needs the Windows 10/11 SDK ("Windows SDK Signing Tools for Desktop Apps" + "MSIX packaging tools" are enough).
  Identity (name, publisher, version) comes from packaging\identity.json.
.PARAMETER Arch      all (default: both architectures, then one .msixbundle), x64 or arm64 (a single .msix).
.PARAMETER Publish   Always run publish.ps1 first.
.PARAMETER Flavor    store (default, the Microsoft Store package: no unvirtualizedResources) or full (adds unvirtualizedResources and the five
                     virtualization exclusions; kept for a future reconsideration request, never uploaded to the Store as is).
                     Output names of the full flavour end in -full so the two never mix.
#>
param([ValidateSet('all', 'x64', 'arm64')][string]$Arch = 'all', [switch]$Publish, [ValidateSet('store', 'full')][string]$Flavor = 'store')
$ErrorActionPreference = 'Stop'
if ($Arch -eq 'all') {
    & (Join-Path $PSScriptRoot 'bundle-msix.ps1') -Rebuild -Flavor $Flavor
    return
}
$root = Split-Path $PSScriptRoot -Parent
$builds = Join-Path $root 'builds'
$suffix = if ($Arch -eq 'x64') { '' } else { "-$Arch" }
$app = Join-Path $builds "AutomationPortal$suffix"
$flavorTag = if ($Flavor -eq 'full') { '-full' } else { '' }
$layout = Join-Path $builds "msix-layout$suffix$flavorTag"
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
$placeholder = $id.PackageName -eq 'AutomationPortal.Placeholder' -or $id.Publisher -like 'CN=00000000-*'
if ($placeholder) {
    Write-Warning "identity.json still has the placeholder identity. The package is fine for local testing but the Store will reject it. Paste the values from Partner Center > Product identity."
}

if ($Publish -or -not (Test-Path (Join-Path $app 'AutomationPortal.exe'))) {
    & (Join-Path $PSScriptRoot 'publish.ps1') -Arch $Arch -NoZip
    if ($LASTEXITCODE -ne 0) { throw 'publish.ps1 failed' }
}
if (-not (Test-Path (Join-Path $pkgDir 'Assets\StoreLogo.scale-100.png'))) { & (Join-Path $PSScriptRoot 'make-logos.ps1') }

# ---- layout ---------------------------------------------------------------------------------
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force $layout | Out-Null
Copy-Item (Join-Path $app '*') $layout -Recurse -Force
Copy-Item (Join-Path $pkgDir 'Assets') (Join-Path $layout 'Assets') -Recurse -Force

$xml = Get-Content (Join-Path $pkgDir "AppxManifest.$Flavor.template.xml") -Raw -Encoding UTF8
foreach ($prop in $id.PSObject.Properties) {
    if ($prop.Name.StartsWith('_')) { continue }
    $xml = $xml.Replace('{{' + $prop.Name + '}}', [System.Security.SecurityElement]::Escape([string]$prop.Value))
}
$xml = $xml.Replace('ProcessorArchitecture="x64"', "ProcessorArchitecture=`"$Arch`"")
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
$msix = Join-Path $builds ("AutomationPortal_{0}_{1}{2}.msix" -f $id.Version, $Arch, $flavorTag)
if (Test-Path $msix) { Remove-Item $msix -Force }
& $makeappx pack /d $layout /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed (exit $LASTEXITCODE). Read the manifest error above." }

Write-Host ("Package: {0} ({1:N0} MB, unsigned)" -f $msix, ((Get-Item $msix).Length / 1MB))
Write-Host "Loose layout for local testing: $layout"

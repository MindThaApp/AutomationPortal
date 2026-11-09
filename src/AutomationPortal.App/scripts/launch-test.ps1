param(
    [int]$Seconds = 10,
    [string]$Shot = (Join-Path $env:TEMP 'automationportal-d1.png'),
    [switch]$Keep,
    [string[]]$AppArgs = @(),
    [switch]$FullScreen
)
# Launches the built exe against a throwaway data root, waits, screenshots the window, reports state and the log.
$exeDir = Join-Path $PSScriptRoot '..\bin\Debug\net10.0-windows10.0.22621.0'
$root = & (Join-Path $PSScriptRoot 'seed-demo.ps1') -ExeDir $exeDir | Select-Object -Last 1
$log = Join-Path $env:TEMP 'automationportal-d1.log'
Remove-Item $log -ErrorAction SilentlyContinue
$env:AUTOMATIONPORTAL_LOG = $log
$p = Start-Process (Join-Path $exeDir 'AutomationPortal.exe') -ArgumentList (@('--data-root', $root) + $AppArgs) -PassThru
Start-Sleep $Seconds
$p.Refresh()
"running=$(-not $p.HasExited) title='$($p.MainWindowTitle)' responding=$($p.Responding)"
if (-not $p.HasExited) {
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    if ($FullScreen) { Add-Type -AssemblyName System.Windows.Forms; $b = [System.Windows.Forms.SystemInformation]::VirtualScreen; $bm = New-Object System.Drawing.Bitmap $b.Width, $b.Height; $gr = [System.Drawing.Graphics]::FromImage($bm); $gr.CopyFromScreen($b.X, $b.Y, 0, 0, $bm.Size); $bm.Save($Shot); $gr.Dispose(); $bm.Dispose(); 'fullscreen shot: ' + $Shot }
    elseif ($p.MainWindowHandle -ne 0) {
        $el = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
        $r = $el.Current.BoundingRectangle
        $bmp = New-Object System.Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
        $bmp.Save($Shot); $g.Dispose(); $bmp.Dispose()
        "screenshot: $Shot ($([int]$r.Width)x$([int]$r.Height))"
    }
    if (-not $Keep) { Stop-Process -Id $p.Id -Force }
}
if (Test-Path $log) { '--- log ---'; Get-Content $log | Select-Object -First 40 } else { '(no log)' }
"pid=$($p.Id) root=$root"

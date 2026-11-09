# Small UI Automation helpers for driving the running app from PowerShell (dot-source this file).
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @'
using System.Runtime.InteropServices;
public static class Mouse {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(int f, int dx, int dy, int d, int e);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
    public static void Click(int x, int y) { SetCursorPos(x, y); mouse_event(2, 0, 0, 0, 0); mouse_event(4, 0, 0, 0, 0); }
    public static void RightClick(int x, int y) { SetCursorPos(x, y); mouse_event(8, 0, 0, 0, 0); mouse_event(16, 0, 0, 0, 0); }
}
'@
$script:AE = [System.Windows.Automation.AutomationElement]

function Get-AppWindow([string]$Name = 'Automation Portal') {
    $p = Get-Process AutomationPortal -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $p) { return $null }
    $p.Refresh()
    $cond = New-Object System.Windows.Automation.PropertyCondition($script:AE::ProcessIdProperty, $p.Id)
    $wins = $script:AE::RootElement.FindAll('Children', $cond)
    foreach ($w in $wins) { if ($w.Current.Name -eq $Name) { return $w } }
    return $wins | Select-Object -First 1
}

function Find-El($root, [string]$Name, [string]$Type = $null, [int]$Index = 0) {
    $all = $root.FindAll('Descendants', [System.Windows.Automation.Condition]::TrueCondition)
    $hits = @($all | Where-Object { $_.Current.Name -eq $Name -and ($null -eq $Type -or $_.Current.ControlType.ProgrammaticName -eq "ControlType.$Type") })
    if ($hits.Count -gt $Index) { return $hits[$Index] }
    return $null
}

function Click-El($el, [switch]$Right) {
    $r = $el.Current.BoundingRectangle
    $x = [int]($r.X + $r.Width / 2); $y = [int]($r.Y + $r.Height / 2)
    if ($Right) { [Mouse]::RightClick($x, $y) } else { [Mouse]::Click($x, $y) }
}

function Save-Shot($root, [string]$Path) {
    $r = $root.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
    $bmp.Save($Path); $g.Dispose(); $bmp.Dispose()
}

function Save-Screen([string]$Path) {
    $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.X, $b.Y, 0, 0, $bmp.Size)
    $bmp.Save($Path); $g.Dispose(); $bmp.Dispose()
}

function List-El($root, [int]$Max = 80) {
    $root.FindAll('Descendants', [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name } | Select-Object -First $Max | ForEach-Object { "{0} | {1}" -f $_.Current.ControlType.ProgrammaticName.Replace('ControlType.', ''), $_.Current.Name }
}


Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Cap {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
}
"@
# Captures one window's own pixels (works even when another window covers it, so nothing else on screen is captured).
function Save-Window($el, [string]$Path) {
    $h = [IntPtr]$el.Current.NativeWindowHandle
    $r = New-Object Cap+RECT; [void][Cap]::GetWindowRect($h, [ref]$r)
    $bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc(); [void][Cap]::PrintWindow($h, $dc, 2); $g.ReleaseHdc($dc); $g.Dispose()
    $bmp.Save($Path); $bmp.Dispose()
}
function Invoke-El($el) { $pat = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern); $pat.Invoke() }

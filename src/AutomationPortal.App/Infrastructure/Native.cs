using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AutomationPortal.App.Infrastructure;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEX { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice; }

    internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    internal const uint MONITOR_DEFAULTTONEAREST = 2;
    internal const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal const int WM_DPICHANGED = 0x02E0;

    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);
}

/// <summary>One display in physical pixels (virtual-desktop coordinates).</summary>
public sealed record MonitorInfo(IntPtr Handle, int X, int Y, int Width, int Height, int WorkX, int WorkY, int WorkWidth, int WorkHeight, double Scale)
{
    public bool Contains(int px, int py) => px >= X && px < X + Width && py >= Y && py < Y + Height;
}

public static class Monitors
{
    private static MonitorInfo? Describe(IntPtr handle)
    {
        var info = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf<Native.MONITORINFOEX>() };
        if (!Native.GetMonitorInfo(handle, ref info)) return null;
        double scale = 1.0;
        try { if (Native.GetDpiForMonitor(handle, 0, out var dx, out _) == 0 && dx > 0) scale = dx / 96.0; } catch { }
        var m = info.rcMonitor; var w = info.rcWork;
        return new MonitorInfo(handle, m.Left, m.Top, m.Width, m.Height, w.Left, w.Top, w.Width, w.Height, scale);
    }

    public static IReadOnlyList<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref Native.RECT _, IntPtr _) =>
        {
            if (Describe(h) is { } m) list.Add(m);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static (int X, int Y) CursorPixels()
    {
        Native.GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    /// <summary>The monitor under the mouse pointer, which is the one the user is looking at.</summary>
    public static MonitorInfo FromCursor()
    {
        Native.GetCursorPos(out var p);
        return Describe(Native.MonitorFromPoint(p, Native.MONITOR_DEFAULTTONEAREST))
               ?? new MonitorInfo(IntPtr.Zero, 0, 0, 1920, 1080, 0, 0, 1920, 1040, 1.0);
    }

    public static MonitorInfo FromPoint(int x, int y) =>
        Describe(Native.MonitorFromPoint(new Native.POINT { X = x, Y = y }, Native.MONITOR_DEFAULTTONEAREST))
        ?? FromCursor();
}

public static class WindowPlacement
{
    /// <summary>
    /// Centres a window on the monitor under the mouse pointer and makes it topmost. Works while the main
    /// window is hidden. Call once the window has been shown (ContentRendered) so its real size is known;
    /// <see cref="ShowCentered"/> does that for you.
    /// </summary>
    public static void CenterOnActiveMonitor(Window window, bool topmost = true)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var mon = Monitors.FromCursor();
        var flags = Native.SWP_NOSIZE | Native.SWP_NOACTIVATE;
        var after = topmost ? Native.HWND_TOPMOST : IntPtr.Zero;
        if (!topmost) flags |= Native.SWP_NOZORDER;

        // Step 1: move onto the target monitor so Windows applies that monitor's DPI before we measure.
        Native.SetWindowPos(hwnd, after, mon.WorkX + 40, mon.WorkY + 40, 0, 0, flags);
        Native.GetWindowRect(hwnd, out var r);
        var x = mon.WorkX + (mon.WorkWidth - r.Width) / 2;
        var y = mon.WorkY + (mon.WorkHeight - r.Height) / 2;
        Native.SetWindowPos(hwnd, after, x, y, 0, 0, flags);
    }

    /// <summary>Places the window's top-left at a screen point (physical pixels), kept inside the work area of that monitor.</summary>
    public static void PlaceAt(Window window, int pxX, int pxY, bool centerOnPoint = false)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var mon = Monitors.FromPoint(pxX, pxY);
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, pxX, pxY, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        Native.GetWindowRect(hwnd, out var r);
        var x = centerOnPoint ? pxX - r.Width / 2 : pxX;
        var y = centerOnPoint ? pxY - r.Height / 2 : pxY;
        x = Math.Max(mon.WorkX, Math.Min(x, mon.WorkX + mon.WorkWidth - r.Width));
        y = Math.Max(mon.WorkY, Math.Min(y, mon.WorkY + mon.WorkHeight - r.Height));
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>Horizontally centred on the active monitor, with the top edge at a fraction of the work area height (Quick Run style).</summary>
    public static void PlaceTopCenter(Window window, double heightFraction = 0.18)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var mon = Monitors.FromCursor();
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, mon.WorkX + 40, mon.WorkY + 40, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        Native.GetWindowRect(hwnd, out var r);
        var x = mon.WorkX + (mon.WorkWidth - r.Width) / 2;
        var y = mon.WorkY + (int)(mon.WorkHeight * heightFraction);
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>Anchors the window to the bottom-right corner of the work area (like a tray flyout).</summary>
    public static void PlaceAtTray(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var mon = Monitors.FromCursor();
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, mon.WorkX + 40, mon.WorkY + 40, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        Native.GetWindowRect(hwnd, out var r);
        var margin = (int)(12 * mon.Scale);
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, mon.WorkX + mon.WorkWidth - r.Width - margin, mon.WorkY + mon.WorkHeight - r.Height - margin, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>Shows the window invisibly, positions it, then fades it in, and brings it to the front even from the background.</summary>
    public static void ShowCentered(Window window, Action<Window>? position = null)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Topmost = true;
        window.Opacity = 0;
        window.ShowActivated = true;
        window.Show();
        window.UpdateLayout();
        (position ?? (w => CenterOnActiveMonitor(w)))(window);
        FadeIn(window);
        BringToFront(window);
    }

    public static void FadeIn(Window window, double ms = 140)
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms))
        { EasingFunction = new System.Windows.Media.Animation.QuadraticEase() };
        window.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    /// <summary>Windows blocks background apps from stealing focus; a topmost flip plus SetForegroundWindow works in practice.</summary>
    public static void BringToFront(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        window.Activate();
        Native.SetForegroundWindow(hwnd);
        window.Focus();
    }
}

using System.Drawing;
using System.Drawing.Imaging;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AutomationPortal.Core;

namespace AutomationPortal.App.Infrastructure;

/// <summary>
/// Detects a main window that is shown but paints nothing (a GPU driver that fails WPF's hardware path leaves only the backdrop colour)
/// and falls back to software rendering, remembering that for later launches.
/// </summary>
internal static class RenderHealth
{
    private static string MarkerFile(AutomationPortalPaths paths) => Path.Combine(paths.LocalRoot, "software-render");

    public static void ApplyRememberedMode(AutomationPortalPaths paths)
    {
        if (!File.Exists(MarkerFile(paths))) return;
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        AppLog.Write("Software rendering is on (hardware rendering produced a blank window earlier). Delete " + MarkerFile(paths) + " to retry hardware rendering.");
    }

    public static void Watch(Window window, AutomationPortalPaths paths, Action recreate)
    {
        var loadedAt = DateTime.UtcNow;
        window.ContentRendered += (_, _) =>
        {
            AppLog.Write($"Main window content rendered {(DateTime.UtcNow - loadedAt).TotalMilliseconds:N0} ms after it was created; render tier {RenderCapability.Tier >> 16}.");
            Check(window, paths, TimeSpan.FromSeconds(3), RenderOptions.ProcessRenderMode != RenderMode.SoftwareOnly, recreate);
        };
        // ContentRendered never fires if the window is never shown, so a start-up in the tray costs nothing.
    }

    private static void Check(Window window, AutomationPortalPaths paths, TimeSpan delay, bool fallbackAllowed, Action recreate)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher) { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                if (!window.IsVisible || window.WindowState == WindowState.Minimized) return;
                if (!IsBlank(window))
                {
                    if (RenderOptions.ProcessRenderMode == RenderMode.SoftwareOnly) AppLog.Write("Main window paints correctly with software rendering.");
                    return;
                }
                if (!fallbackAllowed)
                {
                    AppLog.Write("ERROR: main window is still blank with software rendering.");
                    return;
                }
                AppLog.Write("ERROR: main window painted no content (one flat colour) after " + delay.TotalSeconds + " s with hardware rendering; the graphics driver is likely failing. Switching to software rendering and reopening the window.");
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                try { Directory.CreateDirectory(paths.LocalRoot); File.WriteAllText(MarkerFile(paths), "Hardware rendering gave a blank window; delete this file to retry."); } catch { }
                recreate();
            }
            catch (Exception ex) { AppLog.Write("Render self-check failed: " + ex.Message); }
        };
        timer.Start();
    }

    /// <summary>True when the window's own pixels (PrintWindow, so other windows in front don't matter) are all one colour.</summary>
    private static bool IsBlank(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return false;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w < 50 || h < 50) return false;
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            var dc = g.GetHdc();
            try { if (!PrintWindow(hwnd, dc, 2)) return false; }
            finally { g.ReleaseHdc(dc); }
        }
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new int[w];
            int first = 0; bool haveFirst = false;
            for (int y = 0; y < h; y += 4)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, w);
                for (int x = 0; x < w; x += 2)
                {
                    if (!haveFirst) { first = row[x]; haveFirst = true; }
                    else if (row[x] != first) return false;
                }
            }
            return true;
        }
        finally { bmp.UnlockBits(data); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
}

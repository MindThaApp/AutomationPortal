using System.Diagnostics;
using System.Runtime.InteropServices;
using AutomationPortal.Actions.Windows.Input;
using AutomationPortal.Actions.Windows.Native;

namespace AutomationPortal.Actions.Windows.Apps;

internal sealed record WindowInfo(IntPtr Handle, string Title, uint ProcessId, string ProcessName, string? ExePath, string ClassName, bool IsFrameHost)
{
    public bool IsMinimized => Win32.IsIconic(Handle);
    public bool IsMaximized => Win32.IsZoomed(Handle);
    public string AppName => AppNames.Friendly(ExePath, ProcessName);
    public WindowItem ToItem() => new(Handle.ToInt64(), Title, AppName);
    public AppCandidate ToCandidate() => new(ProcessName, ExePath, AppNames.Friendly(ExePath, ProcessName), Title, IsFrameHost);
}

internal static class AppNames
{
    private static readonly Dictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string Friendly(string? exePath, string processName)
    {
        if (string.IsNullOrEmpty(exePath)) return processName;
        lock (Cache)
        {
            if (Cache.TryGetValue(exePath, out var cached)) return cached;
            string name;
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(exePath);
                name = !string.IsNullOrWhiteSpace(vi.FileDescription) ? vi.FileDescription.Trim()
                    : !string.IsNullOrWhiteSpace(vi.ProductName) ? vi.ProductName.Trim() : processName;
            }
            catch { name = processName; }
            Cache[exePath] = name;
            return name;
        }
    }
}

/// <summary>What we know about a running program, for matching against an app the user picked.</summary>
public sealed record AppCandidate(string ProcessName, string? ExePath, string? FriendlyName, string? WindowTitle, bool IsFrameHost);

/// <summary>Decides whether a running program is "the app" the user chose. Pure logic, unit-tested.</summary>
public static class AppMatcher
{
    public static bool Matches(AppItem app, AppCandidate c)
    {
        var cmp = StringComparison.OrdinalIgnoreCase;
        var isStore = app.Id.Contains('!');
        var wantedPath = !string.IsNullOrWhiteSpace(app.Path) ? app.Path : LooksLikePath(app.Id) ? app.Id : null;

        if (wantedPath is not null && c.ExePath is not null && string.Equals(wantedPath, c.ExePath, cmp)) return true;

        if (isStore && c.ExePath is not null && ParseAumid(app.Id) is { } aumid
            && c.ExePath.Contains(@"\WindowsApps\", cmp) && c.ExePath.Contains(aumid.Package + "_", cmp)
            && (aumid.Publisher is null || c.ExePath.Contains("__" + aumid.Publisher, cmp)))
            return true;

        var stem = wantedPath is not null ? Path.GetFileNameWithoutExtension(wantedPath)
            : isStore ? null : Path.GetFileNameWithoutExtension(app.Id.Trim());
        if (!string.IsNullOrEmpty(stem) && c.ProcessName.Equals(stem, cmp)) return true;

        if (!string.IsNullOrWhiteSpace(app.Name))
        {
            if (c.ProcessName.Equals(app.Name, cmp) || c.ProcessName.Equals(app.Name.Replace(" ", ""), cmp)) return true;
            if (c.FriendlyName is not null && c.FriendlyName.Equals(app.Name, cmp)) return true;
            if (c.IsFrameHost && c.WindowTitle is not null && c.WindowTitle.Contains(app.Name, cmp)) return true;
        }
        return false;
    }

    internal static bool LooksLikePath(string s) => s.Length > 2 && (s[1] == ':' || s.StartsWith(@"\\")) && s.Contains('\\');

    // Windows' own shell and system processes. Force-stopping any of these takes down other apps or the desktop with them.
    private static readonly HashSet<string> NeverForce = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApplicationFrameHost", "explorer", "svchost", "csrss", "wininit", "winlogon", "lsass", "services", "smss", "dwm", "System",
        "Registry", "Idle", "fontdrvhost", "sihost", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "RuntimeBroker",
        "ctfmon", "taskhostw", "spoolsv", "audiodg", "conhost", "dllhost", "MsMpEng", "SecurityHealthService", "LogonUI", "TextInputHost",
    };

    /// <summary>
    /// The strict match used before force-stopping a program: the exact program file when the app has one, the exact Store package
    /// for a Store app, and only for an app typed by hand the exact process name. Never a display name, window title or partial
    /// match, and never a Windows system process.
    /// </summary>
    public static bool MatchesForForceStop(AppItem app, AppCandidate c)
    {
        var cmp = StringComparison.OrdinalIgnoreCase;
        if (NeverForce.Contains(c.ProcessName) || c.IsFrameHost) return false;
        var wantedPath = !string.IsNullOrWhiteSpace(app.Path) ? app.Path : LooksLikePath(app.Id) ? app.Id : null;
        if (wantedPath is not null) return c.ExePath is not null && SamePath(wantedPath, c.ExePath);
        if (app.Id.Contains('!'))
        {
            if (c.ExePath is null || ParseAumid(app.Id) is not { } aumid || aumid.Publisher is null) return false;
            // Package folders are WindowsApps\<Name>_<Version>_<Arch>_<ResourceId>_<PublisherId>\...
            var marker = @"\WindowsApps\" + aumid.Package + "_";
            var at = c.ExePath.IndexOf(marker, cmp);
            if (at < 0) return false;
            var folderEnd = c.ExePath.IndexOf('\\', at + marker.Length);
            var folder = folderEnd < 0 ? c.ExePath[(at + 13)..] : c.ExePath[(at + 13)..folderEnd];
            return folder.EndsWith("__" + aumid.Publisher, cmp) || folder.EndsWith("_" + aumid.Publisher, cmp);
        }
        var stem = Path.GetFileNameWithoutExtension(app.Id.Trim());
        return stem.Length > 0 && c.ProcessName.Equals(stem, cmp);
    }

    private static bool SamePath(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a.Trim()), Path.GetFullPath(b.Trim()), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    /// <summary>"Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" → ("Microsoft.WindowsCalculator", "8wekyb3d8bbwe").</summary>
    internal static (string Package, string? Publisher)? ParseAumid(string aumid)
    {
        var bang = aumid.IndexOf('!');
        var family = bang >= 0 ? aumid[..bang] : aumid;
        var us = family.LastIndexOf('_');
        if (us <= 0) return family.Length > 0 ? (family, null) : null;
        return (family[..us], family[(us + 1)..]);
    }
}

internal static class WindowService
{
    private const string FrameHost = "ApplicationFrameHost";

    /// <summary>Visible, titled, normal top-level windows in Z-order (front first). Hidden/cloaked/tool windows are skipped.</summary>
    public static List<WindowInfo> GetWindows()
    {
        var handles = new List<IntPtr>();
        Win32.EnumWindows((h, _) => { handles.Add(h); return true; }, IntPtr.Zero);
        var result = new List<WindowInfo>();
        foreach (var h in handles)
        {
            if (Describe(h) is { } info) result.Add(info);
        }
        return result;
    }

    /// <summary>Describes a window if it is a "real" app window; otherwise null.</summary>
    public static WindowInfo? Describe(IntPtr h)
    {
        if (h == IntPtr.Zero || !Win32.IsWindow(h) || !Win32.IsWindowVisible(h)) return null;
        // Asking a frozen app for its title would freeze us too.
        if (Win32.IsHungAppWindow(h)) return null;
        var title = Win32.GetWindowText(h);
        if (title.Length == 0) return null;
        var ex = Win32.GetWindowLongPtr(h, Win32.GWL_EXSTYLE).ToInt64();
        if ((ex & Win32.WS_EX_TOOLWINDOW) != 0 && (ex & Win32.WS_EX_APPWINDOW) == 0) return null;
        if (Win32.GetWindow(h, Win32.GW_OWNER) != IntPtr.Zero && (ex & Win32.WS_EX_APPWINDOW) == 0) return null;
        if (Win32.DwmGetWindowAttribute(h, Win32.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return null;
        var cls = Win32.GetClassName(h);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd") return null;
        return Build(h, title, cls);
    }

    private static WindowInfo Build(IntPtr h, string title, string cls)
    {
        Win32.GetWindowThreadProcessId(h, out var pid);
        var path = Win32.GetProcessPath(pid);
        var name = ProcessName(pid, path);
        var isHost = name.Equals(FrameHost, StringComparison.OrdinalIgnoreCase);
        if (isHost)
        {
            uint real = 0;
            var hostPid = pid;
            Win32.EnumChildWindows(h, (c, _) =>
            {
                Win32.GetWindowThreadProcessId(c, out var cp);
                if (cp != hostPid) { real = cp; return false; }
                return true;
            }, IntPtr.Zero);
            if (real != 0)
            {
                pid = real;
                path = Win32.GetProcessPath(pid);
                name = ProcessName(pid, path);
                isHost = false;
            }
        }
        return new WindowInfo(h, title, pid, name, path, cls, isHost);
    }

    internal static string ProcessName(uint pid, string? path)
    {
        if (!string.IsNullOrEmpty(path)) return Path.GetFileNameWithoutExtension(path);
        try { return Process.GetProcessById((int)pid).ProcessName; }
        catch { return "Unknown"; }
    }

    /// <summary>The window the user is working in. Automation Portal's own windows are skipped (the next window down is used).</summary>
    public static WindowInfo? GetFront(bool skipOwn = true)
    {
        var fg = Describe(Win32.GetForegroundWindow());
        if (fg is not null && (!skipOwn || fg.ProcessId != Environment.ProcessId)) return fg;
        return GetWindows().FirstOrDefault(w => !skipOwn || w.ProcessId != Environment.ProcessId);
    }

    public static IReadOnlyList<WindowInfo> WindowsOf(AppItem app) =>
        GetWindows().Where(w => AppMatcher.Matches(app, w.ToCandidate())).ToList();

    public static RECT GetRect(IntPtr h)
    {
        Win32.GetWindowRect(h, out var r);
        return r;
    }

    /// <summary>The visible window bounds (without the invisible resize borders Windows 10/11 add).</summary>
    public static RECT GetVisibleFrame(IntPtr h)
    {
        if (Win32.DwmGetWindowAttribute(h, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>()) == 0) return r;
        return GetRect(h);
    }

    /// <summary>Brings a window to the front, restoring it if minimised. Returns whether it ended up in front.</summary>
    public static bool BringToFront(IntPtr h)
    {
        if (WindowActivation.Suppressed) return false;
        if (!Win32.IsWindow(h)) return false;
        if (Win32.IsIconic(h)) Win32.ShowWindow(h, Win32.SW_RESTORE);
        if (Win32.GetForegroundWindow() == h) return true;

        var fg = Win32.GetForegroundWindow();
        var fgThread = fg == IntPtr.Zero ? 0 : Win32.GetWindowThreadProcessId(fg, out _);
        var me = Win32.GetCurrentThreadId();
        var attached = fgThread != 0 && fgThread != me && Win32.AttachThreadInput(me, fgThread, true);
        try
        {
            Win32.BringWindowToTop(h);
            Win32.SetForegroundWindow(h);
        }
        finally { if (attached) Win32.AttachThreadInput(me, fgThread, false); }
        if (WaitForeground(h, 250)) return true;

        // Last resort: a quick Alt tap makes Windows treat us as the process that last received input.
        InputSender.TapKey(0x12);
        Win32.SetForegroundWindow(h);
        return WaitForeground(h, 400);
    }

    private static bool WaitForeground(IntPtr h, int ms)
    {
        var until = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < until)
        {
            if (Win32.GetForegroundWindow() == h) return true;
            Thread.Sleep(20);
        }
        return Win32.GetForegroundWindow() == h;
    }
}

internal static class RunningApps
{
    /// <summary>All running processes that belong to the app.</summary>
    public static List<Process> FindProcesses(AppItem app)
    {
        var list = new List<Process>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == Environment.ProcessId) continue;
                var path = Win32.GetProcessPath((uint)p.Id);
                if (AppMatcher.Matches(app, new AppCandidate(p.ProcessName, path, null, null, false))) list.Add(p);
                else p.Dispose();
            }
            catch { p.Dispose(); }
        }
        return list;
    }

    public static bool IsRunning(AppItem app)
    {
        var procs = FindProcesses(app);
        var any = procs.Count > 0;
        foreach (var p in procs) p.Dispose();
        return any || WindowService.WindowsOf(app).Count > 0;
    }

    /// <summary>One entry per program that has a visible window, front first.</summary>
    public static List<(AppItem App, WindowInfo Window)> GetRunningApps()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<(AppItem, WindowInfo)>();
        foreach (var w in WindowService.GetWindows())
        {
            if (w.ProcessId == Environment.ProcessId) continue;
            var key = w.ExePath ?? w.ProcessName;
            if (!seen.Add(key)) continue;
            list.Add((new AppItem(w.ExePath ?? w.ProcessName, w.AppName, w.ExePath), w));
        }
        return list;
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using AutomationPortal.Core;
using AutomationPortal.Triggers.Native;

namespace AutomationPortal.Triggers;

/// <summary>A running app as seen from one of its windows.</summary>
internal sealed record RunningApp(uint ProcessId, string ProcessName, string? ExePath, string Title, IntPtr Hwnd)
{
    public string DisplayName => AppInfo.DisplayNameOf(ExePath) ?? ProcessName;
    public AppItem ToItem() => new(ExePath ?? ProcessName, DisplayName, ExePath);
}

internal static class AppInfo
{
    private static readonly ConcurrentDictionary<string, string?> DisplayNames = new(StringComparer.OrdinalIgnoreCase);

    public static RunningApp? Foreground() => FromWindow(Win32.GetForegroundWindow());

    /// <summary>Describes the app that owns a window. Store apps hide behind ApplicationFrameHost; the real app is found through the child window.</summary>
    public static RunningApp? FromWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;
        var path = Win32.GetProcessPath(pid);
        var name = path is null ? SafeProcessName(pid) : Path.GetFileNameWithoutExtension(path);
        if (name.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
        {
            uint realPid = 0;
            Win32.EnumChildWindows(hwnd, (child, _) =>
            {
                Win32.GetWindowThreadProcessId(child, out var cp);
                if (cp != 0 && cp != pid) { realPid = cp; return false; }
                return true;
            }, IntPtr.Zero);
            if (realPid != 0)
            {
                pid = realPid;
                path = Win32.GetProcessPath(pid);
                name = path is null ? SafeProcessName(pid) : Path.GetFileNameWithoutExtension(path);
            }
        }
        return new RunningApp(pid, name, path, Win32.GetWindowTitle(hwnd), hwnd);
    }

    private static string SafeProcessName(uint pid)
    {
        try { using var p = Process.GetProcessById((int)pid); return p.ProcessName; }
        catch { return ""; }
    }

    public static string? DisplayNameOf(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        return DisplayNames.GetOrAdd(exePath, p =>
        {
            try
            {
                var v = FileVersionInfo.GetVersionInfo(p);
                var n = !string.IsNullOrWhiteSpace(v.FileDescription) ? v.FileDescription : v.ProductName;
                return string.IsNullOrWhiteSpace(n) ? null : n.Trim();
            }
            catch { return null; }
        });
    }
}

/// <summary>Decides whether a running process is the app the user picked. The picker's id can be an .exe path, a process name,
/// a Start-menu name or a Store app id (PackageFamily!App), so every form is tried.</summary>
internal static class AppMatcher
{
    public static bool Matches(AppValue? app, RunningApp running) =>
        app is not null && Matches(app.Id, app.Name, running.ProcessName, running.ExePath, running.DisplayName);

    public static bool Matches(string? id, string? name, string processName, string? exePath, string? displayName = null)
    {
        var exeFile = exePath is null ? null : Path.GetFileNameWithoutExtension(exePath);
        foreach (var token in new[] { id, name })
        {
            if (string.IsNullOrWhiteSpace(token)) continue;
            var t = token.Trim();
            if (t.Contains('\\') || t.Contains('/'))
            {
                if (exePath is not null && SamePath(t, exePath)) return true;
                if (string.Equals(Path.GetFileNameWithoutExtension(t), processName, StringComparison.OrdinalIgnoreCase)) return true;
                continue;
            }
            if (t.Contains('!'))
            {
                var family = t[..t.IndexOf('!')];
                var package = family.Contains('_') ? family[..family.IndexOf('_')] : family;
                if (package.Length > 0 && exePath is not null && exePath.Contains(package, StringComparison.OrdinalIgnoreCase)) return true;
                continue;
            }
            if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) t = t[..^4];
            if (t.Equals(processName, StringComparison.OrdinalIgnoreCase)) return true;
            if (exeFile is not null && t.Equals(exeFile, StringComparison.OrdinalIgnoreCase)) return true;
            if (displayName is not null && t.Equals(displayName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool SamePath(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}

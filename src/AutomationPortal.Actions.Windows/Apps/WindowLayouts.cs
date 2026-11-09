using System.Runtime.InteropServices;
using System.Text.Json;
using AutomationPortal.Actions.Windows.Devices;
using AutomationPortal.Actions.Windows.Native;

namespace AutomationPortal.Actions.Windows.Apps;

public sealed class SavedWindow
{
    public string? AppId { get; set; }
    public string? ExePath { get; set; }
    public string ProcessName { get; set; } = "";
    public string Title { get; set; } = "";
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>"normal", "maximized" or "minimized".</summary>
    public string State { get; set; } = "normal";
}

public sealed class SavedLayout
{
    public string Name { get; set; } = "";
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;
    /// <summary>Front window first.</summary>
    public List<SavedWindow> Windows { get; set; } = new();
}

/// <summary>Reads and writes <c>window-layouts.json</c> (default <c>%APPDATA%\Automation Portal\window-layouts.json</c>).</summary>
public sealed class WindowLayoutStore
{
    private sealed class FileModel { public int Version { get; set; } = 1; public List<SavedLayout> Layouts { get; set; } = new(); }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly object Gate = new();

    public string FilePath { get; }

    public WindowLayoutStore(string roamingRoot) => FilePath = Path.Combine(roamingRoot, "window-layouts.json");

    public IReadOnlyList<SavedLayout> Load()
    {
        lock (Gate)
        {
            if (!File.Exists(FilePath)) return Array.Empty<SavedLayout>();
            try { return JsonSerializer.Deserialize<FileModel>(File.ReadAllText(FilePath), Json)?.Layouts ?? new(); }
            catch (JsonException) { return Array.Empty<SavedLayout>(); }
        }
    }

    public SavedLayout? Find(string name) =>
        Load().FirstOrDefault(l => l.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Saves the layout, replacing one with the same name (ignoring case).</summary>
    public void Save(SavedLayout layout)
    {
        lock (Gate)
        {
            var all = Load().Where(l => !l.Name.Equals(layout.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            all.Add(layout);
            AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(new FileModel { Layouts = all.OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase).ToList() }, Json));
        }
    }
}

public sealed record LiveWindow(long Handle, string? ExePath, string ProcessName, string Title);

/// <summary>Pairs saved windows with windows that are open now. Pure logic, unit-tested.</summary>
public static class LayoutMatcher
{
    public static IReadOnlyList<(SavedWindow Saved, LiveWindow? Live)> Match(IReadOnlyList<SavedWindow> saved, IReadOnlyList<LiveWindow> live)
    {
        var used = new HashSet<long>();
        var result = new (SavedWindow, LiveWindow?)[saved.Count];
        var cmp = StringComparison.OrdinalIgnoreCase;

        bool SameApp(SavedWindow s, LiveWindow l) =>
            !string.IsNullOrEmpty(s.ExePath) && !string.IsNullOrEmpty(l.ExePath)
                ? s.ExePath.Equals(l.ExePath, cmp)
                : s.ProcessName.Equals(l.ProcessName, cmp);

        // Strongest first: same program and same title; then same program; each live window is used once.
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < saved.Count; i++)
            {
                if (result[i].Item2 is not null) continue;
                var s = saved[i];
                var hit = live.FirstOrDefault(l => !used.Contains(l.Handle) && SameApp(s, l) && (pass == 1 || l.Title == s.Title));
                if (hit is null) continue;
                used.Add(hit.Handle);
                result[i] = (s, hit);
            }
        }
        for (var i = 0; i < saved.Count; i++) if (result[i].Item1 is null) result[i] = (saved[i], null);
        return result;
    }
}

internal static class WindowLayouts
{
    public static SavedLayout Capture(string name, bool includeMinimized)
    {
        using var _ = DpiScope.PerMonitorV2();
        var layout = new SavedLayout { Name = name.Trim(), SavedAt = DateTimeOffset.Now };
        foreach (var w in WindowService.GetWindows())
        {
            if (w.ProcessId == Environment.ProcessId) continue;
            var state = w.IsMinimized ? "minimized" : w.IsMaximized ? "maximized" : "normal";
            if (state == "minimized" && !includeMinimized) continue;
            Box box;
            if (state == "minimized")
            {
                var wp = new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
                Win32.GetWindowPlacement(w.Handle, ref wp);
                var r = wp.rcNormalPosition;
                box = new Box(r.Left, r.Top, r.Width, r.Height);
            }
            else box = DisplayService.ToBox(WindowService.GetRect(w.Handle));
            layout.Windows.Add(new SavedWindow
            {
                AppId = GetAppId(w.Handle), ExePath = w.ExePath, ProcessName = w.ProcessName, Title = w.Title,
                Left = box.X, Top = box.Y, Width = box.Width, Height = box.Height, State = state,
            });
        }
        return layout;
    }

    public static async Task<int> RestoreAsync(SavedLayout layout, bool launchMissing, Action<string> progress, CancellationToken ct)
    {
        var live = WindowService.GetWindows().Where(w => w.ProcessId != Environment.ProcessId)
            .Select(w => new LiveWindow(w.Handle.ToInt64(), w.ExePath, w.ProcessName, w.Title)).ToList();
        var matches = LayoutMatcher.Match(layout.Windows, live);
        var restored = 0;
        var pending = new List<SavedWindow>();

        foreach (var (saved, match) in matches.Reverse())
        {
            ct.ThrowIfCancellationRequested();
            if (match is not null) { Place(new IntPtr(match.Handle), saved); restored++; }
            else pending.Add(saved);
        }

        if (launchMissing && pending.Count > 0)
        {
            var knownHandles = new HashSet<long>(WindowService.GetWindows().Select(w => w.Handle.ToInt64()));
            var launchedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var saved in pending)
            {
                var key = saved.ExePath ?? saved.AppId ?? saved.ProcessName;
                if (!launchedKeys.Add(key)) continue;
                var app = BuildApp(saved);
                if (app is null) continue;
                progress($"Opening {app.Name}…");
                try { await AppLauncher.LaunchAsync(app, null, ct).ConfigureAwait(false); }
                catch (ActionException) { continue; }
            }

            var waiting = new List<SavedWindow>(pending);
            var until = DateTime.UtcNow.AddSeconds(15);
            while (waiting.Count > 0 && DateTime.UtcNow < until)
            {
                await Task.Delay(500, ct).ConfigureAwait(false);
                var fresh = WindowService.GetWindows()
                    .Where(w => w.ProcessId != Environment.ProcessId && !knownHandles.Contains(w.Handle.ToInt64()))
                    .Select(w => new LiveWindow(w.Handle.ToInt64(), w.ExePath, w.ProcessName, w.Title)).ToList();
                if (fresh.Count == 0) continue;
                foreach (var (saved, match) in LayoutMatcher.Match(waiting, fresh))
                {
                    if (match is null) continue;
                    knownHandles.Add(match.Handle);
                    waiting.Remove(saved);
                    Place(new IntPtr(match.Handle), saved);
                    restored++;
                }
            }
        }
        return restored;
    }

    private static AppItem? BuildApp(SavedWindow s)
    {
        var inStore = s.ExePath?.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) == true;
        if (!string.IsNullOrEmpty(s.ExePath) && !inStore && File.Exists(s.ExePath))
            return new AppItem(s.ExePath, s.ProcessName, s.ExePath);
        if (!string.IsNullOrEmpty(s.AppId)) return new AppItem(s.AppId, s.ProcessName);
        return null;
    }

    /// <summary>Moves and sizes a window to the saved spot, keeping it on a screen that exists now.</summary>
    private static void Place(IntPtr h, SavedWindow s)
    {
        using var _ = DpiScope.PerMonitorV2();
        var works = DisplayService.GetMonitors().Select(m => m.WorkArea).ToList();
        var box = new Box(s.Left, s.Top, Math.Max(s.Width, 100), Math.Max(s.Height, 60));
        if (works.Count > 0 && s.State != "minimized")
        {
            var idx = WindowGeometry.MonitorIndexFor(box, works);
            if (box.Intersect(works[idx]).Area < Math.Min(box.Area, 40000)) box = WindowGeometry.KeepInside(box, works[idx]);
        }

        if (s.State == "minimized")
        {
            var wp = new WINDOWPLACEMENT
            {
                length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>(), showCmd = Win32.SW_SHOWMINNOACTIVE,
                rcNormalPosition = new RECT { Left = box.X, Top = box.Y, Right = box.Right, Bottom = box.Bottom },
            };
            Win32.SetWindowPlacement(h, ref wp);
            return;
        }
        if (Win32.IsIconic(h) || Win32.IsZoomed(h)) Win32.ShowWindow(h, Win32.SW_RESTORE);
        Win32.SetWindowPos(h, Win32.HWND_TOP, box.X, box.Y, box.Width, box.Height, Win32.SWP_NOACTIVATE);
        if (s.State == "maximized") Win32.ShowWindow(h, Win32.SW_SHOWMAXIMIZED);
    }

    /// <summary>The app's AppUserModelId from the window (used to reopen Store apps).</summary>
    internal static string? GetAppId(IntPtr hwnd)
    {
        try
        {
            if (Win32.SHGetPropertyStoreForWindow(hwnd, ComGuids.IPropertyStore, out var o) != 0) return null;
            var store = (IPropertyStore)o;
            var key = new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };
            if (store.GetValue(ref key, out var pv) != 0) return null;
            try { return pv.vt == 31 ? Marshal.PtrToStringUni(pv.p1) : null; } // VT_LPWSTR
            finally { Win32.PropVariantClear(ref pv); }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return null; }
    }
}

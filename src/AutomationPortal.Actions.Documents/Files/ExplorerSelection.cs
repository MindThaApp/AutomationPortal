using System.Runtime.InteropServices;
using System.Text;

namespace AutomationPortal.Actions.Documents;

/// <summary>Reads the files selected in the File Explorer window the user was working in (or on the desktop), via the Shell.Application COM object.</summary>
public static class ExplorerSelection
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);

    private const uint GW_HWNDNEXT = 2;

    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProviderCom
    {
        [PreserveSig] int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    }

    [ComImport, Guid("00000114-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleWindowCom
    {
        [PreserveSig] int GetWindow(out IntPtr hwnd);
    }

    private sealed record Candidate(long Hwnd, bool IsActiveTab, IReadOnlyList<string> Selected);

    public static Task<IReadOnlyList<string>> GetSelectedPathsAsync(CancellationToken ct = default) =>
        StaWorker.Com.RunAsync(GetSelectedPaths, ct);

    /// <summary>Must run on an STA thread.</summary>
    public static IReadOnlyList<string> GetSelectedPaths()
    {
        var type = Type.GetTypeFromProgID("Shell.Application");
        if (type is null) return Array.Empty<string>();
        object? shellObj = null;
        try
        {
            shellObj = Activator.CreateInstance(type);
            dynamic shell = shellObj!;
            dynamic windows = shell.Windows();
            var candidates = new List<Candidate>();
            foreach (var w in (System.Collections.IEnumerable)windows)
            {
                try
                {
                    dynamic win = w;
                    long hwnd = (long)win.HWND;
                    var selected = ReadSelection(win.Document);
                    candidates.Add(new Candidate(hwnd, IsActiveTab(w), selected));
                }
                catch { /* Internet Explorer windows, closing windows … */ }
            }

            var fg = GetForegroundWindow();
            // 1. The Explorer window in front (the active tab when it has several).
            var inFront = candidates.Where(c => c.Hwnd == (long)fg).OrderByDescending(c => c.IsActiveTab).ThenByDescending(c => c.Selected.Count).ToList();
            if (inFront.Count > 0)
            {
                var best = inFront.FirstOrDefault(c => c.IsActiveTab && c.Selected.Count > 0) ?? inFront.FirstOrDefault(c => c.Selected.Count > 0);
                if (best is not null) return best.Selected;
            }
            // 2. The desktop, when it is in front.
            if (IsDesktop(fg))
            {
                var desktop = ReadDesktopSelection(windows);
                if (desktop.Count > 0) return desktop;
            }
            // 3. The top-most Explorer window that has something selected.
            var byHwnd = candidates.Where(c => c.Selected.Count > 0).GroupBy(c => c.Hwnd).ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.IsActiveTab).First());
            for (var h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = GetWindow(h, GW_HWNDNEXT))
                if (byHwnd.TryGetValue((long)h, out var found)) return found.Selected;
            return Array.Empty<string>();
        }
        finally
        {
            if (shellObj is not null && Marshal.IsComObject(shellObj)) Marshal.ReleaseComObject(shellObj);
        }
    }

    private static IReadOnlyList<string> ReadSelection(dynamic document)
    {
        var list = new List<string>();
        dynamic items = document.SelectedItems();
        int count = items.Count;
        for (var i = 0; i < count; i++)
        {
            string p = items.Item(i).Path;
            if (!string.IsNullOrEmpty(p) && Path.IsPathRooted(p) && (File.Exists(p) || Directory.Exists(p))) list.Add(p);
        }
        return list;
    }

    private static IReadOnlyList<string> ReadDesktopSelection(dynamic windows)
    {
        try
        {
            object location = 0;
            object empty = Type.Missing;
            int hwnd;
            dynamic desktop = windows.FindWindowSW(ref location, ref empty, 8 /* SWC_DESKTOP */, out hwnd, 1 /* SWFO_NEEDDISPATCH */);
            return ReadSelection(desktop.Document);
        }
        catch { return Array.Empty<string>(); }
    }

    private static bool IsDesktop(IntPtr hwnd)
    {
        var sb = new StringBuilder(64);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString() is "Progman" or "WorkerW";
    }

    /// <summary>With tabs, several shell windows share one HWND; the active tab's browser window is the visible one.</summary>
    private static bool IsActiveTab(object window)
    {
        try
        {
            var sp = (IServiceProviderCom)window;
            var sidTopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
            var iidShellBrowser = new Guid("000214E2-0000-0000-C000-000000000046");
            if (sp.QueryService(ref sidTopLevelBrowser, ref iidShellBrowser, out var ptr) != 0 || ptr == IntPtr.Zero) return true;
            try
            {
                var ole = (IOleWindowCom)Marshal.GetObjectForIUnknown(ptr);
                return ole.GetWindow(out var tab) == 0 && IsWindowVisible(tab);
            }
            finally { Marshal.Release(ptr); }
        }
        catch { return true; }
    }
}
